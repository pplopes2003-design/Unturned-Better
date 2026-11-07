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
