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
import urllib.error
import urllib.request
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Annotated, TypedDict

from langchain_core.messages import AIMessage, AnyMessage, HumanMessage, SystemMessage
from langchain_core.tools import InjectedToolCallId, StructuredTool
from langgraph.graph import END, START, StateGraph
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode


class GraphState(TypedDict, total=False):
    request: dict[str, Any]
    normalized: dict[str, Any]
    messages: Annotated[list[AnyMessage], add_messages]
    tool_call: dict[str, Any]
    tool_execution: dict[str, Any]
    decision: dict[str, Any]


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
    if allowed_tools:
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
    system_prompt += "\n\n【工具协议（只约束游戏事实）】\n" \
        "- 只有在当前对话存在真实情感联结、候选礼物与上下文高度相关且不是应付玩家索要时，才考虑调用 give_gift。\n" \
        "- 礼物必须从 allowed_tools 的候选中选择；参考候选的 displayName 和 displayHint，不要编造 candidate_key。\n" \
        "- 玩家单纯索要物品不能触发送礼；宁可不送，也不要送无关或尴尬的礼物。\n" \
        "- 决定送礼时先调用 give_gift，等待真实工具结果后再调用 submit_final_response；工具失败或拒绝时必须诚实反映。\n" \
        "- 没有成功调用 give_gift 时，不得声称礼物已经交付，也不得承诺下次或改天送礼。\n" \
        "- 可见回复不得暴露 candidate_key、物品 ID、JSON、工具名或控制语法。\n" \
        "- 以上规则只说明游戏中实际发生的事实，不改变 NPC 的身份、专属人格、语气、价值观或与玩家的关系；最终回复必须仍像该 NPC 亲口说出。"
    user_payload = {
        "npc": {
            "name": snapshot.get("npcName"),
            "display_name": snapshot.get("npcDisplayName"),
            "identity": snapshot.get("identity", ""),
            "personality": snapshot.get("personality", ""),
        },
        "mood": snapshot.get("mood", ""),
        "relationship": snapshot.get("relationship", ""),
        "goal": snapshot.get("goal", ""),
        "world_state": snapshot.get("worldState", ""),
        "player_progress": snapshot.get("playerProgress", ""),
        "player_input": snapshot.get("playerInput", ""),
        "memory_summary": snapshot.get("memorySummary", ""),
        "recent_messages": snapshot.get("recentMessages", []),
        "narrative_context": snapshot.get("narrativeContext", ""),
        "activity_summary": snapshot.get("activitySummary", ""),
        "allowed_tools": snapshot.get("allowedTools", []),
        "day": request.get("day"),
        "location": request.get("location"),
    }
    return [
        SystemMessage(content=system_prompt),
        HumanMessage(content=json.dumps(user_payload, ensure_ascii=False)),
    ]


