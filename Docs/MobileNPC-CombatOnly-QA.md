# Combat Only — independent hostile NPC combat sandbox

## Safety and mode selection

Original A-Life code is preserved on branch `backup/alife-survival-2026-10-16`. The
working implementation remains on `feature/npc-ai-foundation`. Neither branch
deletes the other. The original Normal and mixed Combat modes are STILL
available.

At the main menu, open Mobile NPC settings and press `Modo` to cycle:

1. **Normal** — the existing survival/exploration/trader/factions experience;
2. **Combate** — the existing mixed-faction combat setting;
3. **COMBATE TOTAL** — this new opt-in hostile-only combat sandbox.

This choice persists in PlayerPrefs and is meant to be selected **before**
loading a world, not switched mid-match. CombatOnly uses isolated
`MobileNPCs/<server-id>_<map-name>__CombatOnly.json` instead of overwriting
the existing `<server-id>_<map-name>.json`. The explicit old "erase all NPC
history" button still deletes snapshots of ALL modes after its two-click
confirmation, so do **not** use that to switch modes.

## Spawn expectations (real native items)

Every **fresh** NPC generated while CombatOnly is selected:

- Is hostile to the player (including explicit test-spawn commands originally
  requesting "neutral" or "friendly"). Its `MobileNPCFaction` may be Raiders
  or Military; both have hostile **disposition** toward players.
- Receives **one** rifle, randomly selected from Maplestrike **363** with
  compatible Military Magazine **6**, or Zubeknakov **122** with Ranger
  Magazine **123**. Its real native firearm state includes a loaded compatible
  magazine, with **three separate full spare magazines** in its 12-slot
  authoritative `MobileNPCInventory`. Reloading consumes actual items;
  no infinite ammo is configured. The fallback uses the native Military pair
  if the selected assets/caliber are not available. Native `Assets.find` and
  magazine caliber checks prevent invalid item creation.
- Also receives a Military Knife **121**, two Dressings **394**, a Bandage
  **95**, a Splint **96**, and a Medkit **15**. Medical behavior consumes real
  items for bleeding, fractures and missing HP, subject to its existing
  safety/tactical checks.
- Has genuine native *owned* clothing, selected only after checking item
  category: Desert Military Helmet **1010**, Desert Military Top **1011**,
  Desert Military Bottom **1012**, Desert Military Vest **1013** and
  Alicepack **253**. The military pieces fallback individually to Forest
  Helmet **309**, Top **307**, Bottom **308** and Vest **310** if unavailable.
  `MobileNPCEquipment` already displays these through `HumanClothes` and
  applies their native, quality-sensitive mitigation to the appropriate body
  parts. The backpack is equipment/appearance in the current simulated
  inventory architecture, NOT a separate vanilla backpack grid.
- Inventory holds **18 simulated slots in CombatOnly only** (Normal retains
  12). A full kit uses 15 slots: rifle (1), three spare magazines (3),
  knife (1), two dressings (2), bandage (1), splint (1), medkit (1),
  helmet (1), shirt (1), pants (1), vest (1), backpack (1).
  **Three empty slots remain** for reload swaps and future incidental items.
  The fourth magazine is loaded *inside the rifle item state*, not a
  separate inventory slot. On restore, no starter items or medical supplies
  are duplicated.
- Keeps injury, ranged combat, aiming, civilian-faction-independent combat
  hostility, tactical withdrawal, death/ragdoll, death loot and equipment
  persistence. Has no hunger/thirst loss, food/water gathering, structure
  building, trading, airdrop-loot errands or room-loot scavenging in this mode.
  The ordinary strategic planner does only collision-aware combat patrol.
- A restored combat sandbox NPC loads its exact owned items **from the separate
  JSON save**, without being gifted a second starter kit or refilling ammo.

## Dev Build test plan

1. **Preservation first:** note down normal saved NPC count/inventories,
   enter the main menu, select COMBATE TOTAL and enter Washington. Do not
   delete historical snapshots. Later switch back to Normal and verify its
   existing snapshot remains intact and unchanged.
2. Spawn automatic NPCs then manually spawn one friendly and one neutral
   through the existing test commands. In CombatOnly ALL must report hostile
   `MobileNPCHumanoidDisposition` and be capable of acquiring/attacking
   a valid player target. NPC group/faction combat can still pit Raiders
   against Military; hostility to players is guaranteed by the Role, not by
   treating Raider and Military as the same faction.
3. Inspect `[MobileNPCCombatOnly] Spawn ... kit=issued-once` and actual
   `MobileNPCInventory`. Verify exactly 15 initial slots, one loaded
   compatible native magazine in the rifle, three spare magazines, two
   dressings, bandage, splint and medkit, plus helmet, vest, shirt, pants and
   backpack. `HumanClothes` must display authentic clothing, and a body hit
   must flow through `MobileNPCEquipment.ApplyValidatedBodyArmorServer`.
   Watch real bullets decrement and magazine inventory transfer during reloads;
   ammo must not regenerate.
4. Wound an NPC, stop shooting and observe medical supplies consumed after
   a safe opportunity. If it is bleeding or has a fracture, separate wound
   and splint code should handle those items before generic HP healing.
