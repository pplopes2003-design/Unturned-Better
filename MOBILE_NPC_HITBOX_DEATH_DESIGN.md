# Unturned Better — anatomical mobile NPC hitboxes, death physics, loot, XP

Date: 2026-10-13. Branch: `feature/npc-ai-foundation`. Source only; **not compiled and not tested in Unity**. Canonical issues: `CORE-02` and `CORE-03` in `MOBILE_NPC_PENDING_TASKS.md`.

## Actual NPC_Client source prefab audit

The shipped project asset is `Assets/Resources/Characters/NPC_Client.prefab`, inspected directly on this branch. Its YAML contains:
- **one** `Rigidbody`
- **zero** `CharacterJoint`, `ConfigurableJoint` or other physical joints
- **six** original `BoxCollider` components
- legacy `Animation` and `SkinnedMeshRenderer` hierarchy

It does NOT contain a complete articulated skeletal ragdoll. Re-enabling the six original colliders and lone rigidbody cannot produce a correct multi-bone ragdoll; doing so indiscriminately would also conflict with their authored axes and gameplay presentation. `MobileNPCHumanoidVitals.ActivateAuthoredRagdoll` is designed to fall back to a single physically simulated capsule corpse: actual gravity, tip-over torque and incoming hit impulse but **not** articulated limbs. A real multi-bone ragdoll remains separate `CORE-03 / UI-02` work, requiring bone transform/physics joint authoring in the intended humanoid rig and joint constraints verified in Unity.

## Living damage volumes (world-upright root)

The NPC's locomotion root is on layer `Agent` with the existing `CharacterController`. The humanoid visual uses its own native layer (16) and -90° correction. The additional injury volumes belong to the `MobileNPCHitbox` child on Unturned `Entity` layer **23**, have non-trigger colliders, and never own health. The separate `MobileNPCBodyHitbox` marker identifies anatomical region. The children are authored in root-local/world-upright axes, so the rotated visual hierarchy doesn't alter collision geometry:

| Child | Shape | Center relative to agent root | Radius | Height | Anatomical region |
| --- | --- | --- | --- | --- | --- |
| `Hit_Head` | Sphere | (0, 1.65, 0) | 0.20 | — | Head |
| `Hit_Torso` | Capsule | (0, 1.02, 0) | 0.26 | 0.81 | Torso |
| `Hit_LeftArm` | Capsule | (-0.43, 1.10, 0) | 0.14 | 0.72 | Left arm |
| `Hit_RightArm` | Capsule | (0.43, 1.10, 0) | 0.14 | 0.72 | Right arm |
| `Hit_LeftLeg` | Capsule | (-0.17, 0.39, 0) | 0.16 | 0.72 | Left leg |
| `Hit_RightLeg` | Capsule | (0.17, 0.39, 0) | 0.16 | 0.72 | Right leg |

These shapes are **coarse anatomical regions**, not animation-bone-bound colliders. They can slightly diverge from visible swinging arms/legs and have narrow gaps. Verify sightline, point-hit reliability, ray masks `RayMasks.DAMAGE_CLIENT` and `DAMAGE_SERVER` and first-person/third-person correctness in Dev Build; consider binding to actual bone transforms only once bone hierarchy and local-axis transforms are established from assets.

**Collision constraints:** Child hitboxes are non-trigger because the native damage ray masks and QueryTriggerInteraction might skip triggers. The `MobileNPCBodyHitbox.OnEnable` reapplies `Physics.IgnoreCollision` against its own parent `CharacterController` after an inactive template is instantiated, because IgnoreCollision can reset on reactivation. Do not enable extra Rigidbody dynamics on living hit volumes or use them as movement colliders.

## Server-authoritative damage / anti-duplication

1. A client Unturned `DamageTool.raycast` identifies the actual physical Entity-layer collider. Native `PlayerInputPacket` carries the append-only `MOBILE_NPC` raycast type rather than `SKIP`, including a reported hit point (no remotely trusted runtime Transform).
2. The server `PlayerInput.getInput` raycasts again with `RayMasks.DAMAGE_SERVER`, ensuring the first hit is a *living* `MobileNPCHumanoidVitals` collider close to the reported point, not a wall. Gun / Melee retain their real per-weapon range/ammo logic.
3. `UseableGun` / `UseableMelee` now pass the authoritative collider transform to `ResolveBodyPartAtWorldPoint(point, hitTransform)`. `MobileNPCBodyHitbox.TryResolve` is preferred only when the marker truly belongs to the targeted NPC. Legacy point-height inference remains as fallback if an older prefab has no markers.
4. For real physical head hits only, `ELimb.SKULL` is used; other body regions use `ELimb.SPINE`, not the undefined native limb of an arbitrary custom collider. `ApplyValidatedWeaponImpactServer` gives one health deduction plus limb condition/bleeding/fracture effects, preserves the real last attacker, and reserves lethal state against reentrant hits.
5. NPC-vs-NPC gunshots use the same validated anatomical marker on their physical raycast. Unarmed punch integration still needs separate tracing through native `Punch` input and is NOT covered by the new custom type, currently limited to Gun/Melee.
6. `DieServer` disables **all** six child hit colliders, not just the former root capsule; no subsequent gun should credit extra hits on corpse. `Died` only fires once per lethal transition and no native fake Player/Zombie is created.

