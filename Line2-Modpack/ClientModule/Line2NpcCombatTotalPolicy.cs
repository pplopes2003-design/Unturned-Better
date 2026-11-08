namespace Unturned2.Line2
{
    /// <summary>
    /// Standalone Combat Total contract adapted from Line 1's
    /// MobileNPCCombatOnlyPolicy, without private engine modifications.
    /// Keeps the original Normal/Passive saves and civilian systems intact.
    /// Only an explicit Combat Total choice selects these rules.
    /// </summary>
    internal static class Line2NpcCombatTotalPolicy
    {
        internal const string Mode = "total";
        private const string SaveSuffix = "::Line2CombatTotal";

        internal static bool Enabled(string mode)
        {
            return mode == Mode;
        }

        // Hashing uses this full namespace. It must never alias the
        // unmodified Normal save key, even for the same Washington map.
        internal static string ScopedSaveKey(string nativeMap, string mode)
        {
            if (string.IsNullOrEmpty(nativeMap)) return null;
            return Enabled(mode) ? nativeMap + SaveSuffix : nativeMap;
        }

        internal static string ResolveNewRole(string requested, string mode)
        {
            // Manual-friendly/neutral spawn commands also produce hostile
            // fighters in COMBATE TOTAL. This is intentionally opt-in.
            return Enabled(mode) ? "hostile" : requested;
        }

        /// <summary>
        /// Like Line 1, all Combat Total soldiers are HOSTILE to the
        /// player, but rival combat groups must not all become one allied
        /// Raider faction. The stable group ID makes the two rival military
        /// alignments reproducible after loading a save without inventing a
        /// new JSON field or changing Normal faction identity.
        /// </summary>
        internal static Line2NPCFactionPolicy.Id FactionForGroup(int groupId)
        {
            return groupId > 0 && groupId % 2 == 0
                ? Line2NPCFactionPolicy.Id.Military
                : Line2NPCFactionPolicy.Id.Raiders;
        }

        internal static bool AllowCivilianWork(string mode)
        {
            return !Enabled(mode);
        }

        internal static bool AllowTrade(string mode)
        {
            return !Enabled(mode);
        }

        internal static bool CanUseCombatPatrol(string mode)
        {
            return Enabled(mode);
        }
    }
}
