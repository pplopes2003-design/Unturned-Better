# Mobile NPC radio communications — architecture, implementation history and limits (2026-10-13)

**Status (latest): portable and improvised electrically powered base-radio text endpoints connected in source, awaiting Unity Dev Build. COM-02/COM-03/COM-04 parents remain unfinished.** This file records requested requirements and interface constraints for future agents. Do not treat radio access as a passive skill, magically shared faction memory, or a substitute for line-of-sight/audio evidence.

## Why it exists

NPCs currently share approximate player sightings only face-to-face within a **35 m** group voice range using `MobileNPCGroupMember.BroadcastPlayerContactServer`. Future radio communications must expand range across a group or faction **only** when each communicating endpoint has an eligible physical device, appropriate power, and a working connection. Group/faction affiliation alone must never imply infinite-range telepathy.

## Device-dependent communication

| Endpoint | Acquisition and ownership | Power and operation | Notes |
| --- | --- | --- | --- |
| Portable walkie-talkie | Actual portable radio item in the NPC inventory, assigned and equipped/usable | Real durability, battery/charge rules **after checking native Unturned device assets/APIs** | Must verify radio item ID and power rules in game code; do not fabricate an asset ID or battery system |
| Fixed base radio | Actual placeable radio/receiver at a faction group settlement; build from valid blueprint/items through `MobileNPCSettlementBuilder` | Must check native placement, powered status, generator/fuel/electricity links and destruction | No broadcast if not constructed or not powered; request integrates radio construction into faction/base planner |
| Vehicle radio | Vehicle with valid fitted radio and accessible power | Depends on actual vehicle/battery running state and range | **Future vehicle NPC support** must precede this; no invented vehicle ownership/navigation or radio APIs |

Communication requires a *sender endpoint* and a *receiver endpoint*: functional hardware, power, matched network/channel or known faction contact, plausible range, and authorization. Support radio silence, channel changes, interference, cooldown, limited NPC airtime, and interception if game mechanics allow. Design explicit player counterplay, e.g. destroying power/source radios and blocking communications.

## Message types (structured, not global truth)

- **Contact/threat report**: approximate location, source, timestamp, confidence, faction, report age, hostile class, request for covering fire or caution.
- **Call for help/reinforcements**: wounded/under fire, rally area, urgency, squad count; dispatch only from available living NPCs/groups that have compatible faction/allegiance, reachable path, and valid loadout; do not spawn free units without population budgets.
- **Loot location**: type, rarity/utility, approximate coordinates, last seen time, confidence, possible contest; the receiver rechecks for item existence and may find loot already gone.
- **Airdrop report**: sighting of incoming/landed airdrop, contested or possessed by sender; distinguish observed sight from guessed or outdated intel; coordinate `MobileNPCAirdropContest`.
- **Unknown base**: approximate observed location, estimated threat, unknown ownership until verified, optional scouting order; prevent a radio relay from assigning a magically known owner.

## Event chain / strict guardrails

1. **Evidence generation** (local sight/verified action or valid ally narration) => time-stamped `MobileNPCIntelReport` with source, kind, last-known region, uncertainty, confidence, trust, idempotent message ID and TTL.
2. **Radio capability check**: `MobileNPCInventory` for portable item OR reachable powered `MobileNPCSettlementBuilder` placement OR later validated vehicle radio. Confirm actual item/assets APIs before implementation.
3. **Transmission**: sender consumes radio operation resources and bandwidth; send over channel only within actual device topology. Loss of power or device immediately aborts link.
4. **Receive filter**: recipient must have appropriate device/network, allegiance, hearing/operational permissions, not dead, and capacity to process. Apply delay, jitter, age-based confidence reduction and duplicate suppression.
5. **Decision consumption**: `MobileNPCPlayerKnowledge` / later generalized world knowledge stores **uncertain report**, not a live Player Transform. `MobileNPCBrainDriver` can investigate or request aid; combat still requires sight and ordinary damage constraints. Civilian or nonhostile factions must not become hostile solely because a message exists.
6. **Persistence**: individual knowledge dies with NPC unless explicitly shared; messages may persist with receiving individuals or base terminals if powered and configured. A disconnected radio does not guarantee a delivery.

## Validation checklist

- No hardware: no long-range radio messages.
- One endpoint unpowered/absent: no communication.
- Walkie-equipped living sender + valid distant receiver + matching channel: limited transmission with actual range/TTL.
- Base placement built from inventory/crafting and powered generator; loss of fuel/power breaks relay.
- Different factions and channels: no unintended message leakage or reinforcement.
- Loot removed or airdrop already taken: stale report expires, recipient verifies actual presence.
- Radio reports cannot trigger raycast attacks or high-accuracy tracking through structures.
- Test reinforcements against population caps, faction hostility and group survival/dissolution.
- Vehicle radios remain disabled until vehicle support is operational.
- Profile radio broadcasts to prevent all-world NPC scans every frame.

## Existing code to integrate

