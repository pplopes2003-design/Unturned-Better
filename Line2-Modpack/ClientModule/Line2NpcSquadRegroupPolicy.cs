using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Pure Line1-inspired bounded group rendezvous guard. Group membership
    /// grants the right to cooperate, not omniscient knowledge of members:
    /// the runtime has to verify actual visibility, safe floor, current
    /// absence of enemy contact and no high-priority movement ownership.
    /// </summary>
    internal static class Line2NpcSquadRegroupPolicy
    {
        internal const float MinRallyDistance = 12f;
        internal const float MaxRallyDistance = 42f;
        internal static bool MayRegroup(bool localAuthority,
            bool sameLivingGroup, bool lineOfSight, bool inCombat,
            bool defensiveMovement, bool recruited, bool siteSafe,
            Vector3 participant, Vector3 leader)
        {
            if (!localAuthority || !sameLivingGroup || !lineOfSight ||
                inCombat || defensiveMovement || recruited || !siteSafe)
                return false;
            if (!Finite(participant) || !Finite(leader) ||
                Math.Abs(participant.y - leader.y) > 1.0f)
                return false;
            Vector3 delta = participant - leader;
            delta.y = 0f;
            float squared = delta.sqrMagnitude;
            return squared >= MinRallyDistance * MinRallyDistance &&
                squared <= MaxRallyDistance * MaxRallyDistance;
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
