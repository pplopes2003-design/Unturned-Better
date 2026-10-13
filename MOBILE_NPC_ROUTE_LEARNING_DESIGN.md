# Individual learning of physically traveled routes — CORE-04 / LEARN-05

Date: 2026-10-13. Branch: `feature/npc-ai-foundation`. All integration is **SOURCE ONLY**, not compiled or demonstrated in Unity. This document supersedes any claim that generic CoverAccess memory also stores reusable stairway waypoints.

## User question: do stairs and jumps count as learned routes?

A short real staircase / walkable slope is planned in `TryBuildNearbyVerticalRoute` by a bounded 9×9×2 local A* with actual terrain/step support. Once the agent physically **walks that complete route to the requested destination**, the new independent `MobileNPCTravelMemory` can remember its movement positions. Only personally traversed routes are first-hand knowledge. A candidate A* route never automatically becomes a remembered path. An individual may reuse a learned staircase on a future matched journey, subject to fresh corridor, ground and clearance checks.

A **vertical ladder**, scripted climbing animation, vault, jumping a roof gap or planning a jump between disconnected platforms remains **unimplemented**. The motor has an older reactive, terrain/landing-checked `ShouldJumpOverLowObstacle` and native `CharacterController` jump impulse for small obstacles, but no explicit A* jumping/off-mesh edge or airborne path optimizer. A successful walking journey may incidentally include reactive small hops without teaching a repeatable jump action. This distinction is essential to avoid imaginary platform traversal.

## Implemented architecture

```
Brain destination (actual observer knowledge)
  -> per-NPC travel memory lookup (start/goal must match in 3D)
     -> EACH cached segment revalidated with real current ground,
        geometry/door and capsule safety
        -> if valid: use cached waypoints via CharacterController
        -> if invalid: count failed reuse, plan natively/locally instead
  -> optional native A* / local horizontal or two-level stair A*
  -> CharacterController.Move + *real* displacement samples
     -> upon ACTUAL arrival at FINAL 3D destination:
        store this completed journey, no unwalked speculative A* nodes
        -> persist in this living NPC's own v6 snapshot
        -> death discards the private knowledge; replacements are new
```

### Memory component

`Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCTravelMemory.cs` holds at most **8 personally experienced routes**, with at most **36 Vector3 footsteps** each and a strict <=2.6m gap between successive points. Travel must be at least 3.5m and end near the requested goal. Journey snapshots are not shared automatically with group/faction members or a newly spawned NPC.

Each record stores initial position, goal in 3D, actual physically sampled footsteps, capped success count and failed reuse count. The route lookup requires current location within 3m of remembered origin, current goal within 2m of remembered endpoint and elevation within 0.85m of both. The motor performs the much stricter geometry verification of **every** remembered segment, and final connection to current goal, before accepting the cached route. Attempts to reuse a route through a newly placed wall, invalid floor or blocked door degrade it; after three failed reuses it is excluded from search. It is a finite, contextual personal route cache, **not** a neural network or an omniscient globally optimal navigation graph.

`MobileNPCHumanoidMotor` separately tracks an active *actual* journey (the desired goal plus position samples AFTER `CharacterController.Move`), resetting on goal change. It refuses to teach a teleport/physically discontinuous step (>2.6m), unrealistically long (>36 samples) or unfinished journey. A tracked NPC may take a detour or physically ascend/descend stairs; these movement traces can become routes only when actual final 3D arrival occurs. It queries memory before native/global A*, at most once per 4s while route-less. During visibly threatened `SeekCover` or `Retreat`, memory lookup is skipped so a tactically unsafe old route does not override current firefight constraints.

Reusing a route does NOT skip physics, ignore locked doors, set `Transform.position`, invent clearance, or make a previously unseen area visible. The native `CharacterController.Move` still executes every step and route obstruction checks remain live. The old route is a **candidate** that must pass current checks, not a guarantee.

### Save / lifetime

`MobileNPCPersistence` writes **v6** JSON with an individual `learnedRoutes` list per `NPCRecord`; snapshots from v1–v5 remain readable and have no learned routes. `MobileNPCTravelMemory.RestoreSnapshotServer` clamps record count/point count, rejects non-finite or out-of-bounds coordinates and impossible large hops. Save and restore only refers to the same surviving NPC, never group-level policy. `MobileNPCHumanoidVitals.DieServer` calls `ForgetOnDeathServer`, consistent with existing `MobileNPCAdaptiveMemory` and supply source knowledge. Existing group conversation shares generic bounded experiential lessons **only** where explicitly implemented; detailed route geometry sharing over real audible speech or functioning radio **is not implemented** and must remain tracked under `LEARN-03`.

