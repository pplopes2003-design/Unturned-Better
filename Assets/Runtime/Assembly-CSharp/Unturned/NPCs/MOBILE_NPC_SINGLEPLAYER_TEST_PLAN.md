# Mobile NPC — single-player validation plan

This checklist is for in-game validation of the mobile NPC foundation. It is not evidence that these tests have already passed. Run against a disposable world/save and record the game build, map, population cap, and console output for each test.

## Spawn-source implementation (wired; requires in-game validation)
- The population director reads `LevelPlayers.spawns` and uses map-authored vanilla player-spawn points as regional anchors; regular and alternate spawn points are eligible.
- The NPC is not placed directly on the player spawn point: a randomized offset/ring is used, then safe-ground, slope, capsule occupancy, player visibility, map bounds and NPC spacing checks run before creation.
- If the map has no player-spawn points, a warning is emitted and the director retries on its next normal interval. It does not silently fall back to invented coordinates.
- [ ] On at least two different maps, verify NPC spawn regions derive from each map's own player-spawn points and do not repeatedly place NPCs directly on top of a player spawn.
- [ ] Test a map with no usable player spawn definitions (if available); confirm bounded interval-based warnings and no unsafe fallback spawn.

## 1. Startup and population cap
- [ ] Launch a new single-player world on a supported map; confirm the mobile NPC bootstrap initializes without exceptions.
- [ ] Observe initial spawns over several population intervals; verify the number of living mobile NPCs never exceeds the configured cap (default 20).
- [ ] Confirm vanilla zombies and existing fixed/static NPCs continue to spawn and behave as before; they must not count toward the mobile NPC cap.
- [ ] Temporarily set the cap to 1, then 10, and verify population respects both settings.
- [ ] Test a map with steep terrain, water, and obstructed areas; failed spawn searches should be bounded and should retry later rather than loop every frame.

## 2. Combat and humanoid behavior
- [ ] Confirm hostile mobile NPCs can detect a nearby player and transition into pursuit/attack behavior.
- [ ] Verify melee attacks require a confirmed target collider and stop at walls/other intervening entities.
- [ ] Break line of sight and move away; verify the NPC reacts to last-known position and eventually returns/abandons pursuit instead of tracking the player indefinitely.
- [ ] Fire nearby shots without exposing the player; verify noise investigation does not require direct visual contact.
- [ ] Test ranged combat at short, medium, and long distances. Record hit rate and confirm aim is imperfect, with spread/suppression rather than sentry-like guaranteed hits.
- [ ] Verify the default aim-height offset directs shots toward the humanoid upper body rather than the root/feet; adjust only on prefabs with a different pivot.
- [ ] Verify firing only commits when the stored gun state accepts the decremented round count; a missing/short gun state must not consume a shot.
- [ ] Verify reload reads detached magazine rounds from Item.amount and capacity from ItemMagazineAsset.MaxAmountAsByte; empty magazines must not be selected.
- [ ] Swap a partially loaded magazine; verify the old magazine returns with its remaining rounds and durability, and the new magazine's durability is written into gun state.
- [ ] Verify empty magazines that vanilla preserves can be stored and survive NPC save/restore without becoming reload candidates.
- [ ] Verify Ammo_Per_Shot values greater than one consume the correct number of rounds, and Infinite_Ammo weapons do not decrement the magazine.
- [ ] Verify a weapon with no compatible attached magazine cannot fire, including assets configured with Ammo_Per_Shot = 0.
- [ ] Interrupt reload by removing/disabling the NPC; verify the reserved magazine is returned rather than lost.
- [ ] While reloading, verify pickup/trade/settlement operations cannot consume the reserved inventory slot needed to roll back the magazine.
- [ ] Verify failed gun-state updates return the consumed magazine and never grant free rounds.
- [ ] Verify the simulated magazine reload cycle does not duplicate rounds, and compare the project’s state-byte assumptions against vanilla item assets.
- [ ] Store two copies of the same gun asset with different ammo states; confirm firing/reloading updates only the selected instance.
- [ ] Remove or add items before the selected gun in inventory order; confirm subsequent ammo updates still target that same gun instance.
- [ ] Verify friendly and neutral roles do not automatically behave as hostile targets under the intended disposition rules.

