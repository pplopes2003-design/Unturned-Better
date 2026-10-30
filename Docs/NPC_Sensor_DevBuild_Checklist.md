# NPC sensor and navigation Development Build acceptance checks

Branch: `feature/npc-hybrid-integration`. This is a **test candidate**, not a Unity-compiled or play-tested release.

## Build preparation
1. Pull the branch in the Unity project (Unity 2022.3.62f3). Ensure no console **compile errors** before building.
2. Make a backup of the existing NPC JSON save. Use a disposable/new save for the first persistence test.
3. Select the same map and Combat Total mode used in your prior reproduction, then repeat on Washington / PEI as desired.
4. Keep the Development Build logging enabled and monitor the Player log; collect `nav_*.jsonl` if generated.

## Test sequence
1. **One NPC / open building**: spawn one hostile outside an ordinary doorway opening, draw it toward an indoor destination. Verify actual entry AND subsequent exit, rather than only approaching the threshold. Record `verified_facade_opening` and `physically_crossed_facade`; the latter should only appear after the NPC reaches the far waypoint.
2. **Several entrances**: block or make one known opening unreachable; verify the NPC considers alternate physically observed entrances rather than repeatedly pushing against the same wall. It must never clip through solid walls or closed barriers.
3. **Per-NPC learning**: traverse a passage, leave, and return; verify reuse is physically revalidated. Saving/reloading should preserve individual passage candidates, not make a replacement NPC inherit the dead NPC's memory.
4. **Exploration**: with no enemy or urgent order, observe patrol/scavenge choosing a visited frontier near unknown space. Inaccessible waypoints must not count as a visited room.
5. **Combat and aim**: verify gun points forward and uses the existing animation; NPC should aim and fire at an actually visible hostile, stop returning fire through opaque walls, move toward a verified cover position when appropriate, and still respect friendly targets.
6. **Investigation**: shoot then break visual contact around a building. The NPC should pursue/investigate the last supported clue, not shoot through the wall or loop forever at one blocked search point.
7. **Population and FPS**: repeat with 1, 10 and 30 NPCs. Monitor frame-time spikes, garbage collection, and how often plans are throttled. Compare to the pre-sensor build if available.

## Log clues
- `hybrid_start_capsule_blocked` or `hybrid_start_ground_not_found`: entry condition still invalid; inspect floor, layer mask and self collider hierarchy.
- `node_budget_exhausted`: local A* search needs finer budgeting/horizon or a better gateway.
- `physically_crossed_facade`: physical endpoint reached; still check video evidence for true crossing.
- `outside-local-horizon`: the 30m independent provider deliberately refused a distant direct path; the global planner should handle long-range goals.
- `throttled`: bounded planning interval, not automatically a permanent failure.

## Known limitations
- The independent provider and sensor code have **not** been compiled/run in Unity in this environment.
- Runtime performance and real indoor navigation remain unproven.
- Actor-specific spatial observation cells remain transient; only confirmed crossed passage pairs are persisted.
- NPC faction-to-faction radio sharing of spatial knowledge is not yet enabled. This is intentionally deferred rather than trusting unverified paths.
- The original native A* component file remains in the repository, but NPC motor routing uses the independent provider.
