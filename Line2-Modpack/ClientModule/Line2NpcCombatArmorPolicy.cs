namespace Unturned2.Line2
{
    /// <summary>
    /// Native clothing damage multiplier adjusted for actual owned quality.
    /// A damaged helmet must never become stronger, and invalid Workshop
    /// armor cannot heal, amplify or nullify incoming hit damage.
    /// </summary>
    internal static class Line2NpcCombatArmorPolicy
    {
        internal static float EffectiveMultiplier(float nativeArmor, byte quality)
        {
            if (float.IsNaN(nativeArmor) ||
                float.IsInfinity(nativeArmor) ||
                nativeArmor <= 0f || nativeArmor > 1f)
                return 1f;
            float q = System.Math.Min((int)quality, 100) / 100f;
            float m = nativeArmor + (1f - nativeArmor) * (1f - q);
            return System.Math.Max(0.01f, System.Math.Min(1f, m));
        }
    }
}
