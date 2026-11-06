"""Validation and reversible installation for Unturned .module packages.

Only game-owned Modules/U2M_<staging-name> destinations are ever modified.
No DLL execution, downloading, Steam modification or automatic BattlEye changes.
"""
from __future__ import annotations
from pathlib import Path
import json
import re
import shutil
from datetime import datetime, timezone

ROLES = {"Client", "Server", "Both_Optional", "Both_Required"}
VERSION = re.compile(r"^\d+\.\d+\.\d+\.\d+$")
SAFE_NAME = re.compile(r"^[A-Za-z0-9_.-]{1,100}$")


def under(base: Path, value: str) -> Path:
    """Resolve a module-relative assembly path without traversal or absolute paths."""
    if not isinstance(value, str):
        raise ValueError("Assembly Path must be a string")
    # Unturned's module paths commonly start with / but mean module-root relative.
    relative = value.lstrip("/")
    if not relative or "\\" in relative or ":" in relative:
        raise ValueError("Unsafe assembly path: " + value)
    parts = relative.split("/")
    if any(part in (".", "..", "") for part in parts):
        raise ValueError("Unsafe assembly path: " + value)
    root = base.resolve()
    target = root.joinpath(*parts)
    if target.resolve() != root and root not in target.resolve().parents:
        raise ValueError("Assembly escapes module")
    if target.suffix.lower() != ".dll" or not target.is_file() or target.is_symlink():
        raise ValueError("Missing/invalid assembly: " + value)
    return target


