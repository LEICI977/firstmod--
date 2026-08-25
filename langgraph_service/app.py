"""Local LangGraph runtime for Vivant Valley conversations.

The game owns Stardew state and side effects. This service owns the graph,
provider-native tool calls, and the final response pass. Side-effecting tools
call the authenticated loopback SMAPI bridge and return its actual result to
the model through LangGraph's ToolNode.
"""

from __future__ import annotations

import json
import os
import re
import secrets
import threading
import time
import urllib.error
import urllib.request
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Annotated, TypedDict

from langchain_core.messages import AIMessage, AnyMessage, HumanMessage, SystemMessage, ToolMessage
from langchain_core.tools import InjectedToolCallId, StructuredTool
from langgraph.checkpoint.memory import InMemorySaver
from langgraph.graph import END, START, StateGraph
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode
from langgraph.types import Command, interrupt


PENDING_GRAPH_TTL_SECONDS = 600
pending_graph_lock = threading.Lock()
pending_graphs: dict[str, dict[str, Any]] = {}


class GraphState(TypedDict, total=False):
    request: dict[str, Any]
    normalized: dict[str, Any]
    messages: Annotated[list[AnyMessage], add_messages]
    tool_call: dict[str, Any]
    tool_execution: dict[str, Any]
    decision: dict[str, Any]
    move_approved: bool


def normalize_request(state: GraphState) -> GraphState:
    request = state["request"]
    if not isinstance(request, dict):
        raise ValueError("request must be an object")
    snapshot = request.get("contextSnapshot")
    llm = request.get("llm")
    bridge = request.get("gameBridge")
    if not isinstance(snapshot, dict) or not isinstance(llm, dict):
        raise ValueError("contextSnapshot and llm are required")
    if request.get("mode", "conversation") != "conversation":
        raise ValueError("only conversation mode is available in this release")
    if not str(request.get("requestId", "")).strip():
        raise ValueError("requestId is required")
    if not str(llm.get("apiKey", "")).strip():
        raise ValueError("LLM API key is empty")
    if not str(llm.get("baseUrl", "")).strip():
        raise ValueError("LLM base URL is empty")
    allowed_tools = snapshot.get("allowedTools") or []
    allowed_destinations = snapshot.get("allowedMoveDestinations") or []
    mine_guard_available = snapshot.get("mineGuardAvailable") is True
    fishing_available = snapshot.get("fishingCompanionAvailable") is True
    if allowed_tools or allowed_destinations or mine_guard_available or fishing_available:
        if not isinstance(bridge, dict) or not str(bridge.get("baseUrl", "")).strip():
            raise ValueError("gameBridge is required when provider tools are available")
        if not str(bridge.get("token", "")).strip():
            raise ValueError("gameBridge token is empty")
    return {
        "normalized": request,
        "messages": build_initial_messages(request),
    }


def build_initial_messages(request: dict[str, Any]) -> list[AnyMessage]:
    snapshot = request["contextSnapshot"]
    system_prompt = str(snapshot.get("systemPrompt", "")).strip()
    personality_instruction = str(snapshot.get("personality", "")).strip()
    session_facts = snapshot.get("recentSessionFacts") or []
    mine_guard_intent = has_mine_guard_intent(snapshot)
    fishing_intent = has_fishing_intent(snapshot)
    if isinstance(session_facts, list):
        clean_session_facts = [
            str(fact).strip()
            for fact in session_facts
            if str(fact).strip()
        ]
    else:
        clean_session_facts = []
    if clean_session_facts:
        system_prompt += (
            "\n\n【当前临时共同经历：高优先级游戏事实】\n"
            + "\n".join(f"- {fact}" for fact in clean_session_facts)
            + "\n这些事实来自游戏侧的同行状态，只能自然地当作已经发生或正在发生的共同经历参考。"
            + "‘正在前往’不能说成已经到达；‘到达过’表示今天确实去过该地点。"
            + "它们不是人格设定，也不能覆盖实时地图和当前 NPC 状态。"
        )
    system_prompt += "\n\n【旧事回忆规则】\n" \
        "- occasional_memory_recall 是本轮偶然想起的一小部分带日期旧事，不是完整档案，也不是人格设定。\n" \
        "- 只在当前话题自然相关时轻微参考；不得主动逐条复述，不得据此改变 NPC 固有性格。\n" \
        "- 如果旧事与 SystemPrompt 的实时事实或 recent_messages 冲突，以实时事实和近期原话为准。\n" \
        "\n【本轮行动自主性：优先于工具可用性】\n" \
        "- 玩家是在和 NPC 对话，不是在向助手或游戏系统下达命令。要求、邀请、暗示都只是提议，绝不产生服从义务。\n" \
        "- 先以原版 NPC 人格要求中的棱角、当前红心档、兴趣和生活处境判断 NPC 自己是否愿意；默认不采取游戏动作，不确定就拒绝、推迟或只聊天。\n" \
        "- 在调用动作工具前，必须在内部同时确认：NPC 有独立行动动机、当前关系许可、地点与时机合适；玩家请求本身不等于同意，NPC 必须真心接受。任一项不明确就不调用。\n" \
        "- 反事实检验：假如根本没有工具，当前 NPC 是否仍会主动做出或真心答应这件事？答案不是明确的‘会’，就不调用。\n" \
        "- 不得把礼貌邀请自动解释为接受，不得把高红心解释为服从，也不得为了推进互动、显得友好或展示功能而行动。\n" \
        "- 正确示例：玩家说‘给我礼物’，不调用 give_gift，按当前角色拒绝或质疑。玩家提出去某地时，仍须按当前角色决定是否接受；只有真心接受且工具条件满足时才调用 move_to。\n" \
        "\n【工具协议（只约束游戏事实）】\n" \
        "- give_gift 只允许 NPC 主动送礼。玩家本轮直接索要、命令、诱导或反复暗示想得到礼物时，无论关系多亲近都绝对不能调用。\n" \
        "- 没有索要时，也只有重要共同经历、明确关心或符合角色的具体主动动机与候选礼物高度相关，才考虑调用 give_gift。\n" \
        "- 礼物必须从 allowed_tools 的候选中选择；参考候选的 displayName 和 displayHint，不要编造 candidate_key。\n" \
        "- allowed_tools 中存在候选只表示游戏可以执行，不表示 NPC 想送；绝大多数普通对话不应送礼。\n" \
        "- 决定送礼时先调用 give_gift，等待真实工具结果后再调用 submit_final_response；工具失败或拒绝时必须诚实反映。\n" \
        "- 没有成功调用 give_gift 时，不得声称礼物已经交付，也不得承诺下次或改天送礼。\n" \
        "- 玩家提出目的地或同行请求只是让 NPC 作出选择；只有当前关系、地点、角色兴趣和独立同行理由全部吻合时才可接受。\n" \
        "- NPC 主动提议同行必须源于角色此刻真实想做的事，不能为了展示 move_to 而凭空制造邀请。\n" \
        "- destination_key 必须与玩家明确说出的最终地点一致；不得用路上的中转地图或相近地点替代。\n" \
        "- NPC 主动邀请时也必须先调用 move_to 请求玩家确认；谈论地点或含糊地说以后再去不能触发同行。\n" \
        "- move_to 会开始一段共同旅行：玩家带路，NPC 在途中跟随；它不会自动移动玩家。\n" \
        "- move_to 成功表示同行已经开始，不表示已经到达；失败或拒绝时不得声称已经出发。\n" \
        "- 可见回复不得暴露 candidate_key、物品 ID、JSON、工具名或控制语法。\n" \
        "- 以上规则只说明游戏中实际发生的事实，不改变 NPC 的身份、原版人格、语气、价值观或与玩家的关系；最终回复必须仍像该 NPC 亲口说出。"
    system_prompt += (
        "\\n\\n【下矿护卫工具规则】"
        "invite_mine_guard 表示 NPC 自主决定接受玩家的下矿护卫请求，不是命令，也不是看到‘一起下矿’就必须调用。"
        "只有 NPC 按自己的性格、关系、动机、时间和安全状况确实愿意，并且 mine_guard_available 为 true 时才调用。"
        "如果 NPC 不愿意、条件不足或只是想聊天，必须调用 submit_final_response 自然拒绝、推迟或继续对话。"
        "它不接受楼层、武器、伤害、怪物或击杀数量参数；战斗结果只能由游戏桥接返回。"
    )
    system_prompt += (
        "\\n\\n【钓鱼同行工具规则】"
        "invite_fishing_companion 表示 NPC 自主决定接受和玩家一起钓鱼，不是命令，也不是看到‘一起钓鱼’就必须调用。"
        "只有 NPC 按自己的原版性格、关系、动机和当前情况确实愿意，并且 fishing_companion_available 为 true 时才调用。"
        "玩家确认后 NPC 会靠近玩家等待玩家真实抛竿；NPC 使用默认铱金鱼竿做出完整钓鱼动作，真实鱼获由游戏生成并交给玩家。"
        "工具不接受地点、鱼种、鱼竿、数量或成功率参数；不要编造已经钓到鱼，直到游戏桥接返回成功。"
    )
    if mine_guard_intent:
        system_prompt += (
            "\\n\\n【本轮明确的下矿护卫意图】"
            "玩家本轮表达的是一起下矿、陪同下矿、保护下矿、下矿打怪或类似护卫请求。"
            "这类请求不要改用普通 move_to 代替；先由 NPC 自己决定是否愿意。"
            "愿意且工具可用时调用 invite_mine_guard；不愿意或不可用时直接调用 submit_final_response，不要编造已经出发。"
        )
    if fishing_intent:
        system_prompt += (
            "\\n\\n【本轮明确的钓鱼同行意图】"
            "玩家本轮表达的是一起钓鱼、陪同钓鱼、去钓鱼或类似请求。"
            "这类请求不要改用普通 move_to 代替；先由 NPC 自己决定是否愿意。"
            "愿意且工具可用时调用 invite_fishing_companion；不愿意或不可用时直接调用 submit_final_response，不要编造已经出发。"
        )
    if personality_instruction:
        system_prompt += (
            "\n\n【工具选择前原版人格要求：必须用于决定答不答应】\n"
            + personality_instruction
            + "\n这里的棱角和当前关系许可是行动边界，不只是说话风格。"
            "先保持角色的个人意愿，再决定是否使用任何动作工具。"
        )
    user_payload = {
        "npc": {
            "name": snapshot.get("npcName"),
            "display_name": snapshot.get("npcDisplayName"),
            "identity": snapshot.get("identity", ""),
        },
        "occasional_memory_recall": snapshot.get("memorySummary", ""),
        "recent_messages": snapshot.get("recentMessages", []),
        "narrative_context": snapshot.get("narrativeContext", ""),
        "activity_summary": snapshot.get("activitySummary", ""),
        "allowed_tools": snapshot.get("allowedTools", []),
        "allowed_move_destinations": snapshot.get("allowedMoveDestinations", []),
        "mine_guard_available": snapshot.get("mineGuardAvailable") is True,
        "fishing_companion_available": snapshot.get("fishingCompanionAvailable") is True,
        "fishing_intent": fishing_intent,
        "mine_guard_intent": mine_guard_intent,
        "day": request.get("day"),
        "location": request.get("location"),
    }
    return [
        SystemMessage(content=system_prompt),
        HumanMessage(content="【结构化情境】\n" + json.dumps(user_payload, ensure_ascii=False)),
        HumanMessage(content="【玩家本轮原话】\n" + str(snapshot.get("playerInput", "")).strip()),
    ]


