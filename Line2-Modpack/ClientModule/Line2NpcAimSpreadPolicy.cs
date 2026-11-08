namespace Unturned2.Line2
{
    /// <summary>
    /// Humanized NPC weapon handling. Angular deviation grows with real
    /// movement, sustained automatic fire, injury and verified incoming
    /// suppression. An NPC cannot become a perfect sentry just by standing
    /// still, and invalid Workshop or sensor values never produce NaN aim.
    /// Random angular samples are generated only by the combat executor.
    /// </summary>
    internal static class Line2NpcAimSpreadPolicy
    {
        internal const float MaximumDegrees = 12f;

        internal static float ComputeDegrees(float targetDistance,
            bool moving, int burstRemaining, bool woundedRecently,
            bool underVerifiedSuppression, float healthFraction)
        {
            if (!Finite(targetDistance) || !Finite(healthFraction) ||
                targetDistance < 0f || healthFraction < 0f ||
                healthFraction > 1f)
                return MaximumDegrees;

            float burstPenalty = (5 - Clamp(burstRemaining, 1, 5)) * 0.7f;
            float motionPenalty = moving ? 2.4f : 0f;
            float pressurePenalty = underVerifiedSuppression ? 2.1f : 0f;
            float injuryPenalty = woundedRecently
                ? 1.0f + (1f - healthFraction) * 1.4f : 0f;
            float degrees = 2.8f + (targetDistance / 25f) * 0.75f +
                burstPenalty + motionPenalty + pressurePenalty +
                injuryPenalty;
            return Clamp(degrees, 2f, MaximumDegrees);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return System.Math.Min(maximum, System.Math.Max(minimum, value));
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return System.Math.Min(maximum, System.Math.Max(minimum, value));
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
