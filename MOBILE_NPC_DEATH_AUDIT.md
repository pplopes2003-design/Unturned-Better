# Mobile NPC — end-to-end death audit

Date: 2026-10-14. Branch: `feature/npc-ai-foundation`.

## Scope and assurance level

Goal: maximize confidence that a player or NPC can kill a mobile humanoid NPC, that the corpse and owned items are handled correctly, and that A-Life jobs/brain/population never treat a corpse as living.

**Evidence level:** repository source reviewed and patched, with **26 explicit static source-contract checks passing over 14 related C# files** (string/path/structure checks, plus brace balance). **No Unity compilation, Development Player run, input replay or physical runtime test was performed by the assistant.** This is NOT a guarantee of observed death in the actual game. Do NOT close the P0 until the game displays the logs and death body in a reproduced build.

## Single authority, verified source chain

1. Runtime template instantiates six native `Entity` (layer 23) anatomical damage colliders, independent of the character controller on `Agent` (24). Bootstrap logs actual `DAMAGE_CLIENT` and `DAMAGE_SERVER` mask membership. Client `RayMasks.DAMAGE_CLIENT` includes `ENTITY`; `DAMAGE_SERVER` by design excludes it.
2. `PlayerInput` already sends/reads `ERaycastInfoType.MOBILE_NPC` without serializing transient Transform IDs; `getInput` reconstructs target by the server's FIRST actual ray collision with `DAMAGE_SERVER | ENTITY | PLAYER | ENEMY`, rejects occlusion, wrong point, dead target, wrong input usage and invalid punch facing/range. The expanded PLAYER/ENEMY mask avoids accepting a target hidden behind another player or zombie. Development failures log `[MobileNPCDamageValidation] Rejected`.
3. `UseableGun`, `UseableMelee`, and `PlayerEquipment` (punch) each call `MobileNPCHumanoidVitals.ApplyValidatedWeaponImpactServer` only after their own authorized weapon hit. Gun/melee preserve actual weapon damage/falloff, never create an arbitrary 1 HP hit. `UseableMelee` also has a strictly server-physics-validated fallback for older `NONE` raycasts.
4. `ApplyValidatedWeaponImpactServer` resolves hit part from actual hitbox or bounded anatomical fallback, then `ApplyDamageToPart` updates injury/bleeding and calls `ApplyDamage`. **HP debited ONCE** in Vitals (`CurrentHealth = Max(0, CurrentHealth - damage)`); zero HP reserves lethal transition, disallows healing and further lethal duplicate.
5. `MobileNPCInjurySystem` now isolates `BleedingChanged` and `PartConditionChanged` observers so one animation/HUD subscriber cannot throw between limb effect and HP deduction. Direct bleeding damage over time also enters Vitals.
6. `MobileNPCHumanoidVitals.ApplyDamage` isolates brain reaction, aggro notification, incoming fire and HP/damage subscribers, using `finally { if (lethal) DieServer(impulse); }`. A crashing callback cannot leave an indefinitely alive character with 0 HP and `lethalTransitionReserved=true`.
7. `DieServer` commits `isDead=true; HP=0` first; individually guards memory forgetting, brain reset, disable motor, melee/ranged combat, sensor/aim, anatomical colliders, A-Life job planner, construction, medicine, radio/grenades/generator work and animation; attempts native ragdoll / physical rigid corpse fallback; disables brain; invokes **each `Died` observer independently** so loot/group bookkeeping cannot block one another. Debug-only `[MobileNPCDeathInvariant] PASS/FAILED` reports HP and locomotion/combat shutdown.
8. Existing `MobileNPCDeathLoot` subscribes to `Died`, attempts vanilla owned-item drops on server only, retries refused transfers on later ticks; optional hostile XP award now cannot prevent corpse loot. `MobileNPCGroupMember` subscribes to `Died`, frees claims/groups, never transfers dead individual's knowledge. `MobileNPCPopulationDirector` tracks registered spawns, schedules separate respawn cooldown and timed corpse cleanup; `MobileNPCRegistry` counts living registered drivers only.