- `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCGroupMember.cs`: existing 35m spoken group contact reports and temporary Covering/Advancing roles.
- `MobileNPCFaction.cs`, `MobileNPCHumanoidDisposition.cs`: relationship authorization.
- `MobileNPCInventory.cs`: item snapshots and typed inventory (do **not** guess radio asset IDs).
- `MobileNPCSettlementBuilder.cs`, `MobileNPCSettlementPlanner.cs`: base placement and energy dependency validation, once actual powered radio asset is confirmed.
- `MobileNPCAirdropContest.cs`: airdrop intel.
- `MobileNPCBrainDriver.cs`, `MobileNPCPlayerKnowledge.cs`: uncertain evidence consumption.
- Future vehicle system: equipment install/ownership/power/access.

## Implementation order

1. Verify actual Unturned portable, placeable and vehicle radio assets plus native radio/energy APIs and constraints.
2. Add report data model, channel and trust validation; tests independent of world.
3. Implement portable radio capability and limited sender/receiver link.
4. Add physical base radio as a settlement construction objective; power-aware range relay.
5. Add structured contact, reinforcement, loot, airdrop and base messages plus rate limits.
6. Only after NPC vehicles work, add vehicle radio endpoints.

**Explicit user request:** a complete architecture diagram of NPC AI is due only after all major AI subsystems are structured. This radio design is a future module in that diagram, not a completed feature.

## Confirmed native Unturned mechanics (research, not validated in this Unity branch)

- Vanilla **Walkie Talkie ID 1445**, configured as a `Tool` using `Useable: Walkie_Talkie`. Unturned supports selectable walkie channels/frequencies from the inventory and cross-map voice with compatible equipped radios; don't add an artificial 100m/500m attenuation range to a vanilla walkie without evidence. Official tool asset docs: https://docs.smartlydressedgames.com/en/stable/items/tool-asset.html ; item catalog: https://restoremonarchy.com/browser/vanilla/item/tool/1445 .
- Vanilla **Earpiece ID 1446** is receive-only radio listening equipment; can be used for player interception if tuned to the NPC channel. Source: https://unturned.gameinfo.io/items/1446-earpiece and https://unturned.fandom.com/wiki/Communication . Verify exact local `PlayerVoice`/`PlayerEquipment` hooks and channel representation in this game's runtime; neither has been successfully fetched from the checked-in branch.
- **Important correction:** do not invent battery drain for vanilla portable walkies merely for realism. Respect actual game's portable-radio mechanics unless adding a clearly separately configured survival-extension feature. Conversely, placeable base radios and vehicle radios should only operate if confirmed native objects/components exist; otherwise treat them as future modded devices with explicit construction/power rules, not assumed vanilla features.
- Player intercept concept: a player with **actual suitable earpiece or walkie**, set to the same native channel/frequency, can receive intercepted NPC voice/text; wrong frequency, missing receiver or dead transmission source yields nothing. NPC subtitles should identify the radio speaker, not leak exact coordinates; they can be accompanied by short beeps/noise when actual sound assets are available. **Do not inject arbitrary plain-area UI outside the native channel check.**
- Potential transcripts: `[R1] Contato suspeito no setor 4/7, confirma?`; `[R2] Negativo, indo verificar.`; `[R1] Airdrop avistado, solicito reforços.` Use `MobileNPCRadioMessage.FormatInterceptedSubtitle` templates, grounded in actual reports. Text is a player-readable representation of synthetic NPC speech, not actual microphone VO data.
- Native vanilla voice broadcasts may cover the map regardless of distance. Keep any device-based range restrictions tied to **verified** model or mod extension; don't contradict the chosen equipment's game mechanics.

## Current code work (2026-10-13)

- `MobileNPCRadioMessage.cs` stores bounded data-only reports with confidence, expiry, faction, group, call sign and five kinds: EnemyContact, NeedReinforcements, UsefulLoot, Airdrop, UnknownBase. `FormatInterceptedSubtitle` creates simulated conversational text with coarse 100m map sectors, never precise player location.
- **No code yet** bridges these reports into the native Unturned radio channel, inventory/equipment detection or player HUD. This file must not be considered proof of working interception.
- Voice-only group contact messages are separately implemented in `MobileNPCGroupMember`; do not confuse local voice with broadcast radio.


## 2026-10-13 — Radio report preparation implementation (NOT transmission)

- New `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCRadioOutbox.cs`: non-transmitting server-only outbox. Requires `MobileNPCInventory.ContainsItemID(1445)` and a living NPC with a real group ID. The radio must be owned; inventory ownership by itself does **not** yet prove the radio is held/equipped or that a native frequency is selected.
- Reports are created from server-confirmed player sightings only (through `MobileNPCBrainDriver.ReportVisibleTarget`), at most once every 20 seconds, maximum four queued records per NPC, 24-second expiry. `MobileNPCPopulationBootstrap` now adds the component to the runtime mobile humanoid template.
- `TryPeekFreshReport` and `ConfirmTransmittedReport` are internal handoff operations for a **future channel-verified transport**. They do not send voice/chat or text to players and do not change NPC combat behavior. Messages may disappear when NPC dies/unloads.
- Next requirement is a verified native player receiver/equipped item/frequency check before a message is ever shown or delivered. Do not bypass this by sending `PlayerUI.message` or chat to every player. Compare native Walkie Talkie and Earpiece semantics with this game's actual runtime APIs.
- Existing report templates in `MobileNPCRadioMessage.cs` contain simulated Portuguese dialogue to show only after native receiver validation succeeds.

## Separate user addition: zombies beyond points of interest

