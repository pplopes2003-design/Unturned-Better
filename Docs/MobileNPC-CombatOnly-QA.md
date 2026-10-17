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
   `MobileNPCInventory`. Verify a legitimate selected GunId, at least one
   magazine loaded in native gun state, three spare magazines, bleeding
   dressing and medkit present. Watch real bullets decrement and magazine
   inventory transfer during reloads; ammo must not regenerate.
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
