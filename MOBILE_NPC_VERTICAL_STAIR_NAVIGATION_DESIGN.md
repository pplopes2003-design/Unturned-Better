# CORE-04 — Local vertical navigation using real stair treads and connected floors

Date: 2026-10-13. Branch: `feature/npc-ai-foundation`. **SOURCE-ONLY implementation, not compiled or playtested in Unity.** Canonical tracker `MOBILE_NPC_PENDING_TASKS.md`; issues `CORE-04`, `NAV-STAIR-01` and `PERF-01`.

## Root cause in existing motor

`MobileNPCHumanoidMotor.TryBuildLocalRoute` previously generated a **single navigable height** for each cell and rejected any ground surface differing more than approximately 0.8–1.25m from the NPC's initial elevation. This prevents a naive upstairs teleport but also prevents most genuine paths climbing from a ground-level entrance to the second floor. Route waypoint arrival was already checked in both planar and vertical dimensions; that should remain unchanged.

The native backend `MobileNPCNativeAStarRouteProvider` (reflection-only bridge to licensed ASPFP when actually present in runtime) is still preferred for global long-distance route requests. The public project cannot guarantee multi-floor baked graph links. We do NOT label a floor surface as a stair link based solely on being vertically near another.

## Source changes

- `75a0613f6b4186ba2c6af25a2a6e7318dfdd8445` — In `MobileNPCHumanoidMotor`, add a separate **9 × 9 × 2-surface** local stair A* search space (at most 162 nodes) and trigger it only for a destination on a different floor, at >1m and ≤4.5m absolute height difference, within planar radius √30 ≈5.48m. This is a special search, not an expensive replacement for all ordinary walking.
- `35c83c0adc472551933af964520e57da4677cf49` — `TryBuildNearbyVerticalRoute` collects actual upward-facing, walkable raycast surfaces within a bounded vertical window at each grid cell (max 24 hits/cell, fail closed on saturation). Reject NPC/Player/Zombie body colliders as ground, terrain exceeding the agent's slope capability, clip volumes and occupied head/body space. Keep up to two **independent** supported heights per XZ cell; a lower and an upper floor are not automatically connected.
- Edges are allowed only **between neighboring horizontal cells**, never a pure vertical same-cell connection. All edge candidates must satisfy global slope and then physically verify each ~0.35m of intermediate travel: there must be real floor at the expected height, each ascent must be within `CharacterController.stepOffset + skinWidth + 0.10m`, descent per sample ≤0.52m, and world collision must permit the full 3D chest trajectory. A full-sized capsule occupancy test additionally rejects a low ceiling, wall, railing or another solid at the intermediate heights. Validated registered/unlocked native door edges may use the existing door interaction plan but still require real ground.
- The goal must be a **connected real surface at the destination elevation and in the correct X/Z cell**; it cannot accept the nearest point on the wrong storey. Reconstruct physically validated waypoints in order, reuse `routeWaypoints` and continue normal `CharacterController.Move`. If no connected upper/lower path exists, return false, rather than setting NPC transform.y, jumping to rooftop or pretending the destination was reached.
- `8543f9ccd24e7219eccf1246c436d4bb8a728243` — Maximum **180 tested connector edges per vertical plan**, plus global shared admission cooldown of 0.16s and per-NPC cooldown of 2.5s. Head/torso clearance added at intermediate steps. Searches have per-instance scratch arrays (no per-search route allocation). All caps require actual Unity Profiler confirmation.
- `d50b6fcab163b7dd624f679182c8b49d741593f0` — If *only* a Player, Zombie or another mobile NPC occupies a candidate stair node or corridor, the route may remain provisionally valid: actual movement still cannot pass through the actor and existing collision avoidance handles congestion. Any simultaneously blocking wall, ceiling, door (except native valid unlocked-door link), barricade or vehicle rejects the step. Full physics hit-buffer saturation fails closed.
- Existing route invalidation now also reacts to a target's **vertical relocation** >0.8m, so a route to someone downstairs doesn't persist after the same player climbs upstairs while staying at nearly the same X/Z.