The user wants zombies to remain a credible threat **outside the POI/spawn region**. Add this to the persistent backlog, distinct from NPC radios. Native `Zombie.cs` and `ZombieManager.cs` are not directly accessible at the expected repository paths on this branch; do not invent field names or bypass native network/region sync. Study current zombie chasing/target retention, activation and unloading across region boundaries, leash / return-to-spawn logic, LOS/hearing event response, streaming and host authority, pathfinding distance and performance; compare whether a zombie should **continue an existing chase** vs spawn/wander globally. Require finite loss-of-target policy, safe despawn/respawn, multiplayer compatibility and performance bounds. Dev Build tests: lure a zombie away from POI, cross one/multiple map regions, maintain/lose LOS, sound pursuit, nighttime/environmental scenarios, unloaded region transitions, vehicle noise, NPC group encounters, and how far chasing remains fair. Do not claim zombies already follow seamlessly outside POIs.

## 2026-10-13 — Portable radio text V1, source only

Native evidence in local U3 SDK: `PlayerVoice.canHearRadio` checks equipped walkie or worn earpiece. `UseableWalkieTalkie.equip/dequip` toggles the transmitter state. `PlayerQuests.radioFrequency` is a uint, default 460327, and native `PlayerVoice.handleRelayVoiceCulling_RadioFrequency` requires equal listener and speaker frequencies.

`MobileNPCRadioOutbox` now transmits ONLY a server-observed EnemyContact message from an NPC owning real item 1445. Group frequency is `PlayerQuests.DEFAULT_RADIO_FREQUENCY + (uint)(GroupId % 100000)`, so runtime group 1 uses 460328. Same-group living recipients require their own real 1445, matching faction and channel; receive uncertain/clamped `ReportGroupPlayerContactClue` rather than a visible enemy Transform. Fixed 1.25s shared airtime / 8s per-NPC interval, 20s enqueue cooldown, 24s expiry, no fabricated batteries or radios.

`MobileNPCRadioInterceptHUD` is an independent single-player-host passive subtitle. A living local player must have real `player.voice.canHearRadio` (equipped walkie or mask earpiece 1446) and precisely matched `player.quests.radioFrequency`. Tuning away/removing gear immediately hides text. It does not alter native chat, cursor, interaction HUD or use spoofed native voice NetIds. Only simulated **text** is implemented, no microphone/audio/multiplayer relay. NPC has no PlayerVoice: operating the owned radio is simulated, not a visible equipped-hand prop. Transmissions log limited radio diagnostics. World reload resets registry and airtime.

Scoped `COM-RADIO-PORTABLE-01` is QUASE PRONTA — AGUARDANDO BUILD, source only. Parent `COM-02` EM IMPLEMENTAÇÃO for true NPC hand equip, audio/native channel networking and further message kinds. `COM-03` fixed powered base/vehicle radios and `COM-04` calls for reinforcements/loot/airdrop/base remain open.

Final Dev Build must verify actual ID 1445 inventory ownership, two separate equipped NPCs, same group vs different group, native player equipped 1445 or worn 1446 vs wrong frequency and unequipped inventory radio, removing radio, dying, time expiry, save/map swap, no UI takeover, profiling at 20–40 NPCs. No runtime tests performed.

**Actual acquisition improvement:** source commit `a3ea3d67fddc010efe4d753eb57e57235dc7c7e4` modifies `MobileNPCLooting.ScoreItem` so a surviving group member without item 1445 values a REAL nearby walkie world drop at moderate score 62, below critical medicine or first gun; it still needs real ItemManager spawn, reachable path, an inventory slot and transactional pickup. Duplicates keep existing penalty. No free radio equipment on initial spawn, no synthetic supply.

**Subtitle revocation:** `f84f390f8cfff3f89e512a766b890651b019a3ef` clears intercepted text permanently upon listener gear/frequency mismatch or death, not merely hides it until retuning. It only reappears for a NEW in-band transmission. Validate rapid channel and earpiece toggling in final build.


## 2026-10-13 — Actual airdrop sightings enter radio outbox

- `MobileNPCRadioOutbox.TryQueueObservedAirdrop` accepts a nearby (28m max) real landed airdrop and enqueues an `Airdrop` message (45s expiry, actual walkie item 1445 required, group/faction, existing cooldown).
- `MobileNPCAirdropContest.TickServer` calls it only for a real carepackage storage with contents and only when the NPC itself is within 28m. The broader 220m loot contest scan does not by itself justify a radio message.
- Existing `MobileNPCRadioOutbox.Update` can transmit the resulting simulated text to same-group tuned radio owners and local host player equipped/tuned through `MobileNPCRadioInterceptHUD`. Still no native microphone audio or multiplayer client subtitle bridge.
- **Status:** COM-04 airdrop subfeature is SOURCE-IMPLEMENTED / **QUASE PRONTA — AGUARDANDO BUILD**. COM-04 as a whole remains OPEN (real reinforcements, valuable loot and unknown-base observations).
- Tests: discover landed crate while equipped, no owned radio, far-away scan vs near observation, player tuned vs untuned, empty crate, repeated observations and TTL, faction restrictions, correct subtitle and no duplication. No Unity runtime validation yet.


## 2026-10-13 — COM-02 static integration review / radio queue hardening