## Death and actual loot

`MobileNPCDeathLoot` is present on every runtime NPC:
- Subscribes to its own `MobileNPCHumanoidVitals.Died`, processes only server-side once, while the corpse is dead, and retries refused world-drop transfers every >=4 seconds until corpse cleanup.
- For each genuinely owned item inventory slot, uses the item's full original ID, quantity, quality and state (including ammo) from `MobileNPCInventory.TryTakeItemServer`; then `ItemManager.TryDropMobileNPCItemServer`, which returns true **only after** successful vanilla `ItemRegion.items` insertion and reliable item broadcast. A vetoed drop is restored into the inventory. The death adapter does **not** invent guns, medical items or ammunition.
- World drops preserve native `onServerSpawningItemDrop` restrictions, reject Pro-only assets and out-of-bounds positions; source code checks region validity before and after the spawn hook. A refused Pro-only item remains in the corpse inventory and is cleaned up with the body unless the game supports an explicit alternative.
- A directly lethal player hit on a **Hostile** mobile NPC gives a modest configurable 10 XP via the actual `PlayerSkills.askAward` method. Friendly/Neutral kills yield none. Environmental bleeding/other damage can clear LastAttacker; no XP is credited in that case. This does **not** implement native kill statistics or quest objectives.
- The population director still schedules delayed replacement (default 60s) separately from corpse lifetime (default 30s), and the successor never inherits memories or inventory. Zombie/neutral/friendly kill-specific rewards and quest kill hooks remain pending.

## Commits in this scope

- `d0b109d26df87abec349e1be06fcc274504d2286`: `MobileNPCBodyHitbox` marker class
- `8d3973480172ab0a1b4208a3e3b739b21afd60c7`: six runtime anatomical physical colliders on Entity 23
- `e8895e7f91a38bfeef40aed59fbec8407bb2ae63`: colliders resolve body region, disable all at death
- `6e799bcc7e3646965f0ffb60b9a0df49deb8480c`, `f18b44c9bb104266c6ebe25c1e4d476dc9a74230`: gun and melee native critical multiplier and hit feedback
- `3efe708f21956a9a61d0723366b38d83ff39de73`: NPC firearm ray assigns injuries to physical marker
- `cc278d6b9d6bddf3df0433959d6c6376aef376b0`: reapply self-collision exclusion upon NPC enable
- `68bd3d8b9be71e4b71267cc47d7e057355ab7725`, `ce0da6e8bda2818e2167c331a760f419d04d382c`: server world drop with success and bounds verification
- `21c5c277d2757a715be726ac60314328728804a4`, `b609ed749631a3bde8c75e903547dbbe1376072c`: death-only inventory release and modest genuine hostile player XP
- `1c55dcba22413c296b801c726aab7f57c455ff07`: death loot component added to inactive template

## Required final Dev Build tests

- Hit head, torso, arms and legs separately with firearm and melee from multiple angles; verify actual collider marker, native hitmarker, resulting damage and injury type, not just a particle effect. Test range, cover, penetration, shotgun pellets and body hitbox gaps.
- Verify that hitboxes do **not** push the agent's own controller, particularly after game load, activate/deactivate and when a group of 4 collides in a narrow corridor.
- On lethal shot, `Died` fires once; all anatomical hitboxes turn off; rigid capsule corpse falls visibly when prefab has no bone joints; no movement/loot/medical effects after death. Corpse disappears after lifetime, replacement appears only after respawn delay. A full skeletal ragdoll is NOT currently possible with the inspected source prefab.
- NPC carries actual gun with magazine state, food, medical supplies and clothing: after death, world drop preserves exact IDs, quantities, state and each slot is removed once. If native spawning hook refuses, item stays on corpse for retry, never duplicates. Test Pro-only item, invalid map coordinates, water, elevated structures and a crowded POI.
- If directly killed by player, Hostile yields 10 vanilla XP once; Friendly/Neutral zero. Zombie or NPC as killer never awards player XP; bleeding that kills later with LastAttacker reset yields no XP. No false quest credit or kill stat.
- Observe Unity Console/Player.log and record map/camera/build. Source is uncompiled; do not declare CORE-02/CORE-03/NET-01 complete without gameplay feedback.

**Backlog discipline:** update this design plus `MOBILE_NPC_PENDING_TASKS.md` and `MOBILE_NPC_PROJECT_HANDOFF.md` whenever these systems change.
