namespace Unturned2.Line2
{
    /// <summary>Finite magazine invariants from Line 1 NPC ranged combat.</summary>
    internal static class Line2NPCAmmoRules
    {
        internal static bool CanFire(int rounds, int ammoPerShot, int maxMagazine)
        {
            return ammoPerShot > 0 && ammoPerShot <= 255 &&
                rounds >= ammoPerShot && rounds > 0 &&
                maxMagazine >= ammoPerShot && maxMagazine <= 255 &&
                rounds <= maxMagazine;
        }

        internal static int RemainingAfterShot(int rounds, int ammoPerShot,
                                                int maxMagazine)
        {
            return CanFire(rounds, ammoPerShot, maxMagazine)
                ? rounds - ammoPerShot : -1;
        }

        internal static bool CanLoadReplacement(int roundsInSpare,
                                                 int maxMagazine)
        {
            return roundsInSpare > 0 && maxMagazine > 0 &&
                roundsInSpare <= maxMagazine && maxMagazine <= 255;
        }
    }
}
