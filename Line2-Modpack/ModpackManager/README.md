# Unturned 2 Modpack Manager — Preview 0.4

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

## Preview 0.4: dependency graph and AI bridge preparation

- `dependency_planner.py` builds a directed graph from module `Dependencies`, checks versions in selected and already installed modules, detects cycles and calculates install order.
- Profile plans expose `dependency_issues`; application is blocked on missing dependency, duplicate module name or cycle. The planner does not download libraries or modify Steam.
- `test_dependencies.py` covers ordering, missing dependencies, cycles and preflight.
- `../AIService/server.py` is a local-only HTTP protocol prototype: `GET /health`, `POST /v1/decide`. It responds `idle` and **does not control NPCs**.
- No C# communication bridge or service lifecycle controls have been added yet.
- Important: profile application is not fully transactional across multiple removals; use a backup and close the game before applying.
- Test commands: `python -m unittest discover -s . -p "test_*.py"` from this folder. These tests **have not been run against an actual Windows/Unturned installation** in this development session.

## Preview 0.3: C# modules, maps and safeguards

- Import a **trusted, already compiled** C# module package whose root contains exactly one `.module` file and all referenced DLLs.
- Validate `Name`, four-component `Version`, `Assemblies` paths, `Role`, and `Dependencies`. Reject traversal and symlinks.
- Install only into `Unturned/Modules/U2M_<name>`, never overwrite existing modules or DLLs.
- Check dependency versions against enabled modules already installed in the game. Does not automatically download/resolve dependencies, and does not yet topologically order multiple new modules.
- Remove managed modules into recovery, refusing removal of modules needed by other enabled modules.
- Apply profiles comprising **local Content, Maps, Modules**; Workshop IDs remain informational/exportable and Steam-controlled.
- Content fingerprints now include SHA-256 to detect changes, although pre-v0.3 deployments may need manual migration (old registry snapshots have a different format).
- Mod installations are **not** verified to be loadable by a specific Unturned build.
- Keep Unturned **closed** during any install/remove/apply; the app does not detect running processes.
- Avoid executing untrusted DLLs; code modules can access the game process and local user files.

```powershell
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" import "D:\\Mods\\MyCompiledModule" MyCompiledModule
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" module inspect MyCompiledModule
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" module install MyCompiledModule
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" module install MyCompiledModule --apply
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" module remove MyCompiledModule --apply
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" profile survival --local MyAssetPack --maps MyMap --modules MyCompiledModule --workshop-ids 1234567890
python manager.py --game "D:\\SteamLibrary\\steamapps\\common\\Unturned" apply-profile survival --apply
```

The CLI and GUI are still **prototype code**. Run `python -m unittest discover -s . -p "test_*.py"` from the ModpackManager folder. The tests use fake temporary game directories and dummy DLL files; this checks filesystem behavior **not** module execution.

## Important limitations

- **Steam handles downloading/subscriptions**. The manager only discovers items already downloaded; selected profiles do not stop Steam from loading other subscribed Workshop content.
- No dependency resolution, version compatibility, script execution, automatic downloads or updates yet.
- C# module DLL deployment exists with .module validation, but has no permission sandbox, actual code execution validation or trust verification.
- Local mods must be uncompressed folders; manual categorization as `Content`, `Maps` or `Modules` is required.
- Avoid importing executable content from unknown creators. Backup game saves before experimenting.
- **Do not run mod deployment while Unturned is open**. Program does not yet detect running game.
- Profile activation covers managed local Content, Maps and Modules, but not live changes or per-profile Steam subscription control.
- Actual loading and duplicate GUID/ID behavior requires game testing.
- The GUI has not been executed on a Windows host in this session.

## Development roadmap

1. Import formats and mod-type validation: item/vehicle/map/module.
2. Dependency and GUID/ID conflict analyzer.
3. Profiles with real enable/disable for supported *local* items.
4. Detect Steam library folders and subscriptions robustly.
5. Improve C# module dependency resolution and multiplayer/server support.
6. Better Vortex-like UI: dependency graph, conflicts, deployment plan.

## Auto 32 — asset not found in game

`asset_diagnostics.py` searches `.dat` metadata by folder/name and optional legacy ID, flags missing/invalid GUID, ID duplicates in searched roots, unusual .dat filename, and a default magazine ID not found in those roots. It is **read-only** and cannot verify whether an asset bundle actually contains a valid gun prefab.

Example (adjust Windows paths; repeat --root for other locations):

```powershell
python asset_diagnostics.py --root "C:\\SteamLibrary\\steamapps\\common\\Unturned\\Bundles" --root "C:\\SteamLibrary\\steamapps\\workshop\\content\\304930" --name "Auto 32"
python -m unittest discover -s . -p "test_*.py" -v
```

A zero match is not proof of global absence; this scan does not check arbitrary Steam libraries or packaged Unity bundle internals. The Line 2 branch currently has no custom Auto 32 asset package. Build the gun and its magazine using validated `.dat`, localization, prefab/bundle and registered IDs/GUIDs, then install and test in Unturned Steam **without** BattlEye. Do not assign a new item ID based on guesswork; reserve IDs after inventory of installed mods.