## 3. Groups, death, and replacements
- [ ] Observe newly spawned NPCs joining groups of the intended maximum size and sharing a group settlement anchor.
- [ ] Kill one member while another member survives; confirm the corpse cleanup and replacement cooldown are independent.
- [ ] After the respawn delay, confirm the replacement rejoins the surviving group when that group still exists.
- [ ] Eliminate all members of a group; verify the group dissolves and a later replacement does not join a dead/empty group.
- [ ] Kill multiple NPCs close together; verify replacements are delayed and population never exceeds the cap.

## 4. Settlement construction
- [ ] Allow a group to choose a settlement site; confirm construction does not begin before its delay and only proceeds when the NPC is not actively fighting.
- [ ] Supply buildable items and verify the fixed, constrained pattern places only supported storage/defensive pieces.
- [ ] Test obstructed positions and invalid housing placements; the NPC should retry later or use a supported fallback rather than repeatedly placing invalid pieces.
- [ ] Verify consumed build items are removed from the simulated inventory only after successful placement.
- [ ] Confirm non-construction supplies transfer to the central vanilla storage without duplication or loss.
- [ ] Leave and reload the world; verify placed vanilla structures/storage persist according to the game's normal world persistence.
- [ ] After reloading a settlement, verify the builder reconnects to its existing NPC-owned central storage, does not transfer supplies into a nearby player's storage, and skips already occupied perimeter slots instead of retrying them forever.

## 5. Snapshot persistence and recovery
- [ ] Move several mobile NPCs to different positions, give them different factions/health/inventory, and wait for the periodic save or exit cleanly.
- [ ] Relaunch the same single-player character and map; verify restored positions, role/faction, health, inventory, group membership, and settlement anchor.
- [ ] Relaunch a different character on the same map; verify its NPC snapshot is isolated.
- [ ] Temporarily reduce the cap below the saved population; verify restored NPCs never exceed the cap and pending records resume gradually when slots open.
- [ ] Test a missing snapshot, malformed primary JSON with a valid backup, and both files malformed. Verify recovery logs are clear and the game remains playable.
- [ ] Simulate an interrupted save only in a disposable test copy; verify the temporary file/backup strategy does not silently erase the last valid snapshot.
- [ ] Change maps and return; verify map-key isolation and that deferred records from the previous map are not restored into the new map.

## 6. Friendly trade and airdrops
- [ ] If a host interaction/UI has been wired to `MobileNPCTradeService`, test purchase success, insufficient currency, changed stock, full player inventory, and refund paths.
- [ ] Confirm trade cannot run on clients, at a distance, with dead players, or with non-friendly traders.
- [ ] Start an NPC moving toward a world drop, then trigger combat or fill its inventory; verify the loot goal is cancelled and movement returns to the higher-priority behavior.
- [ ] Observe an airdrop land; verify eligible mobile NPCs move toward it, compete only after landing, and do not duplicate items or bypass storage/inventory rules.
- [ ] Test airdrops with no eligible NPCs, a crate that disappears, and a crate in an inaccessible location.

## 7. Regression and performance
- [ ] Run for at least 30 minutes at caps 10 and 20; watch CPU/frame time, memory, repeated warnings, and entity cleanup.
- [ ] Repeat with the cap at 30 or 40 only after the 20-NPC run is stable.
- [ ] Save logs for any exception and test the smallest reproduction first.
- [ ] Confirm static NPCs and vanilla zombies remain unchanged.
- [ ] Do not consider multiplayer supported until dedicated-server spawning, ownership, replication, persistence, and interaction authority are implemented and tested separately.

## Current implementation caveats
- This code has not been confirmed by a Unity/game compile or an in-game test in this workflow.
- The population/persistence logic is intended for single-player first. A successful local spawn does not prove that the entity is correctly replicated or saved by the vanilla game.
- Trade currently exposes a server-authoritative service primitive; this checklist does not imply a player-facing interaction UI exists.
- Record pass/fail results rather than marking a test passed based only on static source inspection.

