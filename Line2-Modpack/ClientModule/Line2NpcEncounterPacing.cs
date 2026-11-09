using System;
using System.Collections.Generic;

namespace Unturned2.Line2
{
    /// <summary>
    /// Bounded, target-free port of Line1 MobileNPCEncounterDirector's
    /// optional evidence-search leases. This is NOT another brain, does NOT
    /// see the player, alter native perception, spawn characters or inhibit
    /// a genuine firsthand fight. At most two members of a four-NPC group
    /// receive BONUS speculative search/stalk jobs at the same time.
    /// Every lease has an absolute expiry and is scoped to one loaded map.
    /// </summary>
    internal static class Line2NpcEncounterPacing
    {
        private struct Lease
        {
            internal int owner;
            internal float expiresAt;
        }

        private const int MaxGroups = 48;
        private const int MaxOptionalPerGroup = 2;
        private static int _optionalGroupLimit = MaxOptionalPerGroup;
        private static float _respiteUntil;

        // The world-level encounter director can restrict ONLY optional
        // evidence searches. All firsthand senses, defensive moves and
        // legitimate investigations remain independent.
        internal static void Configure(int optionalLimit, float respiteUntil)
        {
            _optionalGroupLimit = Math.Max(0,
                Math.Min(MaxOptionalPerGroup, optionalLimit));
            _respiteUntil = float.IsNaN(respiteUntil) ? 0f : respiteUntil;
        }
        private const float LeaseSeconds = 8f;
        private static readonly Dictionary<string, List<Lease>> Leases =
            new Dictionary<string, List<Lease>>(MaxGroups);
        private static readonly List<string> Empty = new List<string>(MaxGroups);

        internal static void ResetSession()
        {
            Leases.Clear();
            Empty.Clear();
            _optionalGroupLimit = MaxOptionalPerGroup;
            _respiteUntil = 0f;
        }

        private static string Key(int groupId, int owner)
        {
            return groupId > 0 ? "g:" + groupId : "u:" + owner;
        }

        private static void Prune(float now)
        {
            Empty.Clear();
            foreach (KeyValuePair<string, List<Lease>> group in Leases)
            {
                for (int i = group.Value.Count - 1; i >= 0; --i)
                    if (now >= group.Value[i].expiresAt)
                        group.Value.RemoveAt(i);
                if (group.Value.Count == 0)
                    Empty.Add(group.Key);
            }
            foreach (string k in Empty) Leases.Remove(k);
        }

        internal static bool TryLease(int groupId, int ownerId,
            float now, bool personallyInvestigating)
        {
            if (!personallyInvestigating || now < _respiteUntil ||
                _optionalGroupLimit == 0 || ownerId == 0 ||
                float.IsNaN(now) || float.IsInfinity(now) || now < 0f)
                return false;
            Prune(now);
            string key = Key(groupId, ownerId);
            List<Lease> active;
            if (!Leases.TryGetValue(key, out active))
            {
                if (Leases.Count >= MaxGroups) return false;
                active = new List<Lease>(MaxOptionalPerGroup);
                Leases.Add(key, active);
            }
            for (int i = 0; i < active.Count; ++i)
                if (active[i].owner == ownerId)
                {
                    active[i] = new Lease
                    {
                        owner = ownerId,
                        expiresAt = now + LeaseSeconds
                    };
                    return true;
                }
            if (active.Count >= _optionalGroupLimit)
                return false;
            active.Add(new Lease
            {
                owner = ownerId,
                expiresAt = now + LeaseSeconds
            });
            return true;
        }

        internal static void Release(int ownerId)
        {
            if (ownerId == 0) return;
            foreach (List<Lease> active in Leases.Values)
                for (int i = active.Count - 1; i >= 0; --i)
                    if (active[i].owner == ownerId)
                        active.RemoveAt(i);
        }
    }
}