**Review commit:** `9a500877d9969b4a222455a0b455f15e7d4279cc`, branch `feature/npc-ai-foundation`. Source was reviewed against the checked-in native `PlayerVoice.canHearRadio`, `PlayerQuests.radioFrequency` and `DEFAULT_RADIO_FREQUENCY` (460327), and the real `MobileNPCInventory.ContainsItemID(1445)` path. **No Unity build, C# compiler execution, gameplay testing or native microphone integration took place.**

- NPC group channel now equals `DEFAULT_RADIO_FREQUENCY + (uint)GroupId` for a positive runtime group ID. The old `% 100000` mapping shown in earlier historical sections is **superseded**: it could assign identical frequencies to different groups. Group 1 remains 460328. Runtime group IDs are remapped on restore, so the current channel can change after a reload; it is not a permanent real-world saved frequency. The player must tune to the currently assigned channel.
- Transmission still requires a living source NPC and a **real owned** walkie-talkie 1445; a receiving NPC also needs its own living/group/faction-compatible endpoint. Unturned's portable walkie does not acquire an invented battery requirement. NPCs have no native `PlayerVoice` or `PlayerEquipment`: usage is explicitly **simulated**, without visually equipping the item or emitting native voice packets.
- Fresh outbox reports now discard expired records **and records from a former group or faction**. This prevents a stale old-group report from blocking newer observations or being replayed on the newly assigned channel. The source faction component is required before queueing a contact report.
- The single-player player intercept is still conditioned on **equipped** walkie 1445 or **worn** earpiece 1446, matching `PlayerVoice.canHearRadio`, matching native `PlayerQuests.radioFrequency`, and living player state. Tuning away/removing receiver invalidates the currently visible subtitle; no remote multiplayer subtitle replication is claimed.
- Contact message consumption is implemented as an uncertain clue, not real visual acquisition. **Important COM-04 scope correction:** airdrop sightings currently create and transmit an eligible simulated message that the local player can intercept, but the distant NPC receiver does **not** yet turn the airdrop packet into a remembered, path-validated loot/investigation objective. The earlier claim of the entire airdrop subfeature being 'almost ready' applies **only to observation/enqueue/text transmission**, not remote NPC action. Reinforcement calls, useful-loot reports, unknown bases, base electricity and vehicle endpoints remain unfinished.
- Source review confirms referenced symbols and call chains, and a basic delimiter check was performed on the changed file. This is **not** proof of compilation, Unity prefab integration correctness, functional frequency-selector UI, or acceptable runtime performance.

**Status:** scoped `COM-RADIO-PORTABLE-01` **QUASE PRONTA — AGUARDANDO BUILD** (text-only, group/faction/equipment-gated host single-player); parent `COM-02` **EM IMPLEMENTAÇÃO** (NPC hand visuals, audio and native/networked transport outstanding); `COM-03` **NÃO INVESTIGADA** for verified native powered base radio; `COM-04` **EM IMPLEMENTAÇÃO** (partial airdrop report producer/text transport only; recipient behavior and other reports pending). Next planned work: research actual placeable base power/radio assets **before** writing a fictitious powered radio, then separately add bounded reinforcement/intelligence consumers without magical spawning. Tests in section 39 and its static-review addendum of `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md`.


## 2026-10-13 — COM-BASE-PHYSICAL-01: real-items-only powered base station (source milestone)

**Implementation commits** (branch `feature/npc-ai-foundation`): `7c4960b1` (live group leader lookup), `a46dea22` (own real base storage + generator slot), `61f7544a` (base station power and stored walkie validation), `ac008d99` (COM-02 outbox endpoints), `9525da25` (safe bounded inventory iteration), `36e27f5f` (Unity meta), `fc283b1b` (preserve real generator power/fuel state), `bab2bee1` (diagnostics). **No Unity compile/runtime test occurred.**

### Native source research, and what does NOT exist

- Checked actual repository `EBuild.GENERATOR`, `ItemGeneratorAsset`, `InteractableGenerator`, `InteractablePower`, `PowerTool`, `BarricadeManager`, `ItemStorageAsset/InteractableStorage`, `PlayerVoice` and `UseableWalkieTalkie`.
- Native `InteractableGenerator.IsWorldPositionPowered(Vector3)` checks the list of **enabled, fueled, world-based** generators and their authored wiring radii, not infinite virtual electricity. `Level.info.configData.Has_Global_Electricity` is the native separate global power option. `InteractablePower` uses these same two power mechanisms.
- Native `InteractableStereo` plays songs. It does not implement a faction radio transmitter; no genuine ready-to-use native fixed `InteractableRadio` class was found in the scanned runtime. **Do not label the following assembly a vanilla radio asset.** It is a clearly defined extension combining actual native objects.
- Vanilla walkie 1445 remains a portable device without a new fictional battery drain. **The improvised base *station assembly* needs electricity to run**, as a project-specific rule; this is different from claiming that standalone vanilla walkies require batteries.

### Physical station rule (already wired in source)

