# Unturned 2 — Development Build readiness (2026-10-24)

Branch: `feature/npc-ai-foundation`

**Status:** Source stabilization complete for the next *local Unity Development Build*. Source was updated through GitHub, with static inspection of preprocessor blocks and braces. **Not compiled or run in Unity**; visual behavior and performance are not yet verified. This is **not** a multiplayer release.

## Included changes and native comparisons

### Building openings and navigation
- Physical doorway/threshold search and narrow near/far waypoint execution retained; a verified threshold now uses a short arrival radius and maintains its tested heading through the opening when the path is still clear.
- Distant *direct* near-side approaches are accepted only after a standing-capsule sweep and continuous ground checks; walls and raised window sills are not through-paths.
- The native ASPFP backend is missing in this public-SDK environment. `MobileNPCGlobalRoutePlanner` supplies a **moving 60m coarse planning horizon**, not a baked whole-map navmesh. It schedules physics-validated same-surface grid edges incrementally, retaining native ladder / stair / narrow-doorway handlers.
- A badly stuck actor gives the global pathfinder at most a brief 5-second priority window before falling back to local routes. Method delegates are cached per actor to avoid per-frame GC.
- `MobileNPCGlobalRoutePlanner.cs.meta` included to provide a stable Unity import ID.

### Weapons, visuals and firing
- Verified `PlayerEquipment` third-person model parent and +90° Z local rotation on the right-hand hook; also verified `UseableGun` uses `ItemGunAsset.hasAuto`.
- Native equipment `Idle` animation name collided with ordinary NPC locomotion `Idle`. Every native weapon clip now has its own per-item animation state alias; cleanup removes only item states, preserving movement and melee clips.
- Auto-capable weapons fire context-dependent **4–7 shot trigger holds** (capped by ammo) respecting the native rounds-per-second and pause between holds. Bolt/pump weapons cannot enter automatic mode.
- Native rifle posture and muzzle model versus server ballistic origin still require actual visual inspection; do not silently move ballistic hitscan origin onto a model pivot.

### Falls, zombies and hordes
- Fall recovery now chooses supports using `RayMasks.BLOCK_COLLISION` intersected with the configured ground mask, matching `PlayerMovement.checkGround`. Nav-only surfaces and furniture/vehicles cannot serve as rescue anchors.
- Sprinter health is 1.5× before any authored difficulty override; native `seeker.Speed` and off-nav detour now both use 7.4 normally / 6.6 with `Slow_Movement`. Other volatile variants retain their native settings.
- Clustered native-slot horde spawning preflights up to 20 (normal) or 30 (full moon) valid positions, respecting 3m separation and the 40m cluster radius. The old preflight always stopped at 10 / 20 and thereby prevented variable-sized hordes. At most 12 spatial anchors are sampled during each periodic event.
- Dynamic world changes (players entering, revived zombie blocking a point, safezones, region deactivation) may still cause a smaller real horde than the scheduled size. No new network IDs or forced teleport spawning.

## Build and test order

1. **Import and compilation:** Open updated branch in Unity. Check *first C# error*, not subsequent cascades; make sure the new planner's .meta resolves and the runtime Assembly-CSharp compiles.
2. **One NPC, open street:** Walk 50–100m to an ordinary goal, without getting stuck. `[MobileNPCGlobalRoute]` should identify staged verified routes when the native backend is absent.
3. **One NPC, building exterior:** Set a goal inside a house with a real accessible opening. Look for `[MobileNPCGeometryThreshold]`, then `[MobileNPCThresholdTraverse]` showing progression across approach and beyond waypoints.
4. **Solid wall / window sill:** Should not walk through solid geometry. Dynamic blockers should invalidate stale segments without freezing indefinitely.
5. **Two-floor room:** Must find real stairs/ladder when available, or report failure and choose another task. Coarse global search may not synthesize level changes.
6. **Weapon pose:** Equip a rifle (e.g. Maplestrike) and compare both arms, stock, muzzle and third-person facing with a real player. Check `[MobileNPCWeaponMount]`, `[MobileNPCWeaponPose]`, `[MobileNPCMuzzleAudit]`.
7. **Automatic fire:** Engage at a clear, safe 10–25m range with an automatic-capable firearm and enough ammo. Check `[MobileNPCFireMode]` and `[MobileNPCBurst]`; count actual shots, magazine change and rate.
8. **Grounding:** Walk on sloped terrain, building foundation and stairs. No repeated `FALL DIAGNOSTIC` or emergency repositioning onto Nav-only mesh.
9. **Sprinter:** Compare native path chase speeds with ordinary zombie; confirm greater health. Respect native difficulty overrides.
10. **Horde:** Wait for the 180s event with sufficient dead slots, players inside an eligible region and valid spawnpoints. Check `[ZombieHordeSpawn]`. Normal request range 10–20; full moon 20–30. Confirm actual spawns and frame cost. Event may legitimately skip where capacity is insufficient.
11. **Crowd performance:** Repeat navigation/fire/horde test with 10, 20 and 40 NPCs while collecting Unity Profiler CPU/GC and `Player.log`.

## Key uncertainties
- Static checks on source code are **not** a C# compiler or Unity runtime execution. Resolve compilation errors first.
- The native A* third-party DLL is not restored by this work. Macro routing remains same-surface and bounded. Navigation quality on different maps/complex multi-floor buildings still needs evaluation.
- Weapon animation aliases fix a definite state-name collision. Whether every native gun prefab's rig clips physically raise the hands is still visual-test dependent.
- Blood Moon has a world-state prerequisite; not every map/region has enough authored spawnpoints for 30 nearby zombies.
- **Host from in-game menu multiplayer is not implemented yet**. The validated config model does not start a server or integrate a network stack; do not test it as a complete feature.

## Logs to return
- On Unity compilation error: the first actual C# error from Console / Editor.log.
- On successful build: the newest `Player.log` after one complete session; preferably short videos or screenshots for weapon orientation and doorway behavior.
- Any `[MobileNPCGlobalRoute]`, `[MobileNPCThresholdTraverse]`, `[MobileNPCWeaponPose]`, `[MobileNPCWeaponMount]`, `[MobileNPCFireMode]`, `[MobileNPCBurst]`, `[ZombieHordeSpawn]`, and `FALL DIAGNOSTIC` lines.
