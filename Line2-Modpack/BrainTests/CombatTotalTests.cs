using System;
using Unturned2.Line2;

internal static class CombatTotalTests
{
    internal static bool Run()
    {
        string normal = Line2NpcCombatTotalPolicy.ScopedSaveKey(
            "Washington", "normal");
        string combat = Line2NpcCombatTotalPolicy.ScopedSaveKey(
            "Washington", "total");
        if (normal != "Washington" || string.IsNullOrEmpty(combat) ||
            combat == normal ||
            Line2NpcCombatTotalPolicy.ScopedSaveKey(
                "Washington", "total") != combat ||
            Line2NpcCombatTotalPolicy.ScopedSaveKey(
                "PEI", "total") == combat ||
            Line2NpcCombatTotalPolicy.ScopedSaveKey(
                null, "total") != null ||
            Line2NpcCombatTotalPolicy.ScopedSaveKey(
                "Washington", "passive") != normal)
        {
            Console.Error.WriteLine("NPC_COMBAT_TOTAL_SAVE_FAILED: Normal history not isolated.");
            return false;
        }

        if (Line2NpcCombatTotalPolicy.ResolveNewRole(
                "friendly", "total") != "hostile" ||
            Line2NpcCombatTotalPolicy.ResolveNewRole(
                "neutral", "total") != "hostile" ||
            Line2NpcCombatTotalPolicy.ResolveNewRole(
                "hostile", "normal") != "hostile" ||
            Line2NpcCombatTotalPolicy.ResolveNewRole(
                "friendly", "normal") != "friendly" ||
            Line2NpcCombatTotalPolicy.AllowCivilianWork("total") ||
            Line2NpcCombatTotalPolicy.AllowTrade("total") ||
            !Line2NpcCombatTotalPolicy.AllowTrade("normal") ||
            !Line2NpcCombatTotalPolicy.CanUseCombatPatrol("total") ||
            Line2NpcCombatTotalPolicy.CanUseCombatPatrol("normal"))
        {
            Console.Error.WriteLine("NPC_COMBAT_TOTAL_ROLE_FAILED: mode or civilian leakage.");
            return false;
        }

        var raiders = Line2NpcCombatTotalPolicy.FactionForGroup(1);
        var military = Line2NpcCombatTotalPolicy.FactionForGroup(2);
        if (raiders != Line2NPCFactionPolicy.Id.Raiders ||
            military != Line2NPCFactionPolicy.Id.Military ||
            Line2NpcCombatTotalPolicy.FactionForGroup(3) != raiders ||
            !Line2NPCFactionPolicy.AreHostile(raiders, military) ||
            Line2NPCFactionPolicy.AreHostile(raiders, raiders))
        {
            Console.Error.WriteLine("NPC_COMBAT_TOTAL_FACTION_FAILED: squad rivalries.");
            return false;
        }

        float fullVest = Line2NpcCombatArmorPolicy.EffectiveMultiplier(
            0.6f, 100);
        float wornVest = Line2NpcCombatArmorPolicy.EffectiveMultiplier(
            0.6f, 50);
        float emptyVest = Line2NpcCombatArmorPolicy.EffectiveMultiplier(
            0.6f, 0);
        if (Math.Abs(fullVest - 0.6f) > 0.001f ||
            Math.Abs(wornVest - 0.8f) > 0.001f ||
            Math.Abs(emptyVest - 1f) > 0.001f ||
            Line2NpcCombatArmorPolicy.EffectiveMultiplier(
                float.NaN, 100) != 1f ||
            Line2NpcCombatArmorPolicy.EffectiveMultiplier(
                -1f, 100) != 1f ||
            Line2NpcCombatArmorPolicy.EffectiveMultiplier(
                1.2f, 100) != 1f)
        {
            Console.Error.WriteLine("NPC_COMBAT_TOTAL_ARMOR_FAILED: owned quality damage mitigation.");
            return false;
        }

        Console.WriteLine("NPC_COMBAT_TOTAL_OK: isolated saves, hostile squads, "
            + "rival factions, civilian exclusion and native armor quality.");
        return true;
    }
}
