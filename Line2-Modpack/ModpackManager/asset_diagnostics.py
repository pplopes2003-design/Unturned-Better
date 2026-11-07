"""Diagnostic for custom Unturned gun assets. Never edits the game or workshop.

Searches manually installed asset folders and staged mods; an absent result
means not found in the scanned locations, not proof the asset does not exist.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re
from collections import defaultdict

FIELDS = re.compile(r"^\s*([A-Za-z][A-Za-z0-9_]*)\s+(.*?)\s*$")
GUID = re.compile(r"^[0-9a-fA-F]{32}$")
MAX_FILES = 30000

def parse_dat(path):
    result = {}
    for line in path.read_text(encoding="utf-8-sig",errors="replace").splitlines():
        if not line.strip() or line.lstrip().startswith("//"):
            continue
        m=FIELDS.match(line)
        if m:
            result.setdefault(m.group(1).lower(), m.group(2).split("//",1)[0].strip().strip('"'))
    return result

def enumerate_dat(root):
    if not root.is_dir() or root.is_symlink():
        return []
    files=[]
    for p in root.rglob("*.dat"):
        if p.is_symlink() or any(x.is_symlink() for x in p.parents if x != root.parent):
            continue
        files.append(p)
        if len(files) >= MAX_FILES:
            break
    return files

def inspect(roots, name="Auto 32", expected_id=None):
    assets=[]
    scanned=0
    for root in roots:
        for file in enumerate_dat(Path(root)):
            scanned+=1
            fields=parse_dat(file)
            # Unturned v1 asset names come from the folder, file or localization.
            if fields.get("type") or fields.get("id") or fields.get("guid"):
                assets.append({"file":str(file),"fields":fields})
    ids=defaultdict(list)
    guids=defaultdict(list)
    for asset in assets:
        f=asset["fields"]
        if f.get("id"): ids[f["id"]].append(asset["file"])
        if f.get("guid"): guids[f["guid"].lower()].append(asset["file"])
    needles={name.lower(),name.lower().replace(" ",""),name.lower().replace(" ","_")}
    matching=[]
    for asset in assets:
        p=asset["file"].lower()
        f=asset["fields"]
        names=[p,f.get("name","").lower(),f.get("display_name","").lower()]
        if any(n in candidate for n in needles for candidate in names) or (expected_id is not None and f.get("id")==str(expected_id)):
            issues=[]
            if f.get("type","").lower() != "gun": issues.append("type_not_gun_or_missing")
            if not f.get("id","").isdigit(): issues.append("missing_or_invalid_legacy_id")
            if not GUID.fullmatch(f.get("guid","")): issues.append("missing_or_invalid_guid")
            if f.get("id") and len(ids[f["id"]])>1: issues.append("duplicate_legacy_id_in_scanned_roots")
            if f.get("guid") and len(guids[f["guid"].lower()])>1: issues.append("duplicate_guid_in_scanned_roots")
            # Asset v1 convention is folder-name.dat or Asset.dat.
            file=Path(asset["file"])
            if file.stem.lower() not in ("asset",file.parent.name.lower()):
                issues.append("unusual_dat_filename")
            magazine=f.get("magazine")
            if magazine and magazine.isdigit() and magazine != "0" and magazine not in ids:
                issues.append("referenced_magazine_id_not_found_in_scanned_roots")
            matching.append({"file":asset["file"],"id":f.get("id"),"guid":f.get("guid"),
                             "magazine":magazine,"issues":issues})
    return {"query":name,"expected_id":expected_id,"files_scanned":scanned,
            "candidate_count":len(matching),"candidates":matching,
            "note":"Read-only heuristic. Does not inspect bundle content or all installed Steam libraries. Empty results do not establish global absence."}

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--root",type=Path,action="append",required=True,
                   help="Folder to scan; repeat for multiple Workshop/Content/staging locations")
    p.add_argument("--name",default="Auto 32")
    p.add_argument("--id",type=int,dest="expected_id")
    args=p.parse_args()
    print(json.dumps(inspect(args.root,args.name,args.expected_id),indent=2,ensure_ascii=False))

if __name__=="__main__":
    main()
