# NAV-LADDER-01 — Native tagged ladder physical traversal

**Branch:** `feature/npc-ai-foundation` | **Recorded:** 2026-10-13 | **Status:** SOURCE ONLY; NOT COMPILED OR TESTED IN UNITY.

## Native Unturned implementation inspected

- `Assets/Runtime/Assembly-CSharp/Unturned/Interactable/InteractableLadder.cs` uses `RayMasks.LADDER_INTERACT`, `CompareTag("Ladder")`, sphere/ray casts, face `abs(dot(normal, collider.transform.up)) > 0.9` and tilt `abs(dot(Vector3.up, collider.transform.up)) <= 0.1`, LOS and capsule clearance checks.
- `PlayerStance.ReceiveClimbRequest` teleports a **player** to a carefully checked ladder attachment location; mobile NPCs cannot reuse player-only network/teleport functions or falsely claim they physically walked that shift.
- `PlayerStance.simulate` casts ~0.75m forward at +0.5m to remain in CLIMB while the real collider remains present. `PlayerMovement` applies a vertical climbing velocity. The NPC uses these *recognition principles*, not the PlayerStance RPC.
- Checked-in animation importer metadata has `Idle_Climb` and `Move_Climb`. Runtime NPC_Client rig may not reference them; adapter checks before playback, logs absence once.

## Implemented source components

1. `MobileNPCLadderTraversal.cs` (commit `f79fe8ed`) is a separate optional, server-authoritative, stateful action; no `Transform.position` teleports, no switching off collisions, no treating plain vertical walls as ladders.
2. `TryStartServer` samples eight nearby directions (plus a lower ray when looking from above), validates the native Ladder tag/mask, surface face orientation and upright ladder rotation. Entry can occur only near a physical ladder endpoint, not in mid-air halfway up the collider. A grounded capsule-clear approach and physically grounded upper/lower exit near the **actual requested target floor** are prerequisites. No valid exit means no climb.
3. `Approach -> Climb -> Exit`: `MobileNPCHumanoidMotor` alone calls `CharacterController.Move`, first horizontally approaching a reachable entry, then moving vertically while the **same ladder collider** stays in contact, then physically moving onto a verified ground exit. No scripted snap to top. Target changed, collider disabled, timeout or no measurable progress aborts; collisions still apply.
4. Motor `1a8eada0` attaches optional component at Awake, suspends conflicting A*/old detours, physics gravity, stale route memory, forced stuck recovery and landing recognition while traversal is active. Destination remains the original brain goal. `08d5522f` suppresses noisy walking footstep alerts during climbing.
5. `MobileNPCVisualAnimation` `3cdb54bd` uses `Move_Climb` or `Idle_Climb` **only when a runtime clip exists**, and suppresses walking footsteps. Actual upward/downward animation synchronization or third-person network animation replication is not proven.
6. `MobileNPCHumanoidCombat` `c6e450a9` and `MobileNPCHumanoidRangedCombat` `58912459` hold melee/gunfire while climbing (hands occupied); player-only stance RPCs never fire.
7. Physical safety fixes: `86742f34` validates grounded entry and real per-frame climb progress, `8cc44798` checks still-present collider, top/bottom exit and actual controller ground contact, `bdc3f05f` uses real midpoint ground/LOS consistent with native stance, `2702ad0d` uses the NPC actual ground mask rather than all layers.

## Conservative scope and outstanding limitations

- **Near-native-ladder action only.** It is opportunistically proposed when the NPC is roughly within ~1.7m of an authentic Ladder collider, target elevation differs by >=1.4m and target floor is close to a verifiable landing. This is NOT an authored long-range off-mesh graph link to arbitrary distant stairs, rooftops or unknown rooms.
- A map's collider bounds can differ from actual ladder top/bottom, or a ladder can use multiple tagged segments. Those cases may be rejected instead of guessing, by design.
- A ledge beyond the ladder may be walled off; `CharacterController.Move` can fail, the timeout aborts, and the NPC must use another route. No elevation snap or crossing blocked geometry. Native PlayerStance uses a controlled teleport for entry; this NPC intentionally does not, so success is not guaranteed on layouts whose ladder can only be reached with the player teleport.
- No guaranteed descent from every high lip, ladder-to-ladder transitions, multi-level path graph, stamina cost, stamina resource integration, pose IK, vault animation, falling damage simulation or verified network replication. General `NAV-JUMP-01` and `CORE-04` remain open.
- 0.18s shared ladder-entry admission, ~1.2s per-NPC attempt cadence, 9s traversal timeout, and short-period rung contact checks are source budgets, **NOT measured CPU/FPS assurances**.

## Final Dev Build checks (not executed)

- Authentic map Ladder tag and expected layer, front/back face, ascending and descending with actual separate upper/lower floor landings.
- From too far away, unrelated wall, rotated/angled ladder, invisible ladder collider with no exit floor, broken top platform and barricade obstruction: refuse without teleport or false completion.
- Body clearance beside narrow wall/ceiling, low roof at top, player blocking the ladder, water entry, target moves floors while halfway up, collider destroyed/disabled during climb, very long ladder timeout.
- Animation `Move_Climb` / `Idle_Climb` runtime presence, no mid-climb bullets/punches or noise footsteps, no learned walkway shortcut and NPC physically reaches the brain's final destination after exiting.
- 1, 10, 20 and 40 NPCs with simultaneous vertical targets; profile casts/GC, full source compilation, `Player.log`, possible starvation.
- On observed success, classify **scoped near-native-ladder entry/traverse/exit** CONCLUÍDA; errors reopen it. Parent `NAV-LADDER-01` remains EM IMPLEMENTAÇÃO until off-mesh graph/general-world routing and tricky entry/exit cases are handled.

Read with `MOBILE_NPC_PENDING_TASKS.md`, `MOBILE_NPC_PROJECT_HANDOFF.md`, `MOBILE_NPC_SINGLEPLAYER_TEST_PLAN.md` section 34.
