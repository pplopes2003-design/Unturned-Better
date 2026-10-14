# Mobile NPC radio communications — design only (2026-10-13)

**Status (latest): portable radio text V1 connected in source, awaiting Unity Dev Build.** This file records requested requirements and interface constraints for future agents. Do not treat radio access as a passive skill, magically shared faction memory, or a substitute for line-of-sight/audio evidence.

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
