namespace Unturned2.Line2
{
    /// <summary>
    /// Pure, deterministic range-attack state gates. Waiting for a real
    /// cooldown must never erase the already acquired target, but target
    /// loss and a committed tactical withdrawal must erase it immediately.
    /// These tests run without Unity or the Steam game assemblies.
    /// </summary>
    internal static class Line2NpcFireCadencePolicy
    {
        internal static bool RetainTarget(bool hasVisibleTarget,
            bool retreating)
        {
            return hasVisibleTarget && !retreating;
        }

        internal static bool MayFire(float now, float targetAcquiredAt,
            float nextAllowedShotAt)
        {
            return Finite(now) && Finite(targetAcquiredAt) &&
                Finite(nextAllowedShotAt) &&
                now >= nextAllowedShotAt &&
                now - targetAcquiredAt >= 0.28f;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                value >= 0f;
        }
    }
}
