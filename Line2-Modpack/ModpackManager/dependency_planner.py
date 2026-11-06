"""Offline dependency planner for selected local modules.

It checks the *final desired state*, orders installs by dependency, and
detects cycles. It does not download dependencies or alter Steam files.
"""
from __future__ import annotations
from collections import defaultdict
from module_support import validate_package, installed_modules, version_parts, VERSION


def plan_modules(manager, module_names: list[str], removal_names=()):
    selected = {}
    issues = []
    for staging in module_names:
        info = validate_package(manager.staging / staging)
        previous = selected.get(info["name"])
        if previous:
            issues.append({"kind": "duplicate_selected_name", "name": info["name"],
                           "staging": [previous["staging"], staging]})
        selected[info["name"]] = dict(info, staging=staging)
    installed = installed_modules(manager.game / "Modules")
    removed = set(removal_names)
    # Remove entries scheduled for removal; only managed module directories qualify.
    for key in list(installed):
        installed[key] = [
            (ver, path) for ver, path in installed[key]
            if not any(("/U2M_" + n + "/") in path.replace("\\", "/") for n in removed)
        ]
        if not installed[key]:
            del installed[key]

    edges = defaultdict(set)
    for name, info in selected.items():
        if name in installed:
            issues.append({"kind": "duplicate_installed_name", "name": name,
                           "installed": installed[name]})
        for dep in info["dependencies"]:
            dep_name = dep["Name"]
            minimum = version_parts(dep["Version"])
            selected_dep = selected.get(dep_name)
            if selected_dep and version_parts(selected_dep["version"]) >= minimum:
                edges[name].add(dep_name)
                continue
            available = installed.get(dep_name, ())
            if any(VERSION.fullmatch(v) and version_parts(v) >= minimum for v, _ in available):
                continue
            issues.append({"kind": "missing_dependency", "module": name,
                           "dependency": dep_name, "minimum": dep["Version"]})
    order = []
    visiting, visited = set(), set()
    def visit(name):
        if name in visited:
            return
        if name in visiting:
            issues.append({"kind": "dependency_cycle", "module": name})
            return
        visiting.add(name)
        for dependency in sorted(edges[name]):
            visit(dependency)
        visiting.remove(name)
        visited.add(name)
        order.append(selected[name]["staging"])
    for name in sorted(selected):
        visit(name)
    return {"install_order": order, "issues": issues,
            "selected": {name: info["staging"] for name, info in selected.items()}}
