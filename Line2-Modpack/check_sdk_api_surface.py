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
    ("Assets/Runtime/Assembly-CSharp/Unturned/Settings/ControlsSettings.cs",
     ("public static KeyCode primary =>",)),
    ("Assets/Runtime/Assembly-CSharp/Unturned/Tools/DamageTool.cs",
     ("public interface IExplosionDamageable",
      "GetComponentInParent<IExplosionDamageable>()",
      "RayMasks.AGENT")),
    ("Assets/Runtime/Assembly-CSharp/Unturned/Zombies/Zombie.cs",
     ("public bool checkAlert(Player newPlayer)",
      "public void alert(Player newPlayer)",
      "public byte bound;",
      "public ushort id;",
      "public bool isHunting",
      "public bool isDead")),
    ("Assets/Runtime/Assembly-CSharp/Unturned/Regions/Regions.cs",
     ("public static bool tryGetCoordinate(Vector3 point, out byte x, out byte y)",)),
    ("Assets/Resources/Characters/NPC_Client.prefab",
     ("m_Name: NPC_Client", "SkinnedMeshRenderer")),
    ("Line2-Modpack/ClientModule/Nexus.cs",
     ("AddComponent<SteamNpcPrototypeRuntime>()",
      "AddComponent<LiveNpcDecisionBridge>()",
      "AddComponent<NpcAcousticSensorBridge>()",
      "NpcDecisionBrainSelfTest.Run()",
      "AddComponent<ZombiePriorityVisionBridge>()",
      "AddComponent<RepeatingBurstGameBridge>()")),
    ("Line2-Modpack/ClientModule/SteamNpcPrototypeRuntime.cs",
     ("AddComponent<PrototypeObstacleNavigator>()",
      "CopyLiveActors",
      "ObserveThreat",
      "TryApplyAiAction")),
    ("Line2-Modpack/ClientModule/SteamNpcPrototypeRuntime.cs",
     ("NpcDecisionBrain _brain", "NpcIntent intent = _brain.Evaluate",
      "HearVerifiedGunshot")),
    ("Line2-Modpack/ClientModule/Line2NativeAStarRouteProvider.cs",
     ("UnturnedPathfinding.Get()",
      "Pathfinding.Seeker",
      "OnNativePathComplete")),
    ("Line2-Modpack/ClientModule/SteamNpcPrototypeRuntime.cs",
     ("Line2NativeAStarRouteProvider",
      "PrototypeAStarFollower",
      "PrototypeNpcVitals",
      "PrototypeCoverPlanner",
      "Line2MobileNPCTactics",
      "RayMasks.AGENT")),
    ("Line2-Modpack/ClientModule/Line2MobileNPCBrain.cs",
     ("ReportVisibleTarget", "ReportNoise", "ReportDestinationReached",
      "TrySetInvestigationHint", "investigationSearchCount")),
    ("Line2-Modpack/ClientModule/Line2MobileNPCPlayerKnowledge.cs",
     ("ObserveSight", "ObserveSound", "TryGetEvidence",
      "TrySuggestSearchPoint")),
    ("Line2-Modpack/ClientModule/Line2MobileNPCLocomotionPolicy.cs",
     ("Gait", "Decide(Situation situation, float now)")),
    ("Line2-Modpack/ClientModule/Line2MobileNPCAdaptiveMemory.cs",
     ("RecordOwnOutcomeServer", "GetCoverAccessAdjustment",
      "ForgetOnDeathServer")),
    ("Line2-Modpack/ClientModule/SteamNpcPrototypeRuntime.cs",
     ("Line2MobileNPCBrain _legacyBrain",
      "Line2MobileNPCPlayerKnowledge _knowledge",
      "Line2MobileNPCLocomotionPolicy _locomotion",
      "Line2MobileNPCAdaptiveMemory _experience",
      "_legacyBrain.ReportDestinationReached()")),
    ("Line2-Modpack/ClientModule/Line2MobileNPCTactics.cs",
     ("TryChooseDefensiveGoal", "ReportValidatedRetreatPoint",
      "TryStartFlank", "TryStartRadioReinforcement", "TryStartRaidStaging")),
    ("Line2-Modpack/ClientModule/PrototypeNpcVitals.cs",
     ("IExplosionDamageable",
      "ApplyExplosionDamage(in ExplosionParameters",
      "GetClosestPointToExplosion")),
    ("Line2-Modpack/ClientModule/WorldScanner.cs",
     ("fileName != \"gun-api-discovery.txt\"",)),
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
