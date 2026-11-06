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

## Bridge smoke test (v0.5)

From this folder execute `python -m unittest discover -s . -p "test_*.py"`.
Start the service using `python server.py`. In Unturned (without BattlEye), enter a map and press **F9** once. The C# module makes one nonblocking HTTP POST to `127.0.0.1:8765/v1/decide`.

Expected log lines (game working directory / Unturned2-Line2 / probe.log):
- `AI_BRIDGE_SENT` then `AI_BRIDGE_OK` if service is running;
- `AI_BRIDGE_UNAVAILABLE` if service is down;
- `AI_BRIDGE_REJECTED` if reply is incompatible, invalid, or too old.

This probe uses a fixed agent ID `bridge-probe`, never sends player data, and **does not issue gameplay commands**. It is intentionally manual: press F9 for each request. HTTP access is limited to localhost. It must later be replaced with proper NPC observation serialization and an authoritative action execution bridge.

The Unity build may need a target-framework/reference adjustment depending on the installed game's managed DLL layout. Neither compilation nor in-game execution has been verified here.

## v0.6 — game observations and deterministic policy

F9 now captures **real game-derived data**: the main camera position, orientation, scene name, and a bounded count of nearby colliders. A snapshot is created on Unity's main thread, serialized as JSON, and transferred over HTTP in a background task. Network replies are validated for protocol, identity, tick, action and expiry, then logged. **Nothing is executed in the world.**

The AI Service accepts optional `observation` objects of kind `camera` or `npc`:
- Camera: `scene`, `position`, `forward`, `nearby_colliders` -> always `idle`, reason `diagnostic_only`.
- Synthetic NPC: `position`, `health` (0..1), `threat_visible`, `under_fire`, `has_cover`, `last_known_threat` (null or vector).
- Basic test policy: low health under fire -> `retreat`; under fire with cover -> `seek_cover`; visible threat -> `engage`; remembered threat -> `investigate`; otherwise `idle`.

Try: `python -m unittest discover -s . -p "test_*.py"`. No Python dependencies beyond standard library.

**Limitations:** simulated NPC inputs only. No NPC registry, damage integration, pathfinding, target selection, physics control or game action executor. Tests have not been run on the Steam version of the game; the v0.6 C# DLL still requires compilation/validation. An external decision is only advisory until a server-authoritative, validated action adapter is implemented.
