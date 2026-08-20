"""Offline contract smoke test for the provider-native ToolNode graph."""

from __future__ import annotations

import json
import threading
import urllib.request
from http.server import ThreadingHTTPServer

import app


provider_calls = 0
bridge_calls = 0
request_call_counts = {}


def final_tool_call(call_id="call-final"):
    return {
        "content": "",
        "tool_calls": [
            {
                "id": call_id,
                "type": "function",
                "function": {
                    "name": "submit_final_response",
                    "arguments": json.dumps(
                        {
                            "schema_version": 1,
                            "decision": "reply",
                            "reply": "SMOKE_OK",
                            "memory_update": {
                                "summary_patch": "",
                                "signal": {
                                    "valence": 0.2,
                                    "warmth": 0.4,
                                    "concern": 0.0,
                                    "confidence": 0.9,
                                },
                                "topics": [],
                                "open_loops": [],
                            },
                        }
                    ),
                },
            }
        ],
    }


def fake_provider(request, messages, tools, json_mode=False, tool_choice=None):
    global provider_calls
    provider_calls += 1
    request_id = request["requestId"]
    request_call_counts[request_id] = request_call_counts.get(request_id, 0) + 1
    request_call = request_call_counts[request_id]
    tool_names = [item.get("function", {}).get("name") for item in tools]
    if request_id == "smoke-auto-text" and tool_choice is None:
        return {"content": "AUTO_TEXT_DRAFT", "tool_calls": []}
    if request_id == "smoke-multiple-tools" and tool_choice is None:
        first = final_tool_call("call-multiple-a")["tool_calls"][0]
        second = final_tool_call("call-multiple-b")["tool_calls"][0]
        return {"content": "", "tool_calls": [first, second]}
    if request_id == "smoke-invalid-args" and tool_choice is None:
        return {
            "content": "",
            "tool_calls": [
                {
                    "id": "call-invalid-args",
                    "type": "function",
                    "function": {
                        "name": "give_gift",
                        "arguments": "{not-valid-json",
                    },
                }
            ],
        }
    if request_id == "smoke-1" and request_call == 1 and "give_gift" in tool_names:
        return {
            "content": "",
            "tool_calls": [
                {
                    "id": "call-smoke-1",
                    "type": "function",
                    "function": {
                        "name": "give_gift",
                        "arguments": json.dumps({"candidate_key": "abigail_quartz", "reason_tag": "smoke"}),
                    },
                }
            ],
        }
    assert "submit_final_response" in tool_names
    return final_tool_call("call-final-%d" % provider_calls)


def fake_bridge(_, payload):
    global bridge_calls
    bridge_calls += 1
    return {
        "requestId": payload["requestId"],
        "toolCallId": payload["toolCallId"],
        "tool": payload["tool"],
        "status": "completed",
        "ok": True,
        "candidate_key": payload["candidateKey"],
        "displayName": "Quartz",
        "quantity": 1,
        "message": "Gift delivered successfully.",
        "receiptId": "smoke-receipt",
    }


app.call_provider = fake_provider
app.call_game_bridge = fake_bridge

server = ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)
thread = threading.Thread(target=server.serve_forever, daemon=True)
thread.start()
try:
    port = server.server_address[1]
    payload = {
        "requestId": "smoke-1",
        "playerId": "1",
        "npcName": "Abigail",
        "day": 1,
        "location": "Town",
        "actionId": "chat-1",
        "contextVersion": "ctx-1",
        "mode": "conversation",
        "contextSnapshot": {
            "contextVersion": "ctx-1",
            "npcName": "Abigail",
            "playerInput": "hello",
            "allowedTools": [
                {"candidateKey": "abigail_quartz", "displayName": "Quartz"},
            ],
        },
        "gameBridge": {"baseUrl": "http://127.0.0.1:8124", "token": "smoke-token"},
        "llm": {
            "provider": "DeepSeek",
            "baseUrl": "https://example.invalid",
            "model": "test",
            "apiKey": "test-key",
        },
    }
    request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/decision",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request) as response:
        result = json.loads(response.read().decode("utf-8"))
    assert result["decision"]["reply"] == "SMOKE_OK"
    assert result["decision"]["action"]["name"] == "give_gift"
    assert result["tool_execution"]["ok"] is True
    assert provider_calls == 2, provider_calls
    assert bridge_calls == 1, bridge_calls

    no_tool_payload = json.loads(json.dumps(payload))
    no_tool_payload["requestId"] = "smoke-no-tool"
    no_tool_payload["contextVersion"] = "ctx-no-tool"
    no_tool_payload["contextSnapshot"]["allowedTools"] = []
    calls_before = provider_calls
    bridge_before = bridge_calls
    no_tool_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/decision",
        data=json.dumps(no_tool_payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(no_tool_request) as response:
        no_tool_result = json.loads(response.read().decode("utf-8"))
    assert no_tool_result["decision"]["action"]["name"] == "none"
    assert provider_calls == calls_before + 1, provider_calls
    assert bridge_calls == bridge_before, bridge_calls

    auto_text_payload = json.loads(json.dumps(payload))
    auto_text_payload["requestId"] = "smoke-auto-text"
    auto_text_payload["contextVersion"] = "ctx-auto-text"
    calls_before = provider_calls
    bridge_before = bridge_calls
    auto_text_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/decision",
        data=json.dumps(auto_text_payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(auto_text_request) as response:
        auto_text_result = json.loads(response.read().decode("utf-8"))
    assert auto_text_result["decision"]["action"]["name"] == "none"
    assert auto_text_result["decision"]["reply"] == "SMOKE_OK"
    assert provider_calls == calls_before + 2, provider_calls
    assert bridge_calls == bridge_before, bridge_calls

    multiple_payload = json.loads(json.dumps(payload))
    multiple_payload["requestId"] = "smoke-multiple-tools"
    multiple_payload["contextVersion"] = "ctx-multiple-tools"
    calls_before = provider_calls
    bridge_before = bridge_calls
    multiple_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/decision",
        data=json.dumps(multiple_payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(multiple_request) as response:
        multiple_result = json.loads(response.read().decode("utf-8"))
    assert multiple_result["decision"]["action"]["name"] == "none"
    assert provider_calls == calls_before + 3, provider_calls
    assert bridge_calls == bridge_before, bridge_calls

    invalid_args_payload = json.loads(json.dumps(payload))
    invalid_args_payload["requestId"] = "smoke-invalid-args"
    invalid_args_payload["contextVersion"] = "ctx-invalid-args"
    calls_before = provider_calls
    bridge_before = bridge_calls
    invalid_args_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/decision",
        data=json.dumps(invalid_args_payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(invalid_args_request) as response:
        invalid_args_result = json.loads(response.read().decode("utf-8"))
    assert invalid_args_result["decision"]["action"]["name"] == "none"
    assert provider_calls == calls_before + 3, provider_calls
    assert bridge_calls == bridge_before, bridge_calls
    print("LangGraph ToolNode smoke test passed.")
finally:
    server.shutdown()
    thread.join(timeout=5)
