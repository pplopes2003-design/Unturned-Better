using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// The exact Line1 idea of moving a radio-equipped groupmate toward
    /// an approximate observed enemy SECTOR, not to an invisible live target.
    /// Chooses a conservative local waypoint only; Steam collision + native
    /// path planner must independently verify it.
    /// </summary>
    internal static class Line2NpcRadioReinforcementPolicy
    {
        internal static bool TryPropose(bool authorized, bool hostile,
            bool activeCombat, bool underFire, bool busy, bool recruited,
            Vector3 current, Vector3 reportedSector, out Vector3 localGoal)
        {
            localGoal = Vector3.zero;
            if (!authorized || !hostile || activeCombat || underFire ||
                busy || recruited || !Finite(current) ||
                !Finite(reportedSector) ||
                Math.Abs(current.y - reportedSector.y) > 1.25f)
                return false;
            Vector3 direction = reportedSector - current;
            direction.y = 0f;
            float distance = direction.magnitude;
            if (distance < 14f || distance > 90f)
                return false;
            // Group radio is a coarse 20m sector and grants neither a
            // player target nor precise location. Move no more than 12m
            // per radio report, preserving ordinary line-of-sight combat.
            localGoal = current + direction / distance *
                Mathf.Min(12f, Mathf.Max(6f, distance * 0.35f));
            localGoal.y = current.y;
            return true;
        }

        private static bool Finite(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsNaN(point.y) &&
                !float.IsNaN(point.z) && !float.IsInfinity(point.x) &&
                !float.IsInfinity(point.y) && !float.IsInfinity(point.z);
        }
    }
}
