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

## Interior routing and local block QA (2026-10-16 navigation pass)

This pass improves **grounded candidates and doorway evidence**. It does not generate room polygons or assume a passage exists merely because a room contains authored loot spawns.

1. In a dense Washington city block, enable Normal NPC population and observe that the current `LevelItems.spawns` region is inspected **before** neighbors. Per-region candidate quotas avoid spending the bounded search budget outside the NPC's current area.
2. Watch `[MobileNPCInteriorSurvey] BEGIN / NEXT / ARRIVED / INSPECTED / END` in `Player.log`. A successful route must end in actual destination arrival and a completed scanner callback; a `NEXT` assignment is not a successful room visit.
3. The next inspection candidate receives a score based on nearby actual map-authored item spots, physical ground/standing space, floor difference, personal visit history, and a short physical door/wall assessment. A wall is a **penalty**, not a fictitious locked portal or an automatic no-route verdict.
4. Test an authentic player-built **locked door** and an **unlocked registered native door**. The survey never changes door state. Only `MobileNPCDoorNavigation` may request opening an unlocked and registered door, when the motor is physically near and blocked by that door. A closed locked door may require a different physically valid route; never walk through it.
5. Test stairs and small upper floors: a different level is allowed as a **candidate**, but the motor must establish a physically traversed stair/ladder or valid native path. No vertical teleport and no fake "entered second floor" event.
6. Spawn inside a building with the NPC's own capsule near a candidate: `IsValidScoutingFootingServer` must ignore the NPC's own CharacterController but reject an actual occupied location. Test a floor, shelf and narrow corridor.
7. Confirm a staged survey is **not interrupted by optional random roaming** while waiting for next-room candidate admission or teammate sector release. Enemy combat/medical/tactical goals must preempt immediately, however.
8. When a trader with a loaded pack has a real vendor at an established nearby base, the physical **delivery route outranks optional room searches**, and the transfer still requires genuine proximity and authoritative inventory movement.
9. Profile many NPCs (start 10, then 20, then 40) in Washington. The local multi-room pass uses at most 96 authored hints and 10 ground/occupancy probes per survey decision, with a 75ms shared admission spacing between NPCs. Monitor `Physics.Simulate`, NPC motor time, and the log for excessive `BEGIN/END` cycling.
10. Unity script `.meta` GUIDs for `MobileNPCInteriorSurvey`, `MobileNPCOccupation`, `MobileNPCTraderLogistics` and `MobileNPCArchetypePolicy` are now tracked in GitHub. On checkout confirm the imported scripts have stable GUIDs and no missing-script warnings.

**Not proven until the user runs a Windows Unity 2022.3.62f3 Dev Build:** C# compilation, actual navigation through Washington house doors/stairs, absence of stuck loops, loot pickup, death/ragdoll/melee presentation and frame-time. These tests cannot be substituted by a text-only inspection.

## Player(4).log postmortem and critical repair acceptance

**Observed in Windows Test3 Washington (2026-10-16):**

- Four fatal-physics setup events: `Invalid ragdoll parent hierarchy or bone scale at Skeleton`, immediately followed by `authored=False, bodies=0, colliders=0, joints=0` and **rigid capsule fallback**. The prior appearance of a physically rolling block is NOT an articulated ragdoll. Fix: compare actual skeleton anatomical descendants, but allow the legitimate different outer prefab parents of `Ragdoll_Player/Skeleton` and `NPC_Client/Root/Skeleton`.
- `native-aspfp-backend-unavailable` and many `Pathfinding.NavmeshCut` missing-script asset messages. The public SDK has no licensed ASPFP source; the local motor must use its existing physically validated grid/route fallback, including narrow passage checks. This change does **not** add the absent proprietary runtime backend to a shipped build.
- 207 `FALL DIAGNOSTIC` messages, many with `grounded=True` and visible terrain `BLOCK_COLLISION` contact: descent from elevated spawn was falsely logged as a fall. The motor now avoids expensive emergency ground recovery when the authoritative CharacterController is already grounded. A genuine below-ground, non-grounded fall still follows the safety check.
- `MobileNPCInteriorSurvey` entries: 9 BEGIN, 7 NEXT, **0 ARRIVED, 0 INSPECTED** and 3 route-failure ENDs. We must distinguish a candidate assignment from physical entry/inspection. Existing native door/stair and fallback grid still need real traversal tests. The new facade contour samples only physically verified lateral standing positions after an observed local wall stall; it does **not** walk through geometry, unlock doors, or guarantee window vaults.
- NPCs took successive genuine `Zombie` melee hits until death; these are real authoritative vitals, not invulnerability or visual-only blood. New `MobileNPCZombieRiskAssessment` records only personal recent vision and close-range awareness (six short-lived positions, 3s memory), scores local future destinations and grid nodes, and requests a physically reachable short RETREAT goal when unarmed, badly injured or outnumbered. The old perception/brain/combat continue to operate.