5. Wait ten minutes in game: no hunger/thirst loss, survival demand, food
   scavenging, building or trader actions. NPCs can patrol without an active
   target; **patrol does not imply knowing the unseen player's position**.
6. Verify local combat movement: within weapon range the bot should not
   suicide-charge. When a verified lateral firing position is possible
   nearby, it uses the 4.1m local physically reachable tactical reposition
   before speculative long flanks. Obstructed/locked entrances do not
   become teleport links.
7. Test facing 3+ zombies when under-equipped or near death: a known
   dangerous area penalizes planned paths, and 4.2m/5.6m physically checked
   withdrawal candidates replace the old invalid 7m proposal. Genuine
   route failures remain possible; log `[MobileNPCZombieRisk]` and do
   not assume a retreat was possible if no real escape exists.
8. Kill a bot: ragdoll limbs must articulate **11 Rigidbody / 10
   CharacterJoint**, loot should drop from the actual owned inventory.
   Unity `MobileNPCRagdollPrefabTests` is only a prefab correspondence
   check, not a replacement for a physical death playtest.
9. Quit, reopen COMBATE TOTAL and check the same NPCs retain **depleted**
   actual ammo and medical items without a duplicate kit. Switch back to
   Normal and verify its separate original actors. Try 10, 20, then 40 NPCs
   while profiling CPU frame time on the 8GB Intel UHD laptop.

**Still unverified in actual Unity:** this source change has not been compiled
in Unity 2022.3.62f3 or tested in Washington. The missing
`Pathfinding.NavmeshCut`/native ASPFP plugin problem is NOT solved by this
mode. Open doors, assorted window vaults, ragdoll activation, aggressive
spawn encounters and 60 FPS require subsequent Dev Build validation.

## Firearm presentation follow-up — 19 Oct 2026

Player testing: **CombatOnly bots visibly carry their real rifles at the
side of the body without raising their arms to a firing stance**. This
problem concerns THIRD-PERSON VISUAL POSE and should not be "solved" by
changing ballistics, ammo or target acquisition.

Source checks:

- `MobileNPCRangedPresentation` attaches the actual native
  `ItemAsset.equipablePrefab` / item to the same right/left/spine
  hooks used by `PlayerEquipment`, with the native 90° Z item rotation.
- `MobileNPCVisualAnimation.BindEquippedWeaponAnimation` adds item-specific
  legacy clips (`ItemAsset.animations`, typically `Equip`) with mixing
  on the NPC's **Left_Shoulder/Right_Shoulder**. However an AnimationState
  returning `IsPlaying == true` is NOT evidence that the arms moved.
- A runtime **anatomical fall-back** now checks shoulder-to-hand forward
  extension after the authored `Equip` transition has had 0.35 s.
  If either arm is still lowered, `LateUpdate` derives bounded rotations
  on the actual original shoulder transforms so the equipped rifle raises
  in front of the character. It resets to the baseline each update to
  avoid accumulated twisting and preserves the original native animation
  when effective. This is not two-bone IK, nor a new projectile direction.
- When no authored Shoot/Reload clip works, the existing pose gets
  a brief visual recoil/reload lowering; the server remains the only
  authority for firing/reload/actual equipment.
- The fall-back logs `[MobileNPCWeaponPose] Native rifle stance was not
  visually effective; using verified shoulder bones as procedural fallback`
  on a Dev Build. It never adds physics bodies or changes the ragdoll.

**Dev Build tests**:

1. Rifle at rest: the visible model and both hands should rise together;
   verify the right hook follows the right arm and that neither arm
   points vertically downward after the bound period.
2. Walk/run/crouch with rifle, then turn toward a moving target: the
   gun should remain held coherently instead of snapping every 0.5 sec
   or accumulating unnatural shoulder rotations.
3. Fire 20 rounds while moving, then reload a full magazine: verify
   gun animation or brief fallback recoil/reload presentation; compare
   server inventory to actual bullets. Procedural motion must never
   fabricate ammo or impact.
4. Switch to melee for a strike and back to gun: existing punch or
   knife animation must not be blocked by overlay/fallback, and the
   gun should resume its hold when it is reattached.
5. Kill the NPC while aiming: the Death event clears clip ownership
   and stops visual pose so the **player-confirmed native articulated
   ragdoll** can simulate without competing bone writers.
6. Verify first-person player gun animation is unaffected by the
   standalone NPC visual component. Profile 10/20/40 armed NPCs.

**Localhost AI proposal (NOT IMPLEMENTED)**:
A separate local process can accept bounded high-level observations and
return strategic orders asynchronously, but cannot query Unity Physics,
move CharacterControllers, animate bones or perform instantaneous combat
targeting without recreating the entire game state. Singleplayer already
has a server-authoritative logic path via `Provider.isServer`. A localhost
service is worth considering **only after measuring** CPU overhead in
the existing tactical planner; on 8 GB RAM an extra process can worsen
resource pressure. Keep the low-latency perception, physics, shot
validation, melee and animation inside Unity; an optional service can
suggest slow tactical goals and must fail back to the existing AI.
