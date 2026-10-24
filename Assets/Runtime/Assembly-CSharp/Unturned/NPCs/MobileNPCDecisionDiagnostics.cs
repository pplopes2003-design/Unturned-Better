using System;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Opt-in, development-only decision tracing. Not connected to Update or
    /// existing AI callbacks: instrumentation must not affect the baseline build.
    /// Attach explicit probes only after verifying the affected code path.
    /// </summary>
    internal static class MobileNPCDecisionDiagnostics
    {
        internal static bool Enabled { get; set; }

        internal static void Record(string npcId, string subsystem,
            string decision, string reason, Vector3 position)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!Enabled)
                return;
            Debug.Log("[MobileNPCDecision] npc=" + Sanitize(npcId)
                + " subsystem=" + Sanitize(subsystem)
                + " decision=" + Sanitize(decision)
                + " reason=" + Sanitize(reason)
                + " position=" + position.ToString("F1"));
#endif
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "-";
            return value.Replace('\n', ' ').Replace('\r', ' ')
                .Replace('=', ':');
        }
    }
}
