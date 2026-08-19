"""Offline contract smoke test for the provider-native ToolNode graph."""

from __future__ import annotations

import json
import threading
import urllib.request
from http.server import ThreadingHTTPServer

import app


provider_calls = 0
bridge_calls = 0


def fake_provider(request, messages, tools, json_mode=False):
    global provider_calls
    provider_calls += 1
    if tools and provider_calls == 1:
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
    return {
        "content": json.dumps(
            {
                "schema_version": 1,
                "decision": "reply",
                "reply": "SMOKE_OK",
                "memory_update": {"summary_patch": "", "signal": {}, "topics": [], "open_loops": []},
            }
        ),
    }


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
    print("LangGraph ToolNode smoke test passed.")
finally:
    server.shutdown()
    thread.join(timeout=5)
