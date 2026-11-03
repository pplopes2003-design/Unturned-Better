using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Bounded, geometry-aware fallback planner for mobile humanoids.
    /// Independent from WITH_ASPFP and deliberately does NOT modify zombie pathfinding.
    /// Works on the main Unity thread: physics queries must not be called from worker threads.
    /// A global region/portal graph can use this as its local edge validator.
    /// </summary>
    public static class HybridNPCGridPlanner
    {
        // Diagnostics describe the most recent synchronous planning request.
        // Read immediately after TryFindPath on the same Unity main thread.
        public static string LastFailureReason { get; private set; } = "not_started";
        [Serializable]
        public struct Settings
        {
            public float cellSize;
            public float agentRadius;
            public float agentHeight;
            public float maxStepUp;
            public float maxStepDown;
            public float maxSlopeDegrees;
            public float maxSearchRadius;
            public int maxExpandedNodes;
            public LayerMask collisionMask;
            // Exclude the NPC's own rig colliders, regardless of child layer.
            public Transform ignoredRoot;

            public static Settings Default
            {
                get
                {
                    return new Settings
                    {
                        cellSize = 0.75f,
                        agentRadius = 0.30f,
                        agentHeight = 1.75f,
                        maxStepUp = 0.42f,
                        maxStepDown = 0.55f,
                        maxSlopeDegrees = 48f,
                        maxSearchRadius = 35f,
                        maxExpandedNodes = 2800,
                        collisionMask = Physics.DefaultRaycastLayers
                    };
                }
            }
        }

        private struct Key : IEquatable<Key>
        {
            public int x, z, level;
            public Key(int x, int z, int level) { this.x = x; this.z = z; this.level = level; }
            public bool Equals(Key other) { return x == other.x && z == other.z && level == other.level; }
            public override bool Equals(object obj) { return obj is Key && Equals((Key)obj); }
            public override int GetHashCode()
            {
                unchecked { return ((x * 397) ^ z) * 397 ^ level; }
            }
        }

        private sealed class Node
        {
            public Key key;
            public Vector3 point;
            public float g;
            public float h;
            public Node previous;
            public bool closed;
        }

        // Unity physics queries run on the gameplay thread. Reuse buffers to
        // avoid per-edge garbage collection when several NPCs plan together.
        private static readonly Collider[] OccupancyHits = new Collider[48];
        private static readonly RaycastHit[] MovementHits = new RaycastHit[48];
        private static readonly RaycastHit[] GroundHits = new RaycastHit[48];

        private static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] StepZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// Returns world-space ground waypoints, including start and the validated destination
        /// (or nearest reachable point when allowPartial is true). Never reports success
        /// solely because the destination is inside a navmesh bounding box.
        /// </summary>
        public static bool TryFindPath(Vector3 start, Vector3 destination, Settings settings,
            List<Vector3> output, bool allowPartial = false)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            LastFailureReason = "invalid_configuration";
            if (settings.cellSize <= 0f || settings.agentHeight <= 2f * settings.agentRadius
                || settings.maxExpandedNodes <= 0 || settings.maxSearchRadius <= 0f)
                return false;
            if (Vector3.Distance(start, destination) > settings.maxSearchRadius)
            {
                LastFailureReason = "destination_outside_local_radius";
                return false;
            }

            Vector3 groundStart;
            if (!TryGround(start, start.y, settings, out groundStart))
            {
                LastFailureReason = "start_ground_not_found";
                return false;
            }
            if (!IsClear(groundStart, settings))
            {
                LastFailureReason = "start_capsule_blocked";
                return false;
            }

            var nodes = new Dictionary<Key, Node>();
            var open = new List<Node>();
            var origin = new Vector2(groundStart.x, groundStart.z);
            var first = new Node
            {
                key = new Key(0, 0, LevelOf(groundStart.y, settings)),
                point = groundStart,
                h = Heuristic(groundStart, destination)
            };
            nodes.Add(first.key, first);
            open.Add(first);
            Node best = first;
            int expanded = 0;

            while (open.Count > 0 && expanded < settings.maxExpandedNodes)
            {
                int index = 0;
                float lowest = open[0].g + open[0].h;
                for (int i = 1; i < open.Count; i++)
                {
                    float f = open[i].g + open[i].h;
                    if (f < lowest) { index = i; lowest = f; }
                }
                Node current = open[index];
                open.RemoveAt(index);
                if (current.closed) continue;
                current.closed = true;
                expanded++;
                if (current.h < best.h) best = current;

                if (Vector2.Distance(new Vector2(current.point.x, current.point.z),
                    new Vector2(destination.x, destination.z)) <= settings.cellSize * 0.75f
                    && Mathf.Abs(current.point.y - destination.y) <= settings.maxStepUp + settings.maxStepDown)
                {
                    Vector3 targetGround;
                    if (TryGround(destination, current.point.y, settings, out targetGround)
                        && IsClear(targetGround, settings) && CanTraverse(current.point, targetGround, settings))
                    {
                        Reconstruct(current, output);
                        output.Add(targetGround);
                        LastFailureReason = "success";
                        return true;
                    }
                }

                for (int d = 0; d < 8; d++)
                {
                    int nx = current.key.x + StepX[d];
                    int nz = current.key.z + StepZ[d];
                    Vector2 flat = origin + settings.cellSize * new Vector2(nx, nz);
                    if (Vector2.Distance(flat, origin) > settings.maxSearchRadius) continue;
                    Vector3 requested = new Vector3(flat.x, current.point.y, flat.y);
                    Vector3 surface;
                    if (!TryGround(requested, current.point.y, settings, out surface)) continue;
                    if (!IsClear(surface, settings) || !CanTraverse(current.point, surface, settings))
                        continue;
                    // Prevent cutting diagonal corners through narrow walls.
                    if (StepX[d] != 0 && StepZ[d] != 0)
                    {
                        Vector3 sideA, sideB;
                        Vector3 askA = new Vector3(current.point.x + StepX[d] * settings.cellSize,
                            current.point.y, current.point.z);
                        Vector3 askB = new Vector3(current.point.x, current.point.y,
                            current.point.z + StepZ[d] * settings.cellSize);
                        if (!TryGround(askA, current.point.y, settings, out sideA)
                            || !TryGround(askB, current.point.y, settings, out sideB)
                            || !IsClear(sideA, settings) || !IsClear(sideB, settings)
                            || !CanTraverse(current.point, sideA, settings)
                            || !CanTraverse(current.point, sideB, settings))
                            continue;
                    }

                    Key key = new Key(nx, nz, LevelOf(surface.y, settings));
                    Node next;
                    if (!nodes.TryGetValue(key, out next))
                    {
                        next = new Node { key = key, point = surface, g = float.PositiveInfinity };
                        nodes.Add(key, next);
                    }
                    float score = current.g + Vector3.Distance(current.point, surface);
                    if (next.closed || score >= next.g) continue;
                    next.g = score;
                    next.h = Heuristic(surface, destination);
                    next.previous = current;
                    next.point = surface;
                    if (!open.Contains(next)) open.Add(next);
                }
            }
            if (!allowPartial || best == first)
            {
                LastFailureReason = expanded >= settings.maxExpandedNodes
                    ? "node_budget_exhausted" : "no_reachable_corridor";
                return false;
            }
            Reconstruct(best, output);
            LastFailureReason = output.Count > 1 ? "partial_frontier" : "no_reachable_corridor";
            return output.Count > 1;
        }

        private static int LevelOf(float y, Settings s)
        {
            // Distinguishes stacked walkable floors at identical x/z coordinates.
            return Mathf.RoundToInt(y / Mathf.Max(0.1f, s.maxStepUp + s.maxStepDown));
        }

        private static float Heuristic(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(a, b);
        }

        private static bool TryGround(Vector3 point, float referenceY, Settings s, out Vector3 ground)
        {
            ground = Vector3.zero;
            float above = s.maxStepUp + 0.12f;
            float distance = above + s.maxStepDown + 0.12f;
            Vector3 rayOrigin = new Vector3(point.x, referenceY + above, point.z);
            int count = Physics.RaycastNonAlloc(rayOrigin, Vector3.down,
                GroundHits, distance, s.collisionMask, QueryTriggerInteraction.Ignore);
            if (count == GroundHits.Length) return false;
            bool found = false;
            float highestGround = float.NegativeInfinity;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = GroundHits[i];
                if (hit.collider == null || (s.ignoredRoot != null
                    && hit.collider.transform.IsChildOf(s.ignoredRoot)))
                    continue;
                if (Vector3.Angle(hit.normal, Vector3.up) > s.maxSlopeDegrees)
                    continue;
                float delta = hit.point.y - referenceY;
                if (delta > s.maxStepUp + 0.02f || delta < -s.maxStepDown - 0.02f)
                    continue;
                // Choose highest physically supported floor, not an arbitrary
                // unsorted hit; keep upper/lower stories independent.
                if (!found || hit.point.y > highestGround)
                {
                    found = true;
                    highestGround = hit.point.y;
                    ground = hit.point;
                }
            }
            return found;
        }

        private static bool IsClear(Vector3 feet, Settings s)
        {
            Vector3 lower = feet + Vector3.up * (s.agentRadius + 0.07f);
            Vector3 upper = feet + Vector3.up * (s.agentHeight - s.agentRadius - 0.04f);
            // CheckCapsule cannot distinguish this agent's own equipment/rig
            // colliders from level geometry, causing false blocked starts.
            int count = Physics.OverlapCapsuleNonAlloc(lower, upper, s.agentRadius,
                OccupancyHits, s.collisionMask, QueryTriggerInteraction.Ignore);
            // A saturated buffer cannot prove clearance safely.
            if (count == OccupancyHits.Length) return false;
            for (int i = 0; i < count; ++i)
            {
                Collider hit = OccupancyHits[i];
                if (hit == null || (s.ignoredRoot != null &&
                    hit.transform.IsChildOf(s.ignoredRoot))) continue;
                // A supporting floor may slightly touch the lower hemisphere
                // due to floating-point overlap. Walls and raised objects
                // remain blockers.
                if (hit.bounds.max.y <= feet.y + 0.10f) continue;
                return false;
            }
            return true;
        }

        private static bool CanTraverse(Vector3 from, Vector3 to, Settings s)
        {
            Vector3 flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float horizontal = flat.magnitude;
            if (horizontal < 0.001f) return Mathf.Abs(to.y - from.y) < 0.05f;
            float elevation = to.y - from.y;
            if (elevation > s.maxStepUp + 0.02f || elevation < -s.maxStepDown - 0.02f)
                return false;
            // Steep discontinuities may be steps; the motor must handle a physical step-up.
            Vector3 lower = from + Vector3.up * (s.agentRadius + 0.10f);
            Vector3 upper = from + Vector3.up * (s.agentHeight - s.agentRadius);
            Vector3 direction = flat / horizontal;
            int count = Physics.CapsuleCastNonAlloc(lower, upper, s.agentRadius,
                direction, MovementHits, horizontal, s.collisionMask,
                QueryTriggerInteraction.Ignore);
            if (count == MovementHits.Length) return false;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = MovementHits[i];
                if (hit.collider == null || (s.ignoredRoot != null &&
                    hit.collider.transform.IsChildOf(s.ignoredRoot))) continue;
                if (hit.collider.bounds.max.y <= Mathf.Min(from.y, to.y) + 0.10f)
                    continue;
                return false;
            }
            return true;
        }

        private static void Reconstruct(Node node, List<Vector3> output)
        {
            var reversed = new List<Vector3>();
            for (Node cursor = node; cursor != null; cursor = cursor.previous)
                reversed.Add(cursor.point);
            for (int i = reversed.Count - 1; i >= 0; --i)
                output.Add(reversed[i]);
        }
    }
}
