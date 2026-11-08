using System;
using Unturned2.Line2;

internal static class CombatTotalSquadTests
{
    internal static bool Run()
    {
        // Two hostile factions are not automatically on the same team:
        // the runtime validates the living SAME group and BOTH authentic
        // radios before using this policy. A radio contact supplies only
        // a rough sector and never an attack target.
        if (!Line2NpcCombatTotalSquadPolicy.CanBroadcast(
                true, true, true, true, 18f) ||
            Line2NpcCombatTotalSquadPolicy.CanBroadcast(
                false, true, true, true, 18f) ||
            Line2NpcCombatTotalSquadPolicy.CanBroadcast(
                true, false, true, true, 18f) ||
            Line2NpcCombatTotalSquadPolicy.CanBroadcast(
                true, true, false, true, 18f) ||
            Line2NpcCombatTotalSquadPolicy.CanBroadcast(
                true, true, true, false, 18f) ||
            Line2NpcCombatTotalSquadPolicy.CanBroadcast(
                true, true, true, true, 36f) ||
            Line2NpcCombatTotalSquadPolicy.CanBroadcast(
                true, true, true, true, float.NaN))
        {
            Console.Error.WriteLine("NPC_TOTAL_SQUAD_BROADCAST_FAILED: fabricated combat report.");
            return false;
        }

        if (!Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, true, false, false,
                20f, 38f, 0.1f, 100f, -100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                false, true, true, false, false,
                20f, 38f, 0.1f, 100f, -100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, false, true, false, false,
                20f, 38f, 0.1f, 100f, -100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, false, false, false,
                20f, 38f, 0.1f, 100f, -100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, true, true, false,
                20f, 38f, 0.1f, 100f, -100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, true, false, true,
                20f, 38f, 0.1f, 100f, -100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, true, false, false,
                91f, 38f, 0.1f, 100f, -100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, true, false, false,
                20f, 10f, 0.1f, 100f, -100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, true, false, false,
                20f, 38f, 2f, 100f, -100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, true, false, false,
                20f, 38f, 0f, 105f, 100f) ||
            !Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, true, false, false,
                20f, 38f, 0f, 108f, 100f) ||
            Line2NpcCombatTotalSquadPolicy.CanInvestigate(
                true, true, true, false, false,
                20f, 38f, 0f, float.NaN, -100f))
        {
            Console.Error.WriteLine("NPC_TOTAL_SQUAD_REINFORCE_FAILED: authorization, cooldown or floor.");
            return false;
        }
        Console.WriteLine("NPC_TOTAL_SQUAD_OK: real firsthand combat radio, live team, bounded sector movement.");
        return true;
    }
}