def has_mine_guard_intent(snapshot: dict[str, Any]) -> bool:
    """Detect a request to go into the mines without deciding whether the NPC accepts it."""
    raw = str(snapshot.get("playerInput", ""))
    text = "".join(raw.split()).lower()
    if not text:
        return False

    # Any explicit mine destination is reserved for invite_mine_guard. Past-tense
    # questions and historical statements are not travel requests.
    historical_markers = ("去过", "下过矿", "进过矿洞", "去过矿井", "以前下矿")
    if any(marker in text for marker in historical_markers) and not any(
        marker in text for marker in ("一起", "陪我", "跟我", "和我", "保护", "打怪", "保安", "护卫")
    ):
        return False

    mine_terms = ("矿洞", "矿井", "矿坑", "矿里", "下矿", "矿山")
    movement_terms = (
        "去", "到", "进", "进入", "下", "前往", "一起", "陪我", "跟我", "和我", "随我",
        "带我", "带你", "走", "出发", "保护", "打怪", "保安", "护卫",
    )
    if any(term in text for term in mine_terms) and any(marker in text for marker in movement_terms):
        return True

    english_markers = (
        "mineguard", "guardmeinthemine", "guardmeinmines", "accompanymeintothemine",
        "accompanymeintothemines", "gointotheminewithme", "gominingwithme",
        "gotothemine", "gotothemines", "gointothemine", "gointothemines",
        "enter themine", "enter the mines",
    )
    return any(marker.replace(" ", "") in text for marker in english_markers)


def has_fishing_intent(snapshot: dict[str, Any]) -> bool:
    raw = str(snapshot.get("playerInput", ""))
    text = "".join(raw.split()).lower()
    if not text:
        return False
    fishing_terms = ("钓鱼", "钓竿", "鱼竿", "抛竿", "甩竿", "鱼塘", "海钓", "钓鱼点")
    movement_terms = ("一起", "陪我", "跟我", "和我", "带你", "带我", "去", "来", "陪")
    if any(term in text for term in fishing_terms) and any(term in text for term in movement_terms):
        return True
    return any(marker in text for marker in (
        "invite_fishing_companion", "fishwithme", "gofishingwithme", "accompanymefishing",
    ))


