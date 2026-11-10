namespace Unturned2.Line2
{
    /// <summary>
    /// Pure firing-lane adaptation of Line 1 tactical spacing. These gates
    /// never choose an unknown enemy point or authorize damage. They only
    /// allow a stationary firing stance against a currently verified target.
    /// </summary>
    internal static class Line2NpcFiringPositionPolicy
    {
        internal static bool ShouldHoldPosition(bool firsthandSight,
            bool loadedRealGun, bool higherPriorityGoal,
            bool underFire, float targetDistance, float nativeWeaponRange,
            bool precision = false)
        {
            if (!firsthandSight || !loadedRealGun || higherPriorityGoal ||
                underFire || !Finite(targetDistance) ||
                !Finite(nativeWeaponRange) || nativeWeaponRange < 8f)
                return false;

            float minimum = precision
                ? Clamp(nativeWeaponRange * 0.22f, 15f, 28f)
                : Clamp(nativeWeaponRange * 0.12f, 4f, 9f);
            float maximum = precision
                ? System.Math.Min(Line2NpcSniperPolicy.MaximumRange,
                    nativeWeaponRange * 0.90f)
                : System.Math.Min(
                    Clamp(nativeWeaponRange * 0.68f, 13f, 32f),
                    nativeWeaponRange * 0.92f);
            return targetDistance >= minimum &&
                targetDistance <= maximum;
        }

        internal static bool CanSeekReloadCover(bool actuallyReloading,
            bool firsthandSight, bool higherPriorityGoal,
            float targetDistance)
        {
            return actuallyReloading && firsthandSight &&
                !higherPriorityGoal && Finite(targetDistance) &&
                targetDistance >= 7f && targetDistance <= 45f;
        }

        private static float Clamp(float value, float min, float max)
        {
            return System.Math.Max(min, System.Math.Min(max, value));
        }

        private static bool Finite(float n)
        {
            return !float.IsNaN(n) && !float.IsInfinity(n) && n >= 0f;
        }
    }
}
