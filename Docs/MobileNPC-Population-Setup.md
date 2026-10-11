# Mobile NPC population director — setup and limits

Branch: `feature/npc-ai-foundation`

## What this step adds

`MobileNPCPopulationDirector` is an opt-in, map-agnostic population loop for a dedicated mobile humanoid prefab. When attached to an active runtime GameObject and configured, it periodically looks for a living player and asks `MobileNPCSpawnService` to create one NPC in a randomized ring around that player.

It is intentionally bounded:
- checks only at the configured spawn interval, not every frame;
- respects a maximum count from `MobileNPCRegistry`;
- samples a bounded number of candidate positions per attempt;
- delegates ground, slope, map-boundary, collider-occupancy and spacing checks to the spawn service;
- does nothing on clients or while no valid prefab is assigned.

## Required Unity setup

1. Create a dedicated humanoid prefab. Do **not** use `NPC_Server`, `NPC_Client`, a static dialogue NPC, a sentry, or a zombie prefab.
2. On the prefab root, add `CharacterController`, `MobileNPCBrainDriver`, `MobileNPCHumanoidMotor`, `MobileNPCHumanoidPerception`, and `MobileNPCHumanoidDisposition`. Add vitals, aim, and one or both combat components as needed.
3. Add `MobileNPCPopulationDirector` to a runtime bootstrap GameObject that is active while the map is loaded, and assign the dedicated prefab.
4. Set `Ground Mask` to layers that represent walkable terrain and structures for the chosen map. A broad mask can hit unsuitable colliders, so verify this in each map.
5. Start a local single-player session and confirm the Console reports no spawn errors. Tune count, interval, spawn distance and slope after testing.

## Current boundaries

- This component does not create its own bootstrap GameObject and does not auto-attach itself to every map. The game/mod integration must instantiate or attach the director and assign the prefab.
- Population is transient. It does not save NPC identity, health, faction, inventory, home, or world state across a save/reload. Dead bodies spawned by this director are cleaned up after the configured lifetime; living NPCs are not saved or restored.
- The NPC instances are server-local. This step does not add client replication, a persistent world entity protocol, or multiplayer support.
- Spawned NPCs are placed around the nearest valid living player; there is not yet a map-wide region sampler, territory ownership, resource gathering, or base building.
- If no safe candidate is found, the attempt is skipped and retried only after the next interval.
- The code has not been compiled or run in Unity/Unturned in this environment. Treat it as an implementation step requiring editor validation, not as a confirmed in-game feature.

## Single-player smoke test

- Start a disposable map with the configured prefab and director active.
- Confirm the first NPC appears outside immediate player range after one interval.
- Confirm NPC count never exceeds the configured maximum.
- Place a wall or steep slope in a candidate area and verify unsafe placements are rejected.
- Disable the prefab or population toggle and confirm no additional NPCs spawn.
- Restart the save and confirm current limitation: population is recreated by the director, not restored from saved NPC state.
