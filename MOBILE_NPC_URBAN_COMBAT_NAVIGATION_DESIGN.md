# Urban combat navigation — shooter-inspired, bounded implementation

Date: 2026-10-13. Project Unturned Better, branch `feature/npc-ai-foundation`. Canonical backlog: `MOBILE_NPC_PENDING_TASKS.md` (CORE-04, AI-03, LEARN-05, PERF-01). **Code implemented in source; neither Unity compilation nor in-game testing was performed.**

## Research — adapt principles, not unrelated game engines

1. **F.E.A.R. / Jeff Orkin / GOAP:** a soldier has a goal (e.g. eliminate enemy) and can replan an action sequence when a door closes, taking failure into account rather than endlessly trying a blocked direct route. The planner runs under CPU/time restrictions, with distributed/cached expensive computation. `https://ojs.aaai.org/index.php/AIIDE/article/view/18724`; `https://www.gdcvault.com/play/1013459/contactUs`; `https://pages.cs.wisc.edu/~dyer/cs540/handouts/gdc2006_orkin_jeff_fear.pdf`.
2. **Killzone / Guerrilla Games GDC Europe 2005:** plan **tactical paths**, not only geometric shortest paths; add costs for links exposed to hostile lines of fire or crossing allied lines of fire, and use evaluation of world positions for cover/flanks. `https://www.guerrilla-games.com/media/News/Files/gdce05_killzone_ai.pdf`. Game AI Pro, *Tactical Pathfinding on a NavMesh* discusses using more expensive exposed nav nodes and the runtime visibility-ray budget: `https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter27_Tactical_Pathfinding_on_a_NavMesh.pdf`.
3. **Recast / DetourCrowd:** distinguish global path/mesh corridor, up to a few upcoming corners, and **local steering** around other agents. Do NOT graft DetourCrowd's position controller onto a `CharacterController` concurrently: the Detour API explicitly expects control over agent positions, and is costly for large crowds. Apply the safe corridor-lookahead principle locally using existing Unturned physics instead. `https://github.com/recastnavigation/recastnavigation/blob/main/DetourCrowd/Include/DetourCrowd.h`.
4. **A modern mixed approach**: academic research on Arena Breakout combines navmesh navigation with rule-based shooting to avoid handing all combat control to a trained model. For this project incremental per-NPC experience remains separate from deterministic movement collision and ammunition validation: `https://arxiv.org/abs/2410.04936`.

**Architecture for THIS project:**
`MobileNPCBrainDriver` decides real, observable goal → `MobileNPCNativeAStarRouteProvider` seeks global route if game A* is available → `MobileNPCHumanoidMotor` uses a bounded local 9×9 A* and valid physical waypoints → safe path-corner refinement and collision-aware steering → `CharacterController.Move`. `MobileNPCAdaptiveMemory` scores only the NPC's actually experienced cover and blocked routes. No omniscient threat positions and no offscreen fake cover.

## New source work

### A. Exposure-aware tactical local A* (source commit `9d4db3730af9ef2faa10696c5f821d2ddddf3bb2`)

`MobileNPCHumanoidMotor.TryBuildLocalRoute` now adds a non-negative +1.45 **tactical edge cost** to a node that is physically exposed from a **currently visible** hostile `brainDriver.Target` when the goal is specifically `SeekCover` or `Retreat`. This is additive to geometric A* edge length and is bounded: a route through shelter can be preferred even if slightly longer. Non-defensive actions retain shortest-walkable-path search, and a player who has disappeared does not have their live location used for exposure cost. Real solid geometry that intercepts a finite ray can classify a node as sheltered; other characters cannot count as free protective walls. Collider and ground safety are unchanged.

The per-agent arrays `routeSearchExposure` (0 unqueried, 1 exposed, 2 sheltered) and reusable physics buffer avoid per-cell allocations. **Maximum 24 supplementary visibility rays per planned local route**. Any unsampled node conservatively receives exposed cost; this is a local approximation, not a global threat/cover map. The effect is intentionally limited by the local 9×9 grid and a short-range visible hostile (within 40m), and does not yet account for allied lines of fire, multiple enemies, armor penetration, or weapon-specific firing arcs.

### B. Detour-style safe corner-lookahead (same commit)

Before walking a planned local or native route, the motor can skip one intermediate corner point if the direct segment to the following waypoint is at most 4m, its chest-height swept sphere is clear and **each intermediate floor sample** (roughly <=1m spacing) is walkable with valid height. The heading remains processed by `ApplyTerrainAwareSteering` and `CharacterController.Move`. The optional lookahead runs no more frequently than once every 0.35 seconds. It cannot skip a staircase, ceiling gap, closed door, wall, cliff, or the final brain destination; it only skips redundant path bends. A skipped waypoint is not reported as a completed AI goal.