`MobileNPCBaseRadioStation.CanOperateServer` requires **all**:
1. Living registered group and living group leader, actual settlement anchor, builder's **placed or world-restored** central storage; never use a global phantom radio registry.
2. A live `InteractableStorage` within 3.5m of the real group anchor; group storage must have `CSteamID.Nil` owner and owner-group and be referenced by the elected group leader's settlement builder. Do not hijack player-owned chests.
3. **A real Item 1445** inside this actual native storage's `Items` inventory. Looting/removing it immediately revokes the station at the next operation.
4. Power at the storage position from actual activated native generator with real nonzero fuel/wirerange, **or** a map explicitly configured with native global electricity. The radio does **not** operate merely because an unpowered generator mesh exists.
5. **Actual NPC operator within 3m of this storage**. Distant members lacking personal 1445 get no radio endpoint; remote listeners must have their own 1445 or another valid powered base operating position. Messages remain restricted to the same `GroupId`, `MobileNPCFaction.Id` and `460327 + runtimeGroupId`.
6. Sender still needs firsthand `EnemyContact` or near, landed, nonempty airdrop evidence; receiver brain only consumes the EnemyContact kind, as before. Any successful player intercept remains tuned+equipped and host-only text; no native microphone/visual hand equip/network replica.

The `MobileNPCSettlementBuilder` can reserve ONE of its existing four non-storage perimeter positions for a **real owned `ItemGeneratorAsset`** when available (no item ID invented). It does not create a new slot or magically spawn fuel; generator items are no longer prematurely dumped into central storage. Native generator item `state` (on/off and fuel) is preserved in `new Barricade(asset, health, clone(state))` instead of reset to empty. Builders detect nearby existing generators on one-time settlement restore to reduce duplicates, and the leader's actual known central storage is exposed through a no-scanning accessor. Existing four-point base geometry still determines placement. A generator counts as one of the perimeter slots, replacing one defensive piece rather than expanding the default 5-piece cap.

`MobileNPCRadioOutbox` uses either `HasOwnedPortableRadio` **or** `MobileNPCBaseRadioStation.CanOperateServer(group, npc.position)` for both speaker and NPC listener, retaining group/faction/TTL/cooldowns and 1.25-second global airtime. Logs (max once/10s) distinguish `source=portable` vs `source=powered-base` and base station receiver count. No per-frame all-world generator/storages scan is introduced into the radio endpoint: the builder's on-reconcile scan is bounded to first settlement restore, and native generators have an efficient built-in world power registry.

### Strict remaining scope and caveats

- **NOT implemented:** automatic fuel canister selection and transactional refueling, generator switch interaction by an NPC, placing a distinct vanilla fixed-radio object, player-operable fixed-station UI, real voice/audio, radio use animation, cross-group/faction relaying, vehicle radio, station operator travel to an empty seat, radio repairs, or authenticated persistent station ownership per saved group. A fueled generator with ON bit from a real carried item or an already functional world generator / global electricity is needed to demonstrate COM-BASE-PHYSICAL-01. A newly crafted generator with fuel=0 and powered=false does not become active automatically.
- After reloading, builder reconciles nil-owner storage within 3.5m and existing generator within 6m of the saved chosen site. This is a **proximity-based legacy base-ownership heuristic**, not a cryptographically/uniquely tagged station; nearby unowned world objects may be mistaken for the same settlement. Do not consider ownership/persistence final until a reliable placement instance ID is stored and restored. Existing base 5-piece and saved progress limits may prevent adding a generator to an already completed older settlement.
- Real generator placement via `BarricadeManager.dropBarricade` inherits all limitations of earlier base-building code (collision, server registration, consuming the source item only after placement). No guarantee that the prefab is collision-free or that the Unity import compiles.
- **Scoped status:** `COM-BASE-PHYSICAL-01` **QUASE PRONTA — AGUARDANDO BUILD** only for the power-gated, real-stored-1445, near-operator, same-group **text** endpoint and the one-slot real-generator placement. Parent `COM-03` **EM IMPLEMENTAÇÃO**. Generator autonomous activation/refueling `COM-BASE-POWER-02` **INVESTIGADA — pending next engineering round**, no ready claim. Continue `COM-04` consumers/observations separately. User's final Dev Build and feedback decide acceptance or reopening. See test-plan section 40.


## 2026-10-13 — COM-BASE-POWER-NEAR-01: physical generator refuel/start milestone

**Supersedes the older 'NOT implemented' generator autonomous activation/refueling statements above only for this LIMITED source scope.** Commits: `a67739e2`, `5e34c364`, `2a91fae3`, `c1ff2859`, `8aa9e2b1`, `240a5acc`. No Unity build, compiler or real gameplay test has been executed.

**Native flow directly checked in U3 SDK:** `ItemFuelAsset.fuel` is capacity and fuel canister `Item.state[0..1]` holds actual current `ushort` volume (with native `Delete_After_Filling_Target` setting). `UseableFuel` confirms `generator.askFill(amount)` and `BarricadeManager.sendFuel(generator.transform, generator.fuel)` perform server-side fill and synchronization. `BarricadeManager.ServerSetGeneratorPowered(generator, true)` validates a native barricade region and sends persistent power state; do not directly set `_isPowered`. The `InteractableGenerator.IsWorldPositionPowered` authority for base station remains unchanged.

