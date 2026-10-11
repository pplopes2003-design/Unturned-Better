# Mobile humanoid NPC quick test (development branch)

Branch: `feature/npc-ai-foundation`

## Current components

The opt-in humanoid prototype is under `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/`:

- `MobileNPCBrain` / `MobileNPCBrainDriver`: server-side state, target memory, tactical goals and decision events.
- `MobileNPCRegistry`: server-only registry of enabled mobile brain drivers for future faction/population systems.
- `MobileNPCSpawnService`: validates an explicitly supplied mobile humanoid prefab and creates it on the server with a selected role. This is **not network replication**; do not treat it as a multiplayer-ready spawn.
- `MobileNPCHumanoidPerception`: nearest living player within sight range, field of view and unobstructed line of sight.
- `MobileNPCHumanoidDisposition`: opt-in `Hostile`, `Friendly` or `Neutral` role. Hostile NPCs can engage players and opposing Friendly NPCs; Friendly NPCs engage Hostile NPCs but not players; Neutral NPCs engage neither faction.
- `MobileNPCHumanoidMotor`: server-side CharacterController movement.
- `MobileNPCHumanoidAim`: variable reaction time, movement/shot spread and suppression penalty.
- `MobileNPCHumanoidCombat`: server-authoritative melee damage.
- `MobileNPCHumanoidRangedCombat`: baseline hitscan damage, ammunition/reload simulation, variable bursts and pauses.
- `MobileNPCHumanoidVitals` / `MobileNPCTactics`: health-related tactical decisions, retreat/cover/loot goal helpers.

## Spawn service contract

`MobileNPCSpawnService.TrySpawnServer(...)` accepts only a prefab with a root `CharacterController`, `MobileNPCBrainDriver`, `MobileNPCHumanoidMotor`, `MobileNPCHumanoidPerception`, and `MobileNPCHumanoidDisposition`. It rejects client-side calls, null prefabs, and positions outside safe level bounds. The caller must supply a dedicated mobile prefab; do not pass `NPC_Server`, `NPC_Client`, or a legacy static-NPC prefab. The service initializes the server-side brain/motor and, when present, vitals synchronously before returning. `TryDespawnServer(...)` validates server authority and the mobile-NPC component signature before destroying the server instance; this is local lifecycle cleanup only, not a client-visible network despawn. It intentionally does not claim client replication, persistent saving, or full pathfinding.

## Quick smoke test

1. Open the project with its configured Unity editor and wait for script import. Check the Console before launching the build.
2. In a disposable test scene, create a humanoid GameObject with a valid visual/model, collider, `CharacterController`, and these components: `MobileNPCBrainDriver`, `MobileNPCHumanoidMotor`, `MobileNPCHumanoidPerception`, `MobileNPCHumanoidDisposition`, `MobileNPCHumanoidAim`, `MobileNPCHumanoidVitals`, and either or both combat components. Set the disposition to `Hostile` for combat tests; `Friendly` and `Neutral` should remain non-aggressive.
3. Make sure the NPC prefab has appropriate colliders and layer masks, the scene has a floor, and the player is a normal Unturned `Player`.
4. Run as a server. The NPC should detect a living player within 32 m and its field of view, then pursue using local collision-aware movement. Break line of sight: ranged combat must stop firing, aim tracking must clear, and the brain should investigate the last known position before returning.
5. For ranged combat, confirm variable reaction delays, misses, burst pauses, ammunition depletion and reload events. The damage model is a prototype hitscan implementation, not a fully integrated vanilla gun/inventory/projectile simulation.
6. Test NPC death by applying server-validated damage through `MobileNPCHumanoidVitals.ApplyDamage(...)`: health must reach zero once, the brain must reset, movement/collision and combat/perception must stop, the driver must leave the active registry, and the `Died` event must fire once. Healing and repeated damage after death must be rejected.
7. Exercise `TryDespawnServer(...)` with a live mobile NPC: on the server it should destroy the instance and remove its driver from the active registry; a client-side call and a non-mobile GameObject must be rejected. This checks server-local cleanup only, not network despawn.
8. Verify dedicated-server and client behavior separately.

## Faction smoke test

