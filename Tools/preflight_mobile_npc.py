#!/usr/bin/env python3
"""Pre-build source dependency audit for the MobileNPC/HybridNPC prototype."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
NPC = ROOT / "Assets/Runtime/Assembly-CSharp/Unturned/NPCs"
MUST_EXIST = [
    "MobileNPCBrainDriver",
    "MobileNPCInjurySystem",
    "MobileNPCHumanoidMotor",
    "MobileNPCHumanoidCombat",
    "MobileNPCHumanoidRangedCombat",
    "MobileNPCHumanoidPerception",
    "MobileNPCHumanoidAim",
    "MobileNPCMedicalBehavior",
]
HYBRID = [
    "HybridNPCGridPlanner", "HybridNPCRegionNavigator",
    "HybridNPCPortalProbe", "HybridNPCPathFollower",
    "HybridNPCNavigationTelemetry", "HybridNPCNavigationSession",
]

def source_of(name):
    # Search by class declarations rather than assumed file name.
    pattern = re.compile(r"\b(?:class|struct|interface)\s+" + re.escape(name) + r"\b")
    found = []
    runtime = ROOT / "Assets/Runtime"
    for file in runtime.rglob("*.cs"):
        if pattern.search(file.read_text(encoding="utf-8-sig", errors="replace")):
            found.append(str(file.relative_to(ROOT)))
    return found

def main():
    missing = []
    print("MobileNPC dependency preflight (source-level, not a C# compiler)")
    for name in MUST_EXIST + HYBRID:
        locations = source_of(name)
        print(("[OK] " if locations else "[MISSING] ") + name +
              (": " + ", ".join(locations) if locations else ""))
        if not locations:
            missing.append(name)
    print("\nNOTE: Source presence does not prove Unity compilation, prefab wiring or navigation correctness.")
    if missing:
        print("BLOCKED: missing runtime types. Do not generate a validation build assuming NPCs work.")
        return 2
    print("Source dependencies present. Next: compile in Unity and run scene/prefab integration checks.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
