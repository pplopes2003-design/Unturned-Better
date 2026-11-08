using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Pure authorization boundary for native group-stockpile withdrawal.
    /// Knowing a saved site or receiving a radio message never grants item
    /// ownership. Steam caller must independently identify the exact native
    /// instance, verify owner/group == 0, nearby unobstructed physical access,
    /// actual ItemJar presence and one reserved NPC inventory slot.
    /// </summary>
    internal static class Line2NpcCampWithdrawalPolicy
    {
        internal static bool MayWithdraw(bool localAuthority,
            bool sameLivingGroup, bool nativeInstanceVerified,
            bool unownedGroupStorage, bool personalLineOfSight,
            bool recipientNeedsSupply, bool notInCombat,
            bool receiverHasCapacity, bool nativeJarStillPresent,
            float meters)
        {
            return localAuthority && sameLivingGroup &&
                nativeInstanceVerified && unownedGroupStorage &&
                personalLineOfSight && recipientNeedsSupply &&
                notInCombat && receiverHasCapacity &&
                nativeJarStillPresent &&
                !float.IsNaN(meters) && !float.IsInfinity(meters) &&
                meters >= 0.15f && meters <= 3.35f;
        }
    }
}
