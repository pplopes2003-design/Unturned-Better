# Mobile NPC quick test (development branch)

Branch: `feature/npc-ai-foundation`

## What this prototype does
- Runs its decisions only when `Provider.isServer` is true.
- Periodically searches active `SDG.Unturned.Player` objects and selects the nearest visible player inside its sight range and field of view.
- Tracks a visible target, investigates the last known position after losing sight, and can return to its spawn point.
- Accepts explicit sound notifications through `NotifyNoise(position, loudnessRadius)`.

## Important prototype limitations
This is an isolated prototype component, not a complete NPC prefab or a production-ready AI. It does not yet use Unturned's native pathfinding, weapon/inventory/damage systems, animations, or audio event pipeline. Movement currently requires a `CharacterController`; it will not move if one is absent. Do not merge this branch into the release branch yet.

## Quick in-Unity smoke test
1. Open the project using the Unity version configured by the repository.
2. Let Unity finish importing scripts, then check the Console for compiler errors. Fix compile errors before playtesting.
3. In a disposable test scene, create a humanoid NPC GameObject with a collider and `CharacterController`. Add `MobileNPCBrain`.
4. Ensure the scene has a floor collider. Set the obstruction mask so environment geometry blocks sight; ensure the player/NPC colliders are configured so the target's collider is hit by the raycast.
5. Run as a server with a real connected player. The brain only executes on the authoritative server. Place the player in front of the NPC within 32 m; it should pursue. Break line of sight; after the memory period it should investigate the last position.
6. Verify dedicated server and client behavior separately. This component does not replicate movement on its own; the host game's networked character/controller integration is still required before multiplayer acceptance.

## Acceptance checklist
- [ ] Unity compilation succeeds with the repository's configured editor/toolchain.
- [ ] Server acquires the nearest visible connected player.
- [ ] NPC stops acquiring through walls and outside its field of view.
- [ ] NPC investigates last seen position and returns when requested.
- [ ] No change to existing vanilla zombies or sentries.
- [ ] Movement and state are replicated correctly before multiplayer use.
