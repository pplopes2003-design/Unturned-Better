using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Limited port of Line 1 TerritoryRaidPlanner's spoken rally order.
    /// Native visual evidence authorizes the observer to propose a WALKING
    /// waypoint to up to two same-group nearby allies, not a target, damage,
    /// faction intelligence, or an invisible enemy's precise location.
    /// Recipient still has to verify its OWN collision-safe destination.
    /// </summary>
    internal static class Line2NpcRaidGroupOrderPolicy
    {
        internal const int MaximumFollowers = 2;
        internal const float SpeakingRange = 14f;
        internal const float MinimumHealth = 0.40f;

        private static bool Finite(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsNaN(point.y) &&
                !float.IsNaN(point.z) &&
                !float.IsInfinity(point.x) &&
                !float.IsInfinity(point.y) &&
                !float.IsInfinity(point.z);
        }

        internal static bool MayFollow(bool authorized,
            bool sameLivingGroup, bool firsthandSpeechLineOfSight,
            bool armedWithRealFiniteItem, bool hasCurrentEnemy,
            bool underAttackOrRetreating, bool recruited,
            bool validatedStandingGoal, float healthFraction,
            Vector3 leader, Vector3 follower, Vector3 rendezvous,
            int alreadyAssigned)
        {
            if (!authorized || !sameLivingGroup ||
                !firsthandSpeechLineOfSight ||
                !armedWithRealFiniteItem || hasCurrentEnemy ||
                underAttackOrRetreating || recruited ||
                !validatedStandingGoal ||
                alreadyAssigned < 0 || alreadyAssigned >= MaximumFollowers ||
                !Finite(leader) || !Finite(follower) ||
                !Finite(rendezvous) ||
                float.IsNaN(healthFraction) ||
                float.IsInfinity(healthFraction) ||
                healthFraction < MinimumHealth || healthFraction > 1f)
                return false;
            Vector3 distance = follower - leader;
            Vector3 toward = rendezvous - follower;
            if (Math.Abs(distance.y) > 1.2f ||
                Math.Abs(toward.y) > 1.3f)
                return false;
            distance.y = 0f;
            toward.y = 0f;
            return distance.sqrMagnitude >= 0.4f * 0.4f &&
                distance.sqrMagnitude <= SpeakingRange * SpeakingRange &&
                toward.sqrMagnitude >= 2f * 2f &&
                toward.sqrMagnitude <= 38f * 38f;
        }
    }
}
