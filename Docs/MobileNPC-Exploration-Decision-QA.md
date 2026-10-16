# Mobile NPC exploration and profession QA (2026-10-16)

Target branch: `feature/npc-ai-foundation`. These checks are **manual Unity Dev Build acceptance**, not proof that a build has passed.

## Integration contracts

- Archetypes (Raider, Trader, Survivor, Military) are computed from the existing **faction + disposition**; they do not replace the mobile NPC brain, combat, ItemManager, native navigation or individual memory.
- Normal population selects three professions, with the real `LightingManager.isNighttime` influencing the probability of fresh raiders; Combat population selects the two hostile factions. Existing/saved NPCs are not silently retconned when changing the menu mode.
- The current `MobileNPCAutonomyPlanner` picks Patrol/Scavenge/GuardSettlement/Recover based on genuine needs and acquired gear. Trader groups: actual leader tends the settlement, exploratory members collect, other members guard where available.
- The motor validates nearby native item-spawn *locations* and real structure geometry as inspection hints. It may not assume the item is currently present.
- **Reaching a waypoint != inspecting it != transferring a world drop**. The motor requests a native loot scan only after real arrival. `MobileNPCLooting` checks live registered items, LOS, scoring and inventory. The planner separately logs scans and authentic item transfers.
- Three physically completed, empty scans create a temporary scavenging cooldown; the NPC can patrol an alternative district rather than repeatedly inspecting the same empty floor.
- Courier delivery must occur within 3.5m of an actual living same-group vendor, when a physically selected surplus item can move from courier inventory to vendor inventory. Seller has finite stock; no magical loot, remote delivery or generic NPC item grants.
- Do not destroy `MobileNPCTraderLogistics` state upon combat interruption; it retries later only on genuine proximity, without copying memories or currency.
- Death, melee animations, ragdoll, vehicle use and physical shop building are separate acceptance tracks. This exploration change cannot claim they pass.

## On Washington singleplayer Dev Build

1. Set **Normal** mode and enable NPCs. Observe a newly spawned neutral NPC in a town. It should select scavenging and attempt local native spawn sites or building interiors (not blindly leave town).
2. Observe physical arrival and logs. Expect `[MobileNPCUrbanScavenge] selected an authored local loot-search site`, then `scan npc=` only after actual nearby scanning. The scan can correctly report `realLootGoal=False` when no item is visible.
3. Put a reachable, visible **real world item** near a native inspection route and observe pickup. Require `[MobileNPCUrbanScavenge] real pickup` plus an actual inventory change. A scan alone must not increase the real-pickup counter.
4. Test blocked door, furniture and stairway routes. No pickup through walls; stalled sites should be abandoned and remembered as inaccessible. After several empty physical scans, observe routine switching away from `Scavenge` during the bounded cooldown.
5. For traders, form an actual same-faction group with a physically established settlement; observe `MobileNPCAutonomyPlanner` jobs. Seller (living leader) should stay near the group settlement. Guards and collectors should not all select the same duty.
6. With a courier carrying >=10/12 valid inventory slots and a seller with free space, inspect logs for `[MobileNPCTraderLogistics] Courier ... returning physically` and `Real goods delivered`. Verify one real item is removed from courier and added to vendor, not duplicated. With no settlement, no seller, blocked approach or full seller inventory, delivery must NOT happen.
7. Confirm the trader's actual inventory items remain available for `MobileNPCTradeService`, not automatically emptied by `MobileNPCSettlementBuilder`.
8. Switch to **Combat** mode and test fresh automatic spawns: `[MobileNPCPreset] Spawned Military` / `Raider`; test raider-vs-military hostility and both factions' aggression toward the player. Existing NPCs retain saved faction/identity rather than being overwritten.
9. In a long session, monitor `MobileNPCHumanoidMotor.Update`, `MobileNPCLooting.Update`, number of physics probes and FPS. Urban spawn-point inspection is capped at 144 sampled points and 12 physical probes per decision. Optimize if stalls appear.

## Boundaries / next round

- The project does NOT yet guarantee a shop prefab or native vehicle driving. A settlement needs real building assets/items and successful construction before a seller can become stationary; this phase adds *logical* duties and stock delivery only.
- Native melee playback and articulated ragdoll changes need a new Dev Build and `Player.log` verification; source review alone cannot guarantee their appearance.
- Do not infer full feature success merely from a clean code diff. Unity compilation, Play Mode behavior, game-time visual observation and profile metrics are still mandatory.

## Multi-room and independent occupation acceptance (2026-10-16 follow-up)

- `MobileNPCOccupation` is a **separate personal occupation** from `MobileNPCFaction` diplomatic identity. The first automatic spawn usually maps Raider -> Raiders, Trader -> Traders, Survivor -> Scavengers, Military -> Military, but later occupation changes MUST NOT silently change faction, targeting or remembered group identity.
- JSON v8 persists both occupation and faction; loading older v1-v7 snapshots infers occupation from their original faction only once. Back up any valuable save before testing this migration.
- An authored urban item-spawn **cluster** near an actual large map object may initiate `MobileNPCInteriorSurvey`. A single isolated roadside spawn is insufficient. It tries at most four physically visited inspection sites; actual loot is optional, not guaranteed.
- Each site must follow `[MobileNPCInteriorSurvey] NEXT -> ARRIVED -> INSPECTED`, with the ordinary motor, ground/capsule and native door/stair routing. A `NEXT` line alone is not success. The loot scanner and completed ItemManager transfer remain separately logged.
- Occupied or invalid floors, closed/locked doors, furniture and walls must not be bypassed. A failed route records no inspection success. Combat interrupts the route and may later resume the routine, but must never classify the interrupted survey as fully completed.
- On completing at least two genuine inspections and exhausting/budgeting the cluster, expect a temporary personal 120-second survey memory (`Retired surveyed cluster`). Other NPCs do not magically learn the location was empty. Persistence stores the remaining cooldown, not an obsolete Time.time timestamp.
- Test upstairs and downstairs loot points in the same building; separate floors must not be merged into the same visited-point slot. Multi-floor traversal remains subject to real native stairs/ladder and may fail on maps without valid navigation access.
- Observe FPS in dense Washington city blocks with 20 and then 40 NPCs. Surveys are initiated by optional roaming decisions, limited to nearby map regions, with 96 spawn candidates and at most 10 grounded/capsule probes per room decision. They must not run every frame.
- After a mode or occupation change, verify factions remain in control of player hostility and NPC-vs-NPC diplomacy. The combat-mode population choices apply to **fresh automatic spawns**; old saved actors keep their own faction and profession.

**Static source cross-reference checks are NOT a Unity compilation or playtest.** Inspect the first new `Player.log` for compiler errors, spawned actor profession/faction audits, survey state transitions, successful ItemManager transfers and main-thread profiler cost.
