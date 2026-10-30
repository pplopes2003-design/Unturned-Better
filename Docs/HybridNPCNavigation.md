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
