using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Deterministic gate for real finite NPC-owned inventory delivery to an
    /// EXACT manifest-identified native camp storage. Group membership alone
    /// cannot authorize deposit into nearby player containers or teleport
    /// resources from distant actors.
    /// </summary>
    internal static class Line2NpcCampSupplyPolicy
    {
        internal static bool MayDeliver(bool localServer,
            bool sameLivingGroup, bool realSavedNativeStorage,
            bool directSight, bool inCombat, bool resourceIsSurplus,
            bool donorNeedsResource, bool dedicatedVendor,
            float metersFromStorage)
        {
            return localServer && sameLivingGroup &&
                realSavedNativeStorage && !inCombat &&
                resourceIsSurplus && !donorNeedsResource &&
                !dedicatedVendor &&
                !float.IsNaN(metersFromStorage) &&
                !float.IsInfinity(metersFromStorage) &&
                metersFromStorage >= 0f && metersFromStorage <= 3.4f;
        }
    }
}
