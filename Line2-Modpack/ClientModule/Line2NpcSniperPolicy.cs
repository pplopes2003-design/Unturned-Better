using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Deliberate long-range shot gating. This is not a free accuracy buff:
    /// it requires a real long-range semi-capable gun, firsthand LOS,
    /// physical stability, ammunition and the normal first-hit ballistics.
    /// </summary>
    internal static class Line2NpcSniperPolicy
    {
        internal const float MaximumRange = 120f;
        internal const float DefaultSightRange = 28f;

        internal static bool IsPrecisionWeapon(bool semi, bool automatic,
            float nativeRange)
        {
            return semi && !automatic && Valid(nativeRange) &&
                nativeRange >= 85f;
        }

        internal static float SightRange(bool precision, float nativeRange)
        {
            return precision
                ? Math.Min(MaximumRange, Math.Max(DefaultSightRange, nativeRange))
                : DefaultSightRange;
        }

        internal static bool MayTakePrecisionShot(bool precision,
            bool firsthandSight, bool moving, bool retreating,
            bool underSuppression, float settledSeconds,
            float targetDistance, float nativeRange)
        {
            return precision && firsthandSight && !moving &&
                !retreating && !underSuppression &&
                Valid(settledSeconds) && settledSeconds >= 1.25f &&
                Valid(targetDistance) && targetDistance >= 8f &&
                Valid(nativeRange) && targetDistance <=
                    Math.Min(MaximumRange, nativeRange);
        }

        internal static float PrecisionSpread(float distance, bool injured,
            float normalSpread)
        {
            if (!Valid(distance) || !Valid(normalSpread))
                return 12f;
            // Never a perfect laser: angular dispersion and target movement
            // still matter, as do the original game ballistic trajectories.
            float minimum = 0.50f + distance / 220f +
                (injured ? 0.65f : 0f);
            return Math.Min(normalSpread, Math.Max(0.50f, minimum));
        }

        internal static float NextShotDelay(float nativeReload)
        {
            if (!Valid(nativeReload)) return 2.0f;
            return Math.Max(1.4f, Math.Min(3.5f, nativeReload));
        }

        private static bool Valid(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x) && x >= 0f;
        }
    }
}
