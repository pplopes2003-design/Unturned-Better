# Unturned 2 — AI Service (protocol preview)

This directory contains a **standalone local diagnostic service**, not a running NPC brain.

## Run

```powershell
python server.py --port 8765
```

The service binds to `127.0.0.1` only; it does not accept remote connections.

```powershell
curl.exe http://127.0.0.1:8765/health
curl.exe -X POST http://127.0.0.1:8765/v1/decide -H "Content-Type: application/json" -d "{\"protocol\":1,\"agent_id\":\"probe-1\",\"tick\":1}"
```

The `/v1/decide` endpoint **always returns `idle`**. No game bridge, perception, navigation or execution system is connected yet. Requests are length-limited and versioned. This is a contract test for future Unity/Unturned IPC.

The future C# bridge must use async or a bounded worker queue rather than blocking the Unity main thread. Validate `agent_id`, monotonic ticks, timeouts and maximum action age before executing server responses. Server processes and ports should remain opt-in.
