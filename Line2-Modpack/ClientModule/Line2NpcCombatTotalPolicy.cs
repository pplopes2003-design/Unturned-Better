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