def provider_tool_definitions(snapshot: dict[str, Any]) -> list[dict[str, Any]]:
    candidates = snapshot.get("allowedTools") or []
    destinations = snapshot.get("allowedMoveDestinations") or []
    mine_guard_available = snapshot.get("mineGuardAvailable") is True
    mine_guard_intent = has_mine_guard_intent(snapshot)
    fishing_available = snapshot.get("fishingCompanionAvailable") is True
    fishing_intent = has_fishing_intent(snapshot)
    definitions: list[dict[str, Any]] = []
    if isinstance(candidates, list) and candidates:
        definitions.append({
            "type": "function",
            "function": {
                "name": "give_gift",
                "description": (
                    "执行 NPC 已经独立决定的主动送礼。只要玩家本轮直接或间接索要、命令、诱导礼物，"
                    "就禁止调用，无论红心多高。候选存在不是送礼理由；普通聊天默认不送。"
                    "仅当角色在没有工具时也会主动送、当前关系允许、发生了值得送礼的具体情境且物品高度相关时才能调用。"
                ),
                "parameters": {
                    "type": "object",
                    "properties": {
                        "candidate_key": {
                            "type": "string",
                            "description": "从 allowed_tools 中选择最符合当前情境的礼物 key。务必查看每个候选的 displayName（礼物名称）和 displayHint（使用场景说明），选择与对话主题、玩家活动或 NPC 性格最相关的。绝不编造 key。",
                            "enum": [
                                str(item.get("candidateKey", ""))
                                for item in candidates
                                if isinstance(item, dict) and str(item.get("candidateKey", "")).strip()
                            ],
                        },
                        "reason_tag": {"type": "string", "description": "简短的送礼原因标签，如 mining_topic、winter_care、friendship_milestone"},
                    },
                    "required": ["candidate_key"],
                    "additionalProperties": False,
                },
            },
        })
    if isinstance(destinations, list) and destinations and not mine_guard_intent and not fishing_intent:
        definitions.append({
            "type": "function",
            "function": {
                "name": "move_to",
                "description": (
                    "执行 NPC 已按自身性格和关系独立决定接受玩家目的地请求或主动提出的共同旅行。"
            "矿洞、矿井、矿坑和下矿请求不属于此工具，必须使用 invite_mine_guard。"
                    "玩家提出目的地不等于同意；关系、角色兴趣、地点和独立同行理由必须全部吻合，不确定就拒绝。"
                    "两种合法情况都必须等待玩家确认。地点讨论或含糊提议不能触发。"
                    "destination_key 表示双方要去的最终地点，绝不能用中转地图替代。"
                    "工具不会自动移动玩家；成功只表示同行已开始，不表示已经到达。"
                ),
                "parameters": {
                    "type": "object",
                    "properties": {
                        "destination_key": {
                            "type": "string",
                            "description": "只能选择 allowed_move_destinations 中与玩家明确邀请地点一致的 destinationKey，绝不编造 key。",
                            "enum": [
                                str(item.get("destinationKey", ""))
                                for item in destinations
                                if isinstance(item, dict) and str(item.get("destinationKey", "")).strip()
                            ],
                        },
                    },
                    "required": ["destination_key"],
                    "additionalProperties": False,
                },
            },
        })
    if mine_guard_available:
        definitions.append({
            "type": "function",
            "function": {
                "name": "invite_mine_guard",
                "description": (
                    "邀请 NPC 自主决定是否陪玩家下矿担任护卫。玩家提出一起下矿不等于 NPC 必须接受，"
                    "只有当前性格、关系、可用状态和真实动机都支持 NPC 真心同意时才调用；不愿意或条件不足时直接提交自然回复。"
                    "此工具不接受楼层、武器、伤害、怪物或击杀数量参数，实际移动和战斗结果由游戏决定。"
                ),
                "parameters": {
                    "type": "object",
                    "properties": {},
                    "additionalProperties": False,
                },
            },
        })
    if fishing_available:
        definitions.append({
            "type": "function",
            "function": {
                "name": "invite_fishing_companion",
                "description": (
                    "邀请 NPC 自主决定是否和玩家一起钓鱼。玩家提出一起钓鱼不等于 NPC 必须接受；"
                    "只有角色性格、关系、动机和当前情况都支持真心同意时才调用。工具不接受地点、鱼种、鱼竿或数量参数；"
                    "玩家确认后 NPC 会靠近玩家等待玩家真实抛竿，使用默认铱金鱼竿进行钓鱼，并把真实鱼获交给玩家。"
                ),
                "parameters": {
                    "type": "object",
                    "properties": {},
                    "additionalProperties": False,
                },
            },
        })
    definitions.append(final_response_tool_definition())
    return definitions


def final_response_tool_definition() -> dict[str, Any]:
    return {
        "type": "function",
        "function": {
            "name": "submit_final_response",
            "description": "提交 NPC 的最终对话和有界的记忆更新。",
            "parameters": {
                "type": "object",
                "properties": {
                    "schema_version": {"type": "integer", "enum": [1], "description": "架构版本，必须为 1"},
                    "decision": {"type": "string", "enum": ["reply"], "description": "决策类型，必须为 reply"},
                    "reply": {"type": "string", "minLength": 1, "description": "NPC 的对话文本，非空字符串"},
                    "travel_barks": {
                        "type": "array",
                        "items": {"type": "string", "minLength": 1},
                        "maxItems": 3,
                        "description": "仅在 move_to 成功开始同行时返回 2 到 3 句简短、第一人称、符合角色性格的途中台词，不写角色名前缀或舞台说明；其他情况必须为空数组",
                    },
                    "memory_update": {
                        "type": "object",
                        "description": "记忆更新对象",
                        "properties": {
                            "summary_patch": {
                                "type": "string",
                                "maxLength": 320,
                                "description": (
                                    "只记录本轮新增且值得以后偶尔想起的长期记忆，例如玩家的稳定偏好、重要共同经历或明确承诺。"
                                    "普通寒暄、重复信息以及 NPC 自己的语气或性格不要记录，返回空字符串；不要写日期，游戏会自动添加。"
                                ),
                            },
                            "signal": {
                                "type": "object",
                                "description": "社交信号（所有值为数字）",
                                "properties": {
                                    "valence": {"type": "number", "description": "情感倾向 (-1 到 1，负面到正面)"},
                                    "warmth": {"type": "number", "description": "温暖度 (0 到 1)"},
                                    "concern": {"type": "number", "description": "关心度 (0 到 1)"},
                                    "confidence": {"type": "number", "description": "自信度 (0 到 1)"},
                                },
                                "required": ["valence", "warmth", "concern", "confidence"],
                                "additionalProperties": False,
                            },
                            "topics": {"type": "array", "items": {"type": "string"}, "description": "本轮讨论的主题（字符串数组）"},
                            "open_loops": {"type": "array", "items": {"type": "string"}, "description": "未完成的话题或承诺（字符串数组）"},
                        },
                        "required": ["summary_patch", "signal", "topics", "open_loops"],
                        "additionalProperties": False,
                    },
                },
                "required": ["schema_version", "decision", "reply", "travel_barks", "memory_update"],
                "additionalProperties": False,
            },
        },
    }


