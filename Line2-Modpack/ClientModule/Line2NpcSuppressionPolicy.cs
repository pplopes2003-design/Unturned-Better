namespace Unturned2.Line2
{
    /// <summary>
    /// Non-damaging combat pressure from a REAL paid, locally sighted shot
    /// that failed to hit the intended rival NPC. Never adds hitpoints lost,
    /// fabricates a projectile or reveals an enemy without sensor evidence.
    /// A short cooldown prevents automatic-fire bursts from continuously
    /// rearming a tactical reaction.
    /// </summary>
    internal static class Line2NpcSuppressionPolicy
    {
        internal const float MaximumAudiblePressureDistance = 35f;
        internal const float PressureMemorySeconds = 2.5f;
        internal const float MinimumEventSpacing = 1.0f;

        internal static bool CanReport(bool shotDebited,
            bool personallyVerifiedSight, bool intendedRivalNpc,
            bool actualHit, float shooterTargetDistance,
            float now, float lastReportedAt)
        {
            return shotDebited && personallyVerifiedSight &&
                intendedRivalNpc && !actualHit &&
                FiniteNonnegative(shooterTargetDistance) &&
                shooterTargetDistance >= 2f &&
                shooterTargetDistance <= MaximumAudiblePressureDistance &&
                FiniteNonnegative(now) &&
                (lastReportedAt < 0f ||
                 FiniteNonnegative(lastReportedAt) &&
                 now >= lastReportedAt + MinimumEventSpacing);
        }

        internal static bool IsCurrent(float now, float lastReportedAt)
        {
            return FiniteNonnegative(now) &&
                FiniteNonnegative(lastReportedAt) &&
                now >= lastReportedAt &&
                now - lastReportedAt <= PressureMemorySeconds;
        }

        private static bool FiniteNonnegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                   value >= 0f;
        }
    }
}