## State/architecture and limits

`BrainDriver.Goal` → native A* (if present) → near vertical goal and no existing route: bounded dual-surface stair planner → physical per-step A* connector verification → `routeWaypoints` → steering / native `CharacterController.Move`. A route into a different storey still requires real ground and traversable spatial connection. No teleport or instant elevation reset, even when start and goal share X/Z.

This **does NOT solve universal indoor pathfinding**: stairs farther than ~5m from the NPC or from the final goal, an entire U-shaped stairwell beyond the 9 × 9 grid, ladders/elevators, moving platform, complex spiral staircases with >2 overlapping floor surfaces per cell, large multilevel buildings, disconnected native graph regions and navigation across different POIs remain pending. Normal native A* (if installed and correctly baked) remains the proper way to route from across the map to an entrance and stairwell. This local search is only a validated physically bounded supplement, not an auto-generated map-scale staircase graph.

**Potential issues to explicitly test:** a raycast may detect roof floors, underside of a mesh or multiple vertically stacked walkable surfaces; the multi-floor candidate search uses up-facing normals but must verify actual material/collision. The character controller's `stepOffset`, skin width and slope angle need per-map checks. If real stairs are narrow relative to 1.25m grid cells, the planner may legitimately fail until finer graph resolution or authored links are implemented; do not lower collision checks merely to force passage.

## Final singleplayer Dev Build tests (NOT EXECUTED)

- [ ] Player stands on a true second floor with NPC below, about 3m higher and same horizontal coordinate; NPC must **not** climb vertically or repeatedly run into ceiling. If a physically continuous staircase lies within the bounded 9×9 local grid, it should choose it and climb by real character movement.
- [ ] Flat unobstructed ramp rising a floor with slope ≤38°: connected sample floors permitted and waypoint heights increase gradually. Test descending the ramp as well; no free-fall from high landing, no skipping 1.5m steps.
- [ ] Straight staircase, L-shaped staircase, U-turn landing, short split-level mezzanine and a narrow stairwell. Note which fit inside the 9 × 9 / two-height search; failures of larger stairs are honest CORE-04 work, not "success".
- [ ] Two floors directly stacked with NO connecting stair, a hole in the ceiling, and a cliff between floors: no fake vertical edge, no collision bypass, no teleport; normal A* fallback or bounded obstruction response.
- [ ] Closed UNLOCKED registered barricade door in front of actual stairs: local graph may include it only via safe native door validation, NPC must physically open it with BarricadeManager and wait for collision. Locked or unregistered doors never grant a stair edge.
- [ ] Room with low ceiling, staircase guard rail, close roof and a player holding the stairway: occupied solid rejects route. Other living characters are temporary obstacles, not permanent missing map link; no body overlap on actual movement.
- [ ] NPC combat (SeekCover/Retreat/Flank), pursuit and looting across stairs: reaching intermediate elevated waypoint must not acknowledge final goal; NPC whose enemy moves to other floor must discard stale goal height.
- [ ] 1, 4, 20 and 40 NPCs requesting vertical routes: global admission ≤1 new plan every 0.16s, per actor ≥2.5s, ≤180 expensive connector probes per plan, shared budget doesn't starve regular horizontal routing. Measure actual Raycast/Overlap/FPS/GC in Profiler.
- [ ] Repeat stock and custom maps with native ASPFP backend present and absent. Gather `Player.log`, position/height, type of staircase, blocked segment, visual recordings. User only runs final build at end of implementation.
- [ ] Never mark `CORE-04` or `NAV-STAIR-01` done without actual C# Unity compilation and these gameplay observations.

## Source review checklist

Source validation can verify method names, scope, graph dimension, braces, bounded buffers and no calls to set NPC elevation directly. It is **not** compile or behavior validation. Update both `MOBILE_NPC_PENDING_TASKS.md` and `MOBILE_NPC_PROJECT_HANDOFF.md` every round.
