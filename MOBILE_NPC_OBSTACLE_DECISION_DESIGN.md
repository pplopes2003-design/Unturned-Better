# NAV-OBSTACLE-CHOICE-01 — Physically grounded route / door / jump arbitration

Date: 2026-10-13. Branch `feature/npc-ai-foundation`. **Source code only. Not compiled or tested in Unity.**

## Actual decision, from highest to lower priority

1. **Native doors, genuinely short walkable routes and cover/retreat remain preferred.** The brain retains the exact requested destination. A native unlocked `InteractableDoor` is NOT a jump obstacle. Native A*, genuine per-survivor remembered paths, actual nearby door handling, local stairs/ladder movement and corridor detours are attempted upstream.
2. A jump can only be considered when **a static world obstacle is physically detected close to the NPC**, in the direction of the final same-floor destination. Saturated casts reject conservatively. The candidate excludes characters, locked/unlocked doors, vehicles and bodies with rigidbodies. A physically blocked path to another floor does not authorize leaping straight through a building wall.
3. If a walking path exists and ends near the actual goal, sum its remaining segments and compare to the straight planar goal distance. **Prefer walking** when the plan is at most **3m longer**, or at most **1.55×** the direct distance. This is a bounded cost preference, not a promise of optimal graph paths. If the stored path is only an unknown-area frontier, follow that partial path first; its endpoint is not magically treated as the goal.
4. If a detour exists, give it a chance to produce real progress. Reconsider obstacle crossing only after repeated stuck recovery (at least **two** episodes). If no path exists and no prior stuck recovery, wait **1.0 seconds** from encountering that obstacle for normal route and door discovery, rather than reflexively jumping on first contact.
5. For goals within **5m planar distance**, a globally admission-limited and per-survivor cooldown-limited **exact** local 9×9 A* probe can provide stronger evidence of a short route around the obstacle. If found within the walking threshold, install that route and do not jump. The comparison does not add a new walkable graph edge, and partial frontier paths are not accepted as goal-reaching proof.
6. When walking is absent, materially longer or physically failed, authorize the existing **physics-only ballistic preflight**: normal Unturned impulse **7** for low hops or a bounded 25%-higher Parkour-style impulse **8.75** for genuine static parapets. The jump subsystem STILL must verify real body clearance, a safe same-floor landing, native gravity and physically correct trajectory. Upon approved takeoff, the *old planned* walking waypoints are discarded, without penalizing authentic personal route memory or treating the airborne arc as a corridor. The original brain destination stays unchanged.
7. On a missed landing or blocked airborne side collision, record a temporary local dead-end and **prefer alternate walking approaches for 6 seconds** rather than hammering the same failed jump. Native gravity and physical collision continue. Subsequent jumps require an actual grounded landing, cooldown and a new verified geometry probe.
8. **Combat safety:** no discretionary jumps during SeekCover / Retreat, including when the enemy is temporarily out of sight; swimming, listening, ambush, significant leg injury, crouch and ladder climbing also gate the jump. Goal changes invalidate previous obstacle-decision context.

## Physics bug found and corrected

The old motor had serialized `gravity=20` yet calculated fall acceleration as `Physics.gravity * (20/9.81) * 3`, roughly **60m/s²** for standard Unity gravity. The native Unturned `PlayerMovement` actually applies `Physics.gravity.y * 3` with a normal gravity multiplier of 1 during falling. That erroneous multiplier made JUMP=7 incompatible with the NPC motor. Commit `ce26eeb8` introduces a *new field name* `gravityMultiplier=1.0f` bounded 0.5–2, intentionally avoiding the previous serialized `gravity` prefab value (which might otherwise persist at 20). Both normal movement and proposed ballistic arcs now consume identical gravity physics. The original variable `gravity` is removed. This is a meaningful gameplay change to falling behavior and must be validated across maps, swimming edges and damage on landing.

Source commits:
- `ce26eeb8031a5694554f7e224e5fe680833c97cc` — native gravity compatibility.
- `4c1a1bf5cc6b182bf7a6346af3df4cc807aa628c` — ground path vs jumping cost arbitration.
- `06a16b5d4a24497d38c51cf06885c0f8e5b9e6f4` — movement integration and six-second jump failure preference.
- `f1ac8162efb12a0ddb082a1788fa80d378bf7200` — accurate speculative route clearing only after verified jump; no discretionary jumps under defensive tactical goals.

## Honest limitations / final Unity Dev Build checks

- The comparison is local and approximate; it does not imply multi-map optimality or that the optional licensed ASPFP runtime exists.
- CharacterController collisions and crouched/full-body pose may make some otherwise plausible jump arcs unavailable.
- **Test fixtures:** a 0.5m obstacle with a short side path (walk), a 0.9m parapet in a long corridor with no efficient bypass (jump if physically safe), actual doors open/locked (native door interaction vs alternative walking), partial A* frontier (do not assume goal reached), target above by floor (do not jump into wall), an actual failed jump (six-second no-jump), a direct player's attack leading to SeekCover/Retreat (prefer ground), 2 successive low parapets with real landing, and 1/10/20/40 NPCs in an occupied POI.
- Check native gravity consistency, grounded landing, inability to teleport through obstacles and actual frame CPU. On failure report map/coordinates/`Player.log`/repro and return scoped item to implementation. On success, only **NAV-OBSTACLE-CHOICE-01** may be marked runtime-verified. The parent **CORE-04** stays open until broader navigation is actually tested.
