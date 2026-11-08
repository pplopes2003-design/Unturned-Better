namespace Unturned2.Line2
{
    /// <summary>
    /// Small deterministic adaptation of Line 1's group-task roles.
    /// The living roster is authoritative; role is assigned by current
    /// roster position. A fallen leader promotes a surviving teammate
    /// without resurrecting the old NPC or copying its sensory memory.
    /// This policy never assigns targets, damage, items or magic routes.
    /// </summary>
    internal static class Line2NpcSquadRolePolicy
    {
        internal enum Role { Unassigned, Leader, Guard, Quartermaster, Scout }

        internal static Role ForLivingSlot(int slot, int livingCount)
        {
            if (livingCount < 1 || livingCount > 4 ||
                slot < 0 || slot >= livingCount)
                return Role.Unassigned;
            switch (slot)
            {
                case 0: return Role.Leader;
                case 1: return Role.Guard;
                case 2: return Role.Quartermaster;
                case 3: return Role.Scout;
                default: return Role.Unassigned;
            }
        }

        /// <summary>Noncombat, ordinary task biases only. Emergencies,
        /// current combat and player alliance always remain higher priority.</summary>
        internal static bool PrioritizeBuilding(Role role)
        {
            return role == Role.Leader;
        }

        internal static bool PrioritizeGuard(Role role, float elapsedSinceGuard)
        {
            return role == Role.Guard &&
                !float.IsNaN(elapsedSinceGuard) &&
                !float.IsInfinity(elapsedSinceGuard) &&
                elapsedSinceGuard >= 30f;
        }

        internal static bool PrioritizeSupply(Role role)
        {
            return role == Role.Quartermaster;
        }

        internal static bool PrioritizeExploration(Role role)
        {
            return role == Role.Scout;
        }
    }
}