**Mandatory next Unity Dev Build acceptance:**

1. Kill an NPC on open flat ground and on uneven terrain. Require `Authored native articulated NPC ragdoll... bones=11, joints=10` followed by `Death ragdoll activated: rigidbodies=11, colliders=11, joints=10`. Check actual limp, separately moving arms, legs, torso; not a rolling rigid capsule. If hierarchy still fails, provide the new diagnostic with `sourceParent` and `targetParent`; do NOT claim success from a rigid falling body.
2. Attempt a real unlocked native barricade door and an authored open doorway. Confirm NPC crosses the threshold, rather than just being assigned a loot waypoint. Closed locked barricades must remain closed. Windows may be used only if `MobileNPCVaultTraversal` validates the entire physical ballistic arc and a safe landing; this pass **does not promise universal window climbing**.
3. Test an NPC spawned in a town against a group of 3+ zombies, then an isolated zombie, and test both armed and unarmed variants. Look for `[MobileNPCZombieRisk] RETREAT` when heavily disadvantaged and for alternative verified routes rather than blindly charging. Ordinary healthy armed NPCs can still choose to fight.
4. Record `BEGIN -> NEXT -> ARRIVED -> INSPECTED` **at least once** per truly reached local survey point. Zero ARRIVED means the routing bug persists even if the job planner appears active.
5. Profile 10, 20 and 40 NPCs. Risk assessment scans nearby live zombie positions ~0.55-0.85 seconds/NPC, optical LOS is shared-throttled, and route hazard scores use only short-lived cached observations. Ensure no main-thread spikes from new checks.
6. Confirm missing `Pathfinding.NavmeshCut` warnings are still understood as asset/runtime dependency, not fixed by this source-only patch. Do not remove native scripts from original Unturned bundles.
7. Inspect the new `Player.log` and Unity compilation warnings before merging or calling the ragdoll or interior traversal finished.


### Addendum: source-verified prefab matching and defensive exploration

- Verified YAML `Assets/Resources/Characters/Ragdoll_Player.prefab`: 11 rigid bodies, 11 box colliders, 10 CharacterJoint components. Matching render bones in `MobileNPC_Client.prefab`: Right_Arm, Left_Arm, Right_Foot, Right_Leg, Left_Foot, Left_Leg, Skull, Spine, Right_Hand, Left_Hand, Skeleton. **Only Skeleton has a different parent name** (`Ragdoll_Player` vs `Root`). That confirms the prior root-parent error was an incorrect validation condition, not a fundamentally different NPC bone rig.
- Added a Unity EditMode/NUnit regression test `MobileNPCRagdollPrefabTests` (under `Assets/Editor/Assembly-CSharp-Editor/Tests`). Run it in Unity's Test Runner before a costly Dev Build. It checks all 11 mappings, the 10 native joint links, valid collider anatomy and the inert NPC visual-root Rigidbody. It is a PREFAB inspection test; it does not simulate the death ragdoll or replace the in-game physics test.
- `MobileNPCZombieRiskAssessment` now compares the nearest three distinct nearby live zombies for each admitted local visual assessment, not just a single nearest that could be hidden behind a wall. Even close contacts must meet the obstruction test. Empty areas do not consume the global optical budget. At most eight locally observed positions remain in finite short-term memory.
- Scavenging objectives are now postponed when the immediately predicted walking leg INCREASES danger toward personally observed zombies; postponement does not count as visiting or inspecting that room.
- After actual stalling at a static building face, contour probing now examines several short offsets on BOTH sides. It favors a physically clear opening; more importantly, a partial A* frontier that still stops outside a wall no longer automatically suppresses this verified facade search.
- **Remaining unresolved**: the licensed ASPFP graph dependency in `Test3`; generalized long-range entrance/room connectivity; collision-certified jumping/crouching through diverse window shapes. Do not claim these are resolved from source edits alone. No Unity runtime test was executed through the GitHub API.


