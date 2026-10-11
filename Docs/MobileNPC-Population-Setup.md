# Mobile NPC population — runtime bootstrap and limits

Branch: `feature/npc-ai-foundation`

## What is now wired

- `MobileNPCPopulationBootstrap` subscribes to `Level.onPostLevelLoaded` and, for a local server session (single-player), builds a runtime-only mobile humanoid template.
- The template reuses the game's `Resources/Characters/NPC_Client.prefab` as a **visual child only**. The legacy prefab is not modified; its colliders and rigidbody are disabled and legacy gameplay MonoBehaviours are disabled.
- The runtime root receives a `CharacterController`, brain driver, motor, perception, disposition, vitals, aim, melee/ranged combat, and the best-effort legacy animation adapter.
- `MobileNPCPopulationDirector.Configure` receives the inactive template. The spawn service activates each clone before initializing it.
- The population loop starts after the configured interval, spawns one NPC per attempt around the living player, and is capped at 12 active agents by default.

## Single-player test

1. Open the project in the configured Unity Editor and wait for script import.
2. Start a disposable single-player map.
3. Check the Console for `[MobileNPCPopulationBootstrap]` and `[MobileNPCPopulationDirector]` messages.
4. After roughly 18 seconds, look for a humanoid NPC outside immediate player range. If none appears, inspect the warning for rejected ground candidates, then verify the current map's collision layers and bounds.
5. Move within sight range and line of sight. A hostile NPC should detect and pursue the player; firing and melee are experimental server-local prototypes.
6. Verify that the model is visible and that available legacy animation clips are selected sensibly. Some visual assets may not have clip names containing idle/walk/run; in that case the animation adapter falls back to the first available clip or may remain static.
7. Kill an NPC if possible and confirm the controller cleans up its body after the configured delay.
8. Load a second map and confirm the bootstrap is recreated without duplicate directors.

## Important boundaries

- This is a runtime integration prototype, not a verified feature: **Unity compilation and in-game testing have not been performed in this environment**.
- Spawn, damage, AI state and cleanup are local-server only. No generated network replication protocol or client interpolation has been added. Do not treat this as multiplayer-ready.
- The population currently spawns around the player, not across a map-wide region sampler. NPCs do not yet build bases, persist inventory/health/faction/home, or restore settlements after reload.
- The current brain supports idle, investigation, pursuit and return states; autonomous long-distance roaming and settlement behaviour are still pending.
- The movement motor is collision-aware but does not pathfind around obstacles. NPCs can get stuck behind walls or terrain.
- The runtime model adapter is best-effort and has not been validated against all animation states, sounds, or map collision layers.
- Static dialogue NPCs, sentries, and zombie prefabs are not modified by this bootstrap.
