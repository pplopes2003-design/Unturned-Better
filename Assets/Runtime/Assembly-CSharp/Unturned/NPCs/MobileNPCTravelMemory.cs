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
        // Per-survivor reusable DFS workspaces: no per-tick array garbage for
        // 20-40 simultaneous actors evaluating previously visited entrances.
        private readonly int[] accessSearchCandidate = new int[4];
        private readonly int[] accessSearchBest = new int[4];
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
            List<Vector3> continuationOutput, out Vector3 entrance,
            out int routeIndex, out int continuationIndex)
        {
            entrance = Vector3.zero;
            routeIndex = -1;
            continuationIndex = -1;
            if (!CanUseMemory() || output == null || continuationOutput == null
                || !Valid(currentPosition) || !Valid(finalDestination)
                || Mathf.Abs(finalDestination.y - currentPosition.y) < 1.2f)
                return false;

            float bestScore = float.NegativeInfinity;
            int selectedEntranceIndex = -1;
            int selectedContinuationIndex = -1;
            for (int i = 0; i < routes.Count; ++i)
            {
                LearnedRoute record = routes[i];
                if (record == null || record.footsteps == null
                    || record.footsteps.Count < 3 || record.failedReuses >= 3)
                    continue;
                int stairEntranceIndex = FindObservedFloorAccessEntranceIndex(record);
                if (stairEntranceIndex < 0)
                    continue;
                Vector3 observedEntrance = record.footsteps[stairEntranceIndex];
                // Route's entrance must be on the NPC's CURRENT floor and its
                // recorded end on the destination's floor. The opposite
                // direction is not silently assumed to have been experienced.
                if (Mathf.Abs(record.start.y - currentPosition.y) > 0.85f
                    || Mathf.Abs(record.destination.y - finalDestination.y) > 0.85f
                    || Mathf.Abs(record.destination.y - record.start.y) < 1.2f)
                    continue;
                float distanceToEntrance = Vector3.Distance(currentPosition, observedEntrance);
                float exitToGoal = Vector3.Distance(finalDestination,
                    record.footsteps[record.footsteps.Count - 1]);
                // This stage targets entry paths from another room, not
                // arbitrary navigation across an entire unfamiliar map.
                // Do not discard an authentic known stair just because the
                // survivor has already approached within 2.5m of its entry.
                // The motor still requires an exact physical approach path,
                // even for a close entrance; avoid a zero-length route here.
                if (distanceToEntrance < 0.8f || distanceToEntrance > 25f
                    || Mathf.Abs(observedEntrance.y - currentPosition.y) > 0.85f)
                    continue;

                // A separate *previously traversed* same-floor route can
                // continue beyond the known stair landing into another room.
                // This is a real two-record connection, not a guessed shortcut
                // across an upper-floor wall. Actual geometry is motor-verified.
                int possibleContinuation = -1;
                if (exitToGoal > 1.9f)
                {
                    possibleContinuation = FindObservedContinuationRoute(
                        i, record.footsteps[record.footsteps.Count - 1], finalDestination);
                    if (possibleContinuation < 0)
                        continue;
                }
                float score = record.successes * 3f
                    - record.failedReuses * 5f - distanceToEntrance * 0.15f
                    - exitToGoal * 0.15f;
                if (possibleContinuation >= 0)
                {
                    LearnedRoute nextRecord = routes[possibleContinuation];
                    score += nextRecord.successes * 1.5f
                        - nextRecord.failedReuses * 4f;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    routeIndex = i;
                    selectedEntranceIndex = stairEntranceIndex;
                    selectedContinuationIndex = possibleContinuation;
                }
            }

            if (routeIndex < 0 || selectedEntranceIndex < 0)
                return false;
            LearnedRoute learned = routes[routeIndex];
            entrance = learned.footsteps[selectedEntranceIndex];
            output.Clear();
            for (int i = selectedEntranceIndex; i < learned.footsteps.Count; ++i)
                output.Add(learned.footsteps[i]);
            continuationOutput.Clear();
            if (selectedContinuationIndex >= 0)
            {
                LearnedRoute connection = routes[selectedContinuationIndex];
                continuationOutput.AddRange(connection.footsteps);
                continuationIndex = selectedContinuationIndex;
            }
            return true;
        }

        /// <summary>
        /// A learned upper-floor corridor is eligible as a second stage ONLY
        /// when it was physically traversed by this same living survivor.
        /// No reverse link or room-to-room connection is inferred from XZ.
        /// </summary>
        private int FindObservedContinuationRoute(int firstRouteIndex, Vector3 exit,
            Vector3 finalGoal)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < routes.Count; ++i)
            {
                if (i == firstRouteIndex)
                    continue;
                LearnedRoute record = routes[i];
                if (record == null || record.footsteps == null
                    || record.footsteps.Count < 3 || record.failedReuses >= 3)
                    continue;
                float entryGap = Vector3.Distance(exit, record.footsteps[0]);
                float goalGap = Vector3.Distance(finalGoal,
                    record.footsteps[record.footsteps.Count - 1]);
                if (entryGap > 2.0f || goalGap > 1.9f
                    || Mathf.Abs(record.start.y - exit.y) > 0.7f
                    || Mathf.Abs(record.destination.y - finalGoal.y) > 0.7f
                    || Mathf.Abs(record.destination.y - record.start.y) > 0.85f)
                    continue;
                float score = record.successes * 3f
                    - record.failedReuses * 4f - entryGap - goalGap;
                if (score > bestScore)
                {
                    best = i;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>
        /// Derive an observed stair access from ACTUAL footsteps, rather than
        /// treating the arbitrary starting room of the old journey as the
        /// staircase entrance. Return the last near-floor sample before the
        /// recorded ascent/descent. No procedural new connector is created.
        /// </summary>
        private static int FindObservedFloorAccessEntranceIndex(LearnedRoute route)
        {
            if (route == null || route.footsteps == null || route.footsteps.Count < 3)
                return -1;
            float elevationChange = route.destination.y - route.start.y;
            if (Mathf.Abs(elevationChange) < 1.2f)
                return -1;
            float direction = elevationChange > 0f ? 1f : -1f;
            float baseY = route.footsteps[0].y;
            for (int i = 2; i < route.footsteps.Count; ++i)
            {
                if ((route.footsteps[i].y - baseY) * direction < 0.65f)
                    continue;
                int entranceIndex = i - 2;
                if (route.footsteps.Count - entranceIndex < 3
                    || Mathf.Abs(route.footsteps[entranceIndex].y - baseY) > 0.55f)
                    return -1;
                return entranceIndex;
            }
            return -1;
        }

        // A personal, directed graph of completed journeys. It is intentionally
        // limited to the eight routes already stored by this survivor. No global
        // room graph, unseen door edges, inferred reverse journeys or teleports.
        public const int MaxAccessChainRoutes = 4;
        public const int MaxAccessChainFootsteps = MaxAccessChainRoutes * MaxFootsteps;

        /// <summary>
        /// Choose up to four directed first-hand journeys, beginning at an
        /// observed floor-changing entry and continuing through observed
        /// corridors or additional staircases. The caller MUST physically plan
        /// an approach to entrance, and verify every edge (including small
        /// inter-record gaps) before setting movement waypoints.
        /// Excluded entries have failed recent approaches or physical checks;
        /// retries can select an alternative known stair without forgetting it.
        /// </summary>
        public bool TryFindKnownAccessChainServer(Vector3 currentPosition,
            Vector3 finalDestination, List<Vector3> output,
            List<int> outputRouteIndices, List<int> outputStageStarts,
            out Vector3 entrance, int excludedEntryMask = 0)
        {
            entrance = Vector3.zero;
            if (!CanUseMemory() || output == null || outputRouteIndices == null
                || outputStageStarts == null || !Valid(currentPosition)
                || !Valid(finalDestination))
                return false;

            bool changingFloors =
                Mathf.Abs(finalDestination.y - currentPosition.y) >= 1.2f;
            int[] candidate = accessSearchCandidate;
            int[] best = accessSearchBest;
            int bestDepth = 0;
            int bestFirstStep = -1;
            float bestScore = float.PositiveInfinity;

            // At most eight root entries, each with a fixed search allowance.
            // Graph search does no physics work; expensive real-world checking
            // is reserved for the one candidate subsequently used by the motor.
            for (int i = 0; i < routes.Count; ++i)
            {
                if ((excludedEntryMask & (1 << i)) != 0)
                    continue;
                LearnedRoute first = routes[i];
                if (!IsUsableGraphRecord(first))
                    continue;
                // Different-floor goals begin at personally OBSERVED stair
                // access. Same-floor multi-room goals may start at the actual
                // beginning of any successful corridor/door journey.
                int firstStep = changingFloors
                    ? FindObservedFloorAccessEntranceIndex(first) : 0;
                if (firstStep < 0)
                    continue;
                Vector3 entry = first.footsteps[firstStep];
                float approach = Vector3.Distance(currentPosition, entry);
                if (approach > 25f
                    || Mathf.Abs(entry.y - currentPosition.y) > 0.85f
                    || Mathf.Abs(first.start.y - currentPosition.y) > 0.85f)
                    continue;

                candidate[0] = i;
                int nodeBudget = 48;
                float score = approach * 0.18f
                    + (first.footsteps.Count - firstStep) * 0.035f
                    + 2.0f + first.failedReuses * 6f
                    - first.successes * 0.08f;
                SearchObservedAccessChain(1, 1 << i, first.footsteps[first.footsteps.Count - 1],
                    finalDestination, candidate, best, firstStep,
                    ref nodeBudget, score, ref bestScore, ref bestDepth,
                    ref bestFirstStep);
            }
            if (bestDepth <= 0 || bestFirstStep < 0)
                return false;

            output.Clear();
            outputRouteIndices.Clear();
            outputStageStarts.Clear();
            for (int stage = 0; stage < bestDepth; ++stage)
            {
                LearnedRoute route = routes[best[stage]];
                int start = stage == 0 ? bestFirstStep : 0;
                outputStageStarts.Add(output.Count);
                outputRouteIndices.Add(best[stage]);
                for (int point = start; point < route.footsteps.Count; ++point)
                    output.Add(route.footsteps[point]);
            }
            if (output.Count == 0 || output.Count > MaxAccessChainFootsteps)
            {
                output.Clear();
                outputRouteIndices.Clear();
                outputStageStarts.Clear();
                return false;
            }
            entrance = output[0];
            return true;
        }

        private void SearchObservedAccessChain(int depth, int usedMask,
            Vector3 lastExit, Vector3 goal, int[] candidate, int[] best,
            int firstStep, ref int remaining, float score, ref float bestScore,
            ref int bestDepth, ref int bestFirstStep)
        {
            if (--remaining < 0)
                return;
            float finishGap = Vector3.Distance(lastExit, goal);
            if (finishGap <= 1.9f && Mathf.Abs(lastExit.y - goal.y) <= 0.8f)
            {
                float completedScore = score + finishGap;
                if (completedScore < bestScore)
                {
                    bestScore = completedScore;
                    bestDepth = depth;
                    bestFirstStep = firstStep;
                    for (int j = 0; j < depth; ++j)
                        best[j] = candidate[j];
                }
                return;
            }
            if (depth >= MaxAccessChainRoutes)
                return;

            for (int i = 0; i < routes.Count && remaining > 0; ++i)
            {
                if ((usedMask & (1 << i)) != 0)
                    continue;
                LearnedRoute next = routes[i];
                if (!IsUsableGraphRecord(next))
                    continue;
                float gap = Vector3.Distance(lastExit, next.footsteps[0]);
                if (gap > 2.0f || Mathf.Abs(lastExit.y - next.start.y) > 0.7f)
                    continue;

                candidate[depth] = i;
                float nextScore = score + 2.0f + gap
                    + next.footsteps.Count * 0.035f
                    + next.failedReuses * 6f - next.successes * 0.08f;
                SearchObservedAccessChain(depth + 1, usedMask | (1 << i),
                    next.footsteps[next.footsteps.Count - 1], goal, candidate,
                    best, firstStep, ref remaining, nextScore, ref bestScore,
                    ref bestDepth, ref bestFirstStep);
            }
        }

        private static bool IsUsableGraphRecord(LearnedRoute route)
        {
            return route != null && route.footsteps != null
                && route.footsteps.Count >= 3
                && route.footsteps.Count <= MaxFootsteps
                && route.failedReuses < 3;
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
                // A save is untrusted historical geometry. Apply the same
                // invariants as first-hand observation before it can become a
                // candidate for stair/room access after loading.
                if (Vector3.Distance(route.start, route.footsteps[0]) > 0.35f
                    || Vector3.Distance(route.destination,
                        route.footsteps[route.footsteps.Count - 1]) > 2f)
                    continue;
                bool valid = true;
                float walked = 0f;
                for (int j = 0; j < route.footsteps.Count; ++j)
                {
                    if (!Valid(route.footsteps[j]))
                    {
                        valid = false;
                        break;
                    }
                    if (j > 0)
                    {
                        float step = Vector3.Distance(route.footsteps[j - 1],
                            route.footsteps[j]);
                        if (step < 0.01f || step > 2.6f)
                        {
                            valid = false;
                            break;
                        }
                        walked += step;
                    }
                }
                if (valid && walked >= 3.5f && walked <= 80f)
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
