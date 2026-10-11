# Mobile humanoid NPCs — single-player-first implementation plan

## Priority and compatibility

The primary acceptance target is a playable single-player world. Multiplayer replication is explicitly deferred until the single-player loop is stable. Existing static dialogue NPCs and the game's existing zombie behavior must remain intact.

## Single-player definition of done

A mobile NPC must be able to:
- Spawn into any supported map without map-specific coordinates or hand-authored placements.
- Persist its identity and state for the current save, avoid duplicate spawns on reload, and recover safely from invalid or blocked spawn points.
- Roam, investigate nearby sounds, notice visible players, pursue or disengage, and return to a patrol/settlement state.
- Navigate around obstacles using the navigation/movement facilities actually available in the Unturned SDK; never assume Unity NavMesh is present without verifying the project.
- Use humanoid models/animations and reuse existing humanoid audio assets where the SDK exposes them.
- Use firearms with human-like reaction time, aim spread, burst length, cooldowns, range falloff, and imperfect accuracy. NPCs must not behave like perfectly accurate sentries.
- Take damage, die, drop/clean up according to the game's entity lifecycle, and avoid null references when targets or regions disappear.
- Build or establish a settlement/base through a constrained, save-safe placement system. Building should be incremental and should validate terrain, collisions, and permitted structures rather than spawning arbitrary objects.
- Run decisions locally in single-player without requiring a network server/client handshake.

## Architecture boundaries

- Keep the existing static InteractableObjectNPC dialogue path unchanged.
- Keep mobile NPC AI separate from static NPCs and zombies.
- Keep behavior decisions independent of presentation so perception, state transitions, combat, movement, and persistence can be tested separately.
- Reuse native game APIs and entity lifecycle patterns after verifying their signatures in this exact SDK revision. Do not copy multiplayer-only APIs blindly.
- Isolate server-authority/network replication behind a later adapter. It is not a blocker for single-player acceptance.
- Do not claim compilation or in-game verification until it has actually been run in Unity.

## Delivery sequence

1. SDK reconnaissance: locate the current entity, player, damage, item/weapon, audio, save, and navigation APIs; confirm the target Unity/SDK version.
2. Spawn and lifecycle: deterministic save-scoped NPC registry, map-agnostic safe spawn selection, despawn/reload handling.
3. Movement and AI: locomotion, obstacle handling, patrol, sound investigation, perception, pursue/retreat/return state machine.
4. Combat and vitals: damage/death integration and firearm behavior with deliberately imperfect aim.
5. Audio and animation: reuse game humanoid clips and native animation parameters; graceful fallbacks if a clip is unavailable.
6. Settlement: choose a location, gather/consume available resources if supported, place structures safely, and persist ownership/state.
7. Single-player test pass: fresh world, reload save, each supported map, blocked terrain, lost target, death, missing audio, and repeated spawn checks.
8. Multiplayer later: add replication only after the above is stable.

## Important implementation constraint

This document records the agreed target and test gates; it is not evidence that these systems are already implemented. Each delivery step must be backed by code changes and a build/runtime test result before being marked complete.