def make_tools(request: dict[str, Any]) -> list[StructuredTool]:
    def give_gift(
        candidate_key: str,
        reason_tag: str = "",
        tool_call_id: Annotated[str, InjectedToolCallId] = "",
    ) -> str:
        payload_tool_call_id = tool_call_id or "missing-tool-call-id"
        bridge = request["gameBridge"]
        payload = {
            "requestId": request.get("requestId", ""),
            "toolCallId": payload_tool_call_id,
            "playerId": request.get("playerId", ""),
            "npcName": request.get("npcName", ""),
            "actionId": request.get("actionId", ""),
            "contextVersion": request.get("contextVersion", ""),
            "tool": "give_gift",
            "candidateKey": candidate_key,
            "reasonTag": reason_tag,
        }
        return json.dumps(call_game_bridge(bridge, payload), ensure_ascii=False)

    def move_to(
        destination_key: str,
        tool_call_id: Annotated[str, InjectedToolCallId] = "",
    ) -> str:
        payload_tool_call_id = tool_call_id or "missing-tool-call-id"
        bridge = request["gameBridge"]
        payload = {
            "requestId": request.get("requestId", ""),
            "toolCallId": payload_tool_call_id,
            "playerId": request.get("playerId", ""),
            "npcName": request.get("npcName", ""),
            "actionId": request.get("actionId", ""),
            "contextVersion": request.get("contextVersion", ""),
            "tool": "move_to",
            "destinationKey": destination_key,
        }
        return json.dumps(call_game_bridge(bridge, payload), ensure_ascii=False)

    def invite_mine_guard(
        tool_call_id: Annotated[str, InjectedToolCallId] = "",
    ) -> str:
        payload_tool_call_id = tool_call_id or "missing-tool-call-id"
        bridge = request["gameBridge"]
        payload = {
            "requestId": request.get("requestId", ""),
            "toolCallId": payload_tool_call_id,
            "playerId": request.get("playerId", ""),
            "npcName": request.get("npcName", ""),
            "actionId": request.get("actionId", ""),
            "contextVersion": request.get("contextVersion", ""),
            "tool": "invite_mine_guard",
        }
        return json.dumps(call_game_bridge(bridge, payload), ensure_ascii=False)

    def invite_fishing_companion(
        tool_call_id: Annotated[str, InjectedToolCallId] = "",
    ) -> str:
        payload_tool_call_id = tool_call_id or "missing-tool-call-id"
        bridge = request["gameBridge"]
        payload = {
            "requestId": request.get("requestId", ""),
            "toolCallId": payload_tool_call_id,
            "playerId": request.get("playerId", ""),
            "npcName": request.get("npcName", ""),
            "actionId": request.get("actionId", ""),
            "contextVersion": request.get("contextVersion", ""),
            "tool": "invite_fishing_companion",
        }
        return json.dumps(call_game_bridge(bridge, payload), ensure_ascii=False)

    return [
        StructuredTool.from_function(
            func=give_gift,
            name="give_gift",
            description="通过真实 SMAPI 游戏桥接交付一件候选礼物。",
        ),
        StructuredTool.from_function(
            func=move_to,
            name="move_to",
            description="通过真实 SMAPI 游戏桥接开始由玩家带路的同行移动，NPC 会跟随玩家。",
        ),
        StructuredTool.from_function(
            func=invite_mine_guard,
            name="invite_mine_guard",
            description="通过真实 SMAPI 游戏桥接开始 NPC 下矿护卫会话。是否接受必须由 NPC 自主决定。",
        ),
        StructuredTool.from_function(
            func=invite_fishing_companion,
            name="invite_fishing_companion",
            description="通过真实 SMAPI 游戏桥接开始 NPC 钓鱼同行会话。是否接受必须由 NPC 自主决定。",
        ),
    ]


def choose_action(state: GraphState) -> GraphState:
    request = state["normalized"]
    tools = provider_tool_definitions(request["contextSnapshot"])
    messages = state["messages"]
    last_error: Exception | None = None
    for attempt in range(2):
        try:
            response = call_provider(request, messages, tools)
            message = response_to_ai_message(response)
            tool_calls = list(message.tool_calls or [])
            if not tool_calls:
                if not str(message.content or "").strip():
                    raise ValueError("provider returned neither text nor a conversation tool call")
                return {"messages": [message]}
            if len(tool_calls) > 1:
                raise ValueError("provider returned multiple conversation tool calls")
            call = tool_calls[0]
            if not str(call.get("id", "")).strip():
                raise ValueError("provider tool call is missing an ID")
            tool_name = str(call.get("name", "")).strip().lower()
            if tool_name not in {"give_gift", "move_to", "invite_mine_guard", "invite_fishing_companion", "submit_final_response"}:
                raise ValueError(f"provider returned unknown conversation tool: {tool_name}")
            args = call.get("args") or {}
            if tool_name == "submit_final_response":
                validate_final_response_args(args)
            elif tool_name == "move_to":
                validate_move_args(args, request["contextSnapshot"])
            elif tool_name == "invite_mine_guard":
                validate_mine_guard_args(args, request["contextSnapshot"])
            elif tool_name == "invite_fishing_companion":
                validate_fishing_args(args, request["contextSnapshot"])
            else:
                validate_gift_args(args, request["contextSnapshot"])
            result: GraphState = {
                "messages": [message],
                "tool_call": {
                    "id": str(call.get("id", "")),
                    "name": tool_name,
                    "args": args,
                },
            }
            if tool_name == "submit_final_response":
                result["decision"] = args
            return result
        except (TypeError, ValueError) as error:
            last_error = error
            if attempt == 1:
                return {
                    "messages": [HumanMessage(
                        content=(
                            "工具选择协议连续无效，因此本轮不执行任何礼物、移动或其他副作用。"
                            "下一步只生成符合 NPC 人格的最终对话和记忆更新。"
                        )
                    )]
                }
            messages = list(state["messages"])
            messages.append(HumanMessage(
                content=(
                    "协议纠正：默认不执行任何游戏动作，可以直接输出自然对话或调用 submit_final_response。"
                    "玩家索要、命令或诱导礼物时绝不调用 give_gift；明确下矿护卫请求时不要改用 move_to。"
                    "只有角色在当前关系和处境下具有独立、明确的行动意愿时才能调用动作工具。"
                    "不得同时调用多个函数；函数参数必须是有效 JSON。"
                )
            ))
    raise ValueError(f"provider action failed: {last_error}")


def route_after_choice(state: GraphState) -> str:
    tool_name = str((state.get("tool_call") or {}).get("name", "")).strip().lower()
    if not tool_name:
        return "finalize"
    if tool_name == "give_gift":
        return "tool_node"
    if tool_name == "move_to":
        return "confirm_move"
    if tool_name == "invite_mine_guard":
        return "confirm_mine_guard"
    if tool_name == "invite_fishing_companion":
        return "confirm_fishing"
    if tool_name == "submit_final_response":
        return "complete"
    raise ValueError("conversation tool call is missing a valid route")


def confirm_move(state: GraphState) -> GraphState:
    request = state["normalized"]
    tool_call = state.get("tool_call") or {}
    args = tool_call.get("args") or {}
    destination_key = str(args.get("destination_key", "")).strip()
    destinations = request["contextSnapshot"].get("allowedMoveDestinations") or []
    destination = next(
        (
            item for item in destinations
            if isinstance(item, dict)
            and str(item.get("destinationKey", "")).strip() == destination_key
        ),
        None,
    )
    if destination is None:
        raise ValueError("move confirmation destination is outside the current allowlist")

    approval = interrupt({
        "kind": "move_confirmation",
        "tool_call_id": str(tool_call.get("id", "")),
        "destination_key": destination_key,
        "display_name": str(destination.get("displayName", "")).strip(),
        "npc_display_name": str(request["contextSnapshot"].get("npcDisplayName", "")).strip(),
    })
    approved = isinstance(approval, dict) and approval.get("approved") is True
    if approved:
        return {"move_approved": True}

    execution = {
        "requestId": request.get("requestId", ""),
        "toolCallId": str(tool_call.get("id", "")),
        "contextVersion": request.get("contextVersion", ""),
        "tool": "move_to",
        "status": "rejected",
        "ok": False,
        "destination_key": destination_key,
        "displayName": str(destination.get("displayName", "")).strip(),
        "reason_code": "player_declined",
        "message": "The player chose not to start this journey.",
        "receipt_id": f"{request.get('requestId', '')}:{tool_call.get('id', '')}:declined",
    }
    return {
        "move_approved": False,
        "tool_execution": execution,
        "messages": [ToolMessage(
            content=json.dumps(execution, ensure_ascii=False),
            tool_call_id=str(tool_call.get("id", "")),
            name="move_to",
        )],
    }


