# Hybrid NPC navigation — experimental branch

This code is an **unwired navigation prototype**, NOT a tested fix for NPCs in a playable build.

## New components
- `HybridNPCGridPlanner.TryFindPath`: bounded 3D-aware local A* based on ground raycasts, collision capsule clearance, slopes, step limits and diagonal anti-corner-cutting.
- `HybridNPCRegionNavigator`: graph of explicitly registered regions linked by verified geometric portals. A portal is a traversable opening (with or without an interactive door). It concatenates local paths with transitions, rejecting large detours through walls.

## What the repository inspection established
- `UnturnedPathfinding.Initialize` uses `UnturnedPathfinding_ASPFP` only under `WITH_ASPFP`, otherwise `UnturnedPathfinding_Empty`.
- `LevelNavigation.tryGetNavigation` can return inclusion in navigation bounds rather than proof of actual path connectivity.
- In the inspected `main` tree only `MobileNPCHumanoidVitals.cs` was present under `Unturned/NPCs`. References to `MobileNPCBrainDriver`, `MobileNPCHumanoidMotor` and related modules exist, but their definitions were not found in that tree. These are required before integration can be built and tested.

## Required integration work
1. Synchronize the actual current MobileNPC runtime and prefab implementation.
2. Create an object/terrain geometry scanner that generates regions and **discovers doorways and building openings**. AddPortal alone does not discover them.
3. Exclude the NPC's own CharacterController/colliders and other inappropriate layers from `collisionMask`; set terrain/building layers explicitly. Review physics raycast direction/surface behavior for multilevel structures.
4. Connect the AI destination selector to the region navigator and feed validated waypoints to the existing humanoid motor. The motor must handle climbing, falling, animation, speed and turn control.
5. Add path request scheduling, cancellation, failure recovery, blocked-edge cooldowns and shared/cacheable region graphs before scaling to dozens of agents. Both prototypes currently run synchronously.
6. Validate in Unity Editor and Development Player with 100+ doorway/ramp traversal tests, profile cost with 20–30 agents, and log chosen portal sequence on failures.

## Example (after region discovery and runtime integration)

```csharp
var settings = HybridNPCGridPlanner.Settings.Default;
settings.collisionMask = walkableWorldGeometryMask;
var navigator = new HybridNPCRegionNavigator();
navigator.AddRegion(exteriorId, exteriorBounds);
navigator.AddRegion(interiorId, interiorBounds);
navigator.AddPortal(exteriorId, interiorId, outsideDoorPoint, insideDoorPoint);
var waypoints = new List<Vector3>();
bool found = navigator.TryRoute(agentFeet, lootFeet, exteriorId, interiorId,
    settings, waypoints);
// If found: pass waypoints to the real humanoid motor; don't teleport the NPC.
```

Do not merge this branch into main until runtime references resolve and Unity tests pass.

## Added in the second iteration
- `HybridNPCPortalProbe.FindOpenings`: samples rectangle bounds to suggest doorless geometric openings using ground rays and humanoid capsule checks. Candidate discovery **does not** establish that the opening is connected to the global graph. Sampling can miss narrow openings, sloped thresholds, rotated/irregular structures and upper floors; automatically created links require explicit validation and appropriate local region bounds.
- `HybridNPCPathFollower`: motor-agnostic waypoint tracker with progress/stall status. It deliberately does not modify agent physics or AI transitions.

## Important limits
- No C# compilation or Unity Development Player was executed by the GitHub connector.
- The probe assumes axis-aligned rectangular bounds for each region. It is not a robust building-shape detector and must not be described as complete automatic portal recognition.
- The original planner's default physics mask is only a placeholder. Set static world collision layers to exclude NPC colliders, triggers and unwanted surfaces.
- A successful path test does not guarantee animation, weapon alignment, collision-free motor execution or multiplayer replication.

## Dedicated machine-learning-ready diagnostics (third iteration)
- `HybridNPCNavigationTelemetry`: opt-in JSON Lines events for movement, routes, portals, faults and recovery outcomes. Writes into `Application.persistentDataPath/MobileNPC/NavigationReports/` using `nav_<session>_<sequence>.jsonl`. Creates directory lazily, batches writes, rotates by size and prunes older files. Logging is off by default.
- `HybridNPCNavigationSession`: wrapper for route planning, waypoint status, stalled-event emission and reporting recovery outcomes. It needs to be instantiated and called by the actual MobileNPC runtime; not automatically connected.
- `Tools/analyze_npc_navigation.py`: offline standard-library script to group recurring portal failures, route issues, and recovery outcomes.

Example offline analysis:
```shell
python Tools/analyze_npc_navigation.py "<Unity persistentDataPath>/MobileNPC/NavigationReports" --output npc_navigation_report.md
```

**Important:** This is a telemetry/reporting foundation that enables later offline supervised learning, not a trained model, automatic online learning, or an in-game training interface. To train any model, reviewed labels, a replay/test dataset, policy selection and validation are needed. User feedback can be encoded as approved route/portal overrides, rather than blindly mutating navigation while the game runs.

**Activation:** Add the telemetry component to an appropriate persistent Unity object and call `SetEnabled(true)` with a future debug setting; feed events from the real NPC motor and portal traversal; create one `HybridNPCNavigationSession` per NPC. Keep write calls on the Unity main thread. No integration with the missing MobileNPC classes has yet been performed.