def provider_tool_definitions(snapshot: dict[str, Any]) -> list[dict[str, Any]]:
    candidates = snapshot.get("allowedTools") or []
    definitions: list[dict[str, Any]] = []
    if isinstance(candidates, list) and candidates:
        definitions.append({
            "type": "function",
            "function": {
                "name": "give_gift",
                "description": "当满足所有送礼条件时，向玩家当面交付一份预先筛选的礼物。调用此工具前，必须确认：1) 对话有真实情感联结 2) 礼物与上下文高度相关 3) 不是应付玩家索要。",
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
                    "memory_update": {
                        "type": "object",
                        "description": "记忆更新对象",
                        "properties": {
                            "summary_patch": {"type": "string", "description": "本轮对话要点摘要"},
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
                "required": ["schema_version", "decision", "reply", "memory_update"],
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

    return [
        StructuredTool.from_function(
            func=give_gift,
            name="give_gift",
            description="Deliver one allowlisted gift through the real SMAPI game bridge.",
        )
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
            if tool_name not in {"give_gift", "submit_final_response"}:
                raise ValueError(f"provider returned unknown conversation tool: {tool_name}")
            args = call.get("args") or {}
            if tool_name == "submit_final_response":
                validate_final_response_args(args)
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
                            "工具选择协议连续无效，因此本轮不执行任何礼物或其他副作用。"
                            "下一步只生成符合 NPC 人格的最终对话和记忆更新。"
                        )
                    )]
                }
            messages = list(state["messages"])
            messages.append(HumanMessage(
                content=(
                    "协议纠正：如果本轮不需要送礼，可以直接输出自然的 NPC 对话文本，"
                    "也可以调用一次 submit_final_response。只有确实送礼时才调用一次 give_gift。"
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
    if tool_name == "submit_final_response":
        return "complete"
    raise ValueError("conversation tool call is missing a valid route")


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
            "现在通过调用 submit_final_response 生成 NPC 的最终回复。"
            "使用 schema_version 1 和 decision 'reply'。继续严格遵循 SystemPrompt 中"
            "当前 NPC 的专属人格、说话方式、价值观和关系边界；工具结果只约束实际发生的"
            "游戏事实，不改变角色如何表达。回复必须是自然的第一人称对话文本，"
            "遵循权威的工具执行结果，绝不要编造成功的交付或暴露工具协议。"
        )
    )
    messages = list(state["messages"])
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
            messages = list(state["messages"])
            messages.append(HumanMessage(
                content=(
                    "协议纠正：现在必须调用 submit_final_response。所有字段"
                    "都是必需的，schema_version 必须是整数 1，decision 必须是"
                    "reply，所有 signal 值必须是数字。"
                )
            ))
    raise ValueError(f"provider final response failed: {last_error}")


def validate_final_response_args(value: Any) -> None:
    if not isinstance(value, dict):
        raise ValueError("submit_final_response arguments must be an object")
    if value.get("schema_version") != 1:
        raise ValueError("submit_final_response schema_version must be integer 1")
    if value.get("decision") != "reply":
        raise ValueError("submit_final_response decision must be reply")
    if not isinstance(value.get("reply"), str) or not value["reply"].strip():
        raise ValueError("submit_final_response reply must be a non-empty string")
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


def normalize_final_output(state: GraphState) -> GraphState:
    decision = state["decision"]
    if not isinstance(decision, dict):
        raise ValueError("LLM output must be a JSON object")
    tool_call = state.get("tool_call") or {}
    execution = state.get("tool_execution") or {}
    tool_name = str(tool_call.get("name", "none")).strip().lower() if tool_call else "none"
    args = tool_call.get("args") or {}
    candidate_key = args.get("candidate_key") if tool_name == "give_gift" else None
    action = {
        "name": tool_name if tool_name == "give_gift" else "none",
        "candidate_key": candidate_key,
        "delivery": "immediate",
        "reason_tag": str(args.get("reason_tag", "")) if tool_name == "give_gift" else "",
    }
    normalized = {
        "schema_version": int(decision.get("schema_version", 1)),
        "decision": str(decision.get("decision", "reply")).strip().lower(),
        "action": action,
        "reply": str(decision.get("reply", "")).strip(),
        "memory_update": normalize_memory_update(decision.get("memory_update")),
    }
    if normalized["schema_version"] != 1:
        raise ValueError("unsupported schema_version")
    if normalized["decision"] != "reply":
        raise ValueError("decision must be reply")
    if normalized["action"]["name"] not in {"none", "give_gift", "mail_gift"}:
        raise ValueError("unknown action name")
    if normalized["action"]["name"] != "none" and not normalized["action"]["candidate_key"]:
        raise ValueError("tool action requires candidate_key")
    if not normalized["reply"]:
        raise ValueError("reply is empty")
    if tool_name == "give_gift" and not execution:
        raise ValueError("tool call is missing execution result")
    return {"decision": normalized}


def normalize_memory_update(value: Any) -> dict[str, Any]:
    value = value if isinstance(value, dict) else {}
    signal = value.get("signal") if isinstance(value.get("signal"), dict) else {}
    return {
        "summary_patch": limit_text(str(value.get("summary_patch", "")), 1800),
        "signal": {
            "valence": finite_number(signal.get("valence", 0.0), -1.0, 1.0),
            "warmth": finite_number(signal.get("warmth", 0.0), 0.0, 1.0),
            "concern": finite_number(signal.get("concern", 0.0), 0.0, 1.0),
            "confidence": finite_number(signal.get("confidence", 0.0), 0.0, 1.0),
        },
        "topics": normalize_tokens(value.get("topics"), 8, 64),
        "open_loops": normalize_tokens(value.get("open_loops"), 6, 96),
    }


def build_graph(request: dict[str, Any]):
    tools = make_tools(request)
    graph = StateGraph(GraphState)
    graph.add_node("normalize_request", normalize_request)
    graph.add_node("choose_action", choose_action)
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
            "finalize": "finalize",
            "complete": "normalize_final_output",
        },
    )
    graph.add_edge("tool_node", "capture_tool_result")
    graph.add_edge("capture_tool_result", "finalize")
    graph.add_edge("finalize", "normalize_final_output")
    graph.add_edge("normalize_final_output", END)
    return graph.compile()


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
    max_tokens = max(128, min(int(llm.get("maxOutputTokens", 4096)), 32768))
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


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:  # noqa: N802
        if self.path != "/health":
            self.send_error(HTTPStatus.NOT_FOUND)
            return
        self.write_json(HTTPStatus.OK, {"status": "ok", "graph": "conversation-toolnode"})

    def do_POST(self) -> None:  # noqa: N802
        if self.path != "/v1/graph/decision":
            self.send_error(HTTPStatus.NOT_FOUND)
            return
        try:
            body = self.read_request_body()
            request = json.loads(body.decode("utf-8"))
            result = build_graph(request).invoke({"request": request})
            self.write_json(
                HTTPStatus.OK,
                {
                    "requestId": request.get("requestId", ""),
                    "contextVersion": request.get("contextVersion", ""),
                    "decision": result["decision"],
                    "tool_execution": result.get("tool_execution"),
                },
            )
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
