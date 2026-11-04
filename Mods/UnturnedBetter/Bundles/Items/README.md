# Bluntforce Auto-32 (Unturned Better)

**Experimental content pack** for Unturned Better, using the native player gun and magazine systems. Do NOT put these files under Unity Assets/Runtime; the Unity Standalone build postprocessor copies these item .dat files into `Bundles/Items` beside the player executable; for an existing Unturned installation copy only the `Guns/Bluntforce_Auto32` and `Magazines/Bluntforce_32_Drum` folders into its `Bundles/Items` directory. Verify load order and unique item IDs.

- Gun: `59010` Bluntforce Auto-32; `Action Trigger`, `Auto`, `Semi`; magazine `59011`.
- Drum: `59011`; `Amount 32`, 12-gauge magazine caliber 8, `Pellets 6`.
- Source ammo: `113` (8 shells). The crafting recipe trades **four separate, full 8-shell packs** for one 32-shell drum. A server-side `MobileNPCAuto32CraftingGuard` blocks crafting when fewer than four complete packs are found or when any partial 113 pack is present in the inventory (the native crafting sorter might select a partial pack first). Keep incomplete packs in external storage while assembling. The actual debit and output remain native PlayerCrafting operations.
- Both item dats use `Master_Bundle_Override core.masterbundle` and `Bundle_Override_Path` to reuse existing vanilla visuals. **The paths and native animations must be checked in a game build.** Some versions may require adjustment of the magazine's visual bundle path.
- The resulting firearm is visible/usable through Unturned's regular asset registry. Player shooting is handled by **native `UseableGun`**, not NPC-only code. Custom item states require the assets loaded at startup and are not a drop-in runtime prefab.
- NPC pellets use magazine `ItemMagazineAsset.pellets`; the 32-round drum fires six independent pellet trajectories if available.
- Test manually: give 59010/59011, verify asset model and ammo count, fire and reload, assemble from four full 113 magazines, spend a half-full drum, reload, save and restore. Avoid colliding with other mods' IDs.

The same `UseableGun` gameplay executor handles player shooting; the native `UseableThrowable` controls *player* grenade throws, whereas NPC throws are a separate AI trajectory calculation around the vanilla `Grenade` explosion component.