def validate_package(source: Path) -> dict:
    if source.is_symlink() or not source.is_dir():
        raise ValueError("Module source must be a regular folder")
    if any(path.is_symlink() for path in source.rglob("*")):
        raise ValueError("Module package contains symlink")
    manifests = list(source.glob("*.module"))
    if len(manifests) != 1:
        raise ValueError("Expected exactly one top-level .module manifest")
    manifest = manifests[0]
    try:
        cfg = json.loads(manifest.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as exc:
        raise ValueError("Malformed .module JSON") from exc
    if not isinstance(cfg, dict):
        raise ValueError("Module manifest must be a JSON object")
    name = cfg.get("Name")
    if not isinstance(name, str) or not SAFE_NAME.fullmatch(name):
        raise ValueError("Invalid module Name")
    version = cfg.get("Version")
    if not isinstance(version, str) or not VERSION.fullmatch(version):
        raise ValueError("Module Version must use four numeric components")
    assemblies = cfg.get("Assemblies")
    if not isinstance(assemblies, list) or not assemblies:
        raise ValueError("Module requires a nonempty Assemblies list")
    for a in assemblies:
        if not isinstance(a, dict) or a.get("Role") not in ROLES:
            raise ValueError("Invalid module assembly Role")
        under(source, a.get("Path"))
    dependencies = cfg.get("Dependencies", [])
    if not isinstance(dependencies, list):
        raise ValueError("Module Dependencies must be a list")
    for dep in dependencies:
        if not isinstance(dep, dict) or not isinstance(dep.get("Name"), str) or not dep["Name"]:
            raise ValueError("Invalid dependency Name")
        if not isinstance(dep.get("Version"), str) or not VERSION.fullmatch(dep["Version"]):
            raise ValueError("Invalid dependency version")
    return {"name": name, "version": version, "manifest": manifest.name,
            "dependencies": dependencies,
            "assemblies": [{"path": a["Path"], "role": a["Role"]} for a in assemblies]}


def installed_modules(modules_root: Path):
    result = {}
    if not modules_root.is_dir():
        return result
    for path in modules_root.rglob("*.module"):
        if path.is_symlink():
            continue
        try:
            cfg = json.loads(path.read_text(encoding="utf-8-sig"))
            if isinstance(cfg, dict) and cfg.get("IsEnabled", True) and isinstance(cfg.get("Name"), str):
                result.setdefault(cfg["Name"], []).append((str(cfg.get("Version", "")), str(path)))
        except (OSError, ValueError):
            continue
    return result


def version_parts(value: str):
    return tuple(int(x) for x in value.split("."))


def dependency_check(info: dict, game_modules: Path) -> list[dict]:
    present = installed_modules(game_modules)
    issues = []
    if info["name"] in present:
        issues.append({"kind": "duplicate_module", "name": info["name"], "found": present[info["name"]]})
    for dependency in info["dependencies"]:
        name = dependency["Name"]
        minimum = version_parts(dependency["Version"])
        versions = [version for version, _ in present.get(name, []) if VERSION.fullmatch(version)]
        if not any(version_parts(version) >= minimum for version in versions):
            issues.append({"kind": "missing_dependency", "name": name, "minimum": dependency["Version"]})
    return issues


def module_plan(manager, staging_name: str) -> dict:
    if not SAFE_NAME.fullmatch(staging_name):
        raise ValueError("Invalid staging name")
    src = manager.staging / staging_name
    info = validate_package(src)
    target = manager.game / "Modules" / ("U2M_" + staging_name)
    if target.exists() or target.is_symlink():
        raise FileExistsError("Module destination already exists")
    from manager import fingerprint
    return {"action": "install_module", "staging": staging_name, "source": str(src),
            "destination": str(target), "module": info, "issues": dependency_check(info, manager.game / "Modules"),
            "snapshot": fingerprint(src)}


def install_module(manager, staging_name: str, *, confirm: bool = False):
    plan = module_plan(manager, staging_name)
    if not confirm:
        return plan
    if plan["issues"]:
        raise ValueError("Module validation issues: " + json.dumps(plan["issues"]))
    if not (manager.game / "Unturned.exe").is_file():
        raise ValueError("Expected Windows Unturned installation")
    from manager import atomic_json, fingerprint
    state = manager.state()
    key = "Modules/" + staging_name
    if key in state["installed"]:
        raise ValueError("Module already registered")
    target = Path(plan["destination"])
    target.parent.mkdir(parents=True, exist_ok=True)
    try:
        shutil.copytree(plan["source"], target)
        state["installed"][key] = {"destination": str(target), "snapshot": fingerprint(target),
                                    "source": plan["source"], "module_name": plan["module"]["name"],
                                    "installed_at": datetime.now(timezone.utc).isoformat()}
        atomic_json(manager.registry, state)
    except Exception:
        if target.is_dir() and not target.is_symlink():
            shutil.rmtree(target)
        raise
    return plan


def remove_module(manager, staging_name: str, *, confirm: bool = False):
    if not SAFE_NAME.fullmatch(staging_name):
        raise ValueError("Invalid staging name")
    from manager import fingerprint, atomic_json
    state = manager.state()
    key = "Modules/" + staging_name
    record = state["installed"].get(key)
    if record is None:
        raise KeyError("Module not owned by manager")
    target = manager.game / "Modules" / ("U2M_" + staging_name)
    if target.is_symlink() or not target.is_dir():
        raise ValueError("Unsafe/missing module target")
    if Path(record["destination"]).resolve() != target.resolve():
        raise ValueError("Module destination mismatch")
    if fingerprint(target) != record["snapshot"]:
        raise ValueError("Module files changed since install")
    # refuse removal when another enabled module explicitly needs it
    if record.get("module_name"):
        for manifest in (manager.game / "Modules").rglob("*.module"):
            if target in manifest.parents or manifest.is_symlink():
                continue
            try:
                cfg = json.loads(manifest.read_text(encoding="utf-8-sig"))
                if cfg.get("IsEnabled", True) and any(
                    dep.get("Name") == record["module_name"] for dep in cfg.get("Dependencies", [])):
                    raise ValueError("Other module depends on " + record["module_name"])
            except (OSError, json.JSONDecodeError, AttributeError, TypeError):
                pass
    result = {"action": "remove_module", "destination": str(target)}
    if not confirm:
        return result
    recovery_root = manager.data / "recovery"
    recovery_root.mkdir(parents=True, exist_ok=True)
    recovery = recovery_root / ("Module-" + staging_name + "-" + datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S%f"))
    shutil.move(str(target), str(recovery))
    del state["installed"][key]
    atomic_json(manager.registry, state)
    result["recovery"] = str(recovery)
    return result
