namespace Unturned2.Line2
{
    /// <summary>
    /// NPCs must respect the actual selected native ItemGunAsset fire mode.
    /// A semi-only rifle is not an automatic sentry. An enabled safety or
    /// unsupported corrupted gun state cannot commit any cartridge.
    /// Native EFiremode decoding is done by the Steam adapter.
    /// </summary>
    internal static class Line2NpcFireModePolicy
    {
        internal static int ShotsInCycle(bool semi, bool burst, bool auto,
            int nativeBurstSize, int requestedAutoCycle)
        {
            if (semi) return 1;
            if (burst) return Clamp(nativeBurstSize, 1, 6);
            if (auto) return Clamp(requestedAutoCycle, 2, 6);
            return 0;
        }

        internal static bool IsAutomatic(bool auto)
        {
            return auto;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return System.Math.Max(minimum, System.Math.Min(maximum, value));
        }
    }
}