**Source behaviors:**
- `MobileNPCSettlementBuilder` now caches an actual native placed/restored generator as `managedGenerator`; live leader-only accessor requires active in-world, not vehicle, within 6m of group anchor, `BarricadeDrop` registered with genuine `ItemGeneratorAsset`, and native barricade `owner/group==0`. Scans occur only on initial site reconciliation; no all-map generator scan per NPC Update. Nearby unowned world generator still has the historical ownership-by-proximity caveat.
- `MobileNPCBaseGeneratorOperator` is attached in `MobileNPCPopulationBootstrap` and enabled only server/loaded-map. Its 12-second cadence requires a living, unoccupied same-group NPC beside the specific leader-managed generator within 3.3m, with nonallocating LOS test against walls (no through-wall use). In globally powered maps it does not need to operate generator. Does not seek distant generators or interrupt combat/retreat.
- At <=50% generator capacity and with real current fuel, it transfers `min(available, capacity-fuel)` from ONE real `ItemFuelAsset` in the physically nearby owned central `InteractableStorage.Items` or the NPC's private inventory. Original native fuel state is decreased and synchronized via `Items.updateState` or `MobileNPCInventory.TrySpendGeneratorFuelServer`; empty single canisters are only deleted when the asset's native `shouldDeleteAfterFillingTarget` flag is set. Empty/no fuel leaves the generator empty.
- Only after actual fuel is present, native `BarricadeManager.ServerSetGeneratorPowered` is used to switch ON. Does not auto-switch OFF yet. Generator receives native `askFill` and `sendFuel`; original current generator capacity is respected. No invented fuel, no duplicate canisters, no power from empty/off generator, no native use animation claimed.
- Each operation logs only when real fuel or power was changed, avoiding high-volume logs on unsuccessful periodic attempts.

**Status:** `COM-BASE-POWER-NEAR-01` **QUASE PRONTA — AGUARDANDO BUILD** strictly for physical local refuel-and-start with already available native fuel; parent `COM-BASE-POWER-02` **EM IMPLEMENTAÇÃO** for intentional navigation/approach, reserve/fuel acquisition, deliberate OFF/power budgeting, robust placement IDs and integrated UX. `COM-03` parent remains **EM IMPLEMENTAÇÃO**. Final Dev Build section 41 includes native fuel persistence/replication, multiple NPC concurrency, ownership, walls and save/transition. Feedback closes/reopens scoped item only. Next priority after this scoped subtask can return to `COM-04` actionable radio reports, not rework already near-ready components without build evidence.


## 2026-10-13 — COM-AIRDROP-RADIO-01: actionable airdrop rumor, not remote loot knowledge

**Implemented in source only (no Unity compile/runtime evidence):** `MobileNPCAirdropRumor.cs` (`0914bd85`, `39a814d7`); `MobileNPCRadioOutbox.cs` (`7595e2fd`); `MobileNPCPopulationBootstrap.cs` (`7bc111a1`) plus .meta (`007a4946`); `MobileNPCAirdropContest.cs` (`233b468c`). The older references above that say airdrop messages have no distant decision consumer are superseded FOR THIS SCOPED FEATURE ONLY. Other COM-04 message kinds are still not implemented end-to-end.

### Strict chain of evidence

1. **Firsthand sighting**: `MobileNPCAirdropContest.TickServer` scans game-created `InteractableStorage` with a `CarepackageDestroy` component and nonempty actual native `Items`. An NPC can detect a crate only when within **28m**, unobstructed `RayMasks.BLOCK_COLLISION` LOS from head to crate. The former 220m player/AI-global crate detection is removed. NPC FOV/head-orientation is NOT separately tested here: distance + physical LOS is the intended short-range observation scope, not a 360-degree-long-range radar.
2. **Radio source**: same `TryQueueObservedAirdrop` requires a personally carried 1445 or operational physical powered base station, living group/faction, short physical range and rate-limited fresh queue. Interception remains host-only text with tuned/equipped player.
3. **Radio delivery**: `MobileNPCRadioOutbox.Update` already limits receiver to living same `GroupId`, `MobileNPCFaction.Id`, native tuneable channel and actual portable/base equipment. For `Kind.Airdrop`, it now calls `receiver.MobileNPCAirdropRumor.ReceiveAirdropReportServer`. No shared global list or static strategic brain, no message crossing a dead/reassigned group.
4. **Uncertain personal memory**: the receiver records only a rounded **20m sector** with independent noise in x/z +/-8m, not an exact crate transform. It accepts current reports confidence >=0.35, max response radius **105m**, validates an actual safe-ground approximate destination, and retains a TTL never longer than the radio message itself (~45s), with original faction/group. One active rumor per NPC, no silent unlimited refresh of the same position.
5. **Bounded action**: every **2.5s** it checks alive/group/faction/inventory/brain, and attempts ordinary `BrainDriver.TryStartRememberedSupplySearch(ground)` only when idle, free of target/tactical goal and the vanilla safe destination test passes. This activates a normal `Goal.Investigate` with the existing motor/pathfinder; no relocation, spawn, direct carepackage claim, invulnerable dash or telepathic tracking. Combat / other objectives may displace investigation, which is then discarded.
6. **Physical discovery**: only after the `MobileNPCAirdropContest` independently confirms a nonempty, visible carepackage within 28m can the active matching individual rumor transition to the existing `TryStartLoot`. The ordinary NPC loot approach and vanilla `Items.removeItem`/inventory transfer still enforce physical item availability and capacity.
7. **Expiry / anti-loop:** after reaching an approximate position, allow up to ~8s for repeated normal physical scan to verify, then abandon. On group/faction change, death, message expiry or goal replacement, discard rumor and cancel only that rumor's own matching `Investigate` goal; never cancel a different combat or follow order. Repeated reports of same site are suppressed for **95s** after resolution. Unaccepted/remote radio report does not invoke navigation. No saving radio rumors as personal certainty.

