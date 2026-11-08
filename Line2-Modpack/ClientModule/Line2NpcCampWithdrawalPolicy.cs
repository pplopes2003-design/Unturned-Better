using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Pure authorization boundary for finite native faction stockpile
    /// withdrawals. A saved base, rumor, or radio report never grants item
    /// ownership without direct local native inventory/line-of-sight proof.
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
