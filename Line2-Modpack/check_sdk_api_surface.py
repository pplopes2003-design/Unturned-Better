"""Read-only SDK source compatibility gate for Steam mod bridge.

This is NOT a substitute for compiling the C# DLL against installed Steam
assemblies. It detects a subset of accidental symbol changes in the checked-in
U3-SDK source and verifies required local Unity module reference declarations.
"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / "Line2-Modpack" / "ClientModule"

REQUIREMENTS = (
    ("Assets/Runtime/Assembly-CSharp/Unturned/Useable/UseableGun.cs",
     ("public static event BulletSpawnedHandler onBulletSpawned",
      "public override bool startPrimary()",
      "public override void stopPrimary()",
      "public const int FIREMODE",
      "public const int AMMO",
      "public byte pellet")),
    ("Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerEquipment.cs",
     ("public Useable useable =>",
      "public ushort itemID =>",
      "public byte[] state",
      "public bool IsEquipAnimationFinished")),
    ("Assets/Resources/Characters/NPC_Client.prefab",
     ("m_Name: NPC_Client", "SkinnedMeshRenderer")),
    ("Line2-Modpack/ClientModule/Nexus.cs",
     ("AddComponent<SteamNpcPrototypeRuntime>()",
      "AddComponent<RepeatingBurstGameBridge>()")),
    ("Line2-Modpack/ClientModule/Unturned2.Line2.Probe.csproj",
     ("UnityEngine.AnimationModule.dll",
      "UnityEngine.IMGUIModule.dll",
      "UnityEngine.InputLegacyModule.dll")),
)

def main():
    problems = []
    for relative, needles in REQUIREMENTS:
        path = ROOT / relative
        if not path.is_file():
            problems.append("MISSING FILE: " + relative)
            continue
        data = path.read_text(encoding="utf-8-sig", errors="replace")
        for term in needles:
            if term not in data:
                problems.append("MISSING SDK SYMBOL " + relative + ": " + term)
    if problems:
        print("\n".join(problems), file=sys.stderr)
        return 1
    print("SDK source surface check passed: native gun events, ammo/firemode indices,")
    print("NPC_Client visual and Unity modules found. Full Steam C# compile STILL REQUIRED.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
