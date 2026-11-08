using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Pure radius/falloff math shared by native explosion receiver and
    /// Windows tests. Does not decide whether the attacker can deal damage;
    /// that decision remains with the native DamageTool explosion flags.
    /// </summary>
    internal static class NpcExplosionMath
    {
        /// <summary>
        /// Unturned may discover the AGENT collider from zombie/animal
        /// damage flags even when the explosive is forbidden from harming
        /// players (e.g. PvE). Choose only an explicitly permitted damage
        /// channel. Never silently turn a physics-only impulse into damage.
        /// Prefer real player damage when allowed, then zombie damage.
        /// </summary>
        internal static float SelectEligibleDamage(bool playerDamageAllowed,
            bool zombieDamageAllowed, float playerDamage, float zombieDamage)
        {
            if (playerDamageAllowed && FinitePositive(playerDamage))
                return playerDamage;
            if (zombieDamageAllowed && FinitePositive(zombieDamage))
                return zombieDamage;
            return 0f;
        }

        private static bool FinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                value > 0f;
        }

        internal static float Calculate(float baseDamage, float radius, float distance)
        {
            if (float.IsNaN(baseDamage) || float.IsInfinity(baseDamage) ||
                float.IsNaN(radius) || float.IsInfinity(radius) ||
                float.IsNaN(distance) || float.IsInfinity(distance) ||
                baseDamage <= 0f || radius <= 0f || distance < 0f ||
                distance >= radius)
                return 0f;
            float falloff = 1f - (distance / radius);
            return Math.Max(0f, baseDamage * falloff);
        }
    }
}
