"""Read-only validation of the target module set after a profile switch.

Avoids removing a dependency still needed by retained modules. Does not
modify files or rely on the Steam process.
"""
from __future__ import annotations
from module_support import validate_package, installed_modules, VERSION, version_parts
from pathlib import Path
import json


def final_module_issues(manager, selected_modules):
    """Check selected staged modules as well as retained unmanaged installed modules."""
    installed = installed_modules(manager.game / "Modules")
    state = manager.state()["installed"]
    desired_staging = set(selected_modules)
    retained_paths = set()

    # Exclude managed modules not in the desired profile. These will be removed.
    for name, entries in list(installed.items()):
        kept = []
        for version, path in entries:
            belongs_to_removed = False
            for key, record in state.items():
                if not key.startswith("Modules/"):
                    continue
                staging = key.split("/", 1)[1]
                if staging not in desired_staging and Path(record["destination"]).resolve() in Path(path).resolve().parents:
                    belongs_to_removed = True
                    break
            if not belongs_to_removed:
                kept.append((version, path))
                retained_paths.add(path)
        if kept:
            installed[name] = kept
        else:
            del installed[name]

    issues = []
    staged = {}
    for staging in selected_modules:
        info = validate_package(manager.staging / staging)
        staged.setdefault(info["name"], []).append(info)
    for name, copies in staged.items():
        if len(copies) > 1 or (name in installed and
                              any(not any(("/U2M_" + x + "/") in p.replace("\\", "/")
                                          for x in desired_staging) for _, p in installed[name])):
            issues.append({"kind": "duplicate_final_module", "module": name})

    available = {name: [v for v, _ in entries if VERSION.fullmatch(v)]
                 for name, entries in installed.items()}
    for name, copies in staged.items():
        available.setdefault(name, []).extend(info["version"] for info in copies)

    manifests = []
    for info_list in staged.values():
        manifests.extend(info_list)
    for path in retained_paths:
        try:
            cfg = json.loads(Path(path).read_text(encoding="utf-8-sig"))
        except (OSError, ValueError):
            issues.append({"kind": "unreadable_retained_manifest", "path": path})
            continue
        if cfg.get("IsEnabled", True):
            manifests.append({"name": cfg.get("Name", path), "dependencies": cfg.get("Dependencies", [])})

    for info in manifests:
        for dep in info.get("dependencies", []):
            if not isinstance(dep, dict) or not isinstance(dep.get("Name"), str) or not isinstance(dep.get("Version"), str) or not VERSION.fullmatch(dep["Version"]):
                issues.append({"kind": "invalid_dependency", "module": info.get("name")})
                continue
            versions = available.get(dep["Name"], [])
            if not any(version_parts(v) >= version_parts(dep["Version"]) for v in versions):
                issues.append({"kind": "broken_final_dependency", "module": info.get("name"),
                               "dependency": dep["Name"], "minimum": dep["Version"]})
    return issues