1. Spawn one Hostile and one Friendly mobile prefab with vitals and melee or ranged combat enabled. Confirm each acquires the opposing NPC only with line of sight, and damage is applied through the target's server-side vitals.
2. Spawn two NPCs from the same faction and confirm they do not acquire or damage one another. Neutral NPCs should not join faction combat.
3. Fire a mobile NPC's ranged weapon and confirm nearby vanilla zombies investigate the gunfire position. This is a noise reaction only; zombies do not yet treat mobile NPCs as full living attack targets.
4. Repeat with a Hostile shooter and a Friendly shooter. Both should produce the same zombie-noise stimulus.

## World population, settlement and war status

- Mobile NPC spawning is still an explicit server-side call at a supplied position. **Automatic spawning across arbitrary maps is not implemented yet.**
- Persistent regional populations, claiming territory, building/maintaining bases, resource gathering, storage, and long-term settlement are not implemented.
- Hostile-versus-Friendly acquisition and prototype melee/hitscan damage are now connected. Full native weapon integration and replication remain incomplete.
- Zombies currently react to mobile NPC gunfire by investigating its position. They do **not** yet select, chase, and attack mobile NPCs as full targets; that requires integrating NPC perception/health into the native zombie target and damage pipeline.

## Death and ragdoll status

- The mobile NPC currently **does not produce a ragdoll** when it dies. `MobileNPCHumanoidVitals` raises `Died` and disables its movement/combat components, but the visible body can remain in its last pose until a death presentation is added.
- Unturned already has `RagdollTool.ragdollPlayer(...)` and `RagdollTool.ragdollZombie(...)`, plus the `Characters/Ragdoll_Player` and `Characters/Ragdoll_Zombie` assets. These are not drop-in replacements for the mobile NPC: they expect a compatible humanoid skeleton and, for full appearance, player/zombie clothing data.
- `DeathPresentationRequested` is only a server-side hook; it does **not** send an RPC or replicate an entity. Correct multiplayer behavior requires the server to replicate a stable NPC identity plus death pose/impulse, and clients to create the ragdoll locally. Calling the visual ragdoll function only on the server would not make remote clients see it, and directly reusing zombie ragdoll would require zombie-specific appearance data.
- Next implementation step: establish stable network identity and spawn replication for mobile NPCs, then add an opt-in ragdoll presentation adapter using a compatible humanoid skeleton, the lethal-hit impulse, client-side visual spawning, and configurable cleanup. Until that is integrated with the entity replication protocol, ragdolls remain pending rather than being faked with a server-only visual.

## Important limits

- The role component only defines basic hostility toward players. Friendly following, dialogue, squad behavior, and inter-NPC relationships are not implemented yet; these belong to later behavior/faction work.
- **Multiplayer replication is not finished.** The motor currently moves the NPC on the server; a dedicated spawn/lifecycle and client snapshot/interpolation path is still required before remote clients can reliably see movement.
- Local movement is collision-aware but is not full pathfinding around obstacles.
- The ranged component uses server-side hit detection and damage, but is not yet bound to a real item gun, attachments, physical ammo inventory, native muzzle effects, or all vanilla gun sounds.
- Events such as `ShotFired`, `ReloadStarted`, and `ReloadFinished` are available for audio/animation adapters. Reuse existing game audio assets through the game's supported effect/audio pipeline; do not synthesize voice chat or change existing static NPCs.
- Do not merge to the release branch until Unity compilation and singleplayer/dedicated-server tests pass.

## Acceptance checklist

- [ ] Unity compilation succeeds with the configured editor/toolchain.
- [ ] Target detection respects sight range, field of view and obstacles.
- [ ] The NPC does not fire at a player it cannot currently see.
- [ ] Humanized reaction, aim spread, suppression, burst timing and reload work as configured.
- [ ] Dead NPCs cannot move, perceive or attack; death is emitted once and subsequent damage/healing is rejected.
- [ ] Server cleanup rejects clients and non-mobile objects; network despawn remains a separate acceptance item.
- [ ] Existing static NPCs, vanilla zombies and sentries retain their current behavior.
- [ ] Server-to-client spawn and movement replication are implemented and tested before multiplayer acceptance.
- [ ] Population spawning works on arbitrary supported maps without hand-placed NPCs.
- [ ] NPCs can build, defend, repair and persist bases/settlements in claimed regions.
- [ ] Hostile and Friendly factions fight one another using server-authoritative damage.
- [ ] Vanilla zombies can perceive, pursue and damage both mobile NPC factions, not merely investigate their gunfire.
