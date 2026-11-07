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

def build_legendaries(game: Path, staging: Path, *, eagle_id=DEFAULT_EAGLE_ID,
                      fire_id=DEFAULT_INCENDIARY_ID, extra_roots=(),
                      impact_effect_id=None):
    game = Path(game).resolve()
    root = game / "Bundles"
    items = root / "Items"
    if not (game / "Unturned.exe").is_file() or not items.is_dir():
        raise ValueError("Invalid Unturned Steam directory with Bundles/Items")
    if any(type(x) is not int or x < 1 or x > 65535
           for x in (eagle_id, fire_id)) or eagle_id == fire_id:
        raise ValueError("Unique numeric IDs 1..65535 required")
    original_eagle = find_asset(items, str(EAGLEFIRE_ID), "Gun")
    original_shells = find_asset(items, str(SHOTGUN_MAG_ID), "Magazine")
    # Reserved sibling IDs are checked even if the Auto24 has not yet been installed.
    ids = other_item_ids((items, *map(Path,extra_roots)))
    for number in (eagle_id, fire_id):
        if number in (32610,32611):
            raise ValueError("ID is reserved by Auto24: " + str(number))
        if str(number) in ids:
            raise ValueError("ID collision " + str(number) + ": " + "; ".join(ids[str(number)][:3]))
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
        "Pellets":1,  # One small effect per shot; avoids shotgun pellet explosion spam.\n        "Range":1.25,
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
    (staging / "README_TESTING.txt").write_text(
        "EXPERIMENTAL Legendary pack, requires Unturned Steam validation.\n"
        "Eaglefire ID: "+str(eagle_id)+"\nIncendiary 12 Gauge ID: "+str(fire_id)+"\n"
        "Munitions copy original 12G caliber fields for compatibility.\n"
        "Explosive/Range is a small damage area, NOT the actual Burner zombie ignite effect.\n"
        "Explosion effect must be an existing, verified Unturned effect ID.\n"
        "Loot: Rarity Legendary changes item UI rarity; natural loot requires a map spawn-table link.\n"
        "Do not deploy until conflicts and C# runtime tests pass.\n",
        encoding="utf-8")
    return {"eaglefire":eagle_id,"incendiary":fire_id,"path":str(staging),
            "effect_configured":impact_effect_id is not None}

def main():
    p=argparse.ArgumentParser(description="Generate standalone Legendary Eaglefire and 12G ammo")
    p.add_argument("--game",type=Path,required=True)
    p.add_argument("--output",type=Path,required=True)
    p.add_argument("--eagle-id",type=int,default=DEFAULT_EAGLE_ID)
    p.add_argument("--incendiary-id",type=int,default=DEFAULT_INCENDIARY_ID)
    p.add_argument("--scan-root",type=Path,action="append",default=[])
    p.add_argument("--impact-effect-id",type=int)
    args=p.parse_args()
    import json
    print(json.dumps(build_legendaries(args.game,args.output,eagle_id=args.eagle_id,
          fire_id=args.incendiary_id,extra_roots=args.scan_root,
          impact_effect_id=args.impact_effect_id),indent=2))

if __name__=="__main__":
    main()
