using System;

namespace Unturned2.Line2
{
    /// <summary>Original Line 1 radio authorization and 20m area quantization.</summary>
    internal static class Line2NPCRadioRules
    {
        internal const int NativeWalkieId = 1445;
        internal const int AreaSizeMeters = 20;
        internal const float MaxRangeMeters = 90f;

        internal static float QuantizeSector(float coordinate)
        {
            if (float.IsNaN(coordinate) || float.IsInfinity(coordinate) ||
                Math.Abs(coordinate) > 1000000f)
                return float.NaN;
            return (float)(Math.Floor(coordinate / AreaSizeMeters) *
                           AreaSizeMeters + AreaSizeMeters * 0.5);
        }

        internal static bool MayRelay(int senderGroup, int receiverGroup,
            Line2NPCFactionPolicy.Id senderFaction,
            Line2NPCFactionPolicy.Id receiverFaction,
            bool senderAlive, bool receiverAlive,
            bool senderHasWalkie, bool receiverHasWalkie, float distanceMeters)
        {
            return senderGroup > 0 && receiverGroup > 0 &&
                senderGroup == receiverGroup &&
                senderFaction == receiverFaction &&
                senderAlive && receiverAlive &&
                senderHasWalkie && receiverHasWalkie &&
                !float.IsNaN(distanceMeters) &&
                !float.IsInfinity(distanceMeters) &&
                distanceMeters >= 0f && distanceMeters <= MaxRangeMeters;
        }
    }
}