### Limits and explicit pending

- 28m, LOS and a real carepackage are required for observation/confirmation, but extra NPC facing/FOV and human-like scanning animations are still future polish. The current 2s server airdrop scan enumerates actual storages once and loops actors; review performance with 20/40 NPCs before accepting.
- The rumor's round+noise may direct a listener to a wrong side of a building; it relies on the existing safe A* path/route recovery and intentionally fails if inaccessible. A TTL of 45s may be too short for distant obstacles; measure rather than silently extend it. Expiry is not proof airdrop no longer exists.
- No reward for merely *hearing* a rumor. If the crate vanished, is hidden or was looted before arrival, the NPC does not materialize supplies and eventually gives up. Multiple allies may individually investigate the same radio source; their inventories remain distinct, subject to the real crate transfer.
- **Status:** `COM-AIRDROP-RADIO-01` **QUASE PRONTA — AGUARDANDO BUILD** for scoped C# logic (still uncompiled, not necessarily runtime functional); parent `COM-04` **EM IMPLEMENTAÇÃO**. Reinforcement requests, personally observed useful loot and unknown enemy bases still have no completed producer+receiver actions, and NPC vehicle radios remain `VEH-01` pending. The final Dev Build checklist is section **42**.


## 2026-10-13 — COM-REINFORCEMENTS-RADIO-01 — genuine wounded-combat calls and one voluntary responder

**Code committed; NO Unity compilation or game test.** Key commits: `1b0d15b2` tactic; `77c07f0f` brain; `0099f12b`, `60422ac0` physical outbox sender/selecting one recipient; `f36f67e2` response; `593ceb5c` prefab bootstrap; `a2473b98` stable `.meta`.

**Origin of a help call:** `MobileNPCBrainDriver.ReportVisibleTarget` is a genuine server visual-target callback, not a hearsay path. It offers `TryQueueCombatReinforcementsServer(target)` to existing source outbox. Outbox checks real recent server `MobileNPCHumanoidVitals.LastDamageTime` <=7s, actually reduced `HealthFraction` <=0.90, and a **current living threat**: a Player only if this NPC's disposition permits attacking players, another NPC only with `MobileNPCHumanoidDisposition.AreHostile`, or a live Zombie. Equipment/frequency/group remains native-gated. Sender requests no more often than 80s and shares existing bounded outbox's 20s prep, 4 message limit, 8s per-NPC airtime and 1.25s global airtime. Message `Kind.NeedReinforcements` holds only the injured **caller's current position**, group/faction/call sign/confidence 0.78 and **48-second TTL**. No fake enemy position, identity, LOS or unlimited living-group beacon.

**Recipient filtering/choice:** After enforcing individual physical 1445 or valid powered base station, EXACT same runtime `GroupId`, faction and radio channel, `MobileNPCRadioOutbox` calls `MobileNPCReinforcementResponse.GetResponseScoreServer` on each receiver. Candidate must be alive, health >=0.55, armed with real selected native gun and >=3 loaded rounds, not reloading, not busy/in combat/following; distance 12–90m from the caller. Compare distance, health and ammo, and assign **at most one** consenting eligible radio teammate. Others may hear the text but are not teleported, tasked, or magically informed of a target. `ReceiveReinforcementRequestServer` is an individual voluntary assignment, not extra population.

**Response and movement:** delay 1.25–3.25s for human response; coarse quantization into **15m location sectors**, optional +/-6m per-individual position error; existing `MobileNPCSpawnService.TryFindSafeGroundPosition` and `Level.checkSafeIncludingClipVolumes` reject invalid rendezvous. An idle, still-well, still-armed survivor calls `MobileNPCBrainDriver.TryStartRadioReinforcementServer`, which uses `MobileNPCTactics.Goal.Reinforce` (new enum member APPENDED, not renumbering prior roles); existing `MobileNPCHumanoidMotor` handles route/movement. Destination is an approximate ALLEGIANCE rendezvous near caller, not the threat. No gunshot, target assignment or aim from the radio. Stop on expired message (~48s), lost group/faction, health <55%, no ammo, real target acquired locally, objective overwritten, or arrival <=9m then a ~4s ordinary perception window. The brain cancels ONLY its own still-matching `Goal.Reinforce`, never unrelated cover/retreat/follow/loot objectives. A new request cannot forcibly recruit an occupied NPC; new assignments per member cooldown 80s.

**Important limitations/tests:** The feature is deliberately narrower than a full faction-wide reinforcement system: only one *existing* armed same-group living NPC, no static radio base squads spawned, no allied-faction group network, no real faction diplomacy, no unconfirmed/distinct shooter identity from incoming sound, no real native walkie voice or hand animation. Candidate selection happens when text transmits and may select a candidate whose approximate ground rendezvous fails; that message may receive no responder rather than fabricate a route. Goal navigation depends on existing local and optional native A*. Combat/noise events continue to have higher priority, and successful reinforcement means the ally physically reaches and can perceive, not automatically kills anything. End-to-end test plan section 43.

