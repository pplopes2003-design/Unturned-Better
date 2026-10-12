# Mobile NPC — single-player validation plan

This checklist is for in-game validation of the mobile NPC foundation. It is not evidence that these tests have already passed. Run against a disposable world/save and record the game build, map, population cap, and console output for each test.

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