**Privacy and performance:** Event fields do not include chat, SteamID or IP address; in-world positions and map identity are recorded. Logs require the user's opt-in, use bounded rotation, and should not be enabled in routine releases until performance is validated.

## Pre-build gate (2026-10-30)
Run at repository root:
```sh
python Tools/preflight_mobile_npc.py
```
Exit 2 means missing NPC runtime sources; resolve these before attempting a MobileNPC validation build. The current GitHub branch tree does not show definitions for several types required by `MobileNPCHumanoidVitals`. Do not introduce no-op classes solely to silence C# errors: that would conceal broken combat, movement and injury behavior.

The new navigation code is a prototype and is not connected to the actual NPC brain/motor. Before the next functional build:
1. Restore the current implementation of MobileNPCBrainDriver, MobileNPCHumanoidMotor, MobileNPCInjurySystem, perception, combat, aim and medical behavior to the source tree.
2. Run preflight again and fix missing references.
3. Compile scripts in Unity 2022.3.62f3, resolve editor errors, ensure prefabs have components correctly wired.
4. Instrument NPC spawn, path planning, motor movement, portal attempts and recovery with HybridNPCNavigationTelemetry (opt-in).
5. Run a development build on Washington and capture Player.log plus nav JSONL after a doorway/ramp test.
6. Keep main unchanged until the branch passes an actual editor and player test.

The source-only preflight is not equivalent to a Unity compile or successful runtime navigation test.

## Motor integration milestone — 2026-10-30

Commits `7121350` and `2703576` connect `HybridNPCGridPlanner` to the **existing** `MobileNPCHumanoidMotor` as conservative, budgeted local fallback when no native/global/threshold/ladder route is active. Existing movement and special traversal code remains authoritative. The fallback does not yet auto-populate `HybridNPCRegionNavigator` from discovered buildings; only local geometry-aware fallback is wired.

New per-motor serialized flags:
- `Enable Hybrid Recovery`: defaults ON. Planner bounded to 24m, 850 expansions and a shared 0.35s scheduling slot. Uses motor obstacleMask and CharacterController dimensions. This is a test default; profile CPU impact.
- `Enable Navigation Reports`: defaults OFF. To capture telemetry in test builds, enable this on the MobileNPCHumanoidMotor instance/template in the Unity Inspector *before* initialization. Log output: Application.persistentDataPath/MobileNPC/NavigationReports/nav_*.jsonl.

When a hybrid plan is accepted, the motor feeds path waypoints to its existing route follower and separately tracks a watchdog. On a stall, logs a diagnostic event, clears the failing route, and delays retry for 5s. Runs only on server authority.

**Test status:** GitHub source committed only. No Unity compilation or player build was executed. Potential conflicts with existing recovery behavior and physics masks must be validated in Washington. The region portal graph and opening probe remain experimental and not auto-wired; do not assert universal interior navigation is solved.

## Verified doorway discovery wired — build candidate
- `MobileNPCHumanoidMotor` now calls `HybridNPCObservedPortalRegistry.Remember` only **after** its existing facade scanner accepted a real opening (ground, capsule and reachable-approach checks). Successful discoveries are optionally recorded by the JSONL telemetry service.
- `TryHybridRecovery` now attempts a bounded path via one recently observed crossing, revalidating all three legs with geometry-aware local A*, before reverting to the direct local fallback.
- `MobileNPCPopulationBootstrap.OnPostLevelLoaded` clears cached passages across maps. Bounded to 64 observations, 180s TTL. No persistence or sharing as personal NPC knowledge.
- This does not yet build complete multistorey regional graphs: the regional graph/probe modules remain optional research prototypes. Native ladder, stair, existing threshold traversal and global route planner remain in use.
- Changes were committed in GitHub, **not compiled or runtime-tested**. For next Development Build first inspect Unity Console; test one facade/doorway, no-crossing through walls, stairs, and analyze Player.log + JSONL (if report flag enabled). If there are C# errors, report the first compiler diagnostic rather than assuming behavior is ready.

## Follow-up after Player(9).log / reported clustered command spawns

- `MobileNPCPopulationDirector.TrySpawnNPCNearPlayer` now samples 32 sites across four offset rings (7–14.5m) rather than the same eight positions after repeated chat spawns. The existing server-side `IsSpawnCapsuleClear` remains the final placement guard. When all sites are blocked, a spawn is refused instead of knowingly overlapping.
- `MobileNPCHumanoidMotor.TryHybridRecovery` excludes the actor's Agent layer from *static route planning* to avoid mapping temporary NPC crowds as walls. Normal CharacterController collision remains active; this is NOT phasing through other characters.
- Navigation reports default enabled in the motor for the next diagnostic build (disable explicitly before a 40-NPC performance run). Output under `Application.persistentDataPath/MobileNPC/NavigationReports`.
- The new hybrid system is a local on-demand geometry-derived grid, not a baked full-map navmesh. Existing doorway, ladder, multi-floor and global planning code is preserved. Real traversal quality is UNVERIFIED pending a Unity build; no claim of solved interior navigation.
