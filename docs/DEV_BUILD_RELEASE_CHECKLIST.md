# Development Build — validation gate (2026-10-24)

Target branch: `feature/npc-ai-foundation`. This is a **test candidate, not a verified release**.

## Implemented and reviewed in repository
- NPC opening geometry: near/far physically checked aperture route; tighter physical arrival distance; actual traversal logging.
- Native ASPFP unavailable: fallback coarse same-floor global-horizon search using real collision and continuous walkable floor, shared frame budget, goal-change resets and rejecting non-progressing frontiers. Exact local doorway/stair/ladder routes still apply.
- NPC firearms: right-hand native model hook +90° Z, author-provided aim hold prioritized, procedural shoulder fallback when not effective; real gun auto flag aliases, ammunition, fire rate, visibility and squad-safe lane respected. Development logs are now emitted *after* Bolt/Pump restrictions.
- Zombie hordes: incremental native revive-based event, min-size local capacity precheck, 40m spatial clustering, normal 10–20 and Blood Moon 20–30 requests, region population uplift where valid spawnpoints exist.
- Integrated hosting: validated configuration **model only**. There is **no Host Game menu button or functional server launcher** in this build.

## Still requiring Unity validation
No Unity editor/compiler, live NPC prefab, graphics, licensed native ASPFP backend or local Windows runtime is available through this GitHub-only editing connection. Syntax/structure review does **not** prove compilation or gameplay success. No FPS/performance claims can be made.

## Run this build
1. Sync branch, open Unity and resolve the **first** compile error before anything else. If newly added script source files lack Unity-generated `.meta`, let the Unity Editor import them (do not delete pre-existing GUIDs).
2. Development Build + Script Debugging; reproduce using one NPC, then 10–20 to measure frame time.
3. Test an NPC outdoors approaching a building with an actual doorless gap and a different solid window. Verify `MobileNPCGeometryThreshold` **followed by** `MobileNPCThresholdTraverse`; no wall ghosts or endless side-to-side movement.
4. Test a 20–35m goal around a building: `MobileNPCGlobalRoute` staged waypoints, actual progression, no repeated short-hop loops; test a changed target during the pending search.
5. Test equip, move, aim and firing with Maplestrike or Zubeknakov and a semi-auto firearm. Look for `MobileNPCWeaponMount`, `MobileNPCWeaponPose`, `MobileNPCFireMode`, `MobileNPCBurst`, `MobileNPCMuzzleAudit`. Ensure the visual model really points forward and audible firing respects real ammo/cadence.
6. Test NPC falling through floor by slopes, indoor thresholds, staircases. Inspect `FALL DIAGNOSTIC` and ground recovery; watch for Nav-only collision layers.
7. Observe a normal horde event and a Blood Moon event only in high-spawnpoint-capacity regions. Insufficient points or occupancy may still prevent exact horde size. Record zombie count and server performance.
8. Exit normally and attach the fresh `Player.log` and any Unity Console errors here.

## Known constraints / blockers
- Native global ASPFP is absent from the observed runtime. The added coarse planner is a bounded fallback, not guaranteed full-map pathfinding or a vertically connected global navmesh.
- Firearm shoulder poses and item model angles are unverified on actual NPC rigs.
- Automatic bursts depend on the individual asset, visibility, target, ammo and distance; `MobileNPCFireMode` now reports the *effective* automatic capability.
- Hosting from the Unturned menu is not implemented yet.
- No automated Windows Unity compilation, rendering, multiplayer synchronization or load test has been run.

## Decision gate
- **Compile errors:** stop gameplay validation and share first Console error + `Editor.log`.
- **Compiles but navigation or aiming fails:** share `Player.log`, map name, spawned NPC type and firearm ID.
- **Gameplay succeeds:** run 10/20 NPC load tests, then begin separate multiplayer-host runtime integration.
