# Auto 32 — Experimental local asset package

**Status: generated metadata is not yet in-game validated.**

This is the migration path for the former Line 1 custom automatic Bluntforce. It deliberately reuses vanilla **Bluntforce (item ID 112)** and **12 Gauge Shells (magazine ID 113)** visual resources from the player's OWN Steam installation. No asset bundles or copyrighted game models are redistributed here.

The generator creates new `.dat`/localization definitions, with separate GUIDs and candidate IDs.

## Intended mechanics

- Automatic Bluntforce-style gun (Trigger action + Auto mode).
- 32-round magazine with unique item ID.
- Four rounds at a time **incremental reload is NOT implemented**. Requires game-authoritative C# code after Unturned module integration is validated.
- The original Bluntforce pump animations may not match automatic action; animations/audio need in-game checking.

## Generate on Windows

From `Line2-Modpack/ModpackManager`:

```powershell
python build_auto32.py --game "D:\SteamLibrary\steamapps\common\Unturned" --output "$env:USERPROFILE\Unturned2-ModpackManager\staging\Auto32" --scan-root "D:\SteamLibrary\steamapps\workshop\content\304930"
```

The command **does not install** assets into Unturned; it only writes new files to an empty folder. Default candidate IDs are gun `32610`, magazine `32611`. These are NOT reserved or guaranteed available; collision detection checks the specified folders. If they are taken, provide `--gun-id` and `--magazine-id`.

The GUI now offers **Generate Auto 32**, which performs the same staging-only operation and scans the workshop directory when configured.

Generated structure:

```text
Auto32/
  BUILD_NOTES.txt
  Items/
    Guns/
      Auto_32/
        Asset.dat
        English.dat
      Auto_32_Magazine/
        Asset.dat
        English.dat
```

The generated files point to `core.masterbundle` and the original directory paths discovered from your installed vanilla definitions. This is supported conceptually by Unturned's `Master_Bundle_Override` and `Bundle_Override_Path` asset keys, but the actual prefab reuse needs testing with your Steam build.

## Test sequence

1. Confirm `python -m unittest discover -s . -p "test_*.py" -v` passes on Windows.
2. Confirm the new IDs are absent from your active map, vanilla, curated packs, and Workshop mods.
3. Use the manager's profile to deploy the staged mod to the configured Content location. **Close Unturned before installation.**
4. Launch Unturned without BattlEye, choose Washington, test the numeric gun ID in chat with the game's give command.
5. If it reports unknown item, inspect `Unturned/Logs/Client` asset loading errors and validate that the game scans the configured local Workshop Content folder.
6. If it registers but appears invisible, check `Bundle_Override_Path` against the installed Bluntforce/12 Gauge definitions. Do not assume an asset load means the prefab worked.
7. If it registers and shoots, verify 32 rounds, mode, animations, magazine compatibility, reload and multiplayer replication. The four-round increment is intentionally an outstanding feature.

**No item visibility or gameplay success can be claimed until step 4–7 passes.**

Documentation:
- [Unturned Gun Asset](https://docs.smartlydressedgames.com/en/latest/items/gun-asset.html)
- [Asset bundle reuse](https://docs.smartlydressedgames.com/en/stable/assets/asset-bundles.html)
- [Assets v1 format](https://unturned.info/Modding/AssetTypes/AssetsV1/)

## Recoil presets (new)

The user's Steam test confirms Auto 32 **spawns and shoots**, but the recoil was excessive. The default generator now writes the `balanced` recoil preset explicitly, replacing inherited Bluntforce `Recoil_Min/Max_X/Y` rather than carrying over pump-shotgun recoil.

Presets in `auto32_recoil.py`:

| Preset | Horizontal X min/max | Vertical Y min/max | Recover X/Y | ADS multiplier |
|---|---|---|---|---|
| soft | -0.35 / 0.35 | 0.7 / 1.1 | 0.65 / 0.65 | 0.75 |
| balanced | -0.7 / 0.7 | 1.25 / 1.8 | 0.5 / 0.55 | 0.8 |
| strong | -1.2 / 1.2 | 2.2 / 3.1 | 0.35 / 0.4 | 0.9 |

**Updating an already installed Auto 32 safely:**
1. Close Unturned.
2. In manager > **Managed Installs**, select `Content/Auto32` and press **Remove managed mod**. This copies it to recovery and preserves registry integrity.
3. Update/re-download Line 2 branch, open manager, click **Tune Auto 32 recoil**, select `balanced` (or `soft`).
4. Select `Auto32` in Local Staging > **Install local mod** > `Content`.
5. Restart Unturned, use `/give 32610`, test hip fire and ADS in a safe singleplayer area.

CLI equivalent from `Line2-Modpack/ModpackManager` after removal:

```powershell
python auto32_recoil.py --staging "$env:USERPROFILE\Unturned2-ModpackManager\staging\Auto32" --preset balanced
```

**Do not edit `U2M_Auto32` in game folders before manager removal:** deployed file hashes would differ from the registry, intentionally preventing automatic removal. If you have already edited the deployed files, restore original bytes from a backup first or retain them for manual recovery. The preset tool edits *staging only*. New preset values still require gameplay validation.

Docs: https://docs.smartlydressedgames.com/en/latest/items/gun-asset.html#recoil
