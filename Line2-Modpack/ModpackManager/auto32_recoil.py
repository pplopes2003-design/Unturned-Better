"""Auto 32 recoil tuning for generated, staging-only Unturned gun metadata.

Intentionally refuses editing deployed game folders: Modpack Manager's
registry snapshots protect removal integrity. Close Unturned, remove managed
installation to recovery, tune staging, then reinstall.
"""
from __future__ import annotations
import argparse
from pathlib import Path
import re
from tempfile import NamedTemporaryFile
import os

RECOIL_FIELDS = (
    "Recoil_Min_X", "Recoil_Max_X", "Recoil_Min_Y", "Recoil_Max_Y",
    "Recover_X", "Recover_Y", "Aiming_Recoil_Multiplier",
)
# Degrees of camera displacement / shot; X horizontal, Y vertical.
PRESETS = {
    "soft": (-0.35, 0.35, 0.7, 1.1, 0.65, 0.65, 0.75),
    "balanced": (-0.7, 0.7, 1.25, 1.8, 0.5, 0.55, 0.8),
    "strong": (-1.2, 1.2, 2.2, 3.1, 0.35, 0.4, 0.9),
}
FIELDS = re.compile(r"^\s*([A-Za-z_][A-Za-z0-9_]*)\b", re.ASCII)


def tune_text(text: str, preset="balanced") -> str:
    if preset not in PRESETS:
        raise ValueError("Unknown recoil preset: " + str(preset))
    overrides = dict(zip(RECOIL_FIELDS, PRESETS[preset]))
    removed = {k.lower() for k in overrides}
    kept = []
    for line in text.splitlines():
        match = FIELDS.match(line)
        if match and match.group(1).lower() in removed:
            continue
        kept.append(line)
    kept.extend(key + " " + format(value, ".6g") for key, value in overrides.items())
    return "\n".join(kept).rstrip() + "\n"


def tune_staged_auto32(staging_root: Path, preset="balanced"):
    root = Path(staging_root)
    if root.is_symlink() or not root.is_dir():
        raise ValueError("Choose an existing, regular Auto32 staging folder")
    asset = root / "Items" / "Guns" / "Auto_32" / "Asset.dat"
    if asset.is_symlink() or not asset.is_file():
        raise FileNotFoundError(str(asset))
    from asset_diagnostics import parse_dat
    fields = parse_dat(asset)
    if fields.get("type", "").lower() != "gun" or not fields.get("id", "").isdigit():
        raise ValueError("Staged Auto32 gun definition was not recognized")
    original = asset.read_text(encoding="utf-8-sig")
    revised = tune_text(original, preset)
    if revised != original:
        # Replace the staging asset atomically, no deployment side effects.
        with NamedTemporaryFile("w", encoding="utf-8", dir=asset.parent,
                                prefix=".auto32-", suffix=".dat", delete=False) as tmp:
            tmp.write(revised)
            tmp_path = Path(tmp.name)
        try:
            os.replace(tmp_path, asset)
        finally:
            tmp_path.unlink(missing_ok=True)
    return {"id": fields["id"], "preset": preset, "file": str(asset),
            "parameters": dict(zip(RECOIL_FIELDS, PRESETS[preset]))}


def main():
    p = argparse.ArgumentParser(description="Adjust staging-only Auto 32 recoil")
    p.add_argument("--staging", type=Path, required=True)
    p.add_argument("--preset", choices=sorted(PRESETS), default="balanced")
    args = p.parse_args()
    import json
    print(json.dumps(tune_staged_auto32(args.staging, args.preset),
                     indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