**Scoped status:** `COM-REINFORCEMENTS-RADIO-01` **QUASE PRONTA — AGUARDANDO BUILD** in C# source only; `COM-04` parent **EM IMPLEMENTAÇÃO** (useful-loot radio producer/consumer, unknown base, and extended faction response missing). No runtime success has been observed; feedback later closes/reopens exact subitem.


## 2026-10-13 — COM-USEFUL-LOOT-RADIO-01 — need-based, physically verified useful-loot radio reports

**C# source committed only; no compilation, Unity Dev Build, Player.log or runtime performance evidence.** Relevant commits `3c2a8db7` item need API, `81034708` typed radio message, `e89e35e5` new rumor memory and local LOS, `8e03c2b6` radio producer/recipient, `dfa333d0` existing loot-scan integration, `7601def1` runtime component, `affc08ad` .meta, `b5dcb5ff` arrival timing, `e64476dc` late component resolution.

**First-hand producer**: `MobileNPCLooting` already enumerates *genuine* `ItemManager` world drops around the living NPC while idle and inventory has space. During each 2.5s scan it now considers relevant `ItemAsset.type` categories for radio. Only an active nonempty `InteractableItem` whose registered `ItemManager.TryGetWorldItemInstanceID` succeeds, with actual line of sight from NPC head using non-alloc `RayMasks.BLOCK_COLLISION` and within **17m**, may be reported. The most intrinsically interesting locally visible candidate can be queued if the same actor possesses native 1445 or physically operates the group's powered base. Categories: medical, gun, nonempty magazine, water, food, melee, backpack, vest and walkie 1445. This is not a map-wide loot scan; a solid wall should block reports. Orientation/FOV angle itself is not enforced; later polish if needed.

**Radio transport**: `MobileNPCRadioMessage` adds `ReportedItemID: ushort`, defaults to 0 in every existing message kind and is nonzero only when `TryCreateUsefulLoot` validates a real `ItemAsset`. There is NO world drop instance ID/Transform or hidden loot count in the packet. `Kind.UsefulLoot` human-readable interception renders a possible item name and sector; identical equipment/frequency/host HUD rules apply. `MobileNPCRadioOutbox.TryQueueObservedUsefulLootServer(InteractableItem)` re-checks real close LOS, native item quantity, group/faction, 20s shared prep and 95s repeat-item/sector suppression; message TTL **62s**. Outbox sends to only living, correctly tuned/equipped same-group/faction receivers, then calls their personal `MobileNPCUsefulLootRumor.ReceiveUsefulLootReportServer` without granting world access.

**Reception: need comes from the INDIVIDUAL.** `MobileNPCSupplyKnowledge.GetCurrentRadioItemNeedServer(itemID, inventory, health, injuries, survival)` reuses the project's previously existing need classification, including actual weapon/magazine caliber compatibility, wound dressing/splint shortages, hunger/thirst and no firearms; first handheld radio 1445 can also count for a person at a working base station. Utility must be at least **70**, with free inventory capacity and alive status. Reject outside **12–100m** and dangerous long speculative trips with bleeding. Data-only memory stores true item ID, original group/faction, a **15m rounded area plus individual +/-6m random offset**, and TTL <=62s. Validate safe ground; never retrieve live item from the report. Only **one** active radio loot investigation per actor and a 110s same-item/same-sector cooldown after a visit. Another unrelated item report may supersede a *not-yet-started* rumor, never an active travel goal.

**Movement and revalidation**: `MobileNPCUsefulLootRumor` can start the existing `BrainDriver.TryStartRememberedSupplySearch` -> `MobileNPCTactics.Goal.Investigate` only idle, alive, appropriately needy and target/goal-free. The actual motor/pathfinder handles walls/stairs and may fail. A native arrival ack can clear this goal, and the rumor retains up to **8s** of local physical confirmation. `MobileNPCLooting` explicitly allows only the actor's own active radio search to temporarily keep the `Investigate` loop scanning items. ONLY a nearby real `InteractableItem` of exactly `ReportedItemID`, <=34m from the approximate reported area, <=17m from actor, with LOS and a valid registered instance can end Investigate and start normal `Goal.Loot`. That component already calls `MobileNPCInventory.TryPickupWorldItemServer(instanceID, actorPosition, distance)`, removing actual native loot with no creation. Wrong item, empty pickup, vanished source or solid wall do not count. Tactical/combat/health/needs changes or expiration cancel only the owner matching investigation and erase speculative data, never overwrite another urgent goal.

**Limits:** no cross-group sharing, no source-target ItemManager pointer transfer, no automatic guaranteed pickup, no persistent hallucinated group loot map, no loot hoarding policy between humans/NPCs, no player fake access to all drops, no improved FOV beyond actual LOS. This is simulated text not native radio audio or remote multiplayer. Source scanner still sees nearby drops through ItemManager, but **only** LOS-filtered drops produce authoritative UsefulLoot reports; ordinary baseline local loot scoring is otherwise unchanged. Profile 20/40 NPCs to verify raycast budget and ordering in final build.

**Status:** `COM-USEFUL-LOOT-RADIO-01` scoped **QUASE PRONTA — AGUARDANDO BUILD**; `COM-04` parent **EM IMPLEMENTAÇÃO** because `UnknownBase`, faction-wide group communications, vehicle radio and more advanced operational support remain open. Next radio scope `COM-UNKNOWN-BASE-RADIO-01` only when native structure ownership and genuine field perception can be verified. See test plan **section 44**.
