using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Pure, fail-closed decision gate for FINITE group survival-item aid.
    /// Never transfers items using radio/group-roster range alone. The world
    /// adapter must separately verify real source ownership, line-of-sight,
    /// close proximity and recipient inventory space.
    /// </summary>
    internal static class Line2NpcSquadSupplyPolicy
    {
        internal static bool MayShare(bool localServer, bool sameGroup,
            bool bothAlive, bool personalSight, bool inCombat,
            bool recipientNeedsItem, bool donorSafeToGive,
            bool recipientHasCapacity, float distanceMeters)
        {
            return localServer && sameGroup && bothAlive && personalSight &&
                !inCombat && recipientNeedsItem && donorSafeToGive &&
                recipientHasCapacity &&
                !float.IsNaN(distanceMeters) &&
                !float.IsInfinity(distanceMeters) &&
                distanceMeters >= 0.2f && distanceMeters <= 2.75f;
        }
    }
}
