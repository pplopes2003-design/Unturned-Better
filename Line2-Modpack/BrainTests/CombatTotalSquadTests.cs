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
        // Combat Total uses two hostile dispositions, but only verified
        // opposing FACTIONS may damage each other. A missing group is not
        // an excuse to guess at faction identity and friendly-fire.
        var raider = Line2NPCFactionPolicy.Id.Raiders;
        var military = Line2NPCFactionPolicy.Id.Military;
        if (!Line2NpcCombatTotalSquadPolicy.CanEngageRival(
                true, true, 1, 2, raider, military) ||
            !Line2NpcCombatTotalSquadPolicy.CanEngageRival(
                true, true, 2, 1, military, raider) ||
            Line2NpcCombatTotalSquadPolicy.CanEngageRival(
                true, true, 1, 1, raider, military) ||
            Line2NpcCombatTotalSquadPolicy.CanEngageRival(
                true, true, 1, 3, raider, raider) ||
            Line2NpcCombatTotalSquadPolicy.CanEngageRival(
                true, false, 1, 2, raider, military) ||
            Line2NpcCombatTotalSquadPolicy.CanEngageRival(
                true, true, 0, 2, raider, military) ||
            Line2NpcCombatTotalSquadPolicy.CanEngageRival(
                false, true, 1, 2, raider, military))
        {
            Console.Error.WriteLine("NPC_TOTAL_SQUAD_HOSTILITY_FAILED: invalid rival or friendly fire.");
            return false;
        }

        // Assigned squad members have distinct combat roles: leader/scout
        // flank on verified sights, guard/quartermaster hold the gunline.
        // A lone ungrouped actor can flank and Normal mode is unchanged.
        if (!Line2NpcCombatTotalSquadPolicy.CanInitiateFlank(true,
                Line2NpcSquadRolePolicy.Role.Leader) ||
            !Line2NpcCombatTotalSquadPolicy.CanInitiateFlank(true,
                Line2NpcSquadRolePolicy.Role.Scout) ||
            !Line2NpcCombatTotalSquadPolicy.CanInitiateFlank(true,
                Line2NpcSquadRolePolicy.Role.Unassigned) ||
            Line2NpcCombatTotalSquadPolicy.CanInitiateFlank(true,
                Line2NpcSquadRolePolicy.Role.Guard) ||
            Line2NpcCombatTotalSquadPolicy.CanInitiateFlank(true,
                Line2NpcSquadRolePolicy.Role.Quartermaster) ||
            !Line2NpcCombatTotalSquadPolicy.CanInitiateFlank(false,
                Line2NpcSquadRolePolicy.Role.Guard))
        {
            Console.Error.WriteLine("NPC_TOTAL_SQUAD_FLANK_FAILED: role responsibilities.");
            return false;
        }

        Console.WriteLine("NPC_TOTAL_SQUAD_OK: real firsthand combat radio, live team, bounded sector movement.");
        return true;
    }
}
