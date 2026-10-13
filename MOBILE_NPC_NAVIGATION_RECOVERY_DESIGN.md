# Mobile NPC navigation: house corners, real waypoint progress, local A* and tactical failures

**Project:** Unturned Better | **Branch:** `feature/npc-ai-foundation` | **Date:** 2026-10-13

Status **SOURCE IMPLEMENTED / NOT COMPILED OR PLAYTESTED**. Canonical task `CORE-04` remains EM IMPLEMENTAÇÃO. Related `AI-03` (finding real cover) and `LEARN-05` (individual memories of blocked routes) remain open. Read alongside `MOBILE_NPC_PENDING_TASKS.md` and `MOBILE_NPC_PROJECT_HANDOFF.md`.

## Why the NPC oscillated at a house wall

Direct inspection of `MobileNPCHumanoidMotor.Update()` and `TryBuildLocalRoute()` identified several causal issues:

1. **A* waypoint falsely acknowledged as the brain goal.** When the motor was within `stoppingDistance` of the current intermediate waypoint, it unconditionally invoked `brainDriver.ReportDestinationReached()`, even if the true loot/cover/investigation destination was still several meters away. This could prematurely finish seeking cover and produce turn-back behavior. Now a brain destination can only be acknowledged if the **final destination** is within the stopping radius **AND within realistic vertical step tolerance**.
2. **Local and global planning shared `nextRoutePlanTime`.** The motor requested global/native A* in advance and set the shared 0.75-second cooldown, then on encountering a wall it would often immediately refuse local grid A* due to this unrelated cooldown. Separate `nextRoutePlanTime` (native) and `nextLocalRoutePlanTime` (local), initialized and bounded independently.
3. **Invalid local endpoint silently substituted into the route.** `TryBuildLocalRoute` validated ground and occupancy for a selected grid node, but when within 1 meter of a projected local goal it replaced the safe node with the **raw unvalidated localGoal**, which could be inside a wall or void. That substitution has been removed. Every accepted local route point remains the physically checked ground point.
4. **Stuck logic ignored genuine progress around corners.** It measured mostly distance to the far-away brain goal. A good corridor route may temporarily go sideways or *away* from that goal, triggering forced 90-degree recovery and left-right alternation. Now progress toward the active valid A* waypoint or detour waypoint also counts; a stable wall-follow side is kept for approximately 4 seconds rather than flipping every recovery cycle.
5. **Node/waypoint validation crossed floors or collided with the floor.** Planar-only acceptance of waypoints could skip a point on another level. Both regular and detour waypoints now require elevation agreement. Local occupancy test moves its capsule clear of the actual walked-on floor by 0.10m, so ground contact alone does not disconnect all neighboring nodes. Edge heights remain bounded by the real CharacterController stepping limits.
6. **Stale route revalidation on every frame, even during brief crowding.** Route obstruction tests now run at most once per 0.3 seconds (rather than every frame) and distinguish true map obstacles from temporary player/NPC/zombie body collision. Other people waiting in a doorway do not automatically invalidate an otherwise valid global A* route. Walls and closed solid doors still do.
7. **Unreachable cover repeatedly chosen.** After four genuinely stalled recovery events on the same tactical `SeekCover` or `Flank` destination, the motor reports the failed goal to `MobileNPCBrainDriver.ReportTacticalDestinationUnreachableServer`. The brain records personal negative `CoverAccess` evidence for cover, cancels only this optional tactical goal, avoids the same destination within a 3m radius for 14s and may choose another physical candidate later. **Active pursuit, retreat, and other life-or-death actions are not automatically erased.**

## Source commits

| Change | Commit |
| --- | --- |
| Final-goal-only acknowledgement; independent native/local planning cooldowns | `320db52b602caed67e4433121394f3fcaf24c9b3` |
| Don't replace validated A* grid points with unvalidated localGoal; bounded stale route validation | `9fb964973f728bf8c44f3ce90fc01c9e630f305b` |
| Waypoint progress drives stuck recovery; stable wall-follow side | `136a581290fea08add61c080f4af961d4a1f88e2` |
| Waypoint elevation checks + keep occupancy capsule slightly clear of ground | `dabc9f0777c4b8b448f74536bfedffb87d013239` |
| Brief crowding by another actor doesn't discard multi-corner route | `7a718aa67a96f7de6d924739dc2c9e9e6feee624` |
| NPC brain remembers repeatedly unreachable cover and suppresses immediate reselection | `43247204664f8a7e02beeb6866795d15c360b2f3` |
| Motor notifies the brain after four stuck recoveries, without cancelling live pursuit | `ff857bc2a03fdb08694cad6315b6d450dc140307` |

## Routing / state handling

1. A destination is owned by the brain (combat, loot, cover, investigate, follow) or by ordinary roaming. A new destination invalidates stale route state.
2. Native `MobileNPCNativeAStarRouteProvider` may request a path asynchronously from the game's licensed runtime backend, **if actually available**; a failure or pending native request is not permission to teleport or assume a route exists.
3. At short-distance collision, bounded 9×9 local grid A* around the NPC resolves candidate ground, capsule occupancy, slope, step-height, diagonal corner squeezes, and sphere-cast edges. Each accepted waypoint is an existing safe, grounded position.
4. If local A* fails, constrained single-hop detour / terrain-aware steering retains normal collision and checks. Dynamic character congestion allows local avoidance without prematurely deleting a valid static map route.
5. While walking, the motor periodically checks route segment viability; validates reach in both horizontal and vertical axes, and recognizes actual waypoint approach as progress. A waypoint is NOT success of the brain-level objective.
6. If an optional tactical position repeatedly cannot be reached, report explicit failure; record limited individual learning and suppress immediate identical cover choice. Combat pursuit goals remain active for alternative routing or future gameplay decisions.

## Tests to run only in final Dev Build

- House exterior with L-shaped corner, stair landing, doors, windows, narrow corridor, fence and rooftop; player firing from inside and outside.
- Waypoint 2–5m from the final cover objective: reaching the intermediary must not mark cover reached or stop pursuit.
- Two-storey structure with waypoint directly above NPC: do not skip waypoint on vertical overlap or attempt to walk through ceiling.
- Compare native A* backend available vs not available, including pending async requests; local grid must not be blocked by native request cooldown.
- Test 4 allied mobile NPCs and zombies congregating in a doorway: no thrash of routes due purely to temporary bodies; static door closing must still invalidate.
- Sprinting NPC detours around a wall, temporarily increasing its distance to the player; stuck detection must recognize progress to the **current waypoint**.
- Four repeated genuine stuck-recovery cycles at unreachable hard cover: log one failure and choose another cover, avoiding the exact place for 14s; do not discard live pursuit or create cover in thin air.
- 20/40 NPCs simultaneously planning local grids, measure GC/CPU and log volume (0.75s grid cooldown, 0.3s route checks).
- Compare survival action priorities (retreat, cover, loot, medicine), particularly goal cancellation/reach handling. No movement through closed doors, locked buildings or clip volumes.

## Outstanding nontrivial tasks

Not yet implemented or tested: all-map A* connectivity, doors and destructible barricades interaction, full multilevel staircase/ladder links, path smoothing with multi-floor validation, vehicle navigation, swimming under buildings, native dynamic obstacle updates for graph navigation, per-map path cache and detailed `LEARN-05` route memory. A true path failure callback for **general pursuit** needs careful priority semantics and should not silently erase an enemy. All changes remain uncompiled, and none imply a confirmed working Dev Build.
