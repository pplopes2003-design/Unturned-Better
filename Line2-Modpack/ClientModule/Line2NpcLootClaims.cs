using System;
using System.Collections.Generic;

namespace Unturned2.Line2
{
    /// <summary>
    /// Singleplayer-scoped optional scavenging leases. They coordinate
    /// journeys, NOT ownership or pickup permission: ItemManager's real
    /// instanceID remains authoritative on the physical transfer.
    /// Expiration never grants a free item, prevents 32 NPCs marching toward
    /// the same visible drop, and is deterministic for standalone tests.
    /// </summary>
    internal static class Line2NpcLootClaims
    {
        private struct Claim
        {
            internal int Actor;
            internal float Until;
        }

        private const int Maximum = 96;
        private const float LeaseSeconds = 7f;
        private static readonly Dictionary<uint, Claim> Claims =
            new Dictionary<uint, Claim>(Maximum);
        private static readonly List<uint> Stale = new List<uint>(Maximum);

        internal static void ResetSession()
        {
            Claims.Clear();
            Stale.Clear();
        }

        internal static bool TryAcquire(uint instanceID, int actorID, float now)
        {
            if (instanceID == 0 || actorID == 0 || float.IsNaN(now) ||
                float.IsInfinity(now) || now < 0f)
                return false;
            Claim active;
            if (Claims.TryGetValue(instanceID, out active))
            {
                if (active.Actor != actorID && active.Until > now)
                    return false;
                Claims[instanceID] = new Claim
                {
                    Actor = actorID,
                    Until = now + LeaseSeconds
                };
                return true;
            }
            if (Claims.Count >= Maximum)
            {
                Stale.Clear();
                foreach (KeyValuePair<uint, Claim> entry in Claims)
                    if (entry.Value.Until <= now)
                        Stale.Add(entry.Key);
                foreach (uint id in Stale)
                    Claims.Remove(id);
                Stale.Clear();
                if (Claims.Count >= Maximum) return false;
            }
            Claims.Add(instanceID, new Claim
            {
                Actor = actorID,
                Until = now + LeaseSeconds
            });
            return true;
        }

        internal static bool CanAcquire(uint instanceID, int actorID, float now)
        {
            if (instanceID == 0 || actorID == 0 ||
                float.IsNaN(now) || float.IsInfinity(now) || now < 0f)
                return false;
            Claim active;
            return !Claims.TryGetValue(instanceID, out active) ||
                active.Actor == actorID || active.Until <= now;
        }

        internal static bool BelongsTo(uint instanceID, int actorID, float now)
        {
            Claim record;
            return instanceID != 0 && actorID != 0 &&
                !float.IsNaN(now) && !float.IsInfinity(now) &&
                Claims.TryGetValue(instanceID, out record) &&
                record.Actor == actorID && record.Until > now;
        }

        internal static void Release(uint instanceID, int actorID)
        {
            Claim claim;
            if (instanceID != 0 && actorID != 0 &&
                Claims.TryGetValue(instanceID, out claim) &&
                claim.Actor == actorID)
                Claims.Remove(instanceID);
        }

        internal static void ReleaseOwner(int actorID)
        {
            if (actorID == 0) return;
            Stale.Clear();
            foreach (KeyValuePair<uint, Claim> entry in Claims)
                if (entry.Value.Actor == actorID)
                    Stale.Add(entry.Key);
            foreach (uint id in Stale) Claims.Remove(id);
            Stale.Clear();
        }
    }
}
