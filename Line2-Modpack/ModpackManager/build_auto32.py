"""Generate an experimental Auto 32 mod from locally installed *vanilla* assets.

Does not redistribute vanilla models or modify base-game asset files.
Copies metadata and references the existing vanilla core masterbundle.
Requires in-game testing; four-round-at-a-time reloading is NOT implemented.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import re
import uuid
from asset_diagnostics import parse_dat, enumerate_dat

FIELD = re.compile(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*(.*?)\s*$")
GUN_VANILLA_ID = "112"       # Bluntforce
MAG_VANILLA_ID = "113"       # 12 Gauge Shells
DEFAULT_GUN_ID = 32610       # Candidate IDs, not globally reserved
DEFAULT_MAG_ID = 32611
NAMESPACE = uuid.UUID("93165788-9018-4088-9125-786fbcfa920c")


def find_asset(items: Path, item_id: str, type_name: str):
    candidates = []
    for path in enumerate_dat(items):
        fields = parse_dat(path)
        if fields.get("id") == item_id and fields.get("type", "").lower() == type_name.lower():
            candidates.append(path)
    if len(candidates) != 1:
        raise ValueError("Expected one vanilla " + type_name + " ID " + item_id +
                         "; found " + str(len(candidates)) +
                         ". Check your Unturned Bundles/Items folder.")
    return candidates[0]


def other_item_ids(roots):
    taken = {}
    for root in roots:
        for path in enumerate_dat(root):
            data = parse_dat(path)
            value = data.get("id")
            if value is None or "type" not in data:
                continue
            # Unturned item IDs are unique across all Item subclasses.
            taken.setdefault(value, []).append(str(path))
    return taken


def replace_metadata(text, updates, remove):
    """Preserve valid vanilla keys, rewrite only the necessary ones."""
    kept = []
    remove = {v.lower() for v in remove}
    updates = {k.lower(): (k, v) for k, v in updates.items()}
    for line in text.splitlines():
        m = FIELD.match(line)
        key = m.group(1).lower() if m else None
        if key in remove or key in updates:
            continue
        kept.append(line)
    for name, value in updates.values():
        kept.append(name if value is None else name + " " + str(value))
    return "\n".join(kept).strip() + "\n"


def create_auto32(game: Path, target: Path, *, gun_id=DEFAULT_GUN_ID,
                  magazine_id=DEFAULT_MAG_ID, other_roots=()):
    game = game.resolve()
    bundle_dir = game / "Bundles"
    items = bundle_dir / "Items"
    if not (game / "Unturned.exe").is_file() or not items.is_dir():
        raise ValueError("Provide a Windows Unturned directory with Bundles/Items.")
    if any(type(x) is not int or x < 1 or x > 65535 for x in (gun_id, magazine_id)):
        raise ValueError("Both IDs must be integers 1..65535.")
    if gun_id == magazine_id:
        raise ValueError("Gun and magazine IDs must be different.")
    original_gun = find_asset(items, GUN_VANILLA_ID, "Gun")
    original_mag = find_asset(items, MAG_VANILLA_ID, "Magazine")
    taken = other_item_ids((items, *map(Path, other_roots)))
    for number in (gun_id, magazine_id):
        if str(number) in taken:
            raise ValueError("ID " + str(number) + " is already used: " +
                             "; ".join(taken[str(number)][:4]))
    target = Path(target)
    if target.exists() or target.is_symlink():
        raise FileExistsError("Output already exists: " + str(target))
    # Derive original prefab paths from vanilla rather than assuming names.
    # The local game must confirm which prefabs its core bundle contains.
    gun_bundle_path = "/" + str(original_gun.parent.relative_to(bundle_dir)).replace("\\", "/")
    mag_bundle_path = "/" + str(original_mag.parent.relative_to(bundle_dir)).replace("\\", "/")
    gun_guid = uuid.uuid5(NAMESPACE, "auto32-gun-" + str(gun_id)).hex
    mag_guid = uuid.uuid5(NAMESPACE, "auto32-mag-" + str(magazine_id)).hex

    gun_source = original_gun.read_text(encoding="utf-8-sig")
    mag_source = original_mag.read_text(encoding="utf-8-sig")
    gun = replace_metadata(gun_source, {
        "Type": "Gun", "ID": gun_id, "GUID": gun_guid,
        "Action": "Trigger", "Auto": None, "Semi": None, "Safety": None,
        "Firerate": 8, "Magazine": magazine_id,
        "Ammo_Min": 32, "Ammo_Max": 32,
        "Master_Bundle_Override": "core.masterbundle",
        "Bundle_Override_Path": gun_bundle_path,
    }, {"Exclude_From_Master_Bundle", "Bundle_Path_Include_Filename",
        "Blueprints", "Blueprint_0_Type", "Blueprint_0_Supplies",
        "Blueprint_0_Outputs", "Blueprint_0_Output",
        "RechamberAfterShotCount"})
    mag = replace_metadata(mag_source, {
        "Type": "Magazine", "ID": magazine_id, "GUID": mag_guid,
        "Amount": 32,
        "Master_Bundle_Override": "core.masterbundle",
        "Bundle_Override_Path": mag_bundle_path,
    }, {"Exclude_From_Master_Bundle", "Bundle_Path_Include_Filename",
        "Blueprints", "Blueprint_0_Type", "Blueprint_0_Supplies",
        "Blueprint_0_Outputs", "Blueprint_0_Output"})

    gun_folder = target / "Items" / "Guns" / "Auto_32"
    mag_folder = target / "Items" / "Guns" / "Auto_32_Magazine"
    gun_folder.mkdir(parents=True)
    mag_folder.mkdir(parents=True)
    (gun_folder / "Asset.dat").write_text(gun, encoding="utf-8")
    (mag_folder / "Asset.dat").write_text(mag, encoding="utf-8")
    (gun_folder / "English.dat").write_text(
        "Name Auto 32 (Experimental)\nDescription Automatic Bluntforce conversion; 32-round test magazine.\n",
        encoding="utf-8")
    (mag_folder / "English.dat").write_text(
        "Name Auto 32 Magazine\nDescription Experimental 32-round 12 gauge magazine.\n",
        encoding="utf-8")
    (target / "BUILD_NOTES.txt").write_text(
        "EXPERIMENTAL - NOT TESTED IN UNTURNED.\n"
        "Gun ID: " + str(gun_id) + "\nMagazine ID: " + str(magazine_id) + "\n"
        "Uses existing game core.masterbundle prefabs, no game assets included.\n"
        "Neither bundle existence nor item spawning is yet verified.\n"
        "Reloading four rounds at a time requires a validated C# gameplay adapter.\n"
        "Do not distribute copied vanilla source files; this package reuses runtime resources.\n",
        encoding="utf-8")
    return {"gun_id": gun_id, "magazine_id": magazine_id, "gun_guid": gun_guid,
            "magazine_guid": mag_guid, "gun_bundle_path": gun_bundle_path,
            "magazine_bundle_path": mag_bundle_path, "output": str(target)}


def main():
    p = argparse.ArgumentParser(description="Generate experimental Auto 32 from local Unturned assets")
    p.add_argument("--game", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--gun-id", type=int, default=DEFAULT_GUN_ID)
    p.add_argument("--magazine-id", type=int, default=DEFAULT_MAG_ID)
    p.add_argument("--scan-root", type=Path, action="append", default=[])
    args = p.parse_args()
    result = create_auto32(args.game, args.output, gun_id=args.gun_id,
                           magazine_id=args.magazine_id, other_roots=args.scan_root)
    import json
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
