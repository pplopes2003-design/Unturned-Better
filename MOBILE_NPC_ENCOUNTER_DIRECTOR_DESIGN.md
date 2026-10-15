# AI-DIRECTOR-01 — Encounter pacing with first-hand evidence (2026-10-15)

## Objective and inspiration

The requested fusion is (1) **STALKER-style independent survival/A-Life**, with groups, site scouting, limited knowledge, and genuine inventory/health, plus (2) **Alien: Isolation-inspired dramatic pacing**, where a macro-director and a micro-behavior brain are separate. In Alien: Isolation the historical macro-director can know player position and only hand down approximate guidance; see `https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation` and `https://www.gamedeveloper.com/design/revisiting-the-ai-of-alien-isolation`.

**Fairness decision for THIS mod:** stricter than Alien's macro-director. Neither this director nor its output contains or reveals the player's actual location, a suggested enemy spawn, nor any map coordinate. Player clues are generated exclusively by the already existing server-authoritative `MobileNPCHumanoidPerception`, `MobileNPCPlayerFootstepSensor`, `MobileNPCRegistry.NotifyGunfire`, `MobileNPCGroupMember`, and legitimate radio paths. The director only grants bounded permission for *optional extra* uncertain searches that `MobileNPCPlayerKnowledge` already knows how to propose. The main tactical brain remains authoritative.

## What was actually implemented

- `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/MobileNPCEncounterDirector.cs` new server-local `MonoBehaviour`, attached to the existing `MobileNPCPopulationDirector_Runtime` object in `MobileNPCPopulationBootstrap`. Unity `.meta` with stable GUID. Exactly one instance across map changes, including Unity's deferred `Destroy` semantics. OnDisable clears claims.
- At staggered 1.25-second intervals, reuse `MobileNPCRegistry.CopyActiveDrivers`, examine actual `perception.VisiblePlayer` **only** and `disposition.CanEngagePlayer(player)`; do not query hidden player coordinates or anything in the UI/camera. A bounded pressure accumulator grows only when a real allowed hostile visual encounter is underway and naturally declines when absent. This is a pacing statistic, not combat authorization.
- After >=64 pressure units and once real visual contact ceases, start ~19-second recovery. A recovery suppresses **optional** uncertain memory-based stalking/hints, not real target pursuit, audible footstep investigation, verified gunshot, self-defense, fleeing, cover, or radio. No entity removal, teleport, forced reset of brain, invented tracking or guaranteed encounter.
- Group-level active search permits are bounded (two per group normally, one when pressure>=32). A permit is an 8-second temporary reservation for a real living hostile NPC while its existing `MobileNPCBrain` is `Investigating`. Individual agents with no group use a separate key. Expired, dead, disabled, or finished investigators are removed; no member is assigned a target. During recovery permits are denied but **native sensory spiral and tactical goals still work**.
- `MobileNPCBrainDriver.TryUsePlayerKnowledgeHint` and `TryPlanConcealedApproach` consult the new director AFTER their existing first-hand memory/confidence/physical conditions. The director never changes `ReportVisibleTarget`, `ReportNoise`, `TryChooseDefensiveGoal`, or targeting rights. Optional permit released on brain investigation exit, Reset and OnDisable.
- Fixed more AI consistency: `MobileNPCBrainDriver.ReportGroupPlayerContactClue` ignores approximate squad report when `perception.VisiblePlayer` is already confirmed; this avoids crouching because of hearsay in front of a visible peaceful player. `MobileNPCBrain.ReportDestinationReached` now makes investigation terminate into `Returning` (or Idle without home) immediately after the third reached search sample, rather than remaining stuck in `Investigating` with no destination.
- Safe during death: death disables `MobileNPCBrainDriver` and optional permit expires/releases; `MobileNPCHumanoidVitals` remains sole HP/death owner; group membership and loot unchanged.

## Exact constraints of this slice

This is **pacing, not a full Alien enemy system**. It cannot inspect cupboards, hide in vents, remember repeatedly exploited player hiding spots or magically resume a lost trail, and does not produce a world/level objective that moves NPCs near an invisible player. It does not implement A-Life offline simulation and does not replace or move any other AI. No Unity-compiled proof or Dev Build session yet.

Bounds: `sampleInterval=1.25s`, pressure `0–100`, pressure threshold `64`, recover `~19s`, 1–2 optional investigators/group, permit lifetime `8s`. Source-level tunables are private serialized fields. Agent `playerKnowledge` independently decays on age/uncertainty and is only populated by real perception.

## Integration and regression tests

See `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 60. Confirm no false aggression from friendly/neutral actors, no peeking through walls, normal player priority in genuine line-of-sight, no request for NPC to crouch at visible peaceful footsteps, correct group/radio knowledge boundaries, search termination after its reachable points, genuine cover and paths, fps/CPU with 1/10/40 NPCs. Map transition must not disable new Director by stale singleton.

## Status

`AI-DIRECTOR-01 — QUASE PRONTA — AGUARDANDO DEV BUILD` for source architecture and integration; `AI-SEARCH-END-01` and `AI-SOUND-VISION-03` also **QUASE PRONTAS — AGUARDANDO DEV BUILD**. Parent `AI-01` and A-Life offline/director adaptive plans remain **EM IMPLEMENTAÇÃO**. `CORE-02` fatal damage/runtime still P0 until health→0, death-invariant PASS and corpse proof. No claim of Unity compile or in-game verification.
