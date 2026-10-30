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
