# Unturned 2 Modpack Manager — Preview 0.1

A Vortex-inspired **prototype**, not a full mod loader. No Steam credentials, no Steam API or external download service required. Runs with Python 3.10+ on Windows (Tkinter included in typical installer).

## Features available

- Finds already-downloaded Unturned Workshop items by numeric ID from Steam library `steamapps/workshop/content/304930`.
- Imports local mod folders into isolated staging.
- Dry-run install into `Unturned/Bundles/Workshop/Content/U2M_<name>` or `.../Maps/U2M_<name>`; explicit confirmation is required to copy.
- Safe managed removal: verifies installed snapshot, moves to a recovery directory and never deletes original Workshop files.
- Profile saving (local item names and selected Workshop IDs) for later use.
- Exports `WorkshopDownloadConfig.json` for dedicated servers. Export does **not** update a running server.

## Getting started

1. Download the `ModpackManager` folder from the branch.
2. Python 3.10+ installed on Windows.
3. Run `python gui.py`.
4. Choose `Unturned` installation folder and, if needed, the Workshop `304930` folder (Steam > Settings > Storage).
5. Press **Scan / Refresh**.
6. Select local staging and Workshop items, create a named profile.
7. Install selected **local** mod after reviewing the confirmation dialog.

CLI:
```powershell
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" scan
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" import "D:\\Mods\\MyMod" MyMod
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" deploy MyMod --category Content
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" deploy MyMod --category Content --apply
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" profile friends --local MyMod --workshop-ids 1753134636 --activate
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" export-server friends "D:\\WorkshopDownloadConfig.json"
```

## Preview 0.2: conflicts and profiles

The manager now scans common `.dat` assets in a **selected profile** for duplicate GUIDs and duplicate legacy IDs *within the same type family*. This is a heuristic check: missing/auto-generated GUIDs, `.asset` files, vanilla assets and runtime assets are NOT covered. A clean scan is not a guarantee of compatibility.

```powershell
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" conflicts --profile friends
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" apply-profile friends
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" apply-profile friends --apply
python -m unittest discover -s . -p "test_*.py"
```

`apply-profile` previews changes by default. `--apply` deploys selected **local Content** mods and moves previously managed nonselected mods into manager recovery. Modifications to existing installed assets block removal. Conflicts detected between selected profile sources block activation. Existing Steam Workshop subscriptions and contents remain untouched. Profile activation requires Unturned closed (not yet auto-detected). Local Maps profile activation, per-game Workshop filtering and client C# modules still require separate support.

## Important limitations

- **Steam handles downloading/subscriptions**. The manager only discovers items already downloaded; selected profiles do not stop Steam from loading other subscribed Workshop content.
- No dependency resolution, version compatibility, script execution, automatic downloads or updates yet.
- No module DLL deployment yet. C# module installer needs a separate manifest, permission review and load validation.
- Local mods must be provided as uncompressed folders and categorized manually as `Content` or `Maps`.
- Avoid importing executable content from unknown creators. Backup game saves before experimenting.
- **Do not run mod deployment while Unturned is open**. Program does not yet detect running game.
- Profile activation is implemented only for managed local Content mods; it is not live and does not control Steam subscriptions.
- Actual loading and duplicate GUID/ID behavior requires game testing.
- The GUI has not been executed on a Windows host in this session.

## Development roadmap

1. Import formats and mod-type validation: item/vehicle/map/module.
2. Dependency and GUID/ID conflict analyzer.
3. Profiles with real enable/disable for supported *local* items.
4. Detect Steam library folders and subscriptions robustly.
5. Safe installer for C# modules and multiplayer/server support.
6. Better Vortex-like UI: dependency graph, conflicts, deployment plan.
