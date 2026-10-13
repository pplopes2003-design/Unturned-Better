# Mobile NPC radio communications — design only (2026-10-13)

**Status: NOT IMPLEMENTED.** This file records requested requirements and interface constraints for future agents. Do not treat radio access as a passive skill, magically shared faction memory, or a substitute for line-of-sight/audio evidence.

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
