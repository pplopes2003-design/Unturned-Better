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


## 2026-10-13 — Interrupted round resumed: chaining an observed upstairs corridor

**Existing pre-interruption source was already committed:** the motor could approach a personally discovered stair entrance from another room and follow an actual remembered ascent/descent; `TryFindKnownFloorAccessServer` already extracted the observed pre-rise stair entrance, rather than the unrelated start of the original journey. It could only accept a final goal <=1.9m from the learned exit. These earlier commits were retained, not redone.

**New source changes, same feature branch:**

- `d40634ff04ba3494be68647d1f050ba3a4c979fa`: `MobileNPCTravelMemory.TryFindKnownFloorAccessServer` can now select an additional **separately completed, first-hand, same-floor route** whose real start is within 2m of the observed staircase exit and whose real end is within 1.9m of the new upstairs target. Only personally successful routes qualify, never other survivors' knowledge or an imagined corridor. At most one continuation is selected, with explicit index and output list; the backward-compatible per-survivor snapshot version stays v6 and is unchanged.
- `3ce89c4b6f0694e9b6396b0aa6a32508c601e72a`: motor composes **actual approach → observed stair suffix → separate personally traversed upper-floor corridor → final goal**. It tests every boundary and segment with real ground, headroom, collision and native unlocked door behavior before adopting the route; waypoint cap 104 (approach <=32, stair <=36, continuation <=36). It never completes the original AI goal when merely reaching the stair or its landing. Without this second observed trip, a final goal far from the stair exit remains rejected.
- `af7681ee710ec2e28ee85a5def864404a3799c52`: when a saved-route plan is cancelled, the goal changes, the agent is safety-repositioned or it reaches all waypoints, clear **both** current learned-record references, preventing a later failure from penalizing an old unrelated record.
- `8ae284d1439b18150f9c0c4a2ada2c01c74474d8`: track which currently followed waypoint belongs to the newly computed approach, the remembered stair, or the remembered upstairs corridor. If an obstruction makes the route fail, reduce confidence only in the personally remembered **stage currently traversed**. Failing to reach the stair from a different room is NOT evidence the old stair itself was blocked. The same measured-stale policy handles an upper-floor corridor separately.

### What this does and does not generalize

This is a **bounded two-record connector composition** after a physical approach, not an arbitrary portal graph. To connect two remembered routes, the survivor must have *actually walked and completed both routes* with compatible floor, entrance/exit and goal. It does not generate new waypoints into unexplored rooms, connect two staircases, infer reverse travel or share paths by telepathy. Longer routes and entrances >25m away still depend on the game's real native graph (if available). A route that was valid yesterday may be blocked by a new structure, requiring current physical validation. CPU savings by reuse remain an **untested hypothesis**.

### Final Dev Build regressions, not yet executed

1. Same living NPC physically learns (A) one staircase ascent from ground floor to upstairs landing, (B) a separate upper-floor corridor trip from that landing into another room. Start the survivor in a different ground-floor room with upstairs target at the end of B. Confirm all three stages occur in order by walking, with no skipped walls, stairs or landings.
2. Without observed B, the NPC must not simply assume an open hallway beyond the known stair exit or mark upstairs objective achieved.
3. A barricade introduced along upper-floor corridor B must invalidate/penalize only B, not erase route A confidence. A barricade on the *newly planned ground-floor approach* should not penalize A or B.
4. Test the same stair/corridor from reverse direction with no reverse firsthand trips: no reverse connector is inferred.
5. Move upstairs target to a third room far from corridor B destination: the bounded two-record composition refuses to claim a connection; future multi-hop graph is pending.
6. Save/reload v6 preserves each original learned trip on the same survivor, but no new NPC inherits either trip on death. Stress 20–40 NPCs and verify capped physical checks, no route/door spam.
7. Confirm the C# project compiles when the user runs the **last Dev Build** and check the Unity Physics mask and current native A* backend. These source commits are not runtime verification.

`NAV-ACCESS-01`, `CORE-04`, `LEARN-05`, and `PERF-01` remain OPEN; only a limited new case is source implemented.

## Save restoration hardening — 2026-10-13

Commit `80f91042204dd670e5b7b81fc693f6d5f68ae009`: the v6 individual-route snapshot loader now rejects tracks that violate the completed-journey footprint constraints, including discontinuities, degenerate duplicate samples, inconsistent start/end and impossible observed total length. This is a prerequisite for treating a past saved stair/corridor as *candidate knowledge*, not proof of present traversability. On replay the motor must still recheck physical floor, doors and colliders. Unity compilation/playtest deferred to final Dev Build.
