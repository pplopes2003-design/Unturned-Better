using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Line1-style cautious withdrawal when a teammate actually falls within
    /// firsthand hearing/vision range. A group roster entry, remote radio
    /// message or old corpse position is NOT sufficient proof of nearby
    /// danger. The actor still needs a separately verified safe ground route.
    /// </summary>
    internal static class Line2NpcSquadMoralePolicy
    {
        internal static bool MayWithdraw(bool authorized,
            bool sameLivingGroup, bool confirmedRecentViolence,
            bool personallyWitnessedCasualty,
            bool currentlyRetreating, Vector3 self, Vector3 casualty)
        {
            if (!authorized || !sameLivingGroup ||
                !confirmedRecentViolence || !personallyWitnessedCasualty ||
                currentlyRetreating || !Finite(self) || !Finite(casualty) ||
                Math.Abs(self.y - casualty.y) > 1.5f)
                return false;
            Vector3 delta = self - casualty;
            delta.y = 0f;
            return delta.sqrMagnitude > 1.2f * 1.2f &&
                delta.sqrMagnitude <= 24f * 24f;
        }

        private static bool Finite(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsNaN(point.y) &&
                !float.IsNaN(point.z) &&
                !float.IsInfinity(point.x) &&
                !float.IsInfinity(point.y) &&
                !float.IsInfinity(point.z);
        }
    }
}
