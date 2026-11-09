"""Generate a rare acid grenade from installed Unturned assets, without shipping vanilla bundles.

Reuses vanilla grenade prefab/physics, real Thrower acid EffectAsset and an
explicitly verified existing spawn table. No guessed effect or loot root.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import uuid
from asset_diagnostics import enumerate_dat, parse_dat
from build_auto32 import find_asset, other_item_ids, replace_metadata, NAMESPACE

DEFAULT_GRENADE_ID = 32615
DEFAULT_SPAWN_ID = 32616
VANILLA_GRENADE_ID = "254"
RESERVED = {32610, 32611, 32612, 32613, 32614}
# Deliberately conservative: only the previously verified Washington loot
# table. Other maps require an explicitly verified loot root.
WASHINGTON_SPECIAL_ID = "346"


def find_thrower_acid_effect(bundle_root: Path):
    candidates = []
    effects = bundle_root / "Effects"
    for p in enumerate_dat(effects):
        dat = parse_dat(p)
        if dat.get("type", "").lower() != "effect":
            continue
        path = p.parent.name.lower().replace("-", "_").replace(" ", "_")
        # Zombie_3 is the effect ref used by Zombie.askAcid().
        # Also accept unmistakable Thrower acid effect filenames.
        exact = path in ("zombie_3", "zombie_acid", "thrower_acid",
                         "acid_0", "acid_spit", "acid_explosion")
        specific = ("acid" in path and any(word in path
                    for word in ("thrower", "zombie", "spit", "explosion")))
        if not (exact or specific):
            continue
        guid = dat.get("guid", "")
        effect_id = dat.get("id", "")
        if (len(guid) == 32 and all(c in "0123456789abcdefABCDEF" for c in guid)):
            value = guid
        elif effect_id.isdigit() and 1 <= int(effect_id) <= 65535:
            value = effect_id
        else:
            continue
        candidates.append((0 if path == "zombie_3" else 1, str(p), value))
    if not candidates:
        raise ValueError("Thrower acid EffectAsset was not found in installed Bundles/Effects. "
                         "No fake visual will be generated. Verify Zombie_3 or acid effect files.")
    candidates.sort()
    if len(candidates) > 1 and candidates[0][0] == candidates[1][0]:
        raise ValueError("Ambiguous Thrower acid effects: select a unique installed asset.")
    return candidates[0][2]


def find_verified_spawn_root(bundle_root: Path):
    matches = []
    for p in enumerate_dat(bundle_root / "Spawns"):
        dat = parse_dat(p)
        if dat.get("id") != WASHINGTON_SPECIAL_ID or dat.get("type", "").lower() != "spawn":
            continue
        parts = p.parent.as_posix().lower().replace("-", "_").replace(" ", "_")
        if "washington_special_america" in parts:
            matches.append(p)
    if len(matches) != 1:
        raise ValueError("Washington Special America spawn root 346 is not uniquely verified "
                         "in installed Bundles/Spawns. Refusing a disconnected loot table.")
    return int(WASHINGTON_SPECIAL_ID)


def build_acid_grenade(game: Path, staging: Path, *, grenade_id=DEFAULT_GRENADE_ID,
                       spawn_id=DEFAULT_SPAWN_ID, extra_roots=()):
    game = Path(game).resolve()
    bundle = game / "Bundles"
    if not (game / "Unturned.exe").is_file() or not (bundle / "Items").is_dir():
        raise ValueError("Select the Unturned Steam directory with Bundles/Items.")
    if (type(grenade_id) is not int or type(spawn_id) is not int or
        not (1 <= grenade_id <= 65535 and 1 <= spawn_id <= 65535) or
        grenade_id == spawn_id or grenade_id in RESERVED or spawn_id in RESERVED):
        raise ValueError("Acid grenade IDs must be unique unused values 1..65535.")
    # Resolve everything before creating staging files.
    source = find_asset(bundle / "Items", VANILLA_GRENADE_ID, "Throwable")
    effect = find_thrower_acid_effect(bundle)
    root_id = find_verified_spawn_root(bundle)
    taken = other_item_ids((bundle, *map(Path, extra_roots)))
    for ident in (grenade_id, spawn_id):
        if str(ident) in taken:
            raise ValueError("ID collision " + str(ident) + ": " +
                             "; ".join(taken[str(ident)][:3]))
    staging = Path(staging)
    if staging.exists() or staging.is_symlink():
        raise FileExistsError("Staging exists already: " + str(staging))

    native_path = "/" + source.parent.relative_to(bundle).as_posix()
    grenade_guid = uuid.uuid5(NAMESPACE, "thrower-acid-grenade-" + str(grenade_id)).hex
    spawn_guid = uuid.uuid5(NAMESPACE, "thrower-acid-loot-" + str(spawn_id)).hex
    base_text = source.read_text(encoding="utf-8-sig")
    # The original projectile prefab and arming behaviour remain intact.
    # Pure acid-themed weaker impulse blast: no damage to buildings/vehicles.
    data = replace_metadata(base_text, {
        "Type": "Throwable", "ID": grenade_id, "GUID": grenade_guid,
        "Rarity": "Epic", "Explosive": None,
        "Range": 3.0, "Fuse_Length": 2.5,
        "Player_Damage": 18, "Zombie_Damage": 24, "Animal_Damage": 18,
        "Barricade_Damage": 0, "Structure_Damage": 0,
        "Vehicle_Damage": 0, "Resource_Damage": 0, "Object_Damage": 0,
        "Explosion_Launch_Speed": 0.3,
        "Explosion": effect,
        "Master_Bundle_Override": "core.masterbundle",
        "Bundle_Override_Path": native_path,
    }, {"Exclude_From_Master_Bundle", "Bundle_Path_Include_Filename"})
    # Prevent the source grenade's original high-damage multipliers from
    # overriding our reduced values if base uses child multiplier keys.
    grenade_folder = staging / "Items" / "Throwables" / "Thrower_Acid_Grenade"
    loot_folder = staging / "Spawns" / "ThrowerAcidRareLoot"
    grenade_folder.mkdir(parents=True)
    loot_folder.mkdir(parents=True)
    (grenade_folder / "Asset.dat").write_text(data, encoding="utf-8")
    (grenade_folder / "English.dat").write_text(
        "Name Granada de Acido Thrower\n"
        "Description Granada experimental com explosao leve e efeito de acido do Thrower.\n",
        encoding="utf-8")
    # Low insertion weight into a known parent. Not a probability claim:
    # the final chance depends on the parent loot table weights and map.
    table = (f"Type Spawn\nID {spawn_id}\nGUID {spawn_guid}\n"
             f"Tables 1\nTable_0_Asset_ID {grenade_id}\nTable_0_Weight 1\n"
             f"Roots 1\nRoot_0_Spawn_ID {root_id}\nRoot_0_Weight 1\n")
    (loot_folder / "Asset.dat").write_text(table, encoding="utf-8")
    (staging / "README_TESTING.txt").write_text(
        f"Thrower acid grenade ID: {grenade_id}\n"
        f"Spawn table ID: {spawn_id}; verified Washington root: {root_id}\n"
        f"Native Thrower acid effect: {effect}\n"
        "Explosion: 3m, player 18, zombie 24, animal 18, other 0.\n"
        "The effect is a native visual, NOT a persistent corrosive zone.\n"
        "Low relative loot weight 1. Actual spawn rate depends on installed map table.\n"
        "Requires Unturned Steam singleplayer test and regenerated installation.\n",
        encoding="utf-8")
    return {"grenade_id":grenade_id, "spawn_id":spawn_id,
            "effect":effect, "root_id":root_id, "path":str(staging)}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--game", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--grenade-id", type=int, default=DEFAULT_GRENADE_ID)
    p.add_argument("--spawn-id", type=int, default=DEFAULT_SPAWN_ID)
    p.add_argument("--scan-root", type=Path, action="append", default=[])
    a = p.parse_args()
    import json
    print(json.dumps(build_acid_grenade(
        a.game, a.output, grenade_id=a.grenade_id, spawn_id=a.spawn_id,
        extra_roots=a.scan_root), indent=2))


if __name__ == "__main__":
    main()
