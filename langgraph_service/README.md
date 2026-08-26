# Vivant Valley LangGraph service

This process owns the LangGraph state machine for a manual conversation. The
first provider pass uses automatic tool choice. A native `submit_final_response`
call completes in one pass; a plain-text response is treated as a no-action
draft and finalized through a forced `submit_final_response` call. A native
`give_gift` call runs through LangGraph's `ToolNode`, which calls the
authenticated SMAPI bridge before the real result is sent back to the provider.
Malformed or multiple tool calls never execute a side effect and safely fall
back to the final response pass. The SMAPI mod remains the authority for game
state, allowlists, validation, side effects, and save data.

## Run locally

```powershell
python -m venv .langgraph-venv
.\.langgraph-venv\Scripts\python.exe -m pip install -r .\langgraph_service\requirements.txt
.\.langgraph-venv\Scripts\python.exe .\langgraph_service\app.py
```

The default endpoint is `http://127.0.0.1:8123`. Override it with
`VIVANT_LANGGRAPH_HOST` and `VIVANT_LANGGRAPH_PORT`.

The game sends the active provider profile, API key, and a short-lived loopback
bridge token with each local request; the service does not read or persist game
saves. The bridge is available only on `127.0.0.1` and is used for the actual
game-side tool execution.

## Controller voice input

The service also exposes `/v1/stt/start`, `/v1/stt/stop`, and
`/v1/stt/cancel`. It records a 16 kHz mono microphone stream with
`sounddevice` and transcribes Mandarin speech through `faster-whisper`. The
model is loaded only on the first transcription, so graph startup remains
fast. The project bundles the offline `faster-whisper-base` model under
`langgraph_service/models/faster-whisper-base`; no model download is attempted
at runtime. Set `VIVANT_WHISPER_MODEL` only when pointing to another local
model directory. `VIVANT_WHISPER_DEVICE` and `VIVANT_WHISPER_COMPUTE_TYPE`
still override the runtime device and quantization.
