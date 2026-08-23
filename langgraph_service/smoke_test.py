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


def final_tool_call(call_id="call-final", travel_barks=None):
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
                            "travel_barks": travel_barks or [],
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
    personality_instruction = str(request.get("contextSnapshot", {}).get("personality", ""))
    session_facts = request.get("contextSnapshot", {}).get("recentSessionFacts", [])
    if request_call == 1 and personality_instruction:
        assert personality_instruction in str(messages[0].content)
        assert "【本轮行动自主性：优先于工具可用性】" in str(messages[0].content)
        assert "【工具选择前原版人格要求：必须用于决定答不答应】" in str(messages[0].content)
        assert personality_instruction not in str(messages[-2].content)
        assert messages[-1].type == "human"
        assert "【玩家本轮原话】" in str(messages[-1].content)
    if request_call == 1 and session_facts:
        assert "【当前临时共同经历：高优先级游戏事实】" in str(messages[0].content)
        assert str(session_facts[0]) in str(messages[0].content)
    if tool_choice is not None and personality_instruction:
        assert any(
            message.type == "system"
            and "【最终回复原版人格要求】" in str(message.content)
            and personality_instruction in str(message.content)
            for message in messages
        )
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
    if request_id in {"smoke-move", "smoke-move-decline"} and request_call == 1 and "move_to" in tool_names:
        return {
            "content": "",
            "tool_calls": [
                {
                    "id": "call-smoke-move",
                    "type": "function",
                    "function": {
                        "name": "move_to",
                        "arguments": json.dumps({"destination_key": "location:beach"}),
                    },
                }
            ],
        }
    if request_id in {"smoke-mine-guard", "smoke-mine-guard-decline"} and request_call == 1 and "invite_mine_guard" in tool_names:
        return {
            "content": "",
            "tool_calls": [
                {
                    "id": "call-smoke-mine-guard",
                    "type": "function",
                    "function": {
                        "name": "invite_mine_guard",
                        "arguments": "{}",
                    },
                }
            ],
        }
    assert "submit_final_response" in tool_names
    barks = ["The sea air should be nice.", "Keep going, I'm right behind you."] \
        if request_id == "smoke-move" else []
    return final_tool_call("call-final-%d" % provider_calls, barks)


