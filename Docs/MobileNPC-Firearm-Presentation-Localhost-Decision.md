# Mobile NPC firearm pose fix and optional localhost AI architecture

Date: 2026-10-19. Branch: `feature/npc-ai-foundation`.

## New dev-build observation

Player-tested **Combat Total** actors own and visibly equip firearms, but their
arms stay at the default locomotion/body-rest angles and rifle meshes simply
hang from the arm hooks. Prior positive tests confirmed authentic melee
animation and physical ragdoll.

## Root cause in checked-in U3 SDK

- `MobileNPCRangedPresentation` already clones the exact equipped item mesh,
  anchors it to `Skeleton/Spine/Right_Shoulder/Right_Arm/Right_Hand/Right_Hook`
  (or an alternate genuine `EquipableModelParent`) and uses the same
  `Quaternion.Euler(0, 0, 90)` model transform as vanilla `PlayerEquipment`.
- `MobileNPCVisualAnimation` previously played only `Idle_Stand`,
  `Move_Walk`, `Move_Run`, and Punch clips from `NPC_Client`.
  Its fire/reload handlers guessed existing clip names but never ADDED the
  per-weapon animation clips.
- By comparison vanilla `PlayerEquipment` lines ~1612-1617 iterates over
  `ItemAsset.animations` and calls `PlayerAnimator.AddEquippedItemAnimation`.
  `CharacterAnimator.AddEquippedItemAnimation` calls `Animation.AddClip`,
  registers `AddMixingTransform(leftShoulder,rightShoulder)`, sets layer=1,
  and handles `Spine_Hook` and the equipable model transform when needed.
  Original item loading requires an authored `Equip` animation clip.

## Code correction

- On new actual rifle model attachment, call
  `MobileNPCVisualAnimation.BindEquippedWeaponAnimation(ItemAsset, Transform)`.
  Load its **native**, legacy-compatible `ItemAsset.animations` and mix the
  exact two shoulders (plus hook/model as native does) at layer 1, while
  locomotion stays on the ordinary layer.
- A native `Idle` loop is preferred; otherwise clamp the authentic `Equip`
  pose on its authored last frame. Real `Shoot`/`Fire`/`Recoil` and
  `Reload` one-shots temporarily override this upper-body hold and return
  to it. If a specific asset lacks a clip, log instead of pretending the
  pose exists.
- Changing models, fist/knife melee attacks, climbing/jumping, dying and
  despawning suspend or release this overlay. No new damage, infinite ammo,
  fake firearm aiming or unauthored animation asset is introduced. The verified
  Punch_Left/Right clips and 11-body ragdoll remain the same.

**STATUS: SOURCE CHANGE ONLY, NO UNITY BUILD RUN HERE.**

### Smoke test

1. Spawn fresh Maplestrike 363 and Zubeknakov 122 NPCs separately in Combat
   Total. Watch for `[MobileNPCWeaponPose] Native equipped item ... hold=...`.
   Confirm raised, two-handed weapon pose when stationary and during walk/run,
   not merely the weapon model hanging from a hand.
2. Trigger genuine combat. Confirm firing returns to weapon-ready posture and
   finite magazine reload uses the item's native Reload animation if present.
3. Force melee against a nearby zombie: Punch_Left/Right must still run, and
   the gun/weapon overlay must release and restore without permanently
   overriding fists or other animation.
4. Kill the actor: no animation may overwrite released articulated ragdoll.
   Equip another gun and verify no duplicate overlay states or old gun
   animation blend remains.
5. Profile 10/20/40 bots: animation assets must only be bound on actual gun
   changes, not every Update. 0.5s model-refresh polling does not add clips
   repeatedly.

## Localhost AI controller suggestion — not implemented

User mentions seeing a mod with a localhost process for NPC AI. Without its
name, code or link we **cannot conclude** its actual architecture: localhost
might be a listen server, a separate Python/C# decision process, a web UI or
a locally running generative model.

Potential design for this single-player fork:

- **Unity main thread remains authoritative:** native collision, animation,
  characters, path validation, projectiles, inventories, hitboxes, death and
  zombi targeting. No socket or LLM call inside `Update`/combat hit testing.
- **Optional strategic decision worker** receives bounded, firsthand
  perception and position/cost snapshots at ~0.5-2s intervals, returns only
  typed intentions: `Patrol(location)`, `SeekCover(location)`,
  `Withdraw(location)`, `Investigate(soundPosition)`. Apply only after
  normal Unity path/physics validity and a freshness check.
- **Default local, in-process decision system** remains fully functional
  when no worker is installed/running. For limited 8GB integrated-graphics
  machines prefer job-style C# batches and multi-NPC staggering before
  launching another RAM-hungry process.
- A separate localhost service may allow process isolation, alternate
  model/language implementations and no internet cost, but shares the
  machine's CPU/RAM and adds serialization/scheduling latency. It does
  **not** magically discover static house entrances or fix animation.
- **Decision gate:** profile the current build's CPU time per NPC,
  movement/route failure frequency, main-thread spikes and worker serialization
  overhead before selecting a local process. The user's reference project
  is useful if supplied; do not copy an unknown implementation by assumption.