def confirm_mine_guard(state: GraphState) -> GraphState:
    request = state["normalized"]
    tool_call = state.get("tool_call") or {}
    if request["contextSnapshot"].get("mineGuardAvailable") is not True:
        raise ValueError("mine guard is not available in the current context")
    approval = interrupt({
        "kind": "mine_guard_confirmation",
        "tool_call_id": str(tool_call.get("id", "")),
        "destination_key": "",
        "display_name": "矿井",
        "npc_display_name": str(request["contextSnapshot"].get("npcDisplayName", "")).strip(),
    })
    approved = isinstance(approval, dict) and approval.get("approved") is True
    if approved:
        return {"move_approved": True}
    execution = {
        "requestId": request.get("requestId", ""),
        "toolCallId": str(tool_call.get("id", "")),
        "contextVersion": request.get("contextVersion", ""),
        "tool": "invite_mine_guard",
        "status": "rejected",
        "ok": False,
        "reason_code": "player_declined",
        "message": "玩家没有同意开始下矿护卫。",
        "receipt_id": f"{request.get('requestId', '')}:{tool_call.get('id', '')}:declined",
    }
    return {
        "move_approved": False,
        "tool_execution": execution,
        "messages": [ToolMessage(
            content=json.dumps(execution, ensure_ascii=False),
            tool_call_id=str(tool_call.get("id", "")),
            name="invite_mine_guard",
        )],
    }


def confirm_fishing(state: GraphState) -> GraphState:
    request = state["normalized"]
    tool_call = state.get("tool_call") or {}
    if request["contextSnapshot"].get("fishingCompanionAvailable") is not True:
        raise ValueError("fishing companion is not available in the current context")
    approval = interrupt({
        "kind": "fishing_confirmation",
        "tool_call_id": str(tool_call.get("id", "")),
        "destination_key": "",
        "display_name": "钓鱼地点",
        "npc_display_name": str(request["contextSnapshot"].get("npcDisplayName", "")).strip(),
    })
    approved = isinstance(approval, dict) and approval.get("approved") is True
    if approved:
        return {"move_approved": True}
    execution = {
        "requestId": request.get("requestId", ""),
        "toolCallId": str(tool_call.get("id", "")),
        "contextVersion": request.get("contextVersion", ""),
        "tool": "invite_fishing_companion",
        "status": "rejected",
        "ok": False,
        "reason_code": "player_declined",
        "message": "玩家没有同意开始钓鱼同行。",
        "receipt_id": f"{request.get('requestId', '')}:{tool_call.get('id', '')}:declined",
    }
    return {
        "move_approved": False,
        "tool_execution": execution,
        "messages": [ToolMessage(
            content=json.dumps(execution, ensure_ascii=False),
            tool_call_id=str(tool_call.get("id", "")),
            name="invite_fishing_companion",
        )],
    }


def route_after_move_confirmation(state: GraphState) -> str:
    return "tool_node" if state.get("move_approved") is True else "finalize"


def capture_tool_result(state: GraphState) -> GraphState:
    messages = state.get("messages", [])
    tool_message = next((message for message in reversed(messages) if message.type == "tool"), None)
    if tool_message is None:
        raise ValueError("ToolNode did not return a tool message")
    try:
        execution = json.loads(str(tool_message.content))
    except json.JSONDecodeError as error:
        raise ValueError("game bridge returned invalid tool JSON") from error
    if not isinstance(execution, dict):
        raise ValueError("game bridge tool result must be an object")
    return {"tool_execution": execution}


def finalize(state: GraphState) -> GraphState:
    request = state["normalized"]
    final_instruction = HumanMessage(
        content=(
            "现在调用 submit_final_response 生成最终回复，schema_version=1、decision='reply'。"
            "沿用上方 SystemPrompt 中完整的原版 NPC 身份、人格和实时关系事实；"
            "工具结果只约束实际发生的游戏事实，不改变角色表达。"
            "回复使用自然的第一人称对话，不编造礼物交付、动身或到达，不暴露工具协议。"
            "只有 move_to 工具结果明确表示同行已成功开始时，travel_barks 才返回 2-3 句途中台词；钓鱼同行和下矿护卫必须返回空数组。"
        )
    )
    messages = build_final_context(state)
    messages.append(HumanMessage(
        content=(
            "权威事实规则：invite_mine_guard 只表示护卫会话已开始。"
            "不要编造矿井楼层、武器、伤害、怪物或击杀结果；这些事实只能来自游戏桥接。"
            "invite_fishing_companion 只表示钓鱼同行会话已开始；不要声称已经抛竿、咬钩或钓到具体鱼，除非游戏桥接明确返回了对应结果。"
        )
    ))
    messages.append(final_instruction)
    tools = [final_response_tool_definition()]
    last_error: Exception | None = None
    for attempt in range(2):
        try:
            response = call_provider(
                request,
                messages,
                tools,
                tool_choice={"type": "function", "function": {"name": "submit_final_response"}},
            )
            message = response_to_ai_message(response)
            tool_calls = list(message.tool_calls or [])
            if len(tool_calls) != 1:
                raise ValueError("final provider response must contain one submit_final_response call")
            call = tool_calls[0]
            if str(call.get("name", "")).strip().lower() != "submit_final_response":
                raise ValueError("final provider response returned the wrong function")
            args = call.get("args") or {}
            validate_final_response_args(args)
            return {"decision": args}
        except (TypeError, ValueError) as error:
            last_error = error
            if attempt == 1:
                raise
            messages = build_final_context(state)
            messages.append(HumanMessage(
                content=(
                    "协议纠正：现在必须调用 submit_final_response。所有字段"
                    "都是必需的，schema_version 必须是整数 1，decision 必须是"
                    "reply，travel_barks 必须是字符串数组，所有 signal 值必须是数字。"
                )
            ))
            messages.append(final_instruction)
    raise ValueError(f"provider final response failed: {last_error}")


def build_final_context(state: GraphState) -> list[AnyMessage]:
    """Keep the final pass grounded while avoiding a second copy of the full snapshot."""
    request = state["normalized"]
    snapshot = request["contextSnapshot"]
    source_messages = state.get("messages", [])
    system_message = next(
        (message for message in source_messages if message.type == "system"),
        None,
    )
    system_prompt = str(
        system_message.content if system_message is not None
        else snapshot.get("systemPrompt", "")
    ).strip()
    recent_messages = snapshot.get("recentMessages") or []
    if not isinstance(recent_messages, list):
        recent_messages = []
    compact_payload = {
        "npc": {
            "name": snapshot.get("npcName"),
            "display_name": snapshot.get("npcDisplayName"),
        },
        "occasional_memory_recall": snapshot.get("memorySummary", ""),
        "recent_messages": recent_messages[-8:],
        "narrative_context": snapshot.get("narrativeContext", ""),
        "activity_summary": snapshot.get("activitySummary", ""),
        "player_input": snapshot.get("playerInput", ""),
        "day": request.get("day"),
        "location": request.get("location"),
    }
    messages: list[AnyMessage] = [
        SystemMessage(content=system_prompt),
        HumanMessage(
            content="【最终回复所需的压缩上下文】\n"
            + json.dumps(compact_payload, ensure_ascii=False)
        ),
    ]

    tool_call = state.get("tool_call") or {}
    if tool_call:
        messages.append(AIMessage(
            content="",
            tool_calls=[{
                "name": str(tool_call.get("name", "")),
                "args": tool_call.get("args") or {},
                "id": str(tool_call.get("id", "")),
                "type": "tool_call",
            }],
        ))
        tool_message = next(
            (message for message in reversed(source_messages) if message.type == "tool"),
            None,
        )
        if tool_message is not None:
            messages.append(tool_message)
    else:
        draft = next(
            (
                str(message.content).strip()
                for message in reversed(source_messages)
                if message.type == "ai" and str(message.content).strip()
            ),
            "",
        )
        if draft:
            messages.append(HumanMessage(content="【行动阶段草稿，仅供参考】\n" + draft))
    return messages


