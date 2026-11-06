# Unturned 2 Modpack Manager — Preview 0.1

A Vortex-inspired **prototype**, not a full mod loader. No Steam credentials, no Steam API or external download service required. Runs with Python 3.10+ on Windows (Tkinter included in typical installer).

## Features available

- Finds already-downloaded Unturned Workshop items by numeric ID from Steam library `steamapps/workshop/content/304930`.
- Imports local mod folders into isolated staging.
- Dry-run install into `Unturned/Bundles/Workshop/Contents/U2M_<name>` or `.../Maps/U2M_<name>`; explicit confirmation is required to copy.
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
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" deploy MyMod --category Contents
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" deploy MyMod --category Contents --apply
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" profile friends --local MyMod --workshop-ids 1753134636 --activate
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" export-server friends "D:\\WorkshopDownloadConfig.json"
```

## Important limitations

- **Steam handles downloading/subscriptions**. The manager only discovers items already downloaded; selected profiles do not stop Steam from loading other subscribed Workshop content.
- No dependency resolution, version compatibility, script execution, automatic downloads or updates yet.
- No module DLL deployment yet. C# module installer needs a separate manifest, permission review and load validation.
- Local mods must be provided as uncompressed folders and categorized manually as `Contents` or `Maps`.
- Avoid importing executable content from unknown creators. Backup game saves before experimenting.
- **Do not run mod deployment while Unturned is open**. Program does not yet detect running game.
- Profile switching is metadata only, not live activation/deactivation.
- Actual loading and duplicate GUID/ID behavior requires game testing.
- The GUI has not been executed on a Windows host in this session.

## Development roadmap

1. Import formats and mod-type validation: item/vehicle/map/module.
2. Dependency and GUID/ID conflict analyzer.
3. Profiles with real enable/disable for supported *local* items.
4. Detect Steam library folders and subscriptions robustly.
5. Safe installer for C# modules and multiplayer/server support.
6. Better Vortex-like UI: dependency graph, conflicts, deployment plan.
