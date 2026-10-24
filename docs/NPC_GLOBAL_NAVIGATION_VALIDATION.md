# Global-horizon NPC navigation — development-build validation

## Scope and status
Implementation under `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCGlobalRoutePlanner.cs`, integrated into `MobileNPCHumanoidMotor.cs`. **Not compiled or tested in Unity yet**. This is a bounded incremental A* on a moving, physically probed same-surface grid, **not** a baked full-map navmesh and **not** a replacement for vertical/ladder/doorway planners.

## Backend ordering
1. Reuse this NPC's actually walked and revalidated routes / learned access when applicable.
2. Prefer native ASPFP A* whenever it exists and returns a valid route.
3. Without ASPFP: sample a 21x21 grid (3m spacing; approximately 60m across) for distant, nearly same-floor destinations. Accept edges only after capsule-clear and continuous *player collision layer* support checks.
4. The native, local fine-grained search continues serving doorways, staircases, low gaps, tactical cover and final access. The global fallback provides validated segments toward the distant goal rather than declaring the staged endpoint to be the brain's goal.
5. If a macro step is physically blocked after planning, discard the stale route and let the existing local path / observed facade logic take over. No teleport or synthetic portal is introduced.

## Resource limits
- Per-NPC arrays: 441 nodes, all reused. Max 340 expansions/search and 160 reconstructed waypoints.
- Shared ceiling: 6 expanded nodes per Unity frame globally, max 2 per eligible NPC call. Each NPC is scheduled by frame cohort (`GetInstanceID() & 15`) to distribute work.
- Search expires after 75 seconds or if its origin moves more than 3m or its destination moves more than 2m. A failed search has a 4-second retry cooldown; movement/goal changes interrupt stale work.
- Physics checks are still significantly more expensive than raw node expansions; measure server frame timings with 1, 10, 20 and 40 NPCs before increasing these budgets.

## Development-build observations
- `MobileNPCGlobalRoute` with `nodes` and `stagedEnd`: planner produced a physically verified segment. Staged end is **not** the final target.
- `MobileNPCGlobalRoute` with `No connected corridor`: no acceptable route was found within current horizon; existing local fallbacks stay active.
- `MobileNPCGeometryThreshold`: a building gap was geometrically verified.
- `MobileNPCThresholdTraverse`: the NPC progressed across specific doorway route waypoints, or a new collision invalidated them.
- `MobileNPCHumanoidMotor ... nativeAStarAvailable=False`: ASPFP is not available; this may still be expected with the public SDK.

## Manual acceptance scenarios (Windows Development Build)
1. **Long facade:** NPC 20–35m from a target behind a building, and an entrance/outer corner to either side. Observe actual multi-corner movement, not an idle turn-in-place or immediate repeated head-on wall contacts.
2. **Through a doorway:** NPC starts 4–12m from the external wall, target inside. Verify threshold logs progress from near-side approach to far-side waypoint, and finally enters on foot.
3. **Solid wall / blocked or raised window:** no through-wall traversal, no ghost movement through blocked sills, no repeated instant teleport; new obstacle can invalidate existing route.
4. **Two-floor target:** global same-surface planner must not invent a vertical connection. Only actual stairs, ladder or physically checked vertical planner can connect floors.
5. **Sloped terrain / Nav-only layer:** global path must not traverse a floor collider outside `RayMasks.BLOCK_COLLISION`; check falling recovery logs.
6. **Dynamic barricade or vehicle:** revalidate the next motor waypoint and recover through native doors/local detour; do not retain the stale global edge.
7. **Dense population:** 1, 10, 20, 40 NPCs on the same map, in movement and combat. Check frame time, GC allocations, server simulation rate and whether the frame-cohort scheduling starves any NPC.
8. **Changed combat goal:** while global planning is pending, attack NPC from a different direction. Old goal path must not override combat pursuit, cover, retreat or the currently selected victim.
9. **No native ASPFP:** verify both the new fallback and the old local doorway/tactical planner continue functioning without the missing dependency.

## Limitations
- No long-range route guarantee on very large or multilevel structures, complex terrain, isolated islands or vehicle transport.
- The 3m global grid may miss a narrow entrance; the separate 0.625/1.25m local / observed-threshold planner handles such openings when physically observed.
- No Unity compilation, multiplayer replication test or automated performance benchmark was possible in this round. Do not call this navigation production-ready until the scenarios above pass.
