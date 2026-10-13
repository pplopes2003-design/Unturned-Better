# NAV-ACCESS-01 — Individual learned staircase entrance and cross-room routing

**Date:** 2026-10-13. **Branch:** `feature/npc-ai-foundation`. **State:** source implemented for **one observed floor-access connector**, *not compiled or playtested in Unity*. This is not a complete all-map indoor portal graph. See canonical `MOBILE_NPC_PENDING_TASKS.md` and `MOBILE_NPC_PROJECT_HANDOFF.md`.

## Problem before this change

`MobileNPCTravelMemory.TryFindCandidateServer` reuses a real traveled route only when the character's current position is within ~3m of the original trip start and the goal within ~2m of its endpoint. An NPC that previously used a staircase could not select it from another room if the starting position was >3m away. The general motor would ask native A* to go straight upstairs or try a local 9×9×2 stair search near the NPC. This failed to make practical use of a known entrance.

## Source implementation

1. `6c3f3eb2bad7c1b38e38d5e5b510f0a6380ddf0f`: `MobileNPCTravelMemory.TryFindKnownFloorAccessServer` searches only this **same living NPC's own physically completed journeys**, not simulated group or map data. Select a route with first footprint on current floor, final footprint on target floor, observed vertical change >=1.2m and destination near the remembered exit (<=1.9m). May be 2.5–25m from original entrance, representing another room. The observed start is the *entry candidate*; no connecting stair link is inferred from stacked floor coordinates. The direction is only the observed direction, not magically reversed. Successful experience and failed reuse evidence affect candidate score. It returns a **proposed** entrance and actual observed step sequence, not a usable path.
2. `2d5c2936454c751e5f0bf80ab2b619f22d9d2785`: motor now checks for such a known floor-access connector BEFORE requesting native A* straight to a final different-floor destination. It has a separate bounded access planning interval, a pending request state and separate scratch waypoint arrays.
3. `1ff9371fba2bd04c2c16577ba44a5ea198b7ed5a`: `MobileNPCHumanoidMotor.TryBuildKnownFloorAccessRoute` composes two actual movement stages:
   - **Approach:** if remembered entry is <=5m, the existing 9×9 local A* must actually reach that precise entry cell. If farther away (up to 25m), the game's optional **native asynchronous A*** must produce a route to the remembered entrance. If native A* is not available or cannot find a real path, fail closed and keep other normal navigation; never move directly through a room wall simply because the entry is known.
   - **Connector:** revalidate every segment of the individual's physically recorded stair/door journey against **current** terrain, full-body obstruction, stair/ground continuity and native *unlocked registered* door rules. A locked barricade, destroyed stair or blocked shaft invalidates. A connected exit can be appended to the current target only when the final segment has a verified, short physical connection.
   - The existing CharacterController then walks the **combined approach + learned stair waypoints**. The brain's real final destination remains unchanged; reaching the staircase entrance is NOT `ReportDestinationReached`. Success is still judged from actual 3D final position. If current obstacles invalidate a reused route, `ReportLearnedRouteInvalidatedServer` lowers personal reuse confidence. No teleport, no direct transform elevation assignment.
   - This is *not* automatically shared via group/radio, and is not an on-the-fly scan of hidden map buildings. It is evidence-derived episodic navigation, not deep neural ML.
4. `a2e2d028e0554a4161d376ccaf54349a8416104a`: protect the **asynchronous native A*** request to the stair entrance until its result has been consumed. Merely having the native callback complete does not license a request to the upstairs final goal while the entrance output awaits the next scheduled poll. Otherwise the native provider drops the already-computed entrance route as a goal mismatch.
5. Individual route memory remains persisted via **v6** NPC snapshot and cleared on death, per the previous `LEARN-ROUTE-01` implementation. No new global knowledge or save schema is needed.

## Practical constraints and honest engineering risks