- [ ] Verify local obstacle probes do not treat the NPC's own CharacterController/child colliders as walls, and still detect nearby walls and other NPCs.
- [ ] Force a dense collider scene that fills the non-alloc obstacle-hit buffer; verify the complete fallback query still detects blocking geometry.

- [ ] Place an NPC with a tactical destination behind a small wall/obstacle; verify it chooses a short detour waypoint and resumes the original destination instead of replacing the brain goal.
- [ ] Move the destination while the NPC is detouring; verify the stale waypoint is discarded and steering tracks the updated goal.
- [ ] Place the NPC in a fully enclosed corner or blocked corridor; verify detour attempts are bounded, no per-frame ground searches occur, and the existing stuck-recovery behavior remains responsive.
- [ ] Keep a tactical NPC blocked until stuck recovery activates; verify it clears the failed detour waypoint and attempts a fresh detour rather than repeatedly selecting the stale point.
- [ ] Verify detour waypoint selection rejects unsafe ground, steep slopes, and positions blocked by nearby colliders.
- [ ] On uneven ground beside a wall, verify the ground-resolved detour candidate is re-probed from the NPC's actual position and rejected if that resolved route is still blocked.
- [ ] Compare detour candidates by both alignment and reduction in remaining goal distance; verify side-steps are allowed when needed but clearly regressive candidates are deprioritized.


## 8. Indoor navigation (not implemented yet)
- Current movement now attempts a bounded 9×9 local ground grid (1.25 m cells, at most 81 nodes) and reconstructs a multi-waypoint route around local obstacles; a single-hop detour remains as fallback. This is not map-wide pathfinding and does not guarantee routing through an entire building.
- The U3 SDK pathfinding abstraction can compile to `UnturnedPathfinding_Empty` when `WITH_ASPFP` is not enabled; do not assume a usable baked navmesh/path query is available in the target build. The mobile NPC implementation must not call undocumented pathfinding APIs.
- Implemented in source: bounded local grid search, safe-ground sampling constrained near the current floor, and physics probes along candidate edges. Still requires Unity compilation and in-game validation; multi-floor/stair transitions need a separate explicit validation pass.
- [ ] Verify the NPC enters a building through an open doorway instead of pushing directly into its exterior wall.
- [ ] Verify the bounded grid planner reconstructs a continuous route around a wall and advances through each waypoint without oscillation.
- [ ] Verify the planner does not select a node on a nearby roof/floor above or below the NPC; current-floor height filtering must be tested on multi-storey buildings.
- [ ] Place two blocking colliders that form a tight grid corner; verify diagonal route edges cannot cut between them when either adjacent cardinal cell is blocked.
- [ ] Put the preferred goal cell behind an unwalkable barrier; verify the planner chooses a reachable node on the NPC's side rather than a geometrically close node across the barrier.
- [ ] Dynamically block a waypoint after a route is created; verify stuck recovery clears the stale route and a later bounded search selects a different reachable path.
- [ ] Close a door or move another NPC into the next route segment after planning; verify the motor invalidates the stale route and retries through its cooldown-limited detour logic.
- [ ] Profile route planning with multiple NPCs blocked at once; each search must remain bounded to 81 grid nodes and route attempts must respect the retry cooldown.
- [ ] Verify it traverses an L-shaped corridor and a room with furniture, then continues to the original tactical destination.
- [ ] Verify a closed door or blocked passage does not cause an infinite search loop or per-frame route rebuild.
- [ ] Verify floor sampling stays on the NPC's current level and does not select the roof or a floor above/below it.
- [ ] Verify stair traversal between floors; if no safe transition is found, the NPC should report/abandon the route rather than oscillate between levels.
- [ ] Verify routes remain bounded in cost at population caps 10 and 20, including several NPCs navigating indoors at once.


- [ ] Route-grid occupancy regression: place the NPC in an open area and confirm its own CharacterController/child colliders do not mark neighboring grid cells as blocked; verify nearby walls and other NPCs still block cells.
- [ ] Route-grid occupancy overflow: densely populate the test area until the non-alloc overlap buffer saturates; confirm the full-query fallback still rejects cells occupied by external colliders.