### Source commits

- `85f98dcd493e5472c32560e0d38779c60fd59d5d` — `MobileNPCTravelMemory`: 8-record learned journey cache, real-path candidates, confidence, snapshot and death clearing
- `60a2a9ce5b94e0e3e7de58d91b721e1d9e8147ba` — attach it to every runtime survivor
- `c0e284666779793cfada88758ab777cdf4f69351` — clear private route memory on death
- `fbdc7b67384cea8416e2546f5bd0ceae2c7e8e4d` — motor movement-trace and memory-first planner integration
- `64a3ed6ec3fc71e0693281d5081fb59a4172894b` — actual after-movement footstep recording, route revalidation, stale-route invalidation and bounded reuse
- `701a204b91ad0bca8cd3f6f8f1256b6a80ab1641` — individual backward-compatible persistence version 6

## Performance: expected, not yet measured

Reusing a verified path can avoid recomputing a local A* search and makes a survivor more efficient when **repeating the same trip**. Checking every recorded segment still costs physical raycasts and ground/clearance probes. For nearby different start/goal coordinates, novel doors, distant stairwells, global map-scale travel and new buildings, new planning remains required. Memory cannot turn an inaccessible location into a valid route or bypass discovery.

This is an explicit **episodic experience cache** and an incremental success/failure preference, distinct from (but compatible with) other contextual-bandit memories of cover/loot. No external machine-learning framework or pretrained model is necessary; later goal-directed waypoint graphs (POI/stair entrance/exit connectors) can generalize experiences beyond exact start-and-goal matching.

## Pending work that must remain open

- Map-aware **access portal graph** for long trips through multiple rooms, entry doors, stairs distant from goal, multi-floor path stitching and POI-scale cache
- **Jump / vault / climb / vertical ladder** action links with verified trajectory, safe landing, collision and animations, within fatigue/injury constraints
- Knowledge-sharing of specific route entrances/exits by audible conversation or radio only
- Memory expiry, old-map asset revisions, dynamic barricade revision awareness, route time/CPU profilers, candidate scoring by observed danger
- Validate all native C# signatures, runtime `CharacterController` collision, saved JSON migrations and performance in final Dev Build

## Final Dev Build tests

1. Guide same living NPC from downstairs through a REAL staircase to upstairs destination and verify physically sampled step points, then repeat the same trip. Second trip should use the learned route if still geometrically valid, not a teleported jump.
2. Move player slightly within destination tolerance or spawn NPC several meters off previous start: memory must not confidently reuse an unrelated route or skip other rooms.
3. Place a native closed locked door or wall along a previously learned route. Cached path must fail geometry validation, lose reuse confidence and fall back to actual navigation. Unlocking/opening door may make a route feasible again only with real physics.
4. Same NPC save/reload on same map: retains a bounded route list and physically validates it on use; v5 snapshot loads with empty route memory and no data corruption. Different player/world/map key does not inherit another player's NPC snapshots.
5. Kill NPC before communicating: private route memory clears, its replacement starts empty. Other NPCs must NOT magically inherit staircase path or exact waypoints merely by sharing a faction.
6. Repeated roaming and combat target changes must not create completed-route entries for uncompleted journeys. Spurious physics teleport, falling through terrain or large positional gap must not be learned.
7. A tactical cover/retreat near visible enemy skips stale route reuse even if the old shortest path was previously successful: active threat exposure remains important.
8. Check 1 / 4 / 20 / 40 mobile NPCs in Unity Profiler: compare native/local A* invocation counts, CPU, physics probes, allocations, save size and FPS before/after repeated trips. Reduction is a hypothesis until measured.


**Additional fix:** `e89e6580f0f5bb4c3257bf973e1d7389e2a67737` clears cached-route-use tracking after waypoint completion and clears observed journey traces when safety recovery repositions an NPC after a physics fall. An emergency `transform.position` correction is not first-hand route traversal and must never appear in the learned data. Final Dev Build test required.
