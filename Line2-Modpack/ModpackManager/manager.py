"""Unturned 2 Modpack Manager - local-only, standard-library CLI and GUI backend."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
from datetime import datetime, timezone

APP_ID = "304930"
CATEGORIES = ("Content", "Maps")
SAFE_ID = re.compile(r"^[0-9]+$")
SAFE_NAME = re.compile(r"^[A-Za-z0-9_.-]{1,100}$")


def atomic_json(path: Path, data: dict):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent,
                                     prefix=".temp-", suffix=".json", delete=False) as temp:
        json.dump(data, temp, indent=2, ensure_ascii=False)
        name = temp.name
    os.replace(name, path)


def safe_child(root: Path, name: str) -> Path:
    if not SAFE_NAME.fullmatch(name) or name in (".", ".."):
        raise ValueError("Unsafe folder name: " + name)
    path = root / name
    if path.is_symlink():
        raise ValueError("Symlink destination refused: " + str(path))
    return path


def detect_workshop(game_dir: Path) -> Path | None:
    # game: .../steamapps/common/Unturned; Steam may have multiple libraries
    steamapps = game_dir.resolve().parent.parent
    candidate = steamapps / "workshop" / "content" / APP_ID
    return candidate if candidate.is_dir() else None


def fingerprint(folder: Path) -> dict:
    """Non-cryptographic snapshot for detecting source updates."""
    files = 0
    total_size = 0
    latest_ns = 0
    for base, dirs, names in os.walk(folder, followlinks=False):
        dirs[:] = [d for d in dirs if not (Path(base) / d).is_symlink()]
        for name in names:
            p = Path(base) / name
            if p.is_symlink():
                continue
            try:
                st = p.stat()
            except OSError:
                continue
            files += 1
            total_size += st.st_size
            latest_ns = max(latest_ns, st.st_mtime_ns)
    return {"files": files, "bytes": total_size, "latest_ns": latest_ns}


class Manager:
    def __init__(self, game_dir: Path, data_dir: Path, workshop_dir: Path | None = None):
        self.game = game_dir.resolve()
        self.data = data_dir.resolve()
        self.workshop = (workshop_dir.resolve() if workshop_dir else detect_workshop(self.game))
        self.registry = self.data / "registry.json"
        self.profiles = self.data / "profiles.json"
        self.staging = self.data / "staging"
        self.game_bundles = self.game / "Bundles" / "Workshop"

    def state(self):
        if not self.registry.exists():
            return {"schema": 1, "installed": {}}
        return json.loads(self.registry.read_text(encoding="utf-8"))

    def profile_state(self):
        if not self.profiles.exists():
            return {"schema": 1, "active": "default", "profiles": {"default": {"local": [], "workshop": []}}}
        return json.loads(self.profiles.read_text(encoding="utf-8"))

    def workshop_items(self):
        if self.workshop is None or not self.workshop.is_dir():
            return []
        items = []
        for item in sorted(self.workshop.iterdir(), key=lambda p: p.name):
            if item.is_dir() and not item.is_symlink() and SAFE_ID.fullmatch(item.name):
                items.append({"id": item.name, "path": str(item), "size": fingerprint(item)["bytes"]})
        return items

    def local_items(self):
        self.staging.mkdir(parents=True, exist_ok=True)
        result = []
        for p in sorted(self.staging.iterdir()):
            if p.is_dir() and not p.is_symlink() and SAFE_NAME.fullmatch(p.name):
                result.append({"name": p.name, "size": fingerprint(p)["bytes"]})
        return result

    def import_local(self, source: Path, name: str):
        source = source.resolve()
        if not source.is_dir() or source.is_symlink():
            raise ValueError("Local mod source must be a regular directory")
        dest = safe_child(self.staging, name)
        if dest.exists():
            raise FileExistsError("Staging item exists: " + str(dest))
        # Reject symlinks and special file paths to avoid importing outside the selected tree.
        for base, dirs, files in os.walk(source, followlinks=False):
            for filename in dirs + files:
                node = Path(base) / filename
                if node.is_symlink():
                    raise ValueError("Symlink not supported inside mod source: " + str(node))
        shutil.copytree(source, dest)
        return dest

    def deploy(self, name: str, category: str, *, confirm: bool = False):
        if category not in CATEGORIES:
            raise ValueError("Choose Content or Maps")
        source = safe_child(self.staging, name)
        target = safe_child(self.game_bundles / category, "U2M_" + name)
        if not source.is_dir():
            raise FileNotFoundError(str(source))
        if target.exists() or target.is_symlink():
            raise FileExistsError("Destination already exists, refusing overwrite: " + str(target))
        state = self.state()
        key = category + "/" + name
        if key in state["installed"]:
            raise ValueError("Already managed: " + key)
        plan = {"action": "deploy", "source": str(source), "destination": str(target), "snapshot": fingerprint(source)}
        if not confirm:
            return plan
        if not self.game.is_dir() or not (self.game / "Unturned.exe").is_file():
            raise ValueError("Select the actual Windows Unturned installation directory")
        target.parent.mkdir(parents=True, exist_ok=True)
        try:
            shutil.copytree(source, target, symlinks=False)
            state["installed"][key] = {"destination": str(target), "snapshot": fingerprint(target),
                                        "source": str(source), "installed_at": datetime.now(timezone.utc).isoformat()}
            atomic_json(self.registry, state)
        except Exception:
            # Own destination only: failed installations must not linger.
            if target.exists():
                shutil.rmtree(target)
            raise
        return plan

    def remove(self, name: str, category: str, *, confirm: bool = False):
        key = category + "/" + name
        state = self.state()
        entry = state["installed"].get(key)
        if entry is None:
            raise KeyError("Not managed by this application: " + key)
        expected = safe_child(self.game_bundles / category, "U2M_" + name)
        if Path(entry["destination"]).resolve() != expected.resolve():
            raise ValueError("Registry destination mismatch")
        if expected.is_symlink():
            raise ValueError("Refusing to remove symlink")
        if not expected.is_dir():
            raise FileNotFoundError(str(expected))
        if fingerprint(expected) != entry["snapshot"]:
            raise ValueError("Installed files changed since deploy; refusing automatic removal")
        plan = {"action": "remove", "destination": str(expected)}
        if not confirm:
            return plan
        # Move to an application-controlled recovery folder, never permanently delete.
        trash = self.data / "recovery"
        trash.mkdir(parents=True, exist_ok=True)
        recovery = trash / (category + "-" + name + "-" + datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S%f"))
        shutil.move(str(expected), str(recovery))
        del state["installed"][key]
        atomic_json(self.registry, state)
        plan["recovery"] = str(recovery)
        return plan

    def profile(self, name: str | None = None):
        state = self.profile_state()
        key = name or state["active"]
        return state["profiles"][key]

    def save_profile(self, name: str, local: list[str], workshop: list[str], *, activate=False):
        if not SAFE_NAME.fullmatch(name):
            raise ValueError("Unsafe profile name")
        known = {item["id"] for item in self.workshop_items()}
        if any(not SAFE_ID.fullmatch(x) for x in workshop):
            raise ValueError("Invalid Workshop ID")
        if any(x not in known for x in workshop):
            raise ValueError("Workshop item not present in scanned library")
        known_local = {item["name"] for item in self.local_items()}
        if any(x not in known_local for x in local):
            raise ValueError("Local item absent from staging")
        state = self.profile_state()
        state["profiles"][name] = {"local": sorted(set(local)), "workshop": sorted(set(workshop))}
        if activate:
            state["active"] = name
        atomic_json(self.profiles, state)
        return state["profiles"][name]

    def export_server(self, profile_name: str, destination: Path):
        ids = self.profile(profile_name)["workshop"]
        if destination.is_symlink():
            raise ValueError("Refusing symlink destination")
        # Explicit, opt-in server configuration export. No unattended server changes.
        config = {"File_IDs": [int(x) for x in ids], "Ignore_Children_File_IDs": []}
        atomic_json(destination, config)
        return config

    def report(self):
        return {"game": str(self.game), "workshop": str(self.workshop) if self.workshop else None,
                "workshop_items": self.workshop_items(), "local_items": self.local_items(),
                "installed": self.state()["installed"], "profiles": self.profile_state()}


def main():
    p = argparse.ArgumentParser(description="Unturned 2 Modpack Manager (preview)")
    p.add_argument("--game", type=Path, required=True, help="Unturned Steam root directory")
    p.add_argument("--data", type=Path, default=Path.home() / "Unturned2-ModpackManager")
    p.add_argument("--workshop", type=Path, help="Steam Workshop content/304930 folder")
    sub = p.add_subparsers(dest="command", required=True)
    sub.add_parser("scan")
    imp = sub.add_parser("import")
    imp.add_argument("path", type=Path)
    imp.add_argument("name")
    dep = sub.add_parser("deploy")
    dep.add_argument("name")
    dep.add_argument("--category", choices=CATEGORIES, required=True)
    dep.add_argument("--apply", action="store_true")
    rm = sub.add_parser("remove")
    rm.add_argument("name")
    rm.add_argument("--category", choices=CATEGORIES, required=True)
    rm.add_argument("--apply", action="store_true")
    prof = sub.add_parser("profile")
    prof.add_argument("name")
    prof.add_argument("--local", nargs="*", default=[])
    prof.add_argument("--workshop-ids", nargs="*", default=[])
    prof.add_argument("--activate", action="store_true")
    ex = sub.add_parser("export-server")
    ex.add_argument("profile")
    ex.add_argument("destination", type=Path)
    args = p.parse_args()
    mgr = Manager(args.game, args.data, args.workshop)
    try:
        if args.command == "scan":
            result = mgr.report()
        elif args.command == "import":
            result = {"imported": str(mgr.import_local(args.path, args.name))}
        elif args.command == "deploy":
            result = mgr.deploy(args.name, args.category, confirm=args.apply)
        elif args.command == "remove":
            result = mgr.remove(args.name, args.category, confirm=args.apply)
        elif args.command == "profile":
            result = mgr.save_profile(args.name, args.local, args.workshop_ids, activate=args.activate)
        elif args.command == "export-server":
            result = mgr.export_server(args.profile, args.destination)
        print(json.dumps(result, indent=2, ensure_ascii=False))
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as exc:
        print("ERROR: " + str(exc), file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
