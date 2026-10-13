# NAV-VAULT-01 — Standard Unturned-style jumping over parapets

**Decision updated by user, 2026-10-13.** Branch `feature/npc-ai-foundation`. Source integration inspected; **no Unity compilation/playtest or measured physics yet.**

## Product requirement — no dedicated Vault animation

The player-facing behavior is an ordinary Unturned-style **jump**. Crossing a series of obstacles means jumping, actually landing on walkable ground, and jumping again when necessary and safe. There is **NO requirement for an authored Vault animation or hand-planted scripted climb**. Do not make one a blocker for this feature. **Genuine Ladder objects still have a distinct Move_Climb/Idle_Climb animation path**. Do not use a ladder animation for jumping over a wall.

The legacy implementation in this file was a `Rise -> Across -> Land` artificial clamber that prescribed vertical/horizontal motion and suppressed gravity during ascent. **This implementation was replaced** (commit `6d3e969f` planner, `92b6b7c8` motor). Older handoff claims about three-phase artificial clamber are superseded by this document.

## Source implementation — physically checked jump, not levitation

- `MobileNPCVaultTraversal` is now only a **bounded ballistic feasibility checker** (`TryPlanJumpServer`). It never moves the NPC, changes Transform, forces the capsule upward or disables gravity. The existing `MobileNPCHumanoidMotor` is the sole `CharacterController.Move` owner.
- The checker requires a real *static* colliding parapet of height **0.65–1.02m**, horizontal physical top, destination beyond it, same-floor ground for a safe landing about **1.65m** ahead with <=0.22m elevation difference, head/body clearance and a collision-checked simulated ballistic arc. Dynamic bodies, doors, vehicles and ceilings do not become vault links.
- It uses the game player movement values as a reference: `PlayerMovement` defines a normal jump impulse **7.0** and optional Parkour mastery bonus up to **25%**. The optional higher bound is **8.75**, never unconstrained upward lifting. Same gravity multiplier used by the mobile NPC motor calculates apex, forward-delay and landing duration. A jump that cannot clear a given top at this acceleration/speed is rejected, even if it looked visually convenient.
- The single motor jump state (`isPlannedJumping`) now handles **both** low hops and higher parapet jumps. `TryBeginVaultJump` consumes `JumpPlan`, starts the actual physics impulse, briefly delays forward motion until the feet can clear the real top, and moves across at a bounded normal-running speed; velocity.y always evolves under actual gravity and collisions until grounded. There is no `Rise/Across/Land` locomotion overriding physics.
- Once actually grounded, the actor can replan another jump after the cooldown and a fresh physical verification. This allows **sequences of independent jumps** over consecutive obstacles without teleportation or fabricated links between platforms. The air path is never stored as an ordinary walkable route in the individual NPC memory.
- On missed landing/side collision, target change or immersion, the active jump is canceled; gravity and pathfinding recovery continue. Ordinary jump and higher-hop states suppress midair footsteps. A separate scripted fall-recovery routine does not interfere while the NPC is actively jumping (`9c383849`).
- `MobileNPCVisualAnimation` treats a parapet hop as `motor.IsJumping`, not `IsClimbingLadder` (`a53bda29`). If a runtime Jump/Leap clip really exists, it may be used; otherwise the pre-existing walk/run pose continues for the air phase, without a fabricated dedicated asset or climbing animation. Reload/fire/melee handling stays with the ordinary motor/brain combat restrictions.

## Status, limits and open work

Scoped source `NAV-VAULT-CLAMBER-01` retains its **historical ID** for continuity, but now means *standard physics jump across a real parapet*: **QUASE PRONTA — AGUARDANDO BUILD**. Label the previous clamber method **SUPERSEDIDO PELO PULO PADRÃO**; never reintroduce a bespoke rise/hover or mark runtime approved until tested.

Parent `NAV-VAULT-01` remains **EM IMPLEMENTAÇÃO** only for movement coverage not yet demonstrated, final Unity physics behavior and general off-mesh planning, **not because a dedicated Vault animation is missing**. `NAV-JUMP-01` and `CORE-04` global routing likewise remain open until verified. We do not claim that obstacles taller than 1.02m, arbitrary ledges/rooftops, moving platforms or multilevel rooms can be reached.

## Final Dev Build tests — not executed

1. Heights **0.65, 0.75, 0.90, 1.02, 1.10m**: 1.10m should not be attempted; among 0.65–1.02m only physically achievable apex/clearance and safe-floor landings may pass. Compare actual Unturned player physics on same obstacles.
2. Film actual NPC arc and grounded landing, without position snapping, artificial levitation or Move_Climb. A visual Jump clip is optional; lack of one does not block the feature.
3. Two nearby parapets: first verified jump, **physical ground contact**, fresh preflight/cooldown, second jump. Never launch the second hop while airborne or permit endless bunnyhopping.
4. Solid tall wall, locked door, another NPC, moving vehicle, water, missing floor, upper ceiling, steep landing, alternative stair/walkable route: refuse unsafe jump or physically collide, do not tunnel.
5. Brain target change during a jump, leg injury, crouch, ambush and under-fire retreat/cover: preserve tactical guards, no invented jump path memory, no false gun/footstep events.
6. Test 1/10/20/40 NPCs under profiler and record actual FPS, CPU casts and broken cases. Compile all C# and check `Player.log` before changing any scoped state to CONCLUÍDA.

Source commits `6d3e969f`, `92b6b7c8`, `a53bda29`, `9c383849`. Older three-phase design is explicitly obsolete.
