using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Line1 BrainDriver cautious approach policy, independent of Unity.
    /// A tactical stalking request requires personally held, still-decaying
    /// sensory evidence and never turns a radio/sound clue into hit authority.
    /// </summary>
    internal static class Line2NpcAmbushPolicy
    {
        internal static bool CanPlan(bool hostile, bool combatEnabled,
            bool targetVisible, bool underFire, bool investigating,
            bool tacticsAvailable, float confidence, float uncertainty,
            float flatDistance)
        {
            return hostile && combatEnabled && !targetVisible && !underFire &&
                investigating && tacticsAvailable &&
                Finite(confidence) && Finite(uncertainty) &&
                Finite(flatDistance) && confidence >= 0.34f &&
                uncertainty >= 0f && uncertainty <= 12f &&
                flatDistance >= 4f && flatDistance <= 30f;
        }

        internal static bool WithholdPreparedAttack(
            bool stillVisible, bool samePersonallyObservedTarget,
            float now, float releaseAt)
        {
            // Only a SAME physically seen target may be subjected to the
            // brief ready interval. Hidden targets never authorize shots.
            return stillVisible && samePersonallyObservedTarget &&
                Finite(now) && Finite(releaseAt) &&
                releaseAt >= now && releaseAt - now <= 0.9f;
        }

        private static bool Finite(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }
    }
}
