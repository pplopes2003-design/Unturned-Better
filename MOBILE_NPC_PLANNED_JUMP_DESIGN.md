# NAV-JUMP-01 — Verified short off-mesh jump for mobile NPCs

Date: 2026-10-13. Repository `pplopes2003-design/Unturned-Better`. Branch `feature/npc-ai-foundation`. **Source implementation ONLY — NOT compiled or tested in Unity.**

## Exact implementation scope

`MobileNPCHumanoidMotor` no longer launches a blind vertical impulse from `ShouldJumpOverLowObstacle`. Instead, a grounded, mobile, uninjured survivor with a destination beyond an actual low static world obstacle may attempt `TryBeginPlannedLowJump`. It:

1. Throttles attempts to one substantial trajectory probe per ~0.10 s across NPCs and one attempt per survivor ~0.75 s; successful launches also honor the existing jump cooldown.
2. Requires a close low-obstruction spherecast (0.26 m above feet) and a clear higher ray. Rejects other Player/NPC/Zombie colliders and registered native door colliders; does not jump through locked doors or over other people.
3. Samples a **real grounded landing** about 1.35 m in the actual desired direction with surface slope checks, <0.25 m relative elevation change, map/clip bounds and a clear standing capsule. The actual AI destination must lie beyond the landing. This intentionally does not grant arbitrary roof access, ladders, high cliffs, or long-gap traversal.
4. Computes initial vertical impulse from the same gravity scaling as normal motor movement (`Physics.gravity.y * gravity/9.81 * 3`) and the actual prospective horizontal travel time. Bounds impulse and predicted apex; examines 11 intermediate 3D positions with `Physics.CapsuleCastNonAlloc` before approving; buffer saturation or any unowned hit fails closed. All movement after approval uses ordinary gravity + `CharacterController.Move`: **never directly writes Transform position to finish the hop**.
5. Drives the short airborne motion toward the verified landing and keeps the original brain destination. Rejects stale goals, water entry, elapsed timeout and unexpected side collisions. Landing is only considered successful if native grounded contact and position agree with predicted destination; failed landing records a temporary dead-end, not a fabricated successful route.
6. A hop or its immediate settling period **cannot be saved as a normal connected walking route**. The motor keeps the private first-hand path memory trustworthy. Planned jumps are gated by severe injury/physically crouched stance and do not generate footsteps while airborne.
7. The visual adapter only tries real existing clips with fragments `Jump` or `Leap` on the runtime rig, once per jump. The checked-in Human/Basic/NPC and Move animation metadata **does not confirm an authored jump/vault clip**; the engine must not invent it. A verified animation, synchronizing pose, dedicated stamina drain and source-level ladder authoring remain unimplemented.

## Not yet implemented

- General A* jump **edges** with authored off-mesh link locations, route planning across multiple gaps, traversing rooftops or ladders.
- True vault/kneel/crawl moves synchronized with an exact humanoid asset clip and collision pose.
- Climbing vertical ladders with `Move_Climb` and `Idle_Climb`, native ladder detector, entering/exiting, authority and fall risk.
- NPC stamina/energy spending for jumping. NPC current vitals/injury classes do not expose an analogous player stamina budget; do not claim one.
- Multiplayer synchronization and measured 40 NPC performance.
- Any runtime confirmation that Unity physics sample tolerances match the actual maps.

## Final Dev Build cases — NOT EXECUTED

- Low static 0.35–0.65 m obstacle with grounded space behind it, 1.35 m travel, open headroom; check physical arc, animation availability and landing, no transform snap.
- Obstacles >0.7 m, closed/locked doors, another living character, vehicle, upper beam, low roof, missing landing ground, steep landing, water, cliff and excessive height difference: no approval, no wall bypass or fake route.
- Target change, damage to legs, crouch, listening, combat, route invalidation and successive jumps: cooldown and safety. Collision halfway through should abort horizontal jump and obey physics.
- Multiple NPCs: sampling admission and CPU costs; `Player.log` & Unity Profiler.
- Reload same survivor after hopping: do not create a reusable `MobileNPCTravelMemory` stair/corridor from the airborne arc. Death resets only true individual memory, no transfer.
- Confirm both `Animation` legacy prefab variants and no unjustified assumed Jump clip. Climb animations exist by importer name but climbing movement is not yet part of this scope.

Source commits: `b92feff1` (state), `a1892748` (preflight trajectory), `04ccd8b0` (motor execution), `03d4724a` (cost bounds), `b3c9080c` (visual), `ba9c4b36` (walking memory/airborne feet), `6571aaa5` (collision/water abort).

**Classification**: short physically planned static-obstacle hop subitem **QUASE PRONTA — AGUARDANDO BUILD** subject to Unity C# compilation and physics validation. Parent `NAV-JUMP-01` remains **EM IMPLEMENTAÇÃO** until verified vault, planned graph links and ladders exist; `CORE-04` cannot be closed by this bounded hop.
