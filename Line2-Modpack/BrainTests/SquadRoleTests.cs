using System;
using Unturned2.Line2;

internal static class SquadRoleTests
{
    internal static bool Run()
    {
        var policy = Line2NpcSquadRolePolicy.Role.Unassigned;
        if (Line2NpcSquadRolePolicy.ForLivingSlot(-1, 2) != policy ||
            Line2NpcSquadRolePolicy.ForLivingSlot(4, 4) != policy ||
            Line2NpcSquadRolePolicy.ForLivingSlot(0, 0) != policy ||
            Line2NpcSquadRolePolicy.ForLivingSlot(0, 5) != policy ||
            Line2NpcSquadRolePolicy.ForLivingSlot(0, 1) !=
                Line2NpcSquadRolePolicy.Role.Leader ||
            Line2NpcSquadRolePolicy.ForLivingSlot(1, 2) !=
                Line2NpcSquadRolePolicy.Role.Guard ||
            Line2NpcSquadRolePolicy.ForLivingSlot(2, 3) !=
                Line2NpcSquadRolePolicy.Role.Quartermaster ||
            Line2NpcSquadRolePolicy.ForLivingSlot(3, 4) !=
                Line2NpcSquadRolePolicy.Role.Scout)
        {
            Console.Error.WriteLine("NPC_SQUAD_ROLE_FAILED: roster assignment.");
            return false;
        }

        // When a leader dies, the surviving roster shifts left: no dead
        // NPC identity or private sensory memory needs to be revived.
        if (Line2NpcSquadRolePolicy.ForLivingSlot(0, 3) !=
                Line2NpcSquadRolePolicy.Role.Leader ||
            !Line2NpcSquadRolePolicy.PrioritizeBuilding(
                Line2NpcSquadRolePolicy.Role.Leader) ||
            Line2NpcSquadRolePolicy.PrioritizeBuilding(
                Line2NpcSquadRolePolicy.Role.Scout) ||
            !Line2NpcSquadRolePolicy.PrioritizeGuard(
                Line2NpcSquadRolePolicy.Role.Guard, 30f) ||
            Line2NpcSquadRolePolicy.PrioritizeGuard(
                Line2NpcSquadRolePolicy.Role.Guard, 29.9f) ||
            Line2NpcSquadRolePolicy.PrioritizeGuard(
                Line2NpcSquadRolePolicy.Role.Guard, float.NaN) ||
            !Line2NpcSquadRolePolicy.PrioritizeExploration(
                Line2NpcSquadRolePolicy.Role.Scout) ||
            !Line2NpcSquadRolePolicy.PrioritizeSupply(
                Line2NpcSquadRolePolicy.Role.Quartermaster) ||
            Line2NpcSquadRolePolicy.PrioritizeSupply(
                Line2NpcSquadRolePolicy.Role.Leader))
        {
            Console.Error.WriteLine("NPC_SQUAD_ROLE_FAILED: task priority or leader succession.");
            return false;
        }
        Console.WriteLine("NPC_SQUAD_ROLE_OK: leader succession, guard, supply and scout duties.");
        return true;
    }
}
