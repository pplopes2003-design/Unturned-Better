# NAV-VAULT-01 — Collision-authoritative short parapet clamber

Date: 2026-10-13. Branch `feature/npc-ai-foundation`. **No Unity compile or runtime tests yet.** This is not a promise of arbitrary mantle/ledge/vaulting.

## Available source and restricted functional scope

- `MobileNPCVaultTraversal.cs`, added in commit `256609a567924d9c2c990359a43d9139d4f11fab`, is a self-contained server-only, bounded three-phase physical *clamber*: **Rise → Across → Land**. The `MobileNPCHumanoidMotor` is the ONLY caller of `CharacterController.Move` (integration `c606a7eb3b70081fb204230a3ea7542717644795`). The traversal NEVER assigns NPC Transform position/rotation, disables the capsule or teleports the actor.
- It requires a static wall top actually observed with collision raycasts at **0.70–1.10 m over the NPC's feet**, a genuine horizontal upper surface, destination beyond the wall and on the **same floor**, a walkable exit approx. 1.85m farther with <=0.22m height change, and clear standing-capsule volume and swept full-body paths both upward and over the top. Solid door, vehicles, other mobile NPCs, zombies, players or rigidbody-driven moving obstacles cannot be clambered.
- It lifts the *real* capsule along the near side of the wall, moves across only after sufficient elevation, then restores ordinary gravity for the grounded landing. Physics collisions and actual blocked progress may cancel the action. Timeout 3.8 seconds; no forced completion and no proof of target arrival merely from choosing an off-mesh link. A failed maneuver may fall under ordinary gravity, never teleport.
- Integrated into the brain-controlled motor as an optional special action before the short ballistic jump: avoids incompatible planned route, ladder and reactive jump, holds normal firing/melee during entire action, suppresses ground footstep alerts, excludes the vertical/airborne portion from personal walkable route memory, and cancels on loss of target or immersion in water.
- `MobileNPCVisualAnimation` only uses actual `Move_Climb` / `Idle_Climb` loaded on the NPC_Client legacy Animation. There is **no confirmed dedicated Vault clip** in the checked-in Human/Basic importer. The absence of a dedicated animation makes this an approximate *clamber* presentation, **not** a realistic hand-planted fully synchronized vault. It logs once if neither clip is present and maintains physics authority.
- Shared expensive geometric probe admission ~0.12sec and per-actor retry >=1sec, cooldown after finish, bounded fixed 20-hit cast/overlap buffers. The budgets are source limits, not profiled FPS.

## Deliberate unfinished items

- Real authored and rigged arm-hand-vault motion or dedicated animation/IK, third-person replication and synced pose/capsule resizing.
- A* off-mesh **vault links** between remote rooms and multiple parapets, traversing arbitrary rooftops, moving platforms, windows with vertical clearance, climbing walls without physical traversable tops, broad ledge grabs.
- Actual stamina consumption. NPC vital model has no validated stamina budget. Do not fabricate one.
- Runtime proof of Unity collider interaction, ascent direction, game prefab animation, safe fall and multi-NPC performance.

## Dev Build — not executed

1. Standard static parapet with top 0.8–1.05m, standing headroom, same-floor ground behind ~1.85m; NPC chooses physical 3-stage clamber, never a snap, player can collide with it and actual feet reach final ground before AI considers target reached.
2. No upper top collider, blocked ceiling, adjacent second wall, moving vehicle, locked door, player/zombie in wall path, slope/ground missing beyond, platform height >1.1m or target on an upper floor: clamber denied.
3. Another NPC blocks exit *after* preflight, closing doors, damage/target change, water and interruption mid-Rise/Across/Land: no collision bypass, obey real gravity and cooldown.
4. Distinguish low 0.35–0.65m **ballistic jump** from higher **clamber** and from genuine **Ladder**. No false walkable memories and no shots, punching or zombie-footstep noise midair.
5. Measure near 1/10/20/40 NPCs for raycast/overlap CPU cost and frame intervals. Check imported Move_Climb is actually referenced in runtime prefab, and record presentation limitations without falsely claiming a real Vault clip.

**State:** Scoped `NAV-VAULT-CLAMBER-01` source is **QUASE PRONTA — AGUARDANDO BUILD** (Unity/runtime tests necessary). Parent `NAV-VAULT-01` **EM IMPLEMENTAÇÃO** (animation IK/coverage/offmesh). `CORE-04` still open.
