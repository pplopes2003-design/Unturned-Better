"""Upgrade previously generated Auto32 staging to Legendary Auto24 in place.

Do not edit installed game content; first uninstall via ModpackManager to avoid
invalidating its file-integrity registry. The legacy IDs/GUIDs remain unchanged.
"""
from pathlib import Path
from tempfile import NamedTemporaryFile
import os
from asset_diagnostics import parse_dat
from build_auto32 import replace_metadata

def upgrade_staging_auto24(staging):
    root=Path(staging)
    gun=root/"Items"/"Guns"/"Auto_32"/"Asset.dat"
    mag=root/"Items"/"Guns"/"Auto_32_Magazine"/"Asset.dat"
    gun_name=gun.parent/"English.dat"
    mag_name=mag.parent/"English.dat"
    for path in (gun, mag, gun_name, mag_name):
        if path.is_symlink() or not path.is_file():
            raise FileNotFoundError("Missing or linked asset: "+str(path))
    g=parse_dat(gun); m=parse_dat(mag)
    if g.get("type","").lower()!="gun" or m.get("type","").lower()!="magazine":
        raise ValueError("Not a valid generated weapon+magazine package")
    if g.get("magazine")!=m.get("id") or not g.get("guid") or not m.get("guid"):
        raise ValueError("Mismatched magazine references or GUIDs")
    changes={
      gun:replace_metadata(gun.read_text(encoding="utf-8-sig"),
        {"Rarity":"Legendary","Ammo_Min":24,"Ammo_Max":24},set()),
      mag:replace_metadata(mag.read_text(encoding="utf-8-sig"),
        {"Rarity":"Legendary","Amount":24},set()),
      gun_name:"Name Auto 24 (Legendary)\nDescription Automatic Bluntforce conversion; 24-round magazine.\n",
      mag_name:"Name Auto 24 Magazine (Legendary)\nDescription 24-round shotgun magazine.\n",
    }
    # Write temporary siblings before replacing any source.
    prepared=[]
    try:
        for path,content in changes.items():
            with NamedTemporaryFile("w",encoding="utf-8",dir=path.parent,
                                    prefix=".u2-auto24-",suffix=".tmp",delete=False) as temp:
                temp.write(content); prepared.append((Path(temp.name),path))
        for tmp,path in prepared: os.replace(tmp,path)
    finally:
        for temp,path in prepared: temp.unlink(missing_ok=True)
    return {"gun_id":g["id"],"magazine_id":m["id"],"staging":str(root)}

if __name__=="__main__":
    import argparse,json
    p=argparse.ArgumentParser()
    p.add_argument("--staging",type=Path,required=True)
    args=p.parse_args()
    print(json.dumps(upgrade_staging_auto24(args.staging),indent=2))
