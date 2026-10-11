# Mobile humanoid NPC quick test (development branch)

Branch: `feature/npc-ai-foundation`

## Current components

The opt-in humanoid prototype is under `Assets/Runtime/Assembly-CSharp/Unturned/NPCs/`:

- `MobileNPCBrain` / `MobileNPCBrainDriver`: server-side state, target memory, tactical goals and decision events.
- `MobileNPCRegistry`: server-only registry of enabled mobile brain drivers for future faction/population systems.
- `MobileNPCSpawnService`: validates an explicitly supplied mobile humanoid prefab and creates it on the server with a selected role. This is **not network replication**; do not treat it as a multiplayer-ready spawn.
- `MobileNPCHumanoidPerception`: nearest living player within sight range, field of view and unobstructed line of sight.
- `MobileNPCHumanoidDisposition`: opt-in `Hostile`, `Friendly` or `Neutral` role. Only `Hostile` NPCs acquire players as combat targets in this prototype.
- `MobileNPCHumanoidMotor`: server-side CharacterController movement.
- `MobileNPCHumanoidAim`: variable reaction time, movement/shot spread and suppression penalty.
- `MobileNPCHumanoidCombat`: server-authoritative melee damage.
- `MobileNPCHumanoidRangedCombat`: baseline hitscan damage, ammunition/reload simulation, variable bursts and pauses.
- `MobileNPCHumanoidVitals` / `MobileNPCTactics`: health-related tactical decisions, retreat/cover/loot goal helpers.

## Spawn service contract

`MobileNPCSpawnService.TrySpawnServer(...)` accepts only a prefab with a root `CharacterController`, `MobileNPCBrainDriver`, `MobileNPCHumanoidMotor`, `MobileNPCHumanoidPerception`, and `MobileNPCHumanoidDisposition`. It rejects client-side calls, null prefabs, and positions outside safe level bounds. The caller must supply a dedicated mobile prefab; do not pass `NPC_Server`, `NPC_Client`, or a legacy static-NPC prefab. The service initializes the server-side brain/motor only. It intentionally does not claim client replication, persistent saving, or full pathfinding.

## Quick smoke test

1. Open the project with its configured Unity editor and wait for script import. Check the Console before launching the build.
2. In a disposable test scene, create a humanoid GameObject with a valid visual/model, collider, `CharacterController`, and these components: `MobileNPCBrainDriver`, `MobileNPCHumanoidMotor`, `MobileNPCHumanoidPerception`, `MobileNPCHumanoidDisposition`, `MobileNPCHumanoidAim`, `MobileNPCHumanoidVitals`, and either or both combat components. Set the disposition to `Hostile` for combat tests; `Friendly` and `Neutral` should remain non-aggressive.
3. Make sure the NPC prefab has appropriate colliders and layer masks, the scene has a floor, and the player is a normal Unturned `Player`.
4. Run as a server. The NPC should detect a living player within 32 m and its field of view, then pursue using local collision-aware movement. Break line of sight: ranged combat must stop firing, aim tracking must clear, and the brain should investigate the last known position before returning.
5. For ranged combat, confirm variable reaction delays, misses, burst pauses, ammunition depletion and reload events. The damage model is a prototype hitscan implementation, not a fully integrated vanilla gun/inventory/projectile simulation.
6. Verify dedicated-server and client behavior separately.

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
- [ ] Existing static NPCs, vanilla zombies and sentries retain their current behavior.
- [ ] Server-to-client spawn and movement replication are implemented and tested before multiplayer acceptance.