## 2026-10-18: Player-confirmed death/punch; zombie melee and genuine urban entries

**Player's dev-build confirmation:** ragdoll now moves as intended; punch animation now works. The remaining *observed* regressions are: NPC punches while walking backwards with its back to a zombie; NPCs do not find doors/windows and get stuck entering or leaving houses.

### Code changes to check in NEXT dev build

- `MobileNPCHumanoidMotor`: the live, first-hand observed zombie remains the facing target when close, independently of the actual CharacterController movement direction. If unarmed/no working gun and inside real melee reach, stop suicidal forward pursuit, or execute a **collision/ground-validated 1.05m backward step** while the punch is on cooldown. This is not a teleport or global zombie tracking.
- `MobileNPCHumanoidCombat`: zombie melee checks a forward 0.72-dot facing arc before hitting. An unalerted zombie approached from behind can receive a **single opening skull strike** with the *native* `ELimb.SKULL / ELimb.SPINE` multiplier of the actual melee weapon (or matching native unarmed `ZombieDamageMultiplier`); after a surviving surprise hit, call the existing `Zombie.alert(MobileNPCHumanoidVitals)` so the NPC does not repeatedly take free unaware crits. No instant-kill cheat or arbitrary multiplier.
- `MobileNPCDoorNavigation`: in addition to native player-built `InteractableDoor` / `BarricadeManager`, accept map-authored `InteractableObjectBinaryState` **only** when the authored `EObjectInteractabilityHint.DOOR` matches, object is a live registered `LevelObject`, no remote, power or quest conditions, within physical interacting distance, and its actual collider is first in front of the NPC. Open through authoritative `ObjectManager.forceObjectBinaryState` so native state, animations and net replication apply. Local-route edge/node classification also permits only such a registered door, never wall+door mixed overlap. Still no forced lock or through-wall bypass.
- `MobileNPCHumanoidMotor`: physically observed facade contour recovery after a true navigation stall now works for **combat and exploration** instead of only `Job.Scavenge`. A candidate opening must have full upright capsule clearance and a verified same-level floor: a chest-height ray through a window is no longer mistaken for a standing passage.
- `MobileNPCHumanoidMotor`: a *separate* bounded low-opening probe can recognize a floor-level crawl-through when a real full-size standing capsule is blocked but the actual crouched capsule is clear, and a solid near-level floor exists beyond. It changes the real CharacterController stance, never turns off geometry. This does **NOT** solve raised-sill windows. These continue to need the existing real jump/vault planner, including valid clearance and landing.

### Focused test order

1. On Washington, approach a lone zombie in open ground. NPC with a knife should **face** its zombie while stepping backwards, then land strikes only from its actual frontal facing. It must not hit a zombie positioned behind its back simply because it is inside range. Repeat when retreating under low health.
2. Approach an unsuspecting zombie from behind: verify `[MobileNPCMelee] Rear zombie head-opening` is logged only once per encounter and that non-fatal contact causes zombie pursuit. Compare damage with a regular frontal knife hit. Test fists too.
3. Place a NPC within 2m of a closed ordinary **map object door** (not just built barricades). Give it a target beyond: verify `[MobileNPCDoorNavigation] Native map door requested open`, native door animation, then actual collider-supported crossing. An object `SWITCH`, powered door or scripted quest door must **not** open from an NPC scan.
4. Test a closed player-built unlocked door (existing `BarricadeManager` path) and a locked player-built door. Only the unlocked one may open. Nearby collision with the door frame plus the door must not be mistaken for clear passage.
5. Test an already open door and an open ground-level low opening: only permit through-movement if the actual controller fits. For a low door/window opening, inspect `[MobileNPCLowOpening] Physically checked crouch-through`. A solid window sill must never become an imaginary walking/crouch portal.
6. Spawn a zombie inside and NPC outside, then reverse both positions. Test navigating to/from the house with native ASPFP absent, capturing route/stall logs. A partial A* frontier outside the wall does **not** count as reaching the room or finding an entrance.
7. Profile near-house scenarios with 10, 20 and 40 NPCs; local low-opening scan is globally budgeted. Record Unity warnings and whether CPU time rises due to `ObjectManager.tryGetRegion` registration, capsule checks or facade recovery.

