using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Scene-specific bridge between exterior and interior regions.
    /// Portals are logical links across actual openings, NOT physical doors.
    /// Populate regions/links from verified map geometry or an authoring pass.
    /// Do not assume that LevelNavigation bounds describe interior walkability.
    /// </summary>
    public sealed class HybridNPCRegionNavigator
    {
        public struct Portal
        {
            public int regionA;
            public int regionB;
            public Vector3 entryA;
            public Vector3 entryB;
            public float extraCost;
        }

        private sealed class Region
        {
            public int id;
            public Bounds bounds;
        }

        private readonly Dictionary<int, Region> regions = new Dictionary<int, Region>();
        private readonly List<Portal> portals = new List<Portal>();

        public void Clear()
        {
            regions.Clear();
            portals.Clear();
        }

        public void AddRegion(int id, Bounds bounds)
        {
            regions[id] = new Region { id = id, bounds = bounds };
        }

        /// <summary>
        /// Register a validated opening with one endpoint on each side.
        /// It does not imply that either endpoint can be reached.
        /// </summary>
        public bool AddPortal(int regionA, int regionB, Vector3 entryA, Vector3 entryB,
            float extraCost = 0f)
        {
            if (regionA == regionB || !regions.ContainsKey(regionA) ||
                !regions.ContainsKey(regionB) || extraCost < 0f ||
                !regions[regionA].bounds.Contains(entryA) ||
                !regions[regionB].bounds.Contains(entryB))
                return false;
            portals.Add(new Portal { regionA = regionA, regionB = regionB,
                entryA = entryA, entryB = entryB, extraCost = extraCost });
            return true;
        }

        /// <summary>
        /// Finds a sequence of physical local paths connected by portal transitions.
        /// A region may describe an interior room, floor, or exterior sector.
        /// Each transition is validated by geometry; unreachable portals are skipped.
        /// </summary>
        public bool TryRoute(Vector3 start, Vector3 goal, int startRegion, int goalRegion,
            HybridNPCGridPlanner.Settings settings, List<Vector3> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            if (!regions.ContainsKey(startRegion) || !regions.ContainsKey(goalRegion))
                return false;
            if (startRegion == goalRegion)
                return HybridNPCGridPlanner.TryFindPath(start, goal, settings, output);

            // Search states are the far sides of portals, not just region IDs:
            // this preserves alternative entrances and prevents false routes through walls.
            var states = new List<State>();
            var frontier = new List<int>();
            states.Add(new State { region = startRegion, point = start, cost = 0f,
                parent = -1, segment = new List<Vector3> { start } });
            frontier.Add(0);
            int iterations = 0;
            // A bounded shortest-path search. Portal connectivity is intentionally explicit.
            while (frontier.Count > 0 && iterations++ < 1024)
            {
                int bestIndex = 0;
                for (int i = 1; i < frontier.Count; ++i)
                    if (states[frontier[i]].cost < states[frontier[bestIndex]].cost)
                        bestIndex = i;
                int id = frontier[bestIndex];
                frontier.RemoveAt(bestIndex);
                State state = states[id];

                if (state.region == goalRegion)
                {
                    var tail = new List<Vector3>();
                    if (HybridNPCGridPlanner.TryFindPath(state.point, goal, settings, tail))
                    {
                        var pieces = new List<List<Vector3>>();
                        pieces.Add(tail);
                        int walk = id;
                        while (walk >= 0)
                        {
                            pieces.Add(states[walk].segment);
                            walk = states[walk].parent;
                        }
                        for (int p = pieces.Count - 1; p >= 0; --p)
                        {
                            foreach (Vector3 point in pieces[p])
                                if (output.Count == 0 ||
                                    (output[output.Count - 1] - point).sqrMagnitude > 0.0025f)
                                    output.Add(point);
                        }
                        return true;
                    }
                }

                for (int p = 0; p < portals.Count; p++)
                {
                    Portal link = portals[p];
                    bool forward = link.regionA == state.region;
                    if (!forward && link.regionB != state.region) continue;
                    int nextRegion = forward ? link.regionB : link.regionA;
                    Vector3 near = forward ? link.entryA : link.entryB;
                    Vector3 far = forward ? link.entryB : link.entryA;
                    List<Vector3> approach = new List<Vector3>();
                    if (!HybridNPCGridPlanner.TryFindPath(state.point, near, settings, approach))
                        continue;
                    List<Vector3> transition = new List<Vector3>();
                    if (!HybridNPCGridPlanner.TryFindPath(near, far, settings, transition))
                        continue;
                    // A crossing should actually use the opening, not detour around the building.
                    if (PathLength(transition) > Mathf.Max(2f, Vector3.Distance(near, far) * 1.7f))
                        continue;

                    float newCost = state.cost + PathLength(approach) +
                        PathLength(transition) + link.extraCost;
                    // Avoid expanding a worse revisit of the same entry into the same region.
                    bool dominated = false;
                    foreach (State existing in states)
                    {
                        if (existing.region == nextRegion &&
                            (existing.point - far).sqrMagnitude < 0.01f &&
                            existing.cost <= newCost)
                        {
                            dominated = true;
                            break;
                        }
                    }
                    if (dominated) continue;
                    approach.AddRange(transition);
                    var next = new State { region = nextRegion, point = far,
                        cost = newCost, parent = id, segment = approach };
                    states.Add(next);
                    frontier.Add(states.Count - 1);
                }
            }
            return false;
        }

        private static float PathLength(List<Vector3> path)
        {
            float result = 0f;
            for (int i = 1; i < path.Count; ++i)
                result += Vector3.Distance(path[i - 1], path[i]);
            return result;
        }

        private sealed class State
        {
            public int region;
            public Vector3 point;
            public float cost;
            public int parent;
            public List<Vector3> segment;
        }
    }
}
