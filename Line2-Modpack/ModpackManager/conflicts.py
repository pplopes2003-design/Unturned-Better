"""Read-only Unturned asset metadata scanner and cross-mod conflict detector.

Supports common Type/ID/GUID entries in .dat files; does not claim to be a
complete Unturned parser or validate collisions against vanilla assets.
"""
from __future__ import annotations
from collections import defaultdict
from pathlib import Path
import re

GUID = re.compile(r"^[0-9a-fA-F]{32}$")
IDENTITY = re.compile(r"^([A-Za-z_][A-Za-z_0-9]*)\s+(.*?)\s*$")
CATEGORY = {
    "gun": "item", "melee": "item", "food": "item", "water": "item",
    "medical": "item", "supply": "item", "magazine": "item",
    "attachment": "item", "shirt": "item", "pants": "item",
    "backpack": "item", "vest": "item", "hat": "item",
    "item": "item", "vehicle": "vehicle", "object": "object",
    "animal": "animal", "spawn": "spawn", "effect": "effect",
    "skin": "skin", "resource": "resource", "npc": "npc",
}

def read_metadata(path: Path):
    try:
        # Some .dat files are localized strings or contain nested data. Ignore
        # lines that cannot be interpreted as simple top-level key-value pairs.
        lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    except OSError:
        return {}
    fields = {}
    for line in lines:
        s = line.strip()
        if not s or s.startswith("//") or s.startswith("#"):
            continue
        m = IDENTITY.match(s)
        if m and m.group(1).lower() in ("type", "id", "guid"):
            fields.setdefault(m.group(1).lower(), m.group(2).strip().strip('"').split("//", 1)[0].strip())
    if "type" not in fields:
        return {}
    fields["category"] = CATEGORY.get(fields["type"].lower(), fields["type"].lower())
    return fields

def enumerate_assets(folder: Path, owner: str, max_files=20000):
    folder = Path(folder)
    if not folder.is_dir() or folder.is_symlink():
        return []
    results = []
    count = 0
    for path in folder.rglob("*.dat"):
        if any(p.is_symlink() for p in [path, *list(path.parents)[:len(path.parents)]]):
            continue
        count += 1
        if count > max_files:
            break
        fields = read_metadata(path)
        if not fields:
            continue
        asset_id = fields.get("id")
        if asset_id is not None:
            try:
                n = int(asset_id)
                asset_id = str(n) if 0 <= n <= 65535 else None
            except ValueError:
                asset_id = None
        guid = fields.get("guid", "").lower()
        results.append({
            "owner": owner,
            "path": str(path),
            "type": fields["type"],
            "category": fields["category"],
            "id": asset_id,
            "guid": guid if GUID.fullmatch(guid) else None,
            "invalid_guid": bool(guid and not GUID.fullmatch(guid)),
        })
    return results

def analyze_sources(sources: dict[str, Path]):
    records = []
    for owner, folder in sources.items():
        records.extend(enumerate_assets(folder, owner))
    by_guid, by_id = defaultdict(list), defaultdict(list)
    invalid_guids = []
    for asset in records:
        if asset["guid"]:
            by_guid[asset["guid"]].append(asset)
        if asset["id"] is not None and asset["category"] != "object":
            by_id[(asset["category"], asset["id"])].append(asset)
        if asset["invalid_guid"]:
            invalid_guids.append(asset)
    def group_conflicts(grouped, kind):
        return [
            {"kind": kind, "value": (list(key) if isinstance(key, tuple) else key),
             "assets": assets}
            for key, assets in sorted(grouped.items(), key=lambda x: str(x[0]))
            if len({a["path"] for a in assets}) > 1
        ]
    conflicts = group_conflicts(by_guid, "guid") + group_conflicts(by_id, "legacy_id")
    return {"assets_scanned": len(records), "conflicts": conflicts,
            "invalid_guids": invalid_guids,
            "limitations": "Heuristic .dat scanner. Does not examine vanilla, .asset, bundles or runtime-generated GUIDs."}
