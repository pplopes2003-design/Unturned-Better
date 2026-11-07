"""Create two experimental Legendary items from user's installed vanilla files.

No vanilla assets are redistributed. Generation is staging-only and refuses ID
collisions in the game and any additional scanning roots. Spawn-table INTEGRATION
and true Burner damage-over-time remain separate Steam-side work.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import uuid
from asset_diagnostics import enumerate_dat, parse_dat
from build_auto32 import find_asset, other_item_ids, replace_metadata, NAMESPACE

EAGLEFIRE_ID = 4
SHOTGUN_MAG_ID = 113
DEFAULT_EAGLE_ID = 32612
DEFAULT_INCENDIARY_ID = 32613
DEFAULT_LOOT_TABLE_ID = 32614
WASHINGTON_SPECIAL_SPAWN_ID = "346"  # Verify in installed game; do not blindly attach

def discover_burner_effect(bundle_root: Path):
    """Only use an unmistakable locally installed Burner explosion effect."""
    effects = bundle_root / "Effects"
    if not effects.is_dir():
        return None
    matches = []
    for p in enumerate_dat(effects):
        parts = "/".join(x.lower() for x in p.parts[-4:])
        if "burner" not in parts or not any(k in parts for k in ("explosion", "explode")):
            continue
        meta = parse_dat(p)
        value = meta.get("id", "")
        if value.isdigit() and 1 <= int(value) <= 65535:
            matches.append(int(value))
    return matches[0] if len(matches) == 1 else None


def build_legendaries(game: Path, staging: Path, *, eagle_id=DEFAULT_EAGLE_ID,
                      fire_id=DEFAULT_INCENDIARY_ID, extra_roots=(),
                      impact_effect_id=None, loot_table_id=DEFAULT_LOOT_TABLE_ID,
                      auto24_id=32610):
    game = Path(game).resolve()
    root = game / "Bundles"
    items = root / "Items"
    if not (game / "Unturned.exe").is_file() or not items.is_dir():
        raise ValueError("Invalid Unturned Steam directory with Bundles/Items")
    if (any(type(x) is not int or x < 1 or x > 65535
            for x in (eagle_id, fire_id, loot_table_id, auto24_id)) or
            len({eagle_id, fire_id, loot_table_id, auto24_id}) != 4):
        raise ValueError("Unique numeric IDs 1..65535 required")
    original_eagle = find_asset(items, str(EAGLEFIRE_ID), "Gun")
    original_shells = find_asset(items, str(SHOTGUN_MAG_ID), "Magazine")
    # Reserved sibling IDs are checked even if the Auto24 has not yet been installed.
    ids = other_item_ids((root, *map(Path,extra_roots)))
    for number in (eagle_id, fire_id, loot_table_id):
        if number in (32610,32611) and number != auto24_id:
            raise ValueError("ID is reserved by Auto24: " + str(number))
        if str(number) in ids:
            raise ValueError("ID collision " + str(number) + ": " + "; ".join(ids[str(number)][:3]))
    if impact_effect_id is None:
        impact_effect_id = discover_burner_effect(root)
    if impact_effect_id is not None and (
        type(impact_effect_id) is not int or impact_effect_id < 0 or impact_effect_id > 65535
    ):
        raise ValueError("Effect ID must be 0..65535")
    staging = Path(staging)
    if staging.exists() or staging.is_symlink():
        raise FileExistsError("Staging directory already exists: " + str(staging))

    eagle_path = "/" + original_eagle.parent.relative_to(root).as_posix()
    shell_path = "/" + original_shells.parent.relative_to(root).as_posix()
    eagle_guid = uuid.uuid5(NAMESPACE,"legendary-eaglefire-"+str(eagle_id)).hex
    shell_guid = uuid.uuid5(NAMESPACE,"legendary-12g-incendiary-"+str(fire_id)).hex
    eagle = replace_metadata(original_eagle.read_text(encoding="utf-8-sig"),{
        "Type":"Gun", "ID":eagle_id, "GUID":eagle_guid,"Rarity":"Legendary",
        "Action":"Trigger", "Auto":None,"Bursts":3,"Semi":None,"Safety":None,
        "Master_Bundle_Override":"core.masterbundle",
        "Bundle_Override_Path":eagle_path,
    },{"Exclude_From_Master_Bundle", "Bundle_Path_Include_Filename"})

    # Use exactly the vanilla magazine caliber(s) for broad compatibility
    # with other 12-gauge weapons without editing each gun.
    shell_updates = {
        "Type":"Magazine","ID":fire_id,"GUID":shell_guid,"Rarity":"Legendary",
        "Master_Bundle_Override":"core.masterbundle",
        "Bundle_Override_Path":shell_path,
        # Small area hit effect is NOT a literal Burner infection/DoT.
        "Explosive":None,
        "Pellets":1,  # One small effect per shot; avoids shotgun pellet explosion spam.
        "Range":1.25,
        "Player_Damage":3,
        "Zombie_Damage":9,
        "Animal_Damage":3,
        "Barricade_Damage":0,
        "Structure_Damage":0,
        "Vehicle_Damage":0,
        "Resource_Damage":0,
        "Explosion_Plays_Impact_Effects":"false",
    }
    if impact_effect_id is not None:
        shell_updates["Explosion"] = impact_effect_id
    shells = replace_metadata(original_shells.read_text(encoding="utf-8-sig"),shell_updates,
        {"Exclude_From_Master_Bundle", "Bundle_Path_Include_Filename"})

    e = staging / "Items" / "Guns" / "Legendary_Eaglefire"
    m = staging / "Items" / "Ammo" / "Legendary_Incendiary_12Gauge"
    e.mkdir(parents=True)
    m.mkdir(parents=True)
    (e / "Asset.dat").write_text(eagle,encoding="utf-8")
    (e / "English.dat").write_text(
        "Name Eaglefire Automatica (Legendary)\nDescription Semi, 3-shot burst and full-auto modes.\n",
        encoding="utf-8")
    (m / "Asset.dat").write_text(shells,encoding="utf-8")
    (m / "English.dat").write_text(
        "Name 12 Gauge Incendiaria (Legendary)\nDescription Experimental small-area impact ammunition.\n",
        encoding="utf-8")
    # Washington root is attached only when official Spawn ID 346 is present
    # and identifies the Washington Special America loot table. This is not
    # an override and does not modify original files.
    washington_root = None
    spawn_dir = root / "Spawns"
    if spawn_dir.is_dir():
        matches = [(p, parse_dat(p)) for p in enumerate_dat(spawn_dir)]
        matches = [(p, data) for p, data in matches
                   if data.get("id") == WASHINGTON_SPECIAL_SPAWN_ID
                   and data.get("type", "").lower() == "spawn"]
        if len(matches) == 1 and "washington_special_america" in str(matches[0][0]).lower().replace("-", "_").replace(" ", "_"):
            washington_root = WASHINGTON_SPECIAL_SPAWN_ID

    spawn_folder = staging / "Spawns" / "LegendaryLootPool"
    spawn_folder.mkdir(parents=True)
    spawn_guid = uuid.uuid5(NAMESPACE,"legendary-loot-pool-"+str(loot_table_id)).hex
    loot = ("Type Spawn\nID " + str(loot_table_id) + "\nGUID " +
            spawn_guid + "\nTables 3\n"
            "Table_0_Asset_ID " + str(auto24_id) + "\nTable_0_Weight 1\n"
            "Table_1_Asset_ID " + str(eagle_id) + "\nTable_1_Weight 1\n"
            "Table_2_Asset_ID " + str(fire_id) + "\nTable_2_Weight 1\n")
    if washington_root is not None:
        loot += "Roots 1\nRoot_0_Spawn_ID " + washington_root + "\nRoot_0_Weight 1\n"
    (spawn_folder / "Asset.dat").write_text(loot,encoding="utf-8")
    (staging / "README_TESTING.txt").write_text(
        "EXPERIMENTAL Legendary pack, requires Unturned Steam validation.\n"
        "Eaglefire ID: "+str(eagle_id)+"\nIncendiary 12 Gauge ID: "+str(fire_id)+"\n"
        "Munitions copy original 12G caliber fields for compatibility.\n"
        "Explosive/Range is a small damage area, NOT the actual Burner zombie ignite effect.\n"
        "Burner visual effect ID: "+str(impact_effect_id or "NONE FOUND")+"\n"\n        "The effect is visual only; NO persistent burning or status effects.\n"
        "Washington loot root: " + (washington_root or "NOT FOUND/NOT ATTACHED") + "\n"
        "Natural loot requires valid map use of this root. Relative spawn weight=1; actual odds vary.\n"
        "Do not deploy until conflicts and C# runtime tests pass.\n",
        encoding="utf-8")
    return {"eaglefire":eagle_id,"incendiary":fire_id,"loot_table":loot_table_id,
            "washington_root_attached": washington_root is not None, "path":str(staging),
            "effect_configured":impact_effect_id is not None,\n            "effect_id":impact_effect_id}

def main():
    p=argparse.ArgumentParser(description="Generate standalone Legendary Eaglefire and 12G ammo")
    p.add_argument("--game",type=Path,required=True)
    p.add_argument("--output",type=Path,required=True)
    p.add_argument("--eagle-id",type=int,default=DEFAULT_EAGLE_ID)
    p.add_argument("--incendiary-id",type=int,default=DEFAULT_INCENDIARY_ID)
    p.add_argument("--scan-root",type=Path,action="append",default=[])
    p.add_argument("--impact-effect-id",type=int)
    p.add_argument("--loot-table-id",type=int,default=DEFAULT_LOOT_TABLE_ID)
    p.add_argument("--auto24-id",type=int,default=32610)
    args=p.parse_args()
    import json
    print(json.dumps(build_legendaries(args.game,args.output,eagle_id=args.eagle_id,
          fire_id=args.incendiary_id,extra_roots=args.scan_root,
          impact_effect_id=args.impact_effect_id,loot_table_id=args.loot_table_id,
          auto24_id=args.auto24_id),indent=2))

if __name__=="__main__":
    main()
