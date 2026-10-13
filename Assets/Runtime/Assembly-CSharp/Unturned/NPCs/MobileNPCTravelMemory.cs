////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Private, bounded, first-hand route memory. A route is not learned because
    /// A* proposed it: the motor must have physically traversed the recorded
    /// positions and reached the requested goal. Reuse always requires a fresh
    /// collision/ground check by the motor. No faction-wide global path cache.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCHumanoidVitals))]
    public sealed class MobileNPCTravelMemory : MonoBehaviour
    {
        [Serializable]
        public sealed class LearnedRoute
        {
            public Vector3 start;
            public Vector3 destination;
            public List<Vector3> footsteps = new List<Vector3>();
            public int successes;
            public int failedReuses;
        }

        private const int MaxRoutes = 8;
        public const int MaxFootsteps = 36;
        private const float StartMatchRadius = 3.0f;
        private const float GoalMatchRadius = 2.0f;
        private readonly List<LearnedRoute> routes = new List<LearnedRoute>(MaxRoutes);
        private MobileNPCHumanoidVitals vitals;
        public int RouteCount => routes.Count;

        private void Awake()
        {
            vitals = GetComponent<MobileNPCHumanoidVitals>();
        }

        public bool RememberCompletedJourneyServer(Vector3 start, Vector3 destination,
            List<Vector3> traversedFootsteps)
        {
            if (!CanUseMemory() || !Valid(start) || !Valid(destination)
                || traversedFootsteps == null || traversedFootsteps.Count < 3
                || traversedFootsteps.Count > MaxFootsteps
                || Vector3.Distance(start, traversedFootsteps[0]) > 0.35f
                || Vector3.Distance(destination, traversedFootsteps[traversedFootsteps.Count - 1]) > 2f)
                return false;

            float walked = 0f;
            for (int i = 0; i < traversedFootsteps.Count; ++i)
            {
                if (!Valid(traversedFootsteps[i]))
                    return false;
                if (i > 0)
                {
                    float step = Vector3.Distance(traversedFootsteps[i - 1], traversedFootsteps[i]);
                    if (step < 0.01f || step > 2.6f)
                        return false;
                    walked += step;
                }
            }
            if (walked < 3.5f || walked > 80f)
                return false;

            int existing = FindMatchingRoute(start, destination);
            LearnedRoute record;
            if (existing >= 0)
            {
                record = routes[existing];
                record.footsteps.Clear();
            }
            else
            {
                if (routes.Count >= MaxRoutes)
                {
                    // Retire the least useful individual experience, not any
                    // other survivor's private knowledge.
                    int worst = 0;
                    int worstScore = int.MaxValue;
                    for (int i = 0; i < routes.Count; ++i)
                    {
                        int score = routes[i].successes * 2 - routes[i].failedReuses * 3;
                        if (score < worstScore)
                        {
                            worstScore = score;
                            worst = i;
                        }
                    }
                    routes.RemoveAt(worst);
                }
                record = new LearnedRoute();
                routes.Add(record);
            }

            record.start = start;
            record.destination = destination;
            record.footsteps.AddRange(traversedFootsteps);
            record.successes = Mathf.Min(20, record.successes + 1);
            record.failedReuses = 0;
            return true;
        }

        /// <summary>
        /// Return the stored candidate only. Physical reachability is NOT
        /// granted by memory; the motor verifies each segment before using it.
        /// </summary>
        public bool TryFindCandidateServer(Vector3 currentPosition, Vector3 destination,
            List<Vector3> output, out int routeIndex)
        {
            routeIndex = -1;
            if (!CanUseMemory() || output == null || !Valid(currentPosition) || !Valid(destination))
                return false;

            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < routes.Count; ++i)
            {
                LearnedRoute record = routes[i];
                if (record == null || record.footsteps == null || record.footsteps.Count < 3
                    || record.failedReuses >= 3)
                    continue;
                float startDistance = Vector3.Distance(currentPosition, record.start);
                float goalDistance = Vector3.Distance(destination, record.destination);
                if (startDistance > StartMatchRadius || goalDistance > GoalMatchRadius
                    || Mathf.Abs(currentPosition.y - record.start.y) > 0.85f
                    || Mathf.Abs(destination.y - record.destination.y) > 0.85f)
                    continue;
                float score = record.successes * 2f - record.failedReuses * 4f
                    - startDistance - goalDistance;
                if (score > bestScore)
                {
                    bestScore = score;
                    routeIndex = i;
                }
            }
            if (routeIndex < 0)
                return false;

            output.Clear();
            output.AddRange(routes[routeIndex].footsteps);
            return true;
        }

        /// <summary>
        /// Find a known floor-connecting JOURNEY whose original entrance is
        /// farther away than ordinary same-start route reuse. This only
        /// proposes a personally observed entrance and its actual traveled
        /// steps. The motor must separately discover a physical path to the
        /// entrance and recheck every connector segment and the final goal.
        /// Never infer a staircase solely from two stacked floor positions.
        /// </summary>
        public bool TryFindKnownFloorAccessServer(Vector3 currentPosition,
            Vector3 finalDestination, List<Vector3> output,
            out Vector3 entrance, out int routeIndex)
        {
            entrance = Vector3.zero;
            routeIndex = -1;
            if (!CanUseMemory() || output == null
                || !Valid(currentPosition) || !Valid(finalDestination)
                || Mathf.Abs(finalDestination.y - currentPosition.y) < 1.2f)
                return false;

            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < routes.Count; ++i)
            {
                LearnedRoute record = routes[i];
                if (record == null || record.footsteps == null
                    || record.footsteps.Count < 3 || record.failedReuses >= 3)
                    continue;
                // Route's entrance must be on the NPC's CURRENT floor and its
                // recorded end on the destination's floor. The opposite
                // direction is not silently assumed to have been experienced.
                if (Mathf.Abs(record.start.y - currentPosition.y) > 0.85f
                    || Mathf.Abs(record.destination.y - finalDestination.y) > 0.85f
                    || Mathf.Abs(record.destination.y - record.start.y) < 1.2f)
                    continue;
                float distanceToEntrance = Vector3.Distance(currentPosition, record.start);
                float exitToGoal = Vector3.Distance(finalDestination,
                    record.footsteps[record.footsteps.Count - 1]);
                // This stage targets entry paths from another room, not
                // arbitrary navigation across an entire unfamiliar map.
                if (distanceToEntrance < 2.5f || distanceToEntrance > 25f
                    || exitToGoal > 1.9f
                    || Mathf.Abs(record.footsteps[0].y - currentPosition.y) > 0.85f)
                    continue;
                float score = record.successes * 3f
                    - record.failedReuses * 5f - distanceToEntrance * 0.15f
                    - exitToGoal;
                if (score > bestScore)
                {
                    bestScore = score;
                    routeIndex = i;
                }
            }

            if (routeIndex < 0)
                return false;
            LearnedRoute learned = routes[routeIndex];
            entrance = learned.footsteps[0];
            output.Clear();
            output.AddRange(learned.footsteps);
            return true;
        }

        public void ReportReuseFailureServer(int index)
        {
            if (!CanUseMemory() || index < 0 || index >= routes.Count)
                return;
            routes[index].failedReuses = Mathf.Min(3, routes[index].failedReuses + 1);
        }

        public List<LearnedRoute> ExportSnapshotServer()
        {
            List<LearnedRoute> snapshot = new List<LearnedRoute>();
            if (!CanUseMemory())
                return snapshot;
            for (int i = 0; i < routes.Count; ++i)
                snapshot.Add(Copy(routes[i]));
            return snapshot;
        }

        public void RestoreSnapshotServer(List<LearnedRoute> snapshot)
        {
            if (!CanUseMemory())
                return;
            routes.Clear();
            if (snapshot == null)
                return;
            for (int i = 0; i < snapshot.Count && routes.Count < MaxRoutes; ++i)
            {
                LearnedRoute route = snapshot[i];
                if (route == null || route.footsteps == null
                    || route.footsteps.Count < 3 || route.footsteps.Count > MaxFootsteps
                    || !Valid(route.start) || !Valid(route.destination))
                    continue;
                bool valid = true;
                for (int j = 0; j < route.footsteps.Count; ++j)
                {
                    if (!Valid(route.footsteps[j])
                        || (j > 0 && Vector3.Distance(route.footsteps[j - 1],
                            route.footsteps[j]) > 2.6f))
                    {
                        valid = false;
                        break;
                    }
                }
                if (valid)
                    routes.Add(Copy(route));
            }
        }

        public void ForgetOnDeathServer()
        {
            if (Provider.isServer)
                routes.Clear();
        }

        private int FindMatchingRoute(Vector3 start, Vector3 goal)
        {
            for (int i = 0; i < routes.Count; ++i)
                if (Vector3.Distance(routes[i].start, start) <= 2.5f
                    && Vector3.Distance(routes[i].destination, goal) <= 1.5f)
                    return i;
            return -1;
        }

        private static LearnedRoute Copy(LearnedRoute source)
        {
            return new LearnedRoute
            {
                start = source.start,
                destination = source.destination,
                successes = Mathf.Clamp(source.successes, 1, 20),
                failedReuses = Mathf.Clamp(source.failedReuses, 0, 3),
                footsteps = new List<Vector3>(source.footsteps)
            };
        }

        private bool CanUseMemory()
        {
            if (vitals == null)
                vitals = GetComponent<MobileNPCHumanoidVitals>();
            return Provider.isServer && vitals != null && !vitals.IsDead;
        }

        private static bool Valid(Vector3 position)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x)
                && !float.IsNaN(position.y) && !float.IsInfinity(position.y)
                && !float.IsNaN(position.z) && !float.IsInfinity(position.z)
                && Level.checkSafeIncludingClipVolumes(position);
        }
    }
}
