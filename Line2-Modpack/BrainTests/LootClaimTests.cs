using System;
using Unturned2.Line2;

internal static class LootClaimTests
{
    internal static bool Run()
    {
        Line2NpcLootClaims.ResetSession();
        if (!Line2NpcLootClaims.TryAcquire(100u, 1, 1f) ||
            Line2NpcLootClaims.TryAcquire(100u, 2, 2f) ||
            !Line2NpcLootClaims.BelongsTo(100u, 1, 2f) ||
            Line2NpcLootClaims.BelongsTo(100u, 2, 2f) ||
            !Line2NpcLootClaims.TryAcquire(100u, 1, 5f) ||
            Line2NpcLootClaims.TryAcquire(100u, 2, 10f))
            return false;
        Line2NpcLootClaims.Release(100u, 2); // Wrong owner cannot steal it.
        if (!Line2NpcLootClaims.BelongsTo(100u, 1, 10f) ||
            Line2NpcLootClaims.TryAcquire(100u, 2, 10f) ||
            !Line2NpcLootClaims.TryAcquire(100u, 2, 13f))
            return false;
        Line2NpcLootClaims.ReleaseOwner(1); // Does not cancel 2's new lease.
        if (!Line2NpcLootClaims.BelongsTo(100u, 2, 14f))
            return false;
        Line2NpcLootClaims.Release(100u, 2);
        if (!Line2NpcLootClaims.TryAcquire(100u, 3, 14f))
            return false;
        if (Line2NpcLootClaims.TryAcquire(0u, 1, 2f) ||
            Line2NpcLootClaims.TryAcquire(1u, 0, 2f) ||
            Line2NpcLootClaims.TryAcquire(1u, 1, float.NaN) ||
            Line2NpcLootClaims.TryAcquire(1u, 1, -1f))
            return false;
        for (uint id = 1000; id < 1095; id++)
            if (!Line2NpcLootClaims.TryAcquire(id, 1, 20f))
                return false;
        // At t=21 the owner's earlier lease expires, so capacity would
        // actually be freed. Check FULL occupancy strictly before t=21.
        if (Line2NpcLootClaims.TryAcquire(99999u, 2, 20.5f) ||
            !Line2NpcLootClaims.TryAcquire(99999u, 2, 28f))
            return false;
        Line2NpcLootClaims.ResetSession();
        if (Line2NpcLootClaims.BelongsTo(100u, 3, 14f) ||
            !Line2NpcLootClaims.TryAcquire(100u, 9, 1f))
            return false;
        Console.WriteLine("NPC_LOOT_CLAIM_OK: exclusive drop targeting, owner release, absolute expiry, bounded capacity and map reset.");
        return true;
    }
}
