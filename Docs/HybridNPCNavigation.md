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

## Combat Total telemetry — Player(10) and 11 JSONL files (2026-10-30)
The submitted files contain 32 hybrid route events, **zero successful paths**, nine 'verified_facade_opening' events, and zero hybrid movement samples. The old global route planner logs repeated 'No connected corridor' errors and the opening scanner also sometimes reports 'No traversable opening'. This verifies a failure between recognizing openings and producing executable routes; it does not demonstrate that ALL openings are absent. Some 0.02-0.04 ms hybrid failures suggest rejection at the initial validation stage; actual reason was previously omitted.

**Diagnostic code correction:** `HybridNPCGridPlanner.LastFailureReason` now distinguishes `invalid_configuration`, `destination_outside_local_radius`, `start_ground_not_found`, `start_capsule_blocked`, `node_budget_exhausted`, and `no_reachable_corridor`. The motor writes the specific reason to JSONL instead of undifferentiated `hybrid_local_failed`. This is needed to safely choose a physics/mesh fix; not a claim that traversal is fixed.

Scope: user tested **Combat Total only**; do not extrapolate results to Normal mode. Unity rebuild and in-game validation outstanding. Next fix should follow the dominant specific failure reason, and test one NPC standing outside a known unobstructed doorway.

## NPC-only independent routing switchover (2026-10-30)

NPC humanoid motor now instantiates `MobileNPCIndependentRouteProvider` instead of `MobileNPCNativeAStarRouteProvider`. The independent provider uses locally sampled physical A*, the observed aperture registry, a bounded 30 m request horizon, 1700 expansion cap and 0.15 s shared request slot. It exposes the same essential TryGetRoute/status API for motor integrations including ladder and learned-access approaches; no Seeker or Pathfinding runtime library required.

The motor's existing `MobileNPCGlobalRoutePlanner` remains enabled for long-distance routing, regardless of native A* availability, including its brief physical-stall wait. Fine doorway detection, threshold crossing, local refinements, jump/ladder motor behavior are preserved. Other systems, especially zombie movement and the original Pathfinding classes, were not modified. The native bridge source file is retained unused as legacy and may be removed later after regression tests.

**Important limitations:** This creates a self-contained NPC route *provider*, not a fully baked whole-world NavMesh. The owned local A* currently plans synchronously and remains subject to collision layer calibration and CPU budgets; it is not a guarantee of full indoor/stair traversal. First compile in Unity and verify one NPC completing an actual entrance/exit traversal, then profile 10–40 agents. Do not claim runtime success without tests.

## Perception-first hybrid routing — first integrated slice
- `MobileNPCSpatialMemory` is a per-NPC 1m quantized, 192-cell bounded, 120s lifetime memory fed by four short-range physics rays every 0.7s (alternating angles).
- `MobileNPCIndependentRouteProvider` attaches the sensor component and scans only on the server. When its own local A* cannot find a route, it can try one remembered free intermediate, but accepts the proposal **only if both path legs pass full physics-verified A***.
- Combat, cover, search/investigation, escort, loot and exploration already flow through the same `MobileNPCBrainDriver.Destination` -> motor -> independent provider. They therefore share this spatial memory without changing combat policy or zombie code. This is an integration through the common routing interface, not proof of NPCs understanding doors semantically.
- Individual memory is transient (no save/load, sharing or radio broadcast yet); no map-wide perception, no NavMesh dependence and no guarantee the first build successfully traverses entrances. Scan observations mark sightline clearance only; foot placement is still checked by the pathfinder.
- Unity compilation and an actual indoor/outdoor Development Build remain required. Performance checks should include 1, 10 and 30 agents.

## Sensor validity and future gun-sight integration (2026-10-30)

The spatial scanner now requires actual supporting geometry, manageable slope, and standing capsule clearance before storing a free cell. Clear eye-level space alone is not a door and cannot constitute navigability. Blocked horizontal probes are still observations, not movement commands.

`MobileNPCSpatialMemory.HasClearSightline(eye, point, mask, targetRoot)` is a reusable *read-only* sensor API reserved for future combat integration: ignore the observing actor and the intended target's colliders, return whether world geometry occludes the sightline. The shooting pipeline is deliberately untouched. Proposed future link: target recognition (brain) -> visibility of eye/head/torso samples (sensor) -> select shootable aim point (tactics) -> existing weapon pose, ray origin and ballistics (combat) -> memory of last-seen position with decay. Visibility alone must not authorize shooting: faction hostility, target identity, ammo and friendly-fire checks remain mandatory. Add capped scan cadence and occlusion caching before using multiple sightlines per NPC.

Validation still required in actual Unity Player build; no promise that doorway traversal is fixed.

## Traversal memory and sensor-assisted aiming (2026-10-30)

- Motor stores opening entry/far-side endpoints on a completed physical doorway traversal only, at the moment it reaches the last far-side waypoint (within 0.55 m). Distinct telemetry label: `physically_crossed_facade`. Mere geometry detection remains `verified_facade_opening`.
- `MobileNPCSpatialMemory` keeps 12 recent per-actor crossed openings for up to 120 seconds. Independent NPC route provider prefers physically learned entries and still re-runs the three path legs through its own geometry-aware A*. Other NPCs do not automatically inherit private memory; the separately bounded physically verified scene registry remains available.
- `MobileNPCHumanoidAim` now asks that same spatial sensor's `HasClearSightline` before returning ordinary or ballistic shot directions. This checks muzzle-to-aimpoint physical occlusion, ignores the shooter and selected target colliders, and excludes navigation-layer 22. It does **not** grant target acquisition, hostility, ammunition, fire rate, accuracy, or shooting authority; existing brain/perception/ranged combat continues to decide those. The last seen target may still be investigated by the existing brain; firing through current geometry is rejected when a spatial sensor exists.
- These two integrations are server-only in their active use; existing zombie navigation untouched. No claim of compile/run success: Unity development build tests required, especially NPC near doorway, target behind opaque cover, target visible through doorway, friendly target, 10–30 agent performance, and Combat Total vs Normal.