- Only *one* individually observed floor-connecting journey plus an approach is chained. Multiple doors, arbitrary portal graphs/room adjacency, several stairwells or floors, traversing in the reverse direction without personal experience, choosing an alternative staircase when the first is blocked, and POI-scale caching still require additional implementation.
- The final goal must be **near the previously observed exit**. If the enemy is 15m away from the stair exit on a long upper-floor corridor, the motor needs another planning stage; it must not declare the route solved by merely reaching the landing.
- Far-from-entry approach currently depends on an active **native A* backend**. If the runtime graph is missing from the public SDK, the motor can only reach known entrances that fit the local grid; do not invent native availability. Native A* may be asynchronous and its route may be stale.
- Physical corridor revalidation costs CPU. Each candidate search is bounded by up to 8 individual learned routes, one access query per ~0.75s, max 32 native/local approach waypoints, <=36 remembered steps, and the existing physics helper checks. **Not profiled**: these are source caps, not measured performance guarantees.
- The entire connector is revalidated before adoption. Approach path failures due to unrelated world changes do not imply the personally experienced **stair connector** is invalid; connector failures count as stale reuse evidence. A fresh journey may later relearn a usable route.
- Tactical `SeekCover`/`Retreat` when enemy is currently visible bypasses access replay so an exposed remembered route doesn't override current tactical exposure analysis.
- No Unity build/test performed. All route, collision, native graph and C# signatures remain subject to final Dev Build validation.

## Required final tests

- [ ] NPC first climbs a physical stair to a goal on upper floor; `MobileNPCTravelMemory.RouteCount` increments **after actual arrival**. Reset it to another room ~8–15m from remembered entrance with goal near remembered upper exit. If native A* is present, the NPC must navigate **to entrance first** then physically climb remembered stairs to the final goal. The brain must NOT consider the entrance to be the completed objective.
- [ ] Repeat without native A* backend: don't falsely claim passage through a building wall; allow short-range physically validated local entry when within ~5m, otherwise use conservative fallbacks.
- [ ] During entrance A* `requestPending`, ensure subsequent requests do not cancel it for different destination; ready-but-not-consumed response remains eligible until next scheduled tick.
- [ ] Put a real locked barricade door or wall before the entrance; exact approach must fail and NPC must not open/phase through it. Put a barricade on the learned stair connector; revalidation must reject and penalize stale use.
- [ ] Player moves upstairs to a new room far beyond the learned stair exit: motor must NOT teleport/claim full access with the landing alone. Future multi-hop upper-level route needed.
- [ ] Same NPC loads saved v6 memory and can attempt learned entry; newly respawned NPC or allied group member without audible conversation does NOT inherit entrance.
- [ ] 20 and 40 NPCs with repeated known routes and combat: log request rate, A* path failure/ready/pending, physics query costs, native/local fallback, real reached-goal event and FPS. Use actual Unity Profiler / Player.log.
- [ ] Target moves floors while entrance route is pending: request must safely cancel/replan, never follow stale upstairs path to a different goal or mark arrival.
- [ ] Real world stairs with landing, native unlocked door on route and a passing allied NPC: route must respect door animation/collision and not treat transient actors as permanent walls while still physically avoiding them.

## Next iterations

- Discover **graph nodes** for multiple distinct personally observed stair entrances, doors and room-access portals, and compose 2+ validated edges without quadratic searches.
- Last-known staircase used to approach an upstairs **region**, then second-stage route from stair exit to a *different* target destination on that floor, with native A* or safe local planner.
- Per-map building/POI location association, dead-end and damage-risk history, group ingress and tactical fire lane coordination.
- Native nav graph audit + profiling and, separately, physically planned `NAV-JUMP-01` links/vertical ladders. `CORE-04`, `NAV-ACCESS-01`, `PERF-01` remain open.


## Follow-up: observed elevation transition as the actual entry candidate

Source commit `24ba6037f00907978a4c468ddec626ee7532a02d` refines `TryFindKnownFloorAccessServer`: earlier it assumed that the original trip start was the physical staircase entrance, even if the NPC walked through a room/corridor before reaching stairs. Now `FindObservedFloorAccessEntranceIndex` searches **real recorded positions** for the first cumulative elevation transition >=0.65m in the direction of the actually completed upper/lower floor trip; it selects the verified preceding same-floor step as the *known access point*. Only the observed suffix from that point through the personally visited exit is proposed. If the trace lacks an interpretable transition, no staircase portal is inferred. It neither creates a new door nor assumes a reverse link.

**Additional final build test:** first learn a trip starting inside a room 10m before a staircase, walking a corridor then climbing. Move the NPC into a different room closer to the actual stair base. With available native/local navigation it should approach the observed pre-ascent point rather than insist on revisiting the old arbitrary starting room. Confirm it does not pick the wrong floor, skip the first stair tread, or infer a vertical edge from a mere player position upstairs.