def fake_bridge(_, payload):
    global bridge_calls
    bridge_calls += 1
    result = {
        "requestId": payload["requestId"],
        "toolCallId": payload["toolCallId"],
        "tool": payload["tool"],
        "status": "following" if payload["tool"] == "move_to" else "guarding" if payload["tool"] == "invite_mine_guard" else "completed",
        "ok": True,
        "displayName": "Beach" if payload["tool"] == "move_to" else "Quartz",
        "quantity": 0 if payload["tool"] == "move_to" else 1,
        "message": "Tool executed successfully.",
        "receiptId": "smoke-receipt",
    }
    if "candidateKey" in payload:
        result["candidate_key"] = payload["candidateKey"]
    if "destinationKey" in payload:
        result["destination_key"] = payload["destinationKey"]
    return result


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
            "systemPrompt": "SYSTEM_BASE\nORIGINAL_PERSONALITY_SMOKE",
            "personality": "ORIGINAL_PERSONALITY_SMOKE",
            "recentSessionFacts": [
                "[Y1 spring 1 1200] 临时共同经历：NPC 今天已经和玩家一起到达过海滩。",
            ],
            "playerInput": "hello",
            "allowedTools": [
                {"candidateKey": "abigail_quartz", "displayName": "Quartz"},
            ],
            "allowedMoveDestinations": [],
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

    move_payload = json.loads(json.dumps(payload))
    move_payload["requestId"] = "smoke-move"
    move_payload["contextVersion"] = "ctx-move"
    move_payload["contextSnapshot"]["allowedTools"] = []
    move_payload["contextSnapshot"]["allowedMoveDestinations"] = [
        {"destinationKey": "location:beach", "displayName": "Beach"},
    ]
    calls_before = provider_calls
    bridge_before = bridge_calls
    move_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/decision",
        data=json.dumps(move_payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(move_request) as response:
        move_result = json.loads(response.read().decode("utf-8"))
    assert "decision" not in move_result
    assert move_result["confirmation"]["kind"] == "move_confirmation"
    assert move_result["confirmation"]["destination_key"] == "location:beach"
    assert move_result["confirmation"]["tool_call_id"] == "call-smoke-move"
    assert move_result["confirmation"]["resume_token"]
    assert provider_calls == calls_before + 1, provider_calls
    assert bridge_calls == bridge_before, bridge_calls

    confirm_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/confirm",
        data=json.dumps({
            "requestId": "smoke-move",
            "resumeToken": move_result["confirmation"]["resume_token"],
            "approved": True,
        }).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(confirm_request) as response:
        approved_move_result = json.loads(response.read().decode("utf-8"))
    assert approved_move_result["decision"]["action"]["name"] == "move_to"
    assert approved_move_result["decision"]["action"]["destination_key"] == "location:beach"
    assert approved_move_result["tool_execution"]["status"] == "following"
    assert len(approved_move_result["decision"]["travel_barks"]) == 2
    assert provider_calls == calls_before + 2, provider_calls
    assert bridge_calls == bridge_before + 1, bridge_calls

    decline_payload = json.loads(json.dumps(move_payload))
    decline_payload["requestId"] = "smoke-move-decline"
    decline_payload["contextVersion"] = "ctx-move-decline"
    calls_before = provider_calls
    bridge_before = bridge_calls
    decline_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/decision",
        data=json.dumps(decline_payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(decline_request) as response:
        decline_confirmation = json.loads(response.read().decode("utf-8"))
    assert decline_confirmation["confirmation"]["destination_key"] == "location:beach"
    assert bridge_calls == bridge_before, bridge_calls

    decline_resume_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/confirm",
        data=json.dumps({
            "requestId": "smoke-move-decline",
            "resumeToken": decline_confirmation["confirmation"]["resume_token"],
            "approved": False,
        }).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(decline_resume_request) as response:
        declined_move_result = json.loads(response.read().decode("utf-8"))
    assert declined_move_result["decision"]["action"]["name"] == "move_to"
    assert declined_move_result["decision"]["action"]["destination_key"] == "location:beach"
    assert declined_move_result["tool_execution"]["status"] == "rejected"
    assert declined_move_result["tool_execution"]["reason_code"] == "player_declined"
    assert declined_move_result["decision"]["travel_barks"] == []
    assert provider_calls == calls_before + 2, provider_calls
    assert bridge_calls == bridge_before, bridge_calls

    mine_payload = json.loads(json.dumps(payload))
    mine_payload["requestId"] = "smoke-mine-guard"
    mine_payload["contextVersion"] = "ctx-mine-guard"
    mine_payload["contextSnapshot"]["allowedTools"] = []
    mine_payload["contextSnapshot"]["allowedMoveDestinations"] = []
    mine_payload["contextSnapshot"]["mineGuardAvailable"] = True
    calls_before = provider_calls
    bridge_before = bridge_calls
    mine_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/decision",
        data=json.dumps(mine_payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(mine_request) as response:
        mine_confirmation = json.loads(response.read().decode("utf-8"))
    assert mine_confirmation["confirmation"]["kind"] == "mine_guard_confirmation"
    assert mine_confirmation["confirmation"]["destination_key"] == ""
    assert bridge_calls == bridge_before, bridge_calls

    mine_confirm_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/confirm",
        data=json.dumps({
            "requestId": "smoke-mine-guard",
            "resumeToken": mine_confirmation["confirmation"]["resume_token"],
            "approved": True,
        }).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(mine_confirm_request) as response:
        mine_result = json.loads(response.read().decode("utf-8"))
    assert mine_result["decision"]["action"]["name"] == "invite_mine_guard"
    assert mine_result["tool_execution"]["status"] == "guarding"
    assert mine_result["tool_execution"]["ok"] is True
    assert provider_calls == calls_before + 2, provider_calls
    assert bridge_calls == bridge_before + 1, bridge_calls

    mine_decline_payload = json.loads(json.dumps(mine_payload))
    mine_decline_payload["requestId"] = "smoke-mine-guard-decline"
    mine_decline_payload["contextVersion"] = "ctx-mine-guard-decline"
    calls_before = provider_calls
    bridge_before = bridge_calls
    mine_decline_request = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/decision",
        data=json.dumps(mine_decline_payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(mine_decline_request) as response:
        mine_decline_confirmation = json.loads(response.read().decode("utf-8"))
    mine_decline_resume = urllib.request.Request(
        f"http://127.0.0.1:{port}/v1/graph/confirm",
        data=json.dumps({
            "requestId": "smoke-mine-guard-decline",
            "resumeToken": mine_decline_confirmation["confirmation"]["resume_token"],
            "approved": False,
        }).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(mine_decline_resume) as response:
        mine_declined_result = json.loads(response.read().decode("utf-8"))
    assert mine_declined_result["decision"]["action"]["name"] == "invite_mine_guard"
    assert mine_declined_result["tool_execution"]["status"] == "rejected"
    assert mine_declined_result["tool_execution"]["reason_code"] == "player_declined"
    assert provider_calls == calls_before + 2, provider_calls
    assert bridge_calls == bridge_before, bridge_calls
    print("LangGraph ToolNode smoke test passed.")
finally:
    server.shutdown()
    thread.join(timeout=5)