## Frontier exploration wiring
`MobileNPCSpatialMemory.TryGetExplorationFrontier` finds nearby actually observed, physically supported free cells bordering unknown space (1 m grid). It does **not** infer that the unknown neighbor is walkable. The humanoid motor now prefers such a frontier for optional Patrol/Scavenge routines when there is no brain-owned movement order; Combat, investigation and other urgent decisions retain priority. The normal motion/path planning remains responsible for validating and reaching the frontier. This is bounded exploration of discovered space, not a claim that every building door is traversable. Unity compilation and gameplay regression tests are outstanding.

## Combat sensor memory used for cover selection
`MobileNPCSpatialMemory.TryGetRecentObservation` exposes recent known/unknown/blocked 1m cells with height/time checks. `MobileNPCBrainDriver.TryFindNearbyCoverPoint` now incorporates these observations into scoring **after** its existing ground, shielding, direct-corridor or bounded A* accessibility checks. Recently free endpoints receive a small preference; observed blocked endpoints a strong penalty. Unknown sites retain existing validation and are not automatically rejected. This applies to verified combat/reload defensive cover selection without overriding target hostility, firing authorization or player/zombie navigation. No build/test performed yet.

## Investigation and frontier behavior consolidation
Investigation spiral candidates are now rejected if individual sensor memory recently observed a blocked destination, before existing physically grounded route validation. Exploration frontiers are suppressed for 24 seconds after selection, avoiding immediate reselection of identical boundaries. Neither observation alone is sufficient to claim accessibility, enemy location, or successful entry. The prior brain investigation deadlines and combat priority remain unchanged. Compile and Unity playtest still unverified.

## Persistent firsthand passage memory
`MobileNPCSpatialMemory.PassageSnapshot` is an optional backward-compatible field `NPCRecord.crossedPassages`. Save writes at most 12 personal, physically traversed entry/exit pairs; restore rejects invalid/non-finite and implausibly long (>4m) pairs. Restored passages are only candidates: their three path legs are revalidated by physical A* before use. Existing older JSON records without this field restore an empty passage list. Do **not** mark unknown spaces navigable or transfer a dead NPC's memory to a replacement. Current per-cell scans are intentionally transient; only successful doorway traversal pairs persist. Same-faction radio exchange is NOT implemented. Compile and test serialization inside Unity before treating this as a stable save format.

## Shared traversed topology and physical feedback (2026-11-03)
- `HybridNPCObservedPortalRegistry` distinguishes a physically **observed** opening from a physically **crossed** opening. Only the motor reaching the *far-side waypoint* promotes a connection with `ConfirmCrossing`. Confirmed entries remain for up to 600 seconds while tentative entries have a 180-second lifetime. Both remain hypotheses for route planning: **all three A* legs are checked again**, no teleportation or guaranteed crossing.
- `MobileNPCHumanoidMotor` feeds a physically stuck approach to the individual `MobileNPCSpatialMemory` **after** checking temporary player/NPC/zombie obstruction. The blocked observation is placed only 0.8m ahead and expires in ~12 seconds. Tactical goals remain owned by the brain; world geometry is not permanently blacklisted.
- With native map layers retained in the independent provider, `HybridNPCGridPlanner.TryGround` and spatial sensor floor probes now explicitly ignore the owner's colliders. Both use bounded nonalloc ray results and reject saturated scans.
- This is an incremental **verified doorway-edge registry**, not yet a globally complete polygon NavMesh or a connected cross-city sector graph. The existing `MobileNPCGlobalRoutePlanner` remains the coarse long-distance fallback.
- **No Unity compilation or gameplay validation performed**. Next test must capture the first compile error and verify that actual actor displacement crosses the far side of a Washington doorway.

## Long-distance gateway staging (2026-11-03)
- `HybridNPCObservedPortalRegistry.TryStageThroughConfirmedCrossing` uses up to **two** previously crossed entrances as possible short-range staging hops toward a distant goal. Candidate selection requires proximity and meaningful progress; entry and crossing legs both get new ground/capsule A* checks, with no assumed route beyond the crossing.
- `MobileNPCIndependentRouteProvider.TryGetConfirmedGatewayStage` provides a separately throttled public call. Direct requests outside its 30m horizon no longer consume the shared planning slot.
- `MobileNPCHumanoidMotor` attempts a confirmed stage before its coarse global 3m-cell planner for goals farther than 30m; the final brain destination is retained. On reaching the staged route endpoint, the regular planner resumes. The origin waypoint is removed before movement.
- Existing local combat/cover/ladder requests still use `TryGetRoute`, whose success means an exact, physically validated route rather than partial progress. Ordinary global fallback and nearby threshold detection remain available.
- **Not yet verified in Unity**. Test two adjacent buildings at 30-60m, an entrance blocked by furniture, and a destination requiring a second opening; confirm no repeated inside/outside oscillation. Record first compiler error before gameplay debugging.
