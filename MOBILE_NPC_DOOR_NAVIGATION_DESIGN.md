# Unturned Better — genuine door interaction during urban NPC navigation

Date: 2026-10-13. Branch: `feature/npc-ai-foundation`. User compiles and tests at the END. This document records source integration, **not** proof of working gameplay. Canonical `CORE-04` stays `EM IMPLEMENTAÇÃO`.

## Root cause and native API verification

`Assets/Runtime/Assembly-CSharp/Unturned/Interactable/InteractableDoor.cs` defines `isOpen`, `isOpenable`, a **private** asset-derived `isLocked`, `checkToggle` and the native animated collision model (`ExpandedBarrier`). `Assets/Runtime/Assembly-CSharp/Unturned/Managers/BarricadeManager.cs` provides **`ServerSetDoorOpen(InteractableDoor, bool)`** which resolves the barricade region, uses a reliable native network invocation, updates the actual barricade item state, and updates physical door state on the host. `BarricadeRegion.FindBarricadeByRootFast` is internal in the same assembly.

CRITICAL SECURITY DISTINCTION: native `InteractableDoor.checkToggle(...)` has a **single-player/host permission bypass** in some configurations. Calling that method to decide AI access would let an NPC open locked doors. We therefore added **read-only** `IsLockedForMobileNPCNavigation` to the native door class and **always refuse** a genuinely locked door, regardless of player mode or plugin privileges. No fake Player, owner/group impersonation, `ClientToggle`, direct `updateToggle`, disabled colliders or teleportation.

## Implemented source changes

1. `a982f327fa23c9d797c7653ff880b644a18bd8d2`: `InteractableDoor.IsLockedForMobileNPCNavigation` exposes the actual stored asset-derived lock state **read-only**.
2. `18d793e05058f00b43544369f5322c1bf7d8287a`: new `MobileNPCDoorNavigation.cs`, a per-agent component with bounded nonalloc physical SphereCast and owner/locality checks. It will only open the **first physically blocking** native door, unlocked, non-vehicle, within 3m of NPC and 2.4m of obstruction. A wall before the door prevents interaction. It checks `isOpenable` (native debounce) and rate-limits open attempts. Uses `BarricadeManager.ServerSetDoorOpen` for real persisted/synced state.
3. `3e605e9ed0fc1af7b47517271bb9a0698722c898`: installer in `MobileNPCPopulationBootstrap`, after motor and before runtime NPC activation.
4. `2378921a1498080fd031c1c18ae2443f93f11b02`: `MobileNPCHumanoidMotor` handles legitimate door blocks **before discarding an A* path or requesting a local detour**. When a door is being opened, pauses movement rather than pushing against the door; does NOT clear goal or bypass actual CharacterController collision.
5. `79fcc883358fa80cc216592bb2a4b5235d1abd3c`, `ffb23d9ba5d58710a2b16391b9c06476c904c710`: a side-effect-free route-edge test allows local A* to *plan* across the one native unlocked door which blocks an edge, but only if there are no other colliders/walls/vehicles. It does NOT silently open or teleport; the motor physically approaches and opens at short range.
6. `54f24edc64ae6b33174bd66e0591b7f5772a32d4`, `423d0b2e4539076130d1039c4901de061b59ebd9`: the physically blocked *grid cell* itself can be considered traversable only if **every non-self collider** occupying it belongs to a single unlocked native door. Locked doors and mixed solid obstacles remain impassable.
7. `f867b1c798a786a0ff784ace65867a4079813fa8`: before opening, verify the actual `BarricadeRegion` and `FindBarricadeByRootFast` registration exist. This prevents using a stale/invalid door prefab with the native server setter that assumes registered data.
8. `d270eb35387c6c2e3126e53705c0480284e18f08`: preserve a short waiting state during the native hinge animation between throttled obstacle probes; do not discard a viable corridor merely because the raycast throttle briefly suppresses repeat inspection.

## Deliberate constraints

- No locked-door access, ownership spoofing or arrombamento; opening locked doors and breaking door structures (if possible and tactically relevant) need their own resource/permission/damage rules.
- Does not assume all doors are `InteractableDoor`: world objects, custom map doors and platform lifts may have different APIs. Do not pretend this solves doors universally.
- AI still does not have a full multilevel graph, navigation link for stairs, or route through a huge complex building. The local 9×9 grid and optional native A* bound the result.
- A successful `ServerSetDoorOpen` changes actual door state; it does **not** guarantee the doorway has cleared or that the agent actually reached the target. Physics and ground checks remain authoritative.
- Door state may change during combat; route checks still revalidate actual obstacle geometry. A door closed by somebody else after opening can be retried if unlocked and physically near.
- Per-actor physics buffers (16 ray hits / 32 overlap hits) and probing >=0.30s are bounded. Saturated buffers fail closed. Replanning remains limited by motor timers. These budgets require profiler validation at 20–40 NPCs.
- Temporary occupants inside a doorway should not cause route deletion; existing motor checks distinguish transient actors from a fixed blocking door or wall.
- Native `ServerSetDoorOpen` invokes game replication; **this is not a solution for network replication of the mobile NPC itself**.
- Doors may be world-bound (BarricadeRegion not found), welded, barricaded with extra structures or animated collision may behave differently by asset. Those cases remain blocked.

## Final Dev Build acceptance tests

- [ ] Spawn 1 NPC inside/outside a house with a real **unlocked closed barricade door** registered in the map. Give it a real target on the other side; it must plan through the doorway, walk up, open via the native server API, wait for the collider to clear, and physically pass the threshold.
- [ ] Same house with a **locked** native door: NPC must never open it, irrespective of single-player host bypass, owner ID or faction. Must seek alternative route or fail without teleporting.
- [ ] Solid wall directly before a door: can't open a door on the other side just because the ray hits the farther door. Side wall attached to the same door: local A* must fail closed when wall and door both block a node/edge.
- [ ] Doors carried on a vehicle: NPC must not open them using this ground traversal helper; actual driving and vehicle door behaviors are future VEH-01.
- [ ] Door just opened/closed by player: respect `isOpenable` cooldown and real animation. Do not oscillate every frame, repeat network toggle requests or walk through the door during collision.
- [ ] Two to four same-group NPCs reaching one doorway at once: first opens, others recognize shared open state and do not all call server open; no repeated stuck/replanning because other actors are temporarily present.
- [ ] Invalid/stale door prefab without registered BarricadeDrop, custom map object and map-edge clip: no exception, no world-state spoofing, no unintended opening.
- [ ] Cover behind a corner *and* an unlocked door: the local 9×9 A* can accept actual physically connected cells/edges, but never shortcut through a closed collider before native opening.
- [ ] Upstairs target in a two-floor house: DO NOT expect this door component to create a staircase graph; record navigation gaps under CORE-04 separately.
- [ ] With 1/4/20/40 NPCs, profile per-agent sphere casts and overlaps, native network callbacks, animation and FPS. Capture Unity Console, map and Player.log. This test is reserved for the user's FINAL Dev Build.

## Related work still open

`CORE-04`: multi-storey stairs and elevator/ladder links, indoor waypoint planning beyond 9x9 cells, map navigation graph connectivity, doorways blocked by placed barricades, native authored POI doors, group ingress choreography, dynamic replan. `AI-03`: cover, suppression, flanking and entry risk. `PERF-01`: stress performance. `RAG-01` and UI/dialogue tasks remain separate. Do not close or delete these from the canonical backlog before compile and actual gameplay feedback.
