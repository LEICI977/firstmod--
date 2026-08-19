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
    system_prompt += "\n\nTool behavior: when a real gift is appropriate, call the provider tool " \
        "give_gift with a candidate_key from allowed_tools. Never claim a gift was " \
        "delivered before the tool result arrives. The tool result is authoritative. " \
        "If the tool fails or is rejected, say so honestly. Do not expose item IDs, " \
        "internal keys, JSON, or control syntax in the visible reply. If no tool is " \
        "needed, return the final JSON schema directly: schema_version, decision, " \
        "reply, and memory_update."
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
    if not isinstance(candidates, list) or not candidates:
        return []
    return [
        {
            "type": "function",
            "function": {
                "name": "give_gift",
                "description": "Deliver one allowlisted gift to the player now, if appropriate.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "candidate_key": {
                            "type": "string",
                            "description": "Opaque key from allowed_tools; never invent one.",
                            "enum": [str(item.get("candidateKey", "")) for item in candidates if isinstance(item, dict)],
                        },
                        "reason_tag": {"type": "string"},
                    },
                    "required": ["candidate_key"],
                    "additionalProperties": False,
                },
            },
        }
    ]


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
    response = call_provider(
        request,
        state["messages"],
        provider_tool_definitions(request["contextSnapshot"]),
    )
    message = response_to_ai_message(response)
    tool_calls = list(message.tool_calls or [])
    if len(tool_calls) > 1:
        raise ValueError("at most one side-effecting tool call is allowed")
    result: GraphState = {"messages": [message]}
    if tool_calls:
        call = tool_calls[0]
        if not str(call.get("id", "")).strip():
            raise ValueError("provider tool call is missing an ID")
        result["tool_call"] = {
            "id": str(call.get("id", "")),
            "name": str(call.get("name", "")),
            "args": call.get("args") or {},
        }
    else:
        result["decision"] = parse_decision(extract_content(response))
    return result


def route_after_choice(state: GraphState) -> str:
    return "tool_node" if state.get("tool_call") else "complete"


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
            "Generate the final NPC reply now. Return exactly one JSON object with "
            "schema_version 1, decision 'reply', reply, and memory_update containing "
            "summary_patch, signal (valence, warmth, concern, confidence), topics, "
            "and open_loops. The reply must be natural dialogue only. Follow the "
            "authoritative game tool result in the conversation and never invent a "
            "successful delivery."
        )
    )
    messages = list(state["messages"])
    messages.append(final_instruction)
    response = call_provider(request, messages, [], json_mode=True)
    return {"decision": parse_decision(extract_content(response))}


def normalize_final_output(state: GraphState) -> GraphState:
    decision = state["decision"]
    if not isinstance(decision, dict):
        raise ValueError("LLM output must be a JSON object")
    tool_call = state.get("tool_call") or {}
    execution = state.get("tool_execution") or {}
    tool_name = str(tool_call.get("name", "none")).strip().lower() if tool_call else "none"
    args = tool_call.get("args") or {}
    candidate_key = args.get("candidate_key") if tool_call else None
    action = {
        "name": tool_name,
        "candidate_key": candidate_key,
        "delivery": "immediate",
        "reason_tag": str(args.get("reason_tag", "")) if tool_call else "",
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
    if tool_call and not execution:
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
        {"tool_node": "tool_node", "complete": "normalize_final_output"},
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
        payload["tool_choice"] = "auto"
    if json_mode:
        payload["response_format"] = {"type": "json_object"}
    max_tokens = max(128, min(int(llm.get("maxOutputTokens", 4096)), 32768))
    if provider == "openai":
        payload["max_completion_tokens"] = max_tokens
    else:
        payload["max_tokens"] = max_tokens
        payload["thinking"] = {"type": "enabled" if llm.get("enableThinking") else "disabled"}
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
        tool_calls.append({
            "name": str(function.get("name", "")),
            "args": args if isinstance(args, dict) else {},
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