## Actual source corrections in this audit

- `a9722621`: fail-safe lethal completion + per-subscriber `Died` and per-stage death cleanup; preserve all existing health calculations.
- `9f2d549a`: isolate pre-HP injury and bleeding observer errors.
- `c6de814f`: stop new A-Life/radio/animation/world behaviors; Development Build lethal invariant log.
- `163a3ebd`: real inventory drop still attempts when optional XP award throws.
- `68d9c234`: custom hit validation includes PLAYER/ENEMY occluders on top of world + ENTITY.
- Earlier baseline fixes still in effect: `484005ac` (melee NONE verified), `5d33fc18` (A-Life jobs end on death), and prior weapon/IP server-authoritative input and anatomy changes.

## Mandatory test matrix — next Unity Development Build

1. **Physical hit proof:** F8 or project console spawn 1 neutral then 1 hostile; shoot a visible anatomical region with multiple bullets. Capture `Damage accepted: ... health=100.0->...`. Repeat melee weapon + bare fist, separated by actual cooldown. Server HP must decline for each VALID hit, and reject shots obstructed by wall, another NPC, Player, Zombie, or out of range. Client hitmarker alone is insufficient.
2. **Lethal proof:** deal enough damage to reach `health=...->0.0`, then observe EXACTLY ONE `Death transition`, ONE `[MobileNPCDeathInvariant] PASS`, corpse physically falls, ceases all combat/roaming, no living hitboxes intercept subsequent shots. `[MobileNPCDeathInvariant] FAILED` or missing log = test failed.
3. **Limb/bleeding proof:** leg/head/torso hit -> actual damage + injury, bleed tick can kill, no regeneration without owned meds. Damage callbacks must not strand HP=0 without `IsDead`.
4. **Owned drop proof:** corpse releases exactly one native world drop per genuinely owned slot, not synthetic/random loot; refusal stays for retry and logs reason; neutral/friendly kills do not receive hostile kill XP; hostile player kill (if enabled) can award once.
5. **Group death proof:** while guarding one of two reserved group posts, kill a guard; his post becomes available, other live NPC takes it without inheriting his personal memory/loot; kill leader and reconcile actual vanilla base storage; kill all and verify group dissolves.
6. **Population proof:** `Death queued` once, corpse remains for configured duration, delayed fresh NPC replaces lost population, no direct resurrection of dead instance, cleanup eventually destroys corpse. No ghost in active registry.
7. **Stress/persistence:** test 1, 10 and 40 NPCs; overlapping gun pellets, multiple simultaneous attackers, map unload/return, and native zombie/NPC cross-target hits. Evaluate memory/CPU and `Player.log` for exceptions, especially `Failed stage` or `Failed` invariant.
8. **A-Life nonregression:** after actual death, no inventory transaction, jobs, radio transmissions, generator refuel, grenade attack or movement can run; subsequent AI-ALIFE-02/03 extensions must continue respecting `IsDead` and subscribe to existing `Died`, NEVER create parallel health/death systems.

**Acceptance:** compilation successful, all relevant physical checks pass at runtime on the exact HEAD, evidence/logs inspected with user feedback. Only THEN close P0 `CORE-02`. Before that, annotate **CORE-02 death-source hardening = QUASE PRONTO — AGUARDANDO BUILD; parent CORE-02 EM IMPLEMENTAÇÃO**.

## Scope preserved for next A-Life iteration

AI-ALIFE-01/02 still in progress; future jobs may not override `MobileNPCBrainDriver` tactical goals, duplicate `MobileNPCGroupMember` state, bypass `ItemManager`, or re-enable a dead component. Maintain proper `Died` cleanup of reservations. Post death verification, continue physically verified scavenging/recon jobs and GOAP preconditions, not new parallel AI frameworks.
