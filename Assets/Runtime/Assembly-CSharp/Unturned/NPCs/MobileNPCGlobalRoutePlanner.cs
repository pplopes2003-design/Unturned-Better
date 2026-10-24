////////////////////////////////////////////////////////////////////////////////////////
// U3 SDK mobile survivor navigation: bounded, incremental global-horizon fallback.
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Coarse, same-surface A* fallback when the optional native ASPFP
    /// implementation is unavailable. No preauthored portals, unseen map graph,
    /// cross-floor edges or synthetic teleports. The motor validates each edge
    /// using its real CharacterController clearance and continuous floor.
    /// A 61m-wide moving planning horizon advances toward long-range goals
    /// through successive verified routes; it is not a whole-map baked navmesh.
    /// </summary>
    internal sealed class MobileNPCGlobalRoutePlanner
    {
        internal delegate bool ResolveFooting(Vector3 estimate, out Vector3 foot);
        internal delegate bool ValidateEdge(Vector3 from, Vector3 to);

        private const int Side = 21;
        private const int Center = Side / 2;
        private const int NodeCount = Side * Side;
        private const float Cell = 3.0f;
        private const int MaxExpanded = 340;
        private const int MaxWaypoints = 160;
        // Limits expensive physics across all NPCs rather than per survivor.
        private const int SharedExpansionsPerFrame = 6;
        private static int sharedBudgetFrame = -1;
        private static int sharedExpansionsRemaining;

        // 0=unseen, 1=frontier, 2=closed, 3=invalid footing.
        private readonly byte[] state = new byte[NodeCount];
        private readonly Vector3[] world = new Vector3[NodeCount];
        private readonly float[] cost = new float[NodeCount];
        private readonly int[] parent = new int[NodeCount];
        private readonly List<Vector3> reverse = new List<Vector3>(MaxWaypoints);
        private Vector3 origin;
        private Vector3 goal;
        private float startedAt;
        private float nextAttempt;
        private int expanded;
        private int bestFrontier = -1;
        private float bestFrontierScore = float.PositiveInfinity;
        private bool searching;

        internal bool IsSearching => searching;
        internal bool IsWithinBriefWaitWindow(float now)
        {
            return searching && now - startedAt <= 5f;
        }
        internal string LastOutcome { get; private set; } = "idle";
        internal int LastExpanded { get; private set; }

        internal void Reset()
        {
            searching = false;
            expanded = 0;
            bestFrontier = -1;
            nextAttempt = 0f;
        }

        private void Begin(Vector3 start, Vector3 destination, float now)
        {
            origin = start;
            goal = destination;
            startedAt = now;
            LastOutcome = "searching";
            expanded = 0;
            bestFrontier = -1;
            bestFrontierScore = float.PositiveInfinity;
            System.Array.Clear(state, 0, state.Length);
            for (int i = 0; i < NodeCount; ++i)
            {
                cost[i] = float.PositiveInfinity;
                parent[i] = -1;
            }
            int center = Center * Side + Center;
            world[center] = start;
            state[center] = 1;
            cost[center] = 0f;
            searching = true;
        }

        /// <returns>True only if output contains a fully checked route.</returns>
        internal bool Step(Vector3 position, Vector3 destination, float now,
            int frame, int agentBucket, ResolveFooting footing,
            ValidateEdge edge, List<Vector3> output)
        {
            if (output == null || footing == null || edge == null)
                return false;

            // A changing combat objective or moving planning origin cannot
            // inherit paths sampled for a different place.
            if (searching && ((position - origin).sqrMagnitude > 9f
                || (destination - goal).sqrMagnitude > 4f
                // At 30fps with 16 frame-cohorts the 18s timeout permits
                // only ~68 expansions per survivor, much less than the
                // 340-node search limit. Preserve progress across slower
                // machines while retaining an absolute time budget.
                || now - startedAt > 75f))
            {
                Reset();
                nextAttempt = now + 0.6f;
            }
            if (!searching)
            {
                if (now < nextAttempt) return false;
                Begin(position, destination, now);
            }

            // Distribute jobs by frame cohort. No planner monopolizes all
            // frames when dozens of NPCs request paths simultaneously.
            if ((frame & 15) != (agentBucket & 15))
                return false;
            if (frame != sharedBudgetFrame)
            {
                sharedBudgetFrame = frame;
                sharedExpansionsRemaining = SharedExpansionsPerFrame;
            }
            int localBudget = Mathf.Min(2, sharedExpansionsRemaining);
            for (int iteration = 0; iteration < localBudget; ++iteration)
            {
                if (sharedExpansionsRemaining <= 0) break;
                --sharedExpansionsRemaining;
                int current = SelectOpen();
                if (current < 0 || expanded >= MaxExpanded)
                    return FinishBestFrontier(now, output);
                state[current] = 2;
                ++expanded;

                Vector3 place = world[current];
                float remaining = FlatDistance(place, goal);
                float startRemaining = FlatDistance(origin, goal);
                bool atHorizon = current % Side <= 1 || current % Side >= Side - 2
                    || current / Side <= 1 || current / Side >= Side - 2;
                bool nearGoal = remaining <= Cell * 1.10f;
                // A horizon node becomes a staging point, never proof of
                // having reached the actual distant goal.
                if (current != Center * Side + Center
                    && (nearGoal || atHorizon && startRemaining - remaining > 6f))
                {
                    if (BuildPath(current, output))
                    {
                        LastExpanded = expanded;
                        LastOutcome = nearGoal ? "goal-horizon" : "outer-horizon";
                        Reset();
                        return true;
                    }
                }
                // Never accept a frontier that produces a tiny looping hop
                // back into the same blocked facade. Require meaningful
                // progress toward the actual brain destination before
                // committing an incomplete coarse route.
                if (cost[current] >= 9f && startRemaining - remaining >= 6f)
                {
                    float score = remaining + cost[current] * 0.09f;
                    if (score < bestFrontierScore)
                    {
                        bestFrontier = current;
                        bestFrontierScore = score;
                    }
                }

                // Cardinal neighbours only: no diagonal squeezes through
                // fence corners, door jambs or adjacent grid obstacles.
                int x = current % Side;
                int z = current / Side;
                for (int dir = 0; dir < 4; ++dir)
                {
                    int nx = x + (dir == 0 ? -1 : dir == 1 ? 1 : 0);
                    int nz = z + (dir == 2 ? -1 : dir == 3 ? 1 : 0);
                    if (nx < 0 || nx >= Side || nz < 0 || nz >= Side)
                        continue;
                    int next = nz * Side + nx;
                    if (state[next] == 2 || state[next] == 3)
                        continue;
                    if (state[next] == 0)
                    {
                        Vector3 estimate = origin + new Vector3(
                            (nx - Center) * Cell, 0f, (nz - Center) * Cell);
                        if (!footing(estimate, out Vector3 resolved))
                        {
                            state[next] = 3;
                            continue;
                        }
                        world[next] = resolved;
                        // Still unseen as an OPEN node until reached from
                        // an actually traversable edge.
                    }
                    if (!edge(place, world[next]))
                        continue;
                    float length = Vector3.Distance(place, world[next]);
                    float candidateCost = cost[current] + length;
                    if (candidateCost < cost[next])
                    {
                        cost[next] = candidateCost;
                        parent[next] = current;
                        state[next] = 1;
                    }
                }
            }
            return false;
        }

        private int SelectOpen()
        {
            int best = -1;
            float estimate = float.PositiveInfinity;
            for (int i = 0; i < NodeCount; ++i)
            {
                if (state[i] != 1) continue;
                float value = cost[i] + FlatDistance(world[i], goal);
                if (value < estimate)
                {
                    estimate = value;
                    best = i;
                }
            }
            return best;
        }

        private bool FinishBestFrontier(float now, List<Vector3> output)
        {
            bool result = bestFrontier >= 0
                && FlatDistance(origin, world[bestFrontier]) >= 4.5f
                && FlatDistance(origin, goal) - FlatDistance(world[bestFrontier], goal) >= 6f
                && BuildPath(bestFrontier, output);
            LastExpanded = expanded;
            LastOutcome = result ? "verified-frontier" : "no-connected-route";
            Reset();
            if (!result) nextAttempt = now + 4f;
            return result;
        }

        private bool BuildPath(int endpoint, List<Vector3> output)
        {
            reverse.Clear();
            int start = Center * Side + Center;
            int cursor = endpoint;
            while (cursor != start && cursor >= 0 && reverse.Count < MaxWaypoints)
            {
                reverse.Add(world[cursor]);
                cursor = parent[cursor];
            }
            if (cursor != start || reverse.Count == 0)
                return false;
            output.Clear();
            for (int i = reverse.Count - 1; i >= 0; --i)
                output.Add(reverse[i]);
            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
