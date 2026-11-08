using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Safe acoustic-search rules for existing vanilla zombies.
    /// A real gunshot allows a bounded point-investigation request, NEVER
    /// granting the shooter as a visual target or forcing native player
    /// pursuit to give up its current player.
    /// </summary>
    internal static class Line2ZombieSoundPolicy
    {
        internal const float MaxHearingMeters = 67f;
        internal const float MaxFloorGap = 2.25f;
        internal const float MinimumMovementMeters = 0.9f;
        internal const float NoProgressSeconds = 5.0f;
        internal const int MaxRouteRevisions = 2;

        internal static bool MayInvestigate(bool localAuthority,
            bool genuineDischarge, bool zombieAlive,
            bool alreadyHunting, bool hasNativePlayerTarget,
            bool floorVerified, Vector3 zombie, Vector3 sound,
            float now, float lastAlerted)
        {
            if (!localAuthority || !genuineDischarge || !zombieAlive ||
                alreadyHunting || hasNativePlayerTarget || !floorVerified ||
                !Finite(zombie) || !Finite(sound) ||
                float.IsNaN(now) || float.IsInfinity(now) ||
                float.IsNaN(lastAlerted) || now - lastAlerted < 4f)
                return false;
            return Math.Abs(zombie.y - sound.y) <= MaxFloorGap &&
                (zombie - sound).sqrMagnitude <=
                    MaxHearingMeters * MaxHearingMeters;
        }

        internal static bool MayRedirectStalledPoint(bool localAuthority,
            bool stillHunting, bool hasNativePlayerTarget,
            bool floorVerified, bool corridorVerified,
            Vector3 zombie, Vector3 currentGoal, Vector3 saferPoint,
            float secondsSinceProgress, int previousRevisions)
        {
            if (!localAuthority || !stillHunting || hasNativePlayerTarget ||
                !floorVerified || !corridorVerified ||
                previousRevisions >= MaxRouteRevisions ||
                float.IsNaN(secondsSinceProgress) ||
                secondsSinceProgress < NoProgressSeconds ||
                !Finite(zombie) || !Finite(currentGoal) ||
                !Finite(saferPoint) ||
                Math.Abs(saferPoint.y - zombie.y) > 1.0f)
                return false;
            float shortSq = (saferPoint - zombie).sqrMagnitude;
            float farSq = (currentGoal - zombie).sqrMagnitude;
            // Vanilla Zombie.alert(Vector3) only accepts a *closer* point
            // when already hunting. Never mark a rejected proposal as a
            // successful route change or invent inaccessible waypoints.
            return shortSq >= 1.75f * 1.75f &&
                shortSq < farSq - 0.5f;
        }

        private static bool Finite(Vector3 p)
        {
            return !float.IsNaN(p.x) && !float.IsInfinity(p.x) &&
                !float.IsNaN(p.y) && !float.IsInfinity(p.y) &&
                !float.IsNaN(p.z) && !float.IsInfinity(p.z);
        }
    }
}