def validate_final_response_args(value: Any) -> None:
    if not isinstance(value, dict):
        raise ValueError("submit_final_response arguments must be an object")
    if value.get("schema_version") != 1:
        raise ValueError("submit_final_response schema_version must be integer 1")
    if value.get("decision") != "reply":
        raise ValueError("submit_final_response decision must be reply")
    if not isinstance(value.get("reply"), str) or not value["reply"].strip():
        raise ValueError("submit_final_response reply must be a non-empty string")
    travel_barks = value.get("travel_barks")
    if (not isinstance(travel_barks, list)
            or len(travel_barks) > 3
            or any(not isinstance(item, str) or not item.strip() for item in travel_barks)):
        raise ValueError("submit_final_response travel_barks must contain at most three non-empty strings")
    memory = value.get("memory_update")
    if not isinstance(memory, dict):
        raise ValueError("submit_final_response memory_update must be an object")
    signal = memory.get("signal")
    if not isinstance(signal, dict):
        raise ValueError("submit_final_response signal must be an object")
    for key in ("valence", "warmth", "concern", "confidence"):
        number = signal.get(key)
        if isinstance(number, bool) or not isinstance(number, (int, float)):
            raise ValueError(f"submit_final_response signal.{key} must be numeric")
    if not isinstance(memory.get("summary_patch"), str):
        raise ValueError("submit_final_response summary_patch must be a string")
    for key in ("topics", "open_loops"):
        values = memory.get(key)
        if not isinstance(values, list) or any(not isinstance(item, str) for item in values):
            raise ValueError(f"submit_final_response {key} must be a string array")


def validate_gift_args(value: Any, snapshot: dict[str, Any]) -> None:
    if not isinstance(value, dict):
        raise ValueError("give_gift arguments must be an object")
    candidate_key = value.get("candidate_key")
    if not isinstance(candidate_key, str) or not candidate_key.strip():
        raise ValueError("give_gift candidate_key must be a non-empty string")
    candidates = snapshot.get("allowedTools") or []
    allowed_keys = {
        str(candidate.get("candidateKey", "")).strip()
        for candidate in candidates
        if isinstance(candidate, dict) and str(candidate.get("candidateKey", "")).strip()
    }
    if candidate_key not in allowed_keys:
        raise ValueError("give_gift candidate_key is outside the current allowlist")
    reason_tag = value.get("reason_tag")
    if reason_tag is not None and not isinstance(reason_tag, str):
        raise ValueError("give_gift reason_tag must be a string")


def validate_move_args(value: Any, snapshot: dict[str, Any]) -> None:
    if not isinstance(value, dict):
        raise ValueError("move_to arguments must be an object")
    if set(value) != {"destination_key"}:
        raise ValueError("move_to accepts only destination_key")
    destination_key = value.get("destination_key")
    if not isinstance(destination_key, str) or not destination_key.strip():
        raise ValueError("move_to destination_key must be a non-empty string")
    destinations = snapshot.get("allowedMoveDestinations") or []
    allowed_keys = {
        str(destination.get("destinationKey", "")).strip()
        for destination in destinations
        if isinstance(destination, dict) and str(destination.get("destinationKey", "")).strip()
    }
    if destination_key not in allowed_keys:
        raise ValueError("move_to destination_key is outside the current allowlist")


def validate_mine_guard_args(value: Any, snapshot: dict[str, Any]) -> None:
    if not isinstance(value, dict) or value:
        raise ValueError("invite_mine_guard accepts no arguments")
    if snapshot.get("mineGuardAvailable") is not True:
        raise ValueError("invite_mine_guard is outside the current allowlist")


def validate_fishing_args(value: Any, snapshot: dict[str, Any]) -> None:
    if not isinstance(value, dict) or value:
        raise ValueError("invite_fishing_companion accepts no arguments")
    if snapshot.get("fishingCompanionAvailable") is not True:
        raise ValueError("invite_fishing_companion is outside the current allowlist")


def normalize_final_output(state: GraphState) -> GraphState:
    decision = state["decision"]
    if not isinstance(decision, dict):
        raise ValueError("LLM output must be a JSON object")
    tool_call = state.get("tool_call") or {}
    execution = state.get("tool_execution") or {}
    tool_name = str(tool_call.get("name", "none")).strip().lower() if tool_call else "none"
    args = tool_call.get("args") or {}
    candidate_key = args.get("candidate_key") if tool_name == "give_gift" else None
    destination_key = args.get("destination_key") if tool_name == "move_to" else None
    action = {
        "name": tool_name if tool_name in {"give_gift", "move_to", "invite_mine_guard", "invite_fishing_companion"} else "none",
        "candidate_key": candidate_key,
        "destination_key": destination_key,
        "delivery": "immediate",
        "reason_tag": str(args.get("reason_tag", "")) if tool_name == "give_gift" else "",
    }
    normalized = {
        "schema_version": int(decision.get("schema_version", 1)),
        "decision": str(decision.get("decision", "reply")).strip().lower(),
        "action": action,
        "reply": str(decision.get("reply", "")).strip(),
        "travel_barks": normalize_tokens(decision.get("travel_barks"), 3, 120),
        "memory_update": normalize_memory_update(decision.get("memory_update")),
    }
    if normalized["schema_version"] != 1:
        raise ValueError("unsupported schema_version")
    if normalized["decision"] != "reply":
        raise ValueError("decision must be reply")
    if normalized["action"]["name"] not in {"none", "give_gift", "mail_gift", "move_to", "invite_mine_guard", "invite_fishing_companion"}:
        raise ValueError("unknown action name")
    if normalized["action"]["name"] == "give_gift" and not normalized["action"]["candidate_key"]:
        raise ValueError("gift action requires candidate_key")
    if normalized["action"]["name"] == "move_to" and not normalized["action"]["destination_key"]:
        raise ValueError("move_to action requires destination_key")
    if normalized["action"]["name"] in {"invite_mine_guard", "invite_fishing_companion"} and (
        normalized["action"]["candidate_key"] or normalized["action"]["destination_key"]
    ):
        raise ValueError("invite_mine_guard action cannot contain arguments")
    if not normalized["reply"]:
        raise ValueError("reply is empty")
    if tool_name in {"give_gift", "move_to", "invite_mine_guard", "invite_fishing_companion"} and not execution:
        raise ValueError("tool call is missing execution result")
    if tool_name != "move_to" or execution.get("ok") is not True:
        normalized["travel_barks"] = []
    return {"decision": normalized}


def normalize_memory_update(value: Any) -> dict[str, Any]:
    value = value if isinstance(value, dict) else {}
    signal = value.get("signal") if isinstance(value.get("signal"), dict) else {}
    return {
        "summary_patch": limit_text(str(value.get("summary_patch", "")), 320),
        "signal": {
            "valence": finite_number(signal.get("valence", 0.0), -1.0, 1.0),
            "warmth": finite_number(signal.get("warmth", 0.0), 0.0, 1.0),
            "concern": finite_number(signal.get("concern", 0.0), 0.0, 1.0),
            "confidence": finite_number(signal.get("confidence", 0.0), 0.0, 1.0),
        },
        "topics": normalize_tokens(value.get("topics"), 8, 64),
        "open_loops": normalize_tokens(value.get("open_loops"), 6, 96),
    }