**Not validated in Unity in this source-code round.** Multi-room path finding without ASPFP, upstairs transitions and general window sill vaulting still remain open. The user's confirmed ragdoll/punch success is real playtest evidence for the old bug, not proof of these new changes.

## Corrected requirement: EMPTY architectural doorway / portal, NOT an interactable door (2026-10-18)

The player clarified that the "doors" they report are **doorless openings in map-built houses**; normal Unturned POIs often have a gap framed by static walls, with NO `InteractableDoor`, `InteractableObjectBinaryState`, or other entity to click. Existing built-barricade and binary-state door interaction handlers may remain for the rare relevant asset, but **they are NOT the fix for these ordinary house entrances**.

### Source-level gap analysis and corrections

- The native ASPFP backend can be absent in the custom SDK build. The fallback is a fixed 9×9 local grid, normally 1.25m between cells. An empty doorway narrower than its grid or located beyond its grid boundary is not consistently found. A partial path ending directly outside a wall is not evidence of entering the house.
- New `MobileNPCHumanoidMotor.TryPlanObservedBuildingThresholdServer` is attempted **only after genuine lack-of-progress evidence** (`consecutiveStuckRecoveries`), through the existing throttled detour pipeline and a globally throttled gap scanner. It starts by **physically contacting a wall**, then searches at most 5.85m along both tangents. A candidate passage requires same-level, supported floor on BOTH sides, full-size capsule clearance through the threshold, mid-passage floor continuity, map bounds and no false furniture support. It never invents a door, changes native map object state, disables colliders or teleports.
- The NPC commits a collision-checked **two-sided threshold route**: get to an EXACT physically accessible exterior/interior approach, then traverse the measured opening to the verified far side. It can do the reverse trip using the opposite wall normal. An approach blocked by other geometry is rejected; at most one alternative candidate is attempted per admission. Near/far threshold waypoints require within **0.4m** instead of the usual 1m, and the general corner-skip optimization cannot jump over their alignment waypoint.
- Previously an upper-body-only blocker ray could miss a *solid window sill*, preventing the detour planner from being invoked at all. A **measured motor stall** now requests replanning even if the chest probe is clear; a second 0.42m low-height cast observes the actual sill. A raised window is NOT silently converted into a walkable door: its full-body capsule sweep must still reject it. Authentic elevated-window vaults continue as a separate unsolved feature.
- Source marker: `[MobileNPCGeometryThreshold] Valid physical gap npc=... approach=... beyond=... goal=...` in development builds. This marker proves ONLY accepted geometric checks and a planned route, **NOT** successful physical arrival. True crossing requires visual observation / movement positions and ordinary `MobileNPCInteriorSurvey ARRIVED` and `INSPECTED` when that job is enabled.

### Exact Dev Build acceptance tests

1. **Empty frame outside → inside:** use a Washington house doorway without a physical door. Spawn an NPC outside and give it a real pursuit/loot goal inside. It must approach the actual empty entrance, pass between the wall colliders and physically reach the inside location. Watch gap scan logs AND position over time.
2. **Inside → outside:** place a pursuit target outside with NPC inside the same house. It must discover the same geometry from the interior face and pass the exit without teleporting.
3. **Aperture narrow vs 1m waypoint arrival:** pick a tight open door frame; the NPC must align at the approach to within 0.4m, not skip it because an A* cell is within 1m of the threshold.
4. **Solid wall and raised window:** with a window opening but intact lower sill, the measured low cast may detect the obstruction; the full-height threshold classifier MUST reject passage in the standing route. Only physics-verified vault could allow that route, and it is not promised to work universally.
5. **Distant doorway and interior furniture:** a gap >5m sideways may require multiple physically grounded contour hops. When two gaps are near, a blocked near-side approach to the first must not suppress checking one alternate gap. Furniture/rubble must not be classified as walkable doorway.
6. **Combat:** test with a zombie target behind walls and same-room navigation. Do not permit attacking unseen opponents through solid walls; investigate sounds only within existing perception.
7. **Performance:** test 1/10/20/40 NPCs at houses, compare CPU spikes and memory. Physical threshold scans are admitted once about every 0.32s globally and once per 2.0s per eligible NPC. No proof of 60 FPS without profiler.

**Status:** source committed; the user has NOT yet tested this new geometry-threshold route in Unity. The user's existing positive ragdoll/punch playtest stands, but doorless ingress/egress, raised window vaulting and broad multi-room routing remain runtime-unverified.