### C. Bounded alternate entrance / cover around a corner (`ceef34f459de64ab8a2eceda83bcda4e8ea96868`, `42e68122574b1e4523d74352134efe05dcc93e28`)

Old `MobileNPCBrainDriver.TryFindNearbyCoverPoint` only accepted candidate cover if there was a **straight, unblocked spherecast from NPC to cover**. This mistakenly eliminated many proper urban positions behind a wall, even if accessible by walking through an open side corridor.

New `MobileNPCHumanoidMotor.CanReachNearbyCoverServer` reuses the already present local 9×9 A* as an **independent feasibility query**, writing to dedicated `coverRouteScratch` rather than overwriting the NPC's currently followed `routeWaypoints`, `routeWaypointIndex` or cooldown. A successful query must reach the **actual requested preferred goal cell**, be on the correct floor, leave the final collider space free, and have an unobstructed short final segment. No "closest reachable node" is accepted as proof of cover access. Candidate proximity is limited to approximately 6.5m and 1m vertical delta, matching the bounded local grid.

For a cover candidate which has real world shielding but is **straight-line blocked**, `MobileNPCBrainDriver` may perform this special route query for at most **three candidates per defensive decision**. Physically verified ones join the existing cover-scoring system, with individual learning and safe-ground checks. No teleport, no ignored solid walls, no assumption that all windows/doors can be traversed, and no arbitrary long-range route claim. The full motor still must follow the chosen path and report actual arrival; a failure is learned under CORE-04/LEARN-05.

## Safety/performance contract

- No network packet, save version, player's controller, or licensed Pathfinding Project sources changed in this round.
- Only server-owned humanoid NPC navigation is affected. Native A* and physics remain authoritative for movement.
- Max 24 tactical LOS samples per local grid route; at most one safe lookahead every 0.35s; max 3 extra 81-node searches during a cover decision.
- Same-group communications and radio do not magically confer valid routes or enemy location.
- Source-level physics query buffer has fixed size and falls back conservatively on saturation; no hardcoded universal safe cover coordinate.
- Inspect Unity 2022.3.62f3 C# compile + `RayMasks.BLOCK_COLLISION`, `CharacterController` and loaded physics on final Dev Build.

## Final Unity test matrix (not executed)

1. Enemy visible in a courtyard, two nearby routes to a cover position: short exposed path versus longer route behind wall. NPC should usually prefer the sheltered segment if it exists, but not if doing so needs wall penetration. Repeat after loss of sight: no magically updated enemy position.
2. Add allies standing in line of fire: these **are not yet added to tactical A* cost**; retain AI-03 subtask and test friendly fire separately.
3. Navigate an L-shaped wall and open doorway with one redundant waypoint: shortcut only if the segment, all ground samples and floors are physically traversable. A closed door, second floor or drop must prevent path corner cutting.
4. Shooter inside building, reachable cover behind a corner within ~6m: candidate previously rejected by straight spherecast may now be accepted **only if exact local A* is feasible**; keep actual path/goal state intact while evaluating candidate.
5. Unreachable covered position behind a solid wall with no door: query fails, tactical fallback chooses another position or proper retreat; never teleport. Validate partial grid closest-cell paths are not counted as success.
6. Door partly obstructed by moving NPC/zombie: transient blockage alone does not cause endless replanning. Dynamic door actually closed must invalidate route; group traffic should not make NPCs press permanently into walls.
7. 1 / 4 / 20 / 40 NPCs with visible hostiles; profile raycasts, GC, time per decision and local A* rebuilds. Compare cover behind corners and outdoor nav without tactical costs.
8. Repeat on two different stock maps and at least one custom map; A* backend may not be distributed with public SDK. Capture map, points, Player.log, hit geometry and performance.
9. Verify no early `ReportDestinationReached`, no infinite route oscillation, no shooting through structures and no unlimited route retries. Mark CORE-04 only after runtime evidence.

## Still pending

- Reliable long-distance path networks across multiple houses/POIs and floors, actual door open/breach decision API and stairs/offmesh links, dynamic barricades, true squad doorway sequencing, tactical cost for allies' firing lanes and grenade danger, full cover crouch/peek/lean animations, persistent personally learned routes. No claims of universal urban navigation or tested AI.
- `CORE-04` stays **EM IMPLEMENTAÇÃO**, `AI-03` and `LEARN-05` still active. User requested Unity build/tests only after all implementation work.