def build_graph(request: dict[str, Any], checkpointer: Any = None):
    tools = make_tools(request)
    graph = StateGraph(GraphState)
    graph.add_node("normalize_request", normalize_request)
    graph.add_node("choose_action", choose_action)
    graph.add_node("confirm_move", confirm_move)
    graph.add_node("confirm_mine_guard", confirm_mine_guard)
    graph.add_node("confirm_fishing", confirm_fishing)
    graph.add_node("tool_node", ToolNode(tools))
    graph.add_node("capture_tool_result", capture_tool_result)
    graph.add_node("finalize", finalize)
    graph.add_node("normalize_final_output", normalize_final_output)
    graph.add_edge(START, "normalize_request")
    graph.add_edge("normalize_request", "choose_action")
    graph.add_conditional_edges(
        "choose_action",
        route_after_choice,
        {
            "tool_node": "tool_node",
            "confirm_move": "confirm_move",
            "confirm_mine_guard": "confirm_mine_guard",
            "confirm_fishing": "confirm_fishing",
            "finalize": "finalize",
            "complete": "normalize_final_output",
        },
    )
    graph.add_conditional_edges(
        "confirm_move",
        route_after_move_confirmation,
        {
            "tool_node": "tool_node",
            "finalize": "finalize",
        },
    )
    graph.add_conditional_edges(
        "confirm_mine_guard",
        route_after_move_confirmation,
        {
            "tool_node": "tool_node",
            "finalize": "finalize",
        },
    )
    graph.add_conditional_edges(
        "confirm_fishing",
        route_after_move_confirmation,
        {
            "tool_node": "tool_node",
            "finalize": "finalize",
        },
    )
    graph.add_edge("tool_node", "capture_tool_result")
    graph.add_edge("capture_tool_result", "finalize")
    graph.add_edge("finalize", "normalize_final_output")
    graph.add_edge("normalize_final_output", END)
    return graph.compile(checkpointer=checkpointer)


