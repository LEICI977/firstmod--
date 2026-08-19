# Vivant Valley LangGraph service

This process owns the LangGraph state machine for a manual conversation. The
provider is called once for a normal reply, or twice when the model emits a
native `give_gift` tool call: the LangGraph `ToolNode` calls the authenticated
SMAPI bridge, then the real tool result is sent back to the provider for the
final reply. The SMAPI mod remains the authority for game state, allowlists,
validation, side effects, and save data.

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
