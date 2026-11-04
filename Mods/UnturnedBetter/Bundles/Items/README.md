# Bluntforce Auto-32 (Unturned Better)

**Experimental content pack** for Unturned Better, using the native player gun and magazine systems. Do NOT put these files under Unity Assets/Runtime; copy the folder `Mods/UnturnedBetter/Bundles` into a game content folder scanned by Unturned (such as a development workshop content folder). Verify load order and unique item IDs.

- Gun: `59010` Bluntforce Auto-32; `Action Trigger`, `Auto`, `Semi`; magazine `59011`.
- Drum: `59011`; `Amount 32`, 12-gauge magazine caliber 8, `Pellets 6`.
- Source ammo: `113` (8 shells). The built-in crafting blueprint trades **four individual 12-gauge packs** for one 32-shell drum. A native blueprint does NOT prove all four consumed packs were completely full. **Do not treat the recipe as audited conservation of 32 rounds for arbitrary partial packs.** For strict partial-magazine accounting, add a separate validated transaction flow.
- Both item dats use `Master_Bundle_Override core.masterbundle` and `Bundle_Override_Path` to reuse existing vanilla visuals. **The paths and native animations must be checked in a game build.** Some versions may require adjustment of the magazine's visual bundle path.
- The resulting firearm is visible/usable through Unturned's regular asset registry. Player shooting is handled by **native `UseableGun`**, not NPC-only code. Custom item states require the assets loaded at startup and are not a drop-in runtime prefab.
- NPC pellets use magazine `ItemMagazineAsset.pellets`; the 32-round drum fires six independent pellet trajectories if available.
- Test manually: give 59010/59011, verify asset model and ammo count, fire and reload, assemble from four full 113 magazines, spend a half-full drum, reload, save and restore. Avoid colliding with other mods' IDs.