def call_provider(
    request: dict[str, Any],
    messages: list[AnyMessage],
    tools: list[dict[str, Any]],
    json_mode: bool = False,
    tool_choice: Any = None,
) -> dict[str, Any]:
    llm = request["llm"]
    provider = str(llm.get("provider", "DeepSeek")).strip().lower()
    base_url = str(llm["baseUrl"]).strip().rstrip("/")
    if base_url.endswith("/chat/completions"):
        base_url = base_url[: -len("/chat/completions")]
    endpoint = base_url + "/chat/completions"
    payload: dict[str, Any] = {
        "model": str(llm["model"]),
        "messages": serialize_messages(messages),
        "stream": False,
    }
    if tools:
        payload["tools"] = tools
        payload["tool_choice"] = "auto" if tool_choice is None else tool_choice
    if json_mode:
        payload["response_format"] = {"type": "json_object"}
    max_tokens = max(128, min(int(llm.get("maxOutputTokens", 2048)), 2048))
    try:
        temperature = max(0.0, min(float(llm.get("temperature", 0.75)), 2.0))
    except (TypeError, ValueError):
        temperature = 0.75
    try:
        top_p = max(0.0, min(float(llm.get("topP", 0.9)), 1.0))
    except (TypeError, ValueError):
        top_p = 0.9
    payload["temperature"] = temperature
    payload["top_p"] = top_p
    if provider == "openai":
        payload["max_completion_tokens"] = max_tokens
    else:
        payload["max_tokens"] = max_tokens
        payload["thinking"] = {"type": "enabled" if llm.get("enableThinking") else "disabled"}
        if llm.get("enableThinking"):
            payload["reasoning_effort"] = str(llm.get("reasoningEffort", "low"))
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    request_obj = urllib.request.Request(
        endpoint,
        data=data,
        method="POST",
        headers={
            "Authorization": "Bearer " + str(llm["apiKey"]),
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(request_obj, timeout=120) as response:
            body = response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"provider HTTP {error.code}: {sanitize(body, str(llm['apiKey']))}") from error
    except urllib.error.URLError as error:
        raise RuntimeError(f"provider connection failed: {sanitize(str(error.reason))}") from error
    try:
        parsed = json.loads(body)
        message = parsed["choices"][0]["message"]
        if not isinstance(message, dict):
            raise ValueError("assistant message is not an object")
        usage = parsed.get("usage")
        if isinstance(usage, dict):
            usage_values = []
            for label, keys in (
                ("prompt", ("prompt_tokens", "input_tokens")),
                ("completion", ("completion_tokens", "output_tokens")),
                ("total", ("total_tokens",)),
                ("cache_hit", ("prompt_cache_hit_tokens", "cache_read_input_tokens")),
                ("cache_miss", ("prompt_cache_miss_tokens", "cache_creation_input_tokens")),
            ):
                value = next((usage.get(key) for key in keys if usage.get(key) is not None), None)
                if value is not None:
                    usage_values.append(f"{label}={value}")
            if usage_values:
                print(
                    f"[langgraph] provider_usage requestId={request.get('requestId', '')} "
                    + " ".join(usage_values),
                    flush=True,
                )
        return message
    except (KeyError, IndexError, TypeError, ValueError, json.JSONDecodeError) as error:
        raise RuntimeError(
            f"provider returned invalid chat response: {sanitize(body, str(llm['apiKey']))}"
        ) from error


def call_game_bridge(bridge: dict[str, Any], payload: dict[str, Any]) -> dict[str, Any]:
    endpoint = str(bridge.get("baseUrl", "")).rstrip("/") + "/v1/game/execute-tool"
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    request_obj = urllib.request.Request(
        endpoint,
        data=data,
        method="POST",
        headers={
            "Authorization": "Bearer " + str(bridge.get("token", "")),
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(request_obj, timeout=15) as response:
            body = response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        try:
            parsed = json.loads(body)
        except json.JSONDecodeError:
            parsed = {"ok": False, "status": "failed", "reason_code": f"http_{error.code}", "message": sanitize(body)}
        return parsed if isinstance(parsed, dict) else {"ok": False, "status": "failed", "message": "bridge request failed"}
    except urllib.error.URLError as error:
        return {"ok": False, "status": "failed", "reason_code": "bridge_unavailable", "message": sanitize(str(error.reason))}
    parsed = json.loads(body)
    return parsed if isinstance(parsed, dict) else {"ok": False, "status": "failed", "message": "invalid bridge response"}


def response_to_ai_message(response: dict[str, Any]) -> AIMessage:
    tool_calls: list[dict[str, Any]] = []
    for raw_call in response.get("tool_calls") or []:
        function = raw_call.get("function") or {}
        raw_args = function.get("arguments", {})
        try:
            args = json.loads(raw_args) if isinstance(raw_args, str) else raw_args
        except json.JSONDecodeError as error:
            raise ValueError("provider returned invalid tool arguments") from error
        if not isinstance(args, dict):
            raise ValueError("provider tool arguments must be an object")
        tool_calls.append({
            "name": str(function.get("name", "")),
            "args": args,
            "id": str(raw_call.get("id", "")),
            "type": "tool_call",
        })
    return AIMessage(content=response.get("content") or "", tool_calls=tool_calls)


def serialize_messages(messages: list[AnyMessage]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for message in messages:
        if message.type == "system":
            result.append({"role": "system", "content": str(message.content)})
        elif message.type == "human":
            result.append({"role": "user", "content": str(message.content)})
        elif message.type == "ai":
            item: dict[str, Any] = {"role": "assistant", "content": str(message.content or "")}
            if message.tool_calls:
                item["tool_calls"] = [
                    {
                        "id": str(call.get("id", "")),
                        "type": "function",
                        "function": {
                            "name": str(call.get("name", "")),
                            "arguments": json.dumps(call.get("args") or {}, ensure_ascii=False),
                        },
                    }
                    for call in message.tool_calls
                ]
            result.append(item)
        elif message.type == "tool":
            result.append({
                "role": "tool",
                "tool_call_id": str(message.tool_call_id or ""),
                "content": str(message.content),
            })
    return result


def extract_content(response: dict[str, Any]) -> str:
    content = response.get("content")
    if isinstance(content, str) and content.strip():
        return content.strip()
    raise ValueError("provider returned empty final content")


def parse_decision(raw: str) -> dict[str, Any]:
    cleaned = raw.strip()
    if cleaned.startswith("```"):
        cleaned = re.sub(r"^```(?:json)?\s*|\s*```$", "", cleaned, flags=re.IGNORECASE).strip()
    try:
        value = json.loads(cleaned)
    except json.JSONDecodeError as error:
        raise ValueError("LLM did not return valid final decision JSON") from error
    if not isinstance(value, dict):
        raise ValueError("LLM decision JSON must be an object")
    return value


def finite_number(value: Any, minimum: float, maximum: float) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return minimum
    if number != number or number in (float("inf"), float("-inf")):
        return minimum
    return max(minimum, min(maximum, number))


def normalize_tokens(values: Any, count: int, length: int) -> list[str]:
    if not isinstance(values, list):
        return []
    result: list[str] = []
    seen: set[str] = set()
    for value in values:
        text = limit_text(str(value).replace("\r", " ").replace("\n", " "), length)
        if text and text.lower() not in seen:
            result.append(text)
            seen.add(text.lower())
        if len(result) >= count:
            break
    return result


def limit_text(value: str, length: int) -> str:
    return " ".join(value.split())[:length]


def sanitize(value: str, secret: str = "") -> str:
    clean = value
    if secret:
        clean = clean.replace(secret, "[REDACTED]")
    return limit_text(clean, 500)


def cleanup_pending_graphs() -> None:
    cutoff = time.monotonic() - PENDING_GRAPH_TTL_SECONDS
    with pending_graph_lock:
        expired = [
            token for token, pending in pending_graphs.items()
            if float(pending.get("created_at", 0.0)) < cutoff
        ]
        for token in expired:
            pending_graphs.pop(token, None)


def completed_graph_response(request: dict[str, Any], result: dict[str, Any]) -> dict[str, Any]:
    decision = result.get("decision")
    if not isinstance(decision, dict):
        raise ValueError("completed graph is missing its decision")
    return {
        "requestId": request.get("requestId", ""),
        "contextVersion": request.get("contextVersion", ""),
        "decision": decision,
        "tool_execution": result.get("tool_execution"),
    }


def start_graph_request(request: dict[str, Any]) -> dict[str, Any]:
    cleanup_pending_graphs()
    request_id = str(request.get("requestId", "")).strip()
    checkpointer = InMemorySaver()
    graph = build_graph(request, checkpointer)
    config = {"configurable": {"thread_id": request_id}}
    result = graph.invoke({"request": request}, config=config)
    interruptions = result.get("__interrupt__") or []
    if not interruptions:
        return completed_graph_response(request, result)

    value = getattr(interruptions[0], "value", None)
    if not isinstance(value, dict) or value.get("kind") not in {"move_confirmation", "mine_guard_confirmation", "fishing_confirmation"}:
        raise ValueError("graph returned an unknown interrupt")
    resume_token = secrets.token_urlsafe(32)
    with pending_graph_lock:
        pending_graphs[resume_token] = {
            "created_at": time.monotonic(),
            "request_id": request_id,
            "request": request,
            "graph": graph,
            "config": config,
        }
    confirmation = dict(value)
    confirmation["resume_token"] = resume_token
    return {
        "requestId": request_id,
        "contextVersion": request.get("contextVersion", ""),
        "confirmation": confirmation,
    }


def resume_graph_request(resume_request: dict[str, Any]) -> dict[str, Any]:
    cleanup_pending_graphs()
    if not isinstance(resume_request, dict):
        raise ValueError("resume request must be an object")
    request_id = str(resume_request.get("requestId", "")).strip()
    resume_token = str(resume_request.get("resumeToken", "")).strip()
    approved = resume_request.get("approved")
    if not request_id or not resume_token or not isinstance(approved, bool):
        raise ValueError("requestId, resumeToken, and boolean approved are required")

    with pending_graph_lock:
        pending = pending_graphs.pop(resume_token, None)
    if pending is None or pending.get("request_id") != request_id:
        raise ValueError("move confirmation is missing, expired, or already resolved")

    request = pending["request"]
    graph = pending["graph"]
    result = graph.invoke(
        Command(resume={"approved": approved}),
        config=pending["config"],
    )
    if result.get("__interrupt__"):
        raise ValueError("graph requested an unexpected second confirmation")
    return completed_graph_response(request, result)


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:  # noqa: N802
        if self.path != "/health":
            self.send_error(HTTPStatus.NOT_FOUND)
            return
        self.write_json(HTTPStatus.OK, {"status": "ok", "graph": "conversation-toolnode"})

    def do_POST(self) -> None:  # noqa: N802
        if self.path not in {"/v1/graph/decision", "/v1/graph/confirm"}:
            self.send_error(HTTPStatus.NOT_FOUND)
            return
        try:
            body = self.read_request_body()
            request = json.loads(body.decode("utf-8"))
            result = start_graph_request(request) if self.path == "/v1/graph/decision" \
                else resume_graph_request(request)
            self.write_json(HTTPStatus.OK, result)
        except Exception as error:
            self.write_json(HTTPStatus.BAD_GATEWAY, {"error": sanitize(str(error))})

    def read_request_body(self) -> bytes:
        transfer_encoding = self.headers.get("Transfer-Encoding", "").lower()
        if "chunked" in transfer_encoding:
            chunks: list[bytes] = []
            total = 0
            while True:
                size_line = self.rfile.readline().split(b";", 1)[0].strip()
                if not size_line:
                    raise ValueError("invalid chunked request")
                size = int(size_line, 16)
                if size == 0:
                    while self.rfile.readline() not in (b"\r\n", b"\n", b""):
                        pass
                    break
                total += size
                if total > 2_000_000:
                    raise ValueError("request is too large")
                chunks.append(self.rfile.read(size))
                if self.rfile.read(2) != b"\r\n":
                    raise ValueError("invalid chunk terminator")
            return b"".join(chunks)
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0 or length > 2_000_000:
            raise ValueError("invalid request size")
        return self.rfile.read(length)

    def write_json(self, status: HTTPStatus, value: dict[str, Any]) -> None:
        encoded = json.dumps(value, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

    def log_message(self, format: str, *args: Any) -> None:
        print("[langgraph] " + format % args, flush=True)


def main() -> None:
    host = os.environ.get("VIVANT_LANGGRAPH_HOST", "127.0.0.1")
    port = int(os.environ.get("VIVANT_LANGGRAPH_PORT", "8123"))
    server = ThreadingHTTPServer((host, port), Handler)
    print(f"Vivant Valley LangGraph service listening on http://{host}:{port}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
