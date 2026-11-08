namespace Unturned2.Line2
{
    /// <summary>
    /// Validates materialized per-pellet weapon damage before entering the
    /// native Player/Zombie health pipeline or the NPC vitals pipeline.
    /// A corrupted Workshop multiplier must not apply NaN/Infinity,
    /// negative health or a one-shot arithmetic overflow.
    /// Does not invent damage on misses, increase accuracy, or bypass armor.
    /// </summary>
    internal static class Line2NpcGunDamagePolicy
    {
        internal const float MaximumSinglePelletDamage = 5000f;

        internal static bool IsValid(float damage)
        {
            return !float.IsNaN(damage) && !float.IsInfinity(damage) &&
                   damage > 0f && damage <= MaximumSinglePelletDamage;
        }

        internal static bool IsValidFalloff(float multiplier)
        {
            return !float.IsNaN(multiplier) &&
                   !float.IsInfinity(multiplier) &&
                   multiplier > 0f && multiplier <= 8f;
        }
    }
}
