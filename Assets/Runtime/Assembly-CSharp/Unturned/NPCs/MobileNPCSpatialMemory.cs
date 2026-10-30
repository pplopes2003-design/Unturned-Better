using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Per-individual bounded spatial observations from short-range physics sensors.
    /// Unknown is not assumed walkable. This is NOT a baked level map.
    /// The owner can reuse sensed free corridors during combat and exploration.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCSpatialMemory : MonoBehaviour
    {
        [System.Serializable]
        public sealed class PassageSnapshot
        {
            public Vector3 entry;
            public Vector3 exit;
        }

        public List<PassageSnapshot> ExportPassagesServer()
        {
            var result = new List<PassageSnapshot>(crossed.Count);
            if (!Provider.isServer) return result;
            foreach (CrossedPassage p in crossed)
                result.Add(new PassageSnapshot { entry = p.entry, exit = p.exit });
            return result;
        }

        public void RestorePassagesServer(List<PassageSnapshot> saved)
        {
            if (!Provider.isServer) return;
            crossed.Clear();
            if (saved == null) return;
            foreach (PassageSnapshot p in saved)
            {
                if (p == null || !ValidPoint(p.entry) || !ValidPoint(p.exit)
                    || Vector3.Distance(p.entry, p.exit) > 4f)
                    continue;
                RememberObservedPassage(p.entry, p.exit);
                if (crossed.Count >= 12) break;
            }
        }

        private static bool ValidPoint(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y)
                && !float.IsInfinity(v.z);
        }

        private struct CrossedPassage
        {
            public Vector3 entry;
            public Vector3 exit;
            public float observedAt;
        }
        private readonly List<CrossedPassage> crossed = new List<CrossedPassage>(12);
        private readonly RaycastHit[] scanHits = new RaycastHit[24];
        private readonly Collider[] clearanceHits = new Collider[24];

        public void RememberObservedPassage(Vector3 entry, Vector3 exit)
        {
            for (int i = 0; i < crossed.Count; ++i)
            {
                if ((crossed[i].entry - entry).sqrMagnitude < 0.64f
                    && (crossed[i].exit - exit).sqrMagnitude < 0.64f)
                {
                    crossed[i] = new CrossedPassage { entry = entry, exit = exit,
                        observedAt = Time.time };
                    return;
                }
            }
            if (crossed.Count >= 12) crossed.RemoveAt(0);
            crossed.Add(new CrossedPassage { entry = entry, exit = exit,
                observedAt = Time.time });
        }

        public bool TryRouteCrossedPassage(Vector3 start, Vector3 goal,
            HybridNPCGridPlanner.Settings settings, List<Vector3> result)
        {
            for (int i = crossed.Count - 1; i >= 0; --i)
            {
                CrossedPassage p = crossed[i];
                if (Time.time - p.observedAt > 120f) continue;
                bool reverse = Vector3.Distance(start, p.exit) +
                    Vector3.Distance(goal, p.entry) <
                    Vector3.Distance(start, p.entry) + Vector3.Distance(goal, p.exit);
                Vector3 entry = reverse ? p.exit : p.entry;
                Vector3 exit = reverse ? p.entry : p.exit;
                var first = new List<Vector3>(24);
                var middle = new List<Vector3>(12);
                var last = new List<Vector3>(24);
                if (!HybridNPCGridPlanner.TryFindPath(start, entry, settings, first)
                    || !HybridNPCGridPlanner.TryFindPath(entry, exit, settings, middle)
                    || !HybridNPCGridPlanner.TryFindPath(exit, goal, settings, last))
                    continue;
                result.Clear();
                Append(first, result);
                Append(middle, result);
                Append(last, result);
                if (result.Count >= 2) return true;
            }
            return false;
        }

        private static void Append(List<Vector3> leg, List<Vector3> route)
        {
            foreach (Vector3 point in leg)
                if (route.Count == 0 ||
                    (route[route.Count - 1] - point).sqrMagnitude > 0.0025f)
                    route.Add(point);
        }

        private struct Observation
        {
            public Vector3 position;
            public bool blocked;
            public float seenAt;
        }

        private readonly Dictionary<Vector2Int, Observation> cells =
            new Dictionary<Vector2Int, Observation>(192);
        private readonly List<Vector2Int> eviction = new List<Vector2Int>(24);
        private const float Cell = 1f;
        private const float Lifetime = 120f;
        private const int MaxCells = 192;
        private float nextScanAt;
        private int scanPhase;
        private Vector2Int lastFrontierKey;
        private float lastFrontierChosenAt;
        private bool hasLastFrontier;

        private static Vector2Int Key(Vector3 position)
        {
            return new Vector2Int(Mathf.RoundToInt(position.x / Cell),
                Mathf.RoundToInt(position.z / Cell));
        }

        private void Remember(Vector3 position, bool blocked)
        {
            Vector2Int key = Key(position);
            cells[key] = new Observation
            {
                position = position,
                blocked = blocked,
                seenAt = Time.time
            };
            if (cells.Count <= MaxCells) return;
            eviction.Clear();
            foreach (KeyValuePair<Vector2Int, Observation> item in cells)
                if (Time.time - item.Value.seenAt > Lifetime)
                    eviction.Add(item.Key);
            foreach (Vector2Int stale in eviction) cells.Remove(stale);
            if (cells.Count <= MaxCells) return;
            Vector2Int oldestKey = default(Vector2Int);
            float oldestTime = float.PositiveInfinity;
            foreach (KeyValuePair<Vector2Int, Observation> item in cells)
                if (item.Value.seenAt < oldestTime)
                {
                    oldestTime = item.Value.seenAt;
                    oldestKey = item.Key;
                }
            cells.Remove(oldestKey);
        }

        /// <summary>One scan per 0.7s, 8 rays spread over two scan phases.</summary>
        public void Scan(Vector3 feet, LayerMask mask, Transform owner)
        {
            if (Time.time < nextScanAt) return;
            nextScanAt = Time.time + 0.7f;
            // The owner is actually standing here; avoid projecting a "free"
            // tile merely because a horizontal ray passed through air.
            Remember(feet, false);
            Vector3 origin = feet + Vector3.up * 0.95f;
            for (int i = 0; i < 4; ++i)
            {
                float degrees = ((i * 2 + scanPhase) * 45f);
                Vector3 direction = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
                int hitCount = Physics.RaycastNonAlloc(origin, direction, scanHits, 5f,
                    mask, QueryTriggerInteraction.Ignore);
                if (hitCount == scanHits.Length) continue;
                float nearest = 5f;
                for (int h = 0; h < hitCount; ++h)
                {
                    RaycastHit hit = scanHits[h];
                    if (hit.collider == null ||
                        (owner != null && hit.collider.transform.IsChildOf(owner))) continue;
                    if (hit.distance < nearest) nearest = hit.distance;
                }
                // A clear horizontal sightline does not prove that there is a
                // floor. Validate support, slope and standing clearance for
                // every candidate before registering navigable cells.
                for (float d = 1f; d < nearest - 0.2f; d += 1f)
                {
                    Vector3 sample = feet + direction * d;
                    RaycastHit floor;
                    if (!Physics.Raycast(sample + Vector3.up * 0.6f,
                        Vector3.down, out floor, 1.7f, mask,
                        QueryTriggerInteraction.Ignore))
                        continue;
                    if (Vector3.Angle(floor.normal, Vector3.up) > 48f ||
                        Mathf.Abs(floor.point.y - feet.y) > 0.7f)
                        continue;
                    Vector3 low = floor.point + Vector3.up * 0.45f;
                    Vector3 high = floor.point + Vector3.up * 1.4f;
                    int contactCount = Physics.OverlapCapsuleNonAlloc(low, high, 0.30f,
                        clearanceHits, mask, QueryTriggerInteraction.Ignore);
                    bool occupied = contactCount == clearanceHits.Length;
                    for (int c = 0; c < contactCount && !occupied; ++c)
                    {
                        Collider contact = clearanceHits[c];
                        if (contact == null ||
                            (owner != null && contact.transform.IsChildOf(owner)) ||
                            contact.bounds.max.y <= floor.point.y + 0.10f)
                            continue;
                        occupied = true;
                    }
                    if (!occupied) Remember(floor.point, false);
                }
                if (nearest < 5f)
                    Remember(feet + direction * nearest, true);
            }
            scanPhase = 1 - scanPhase;
        }

        /// <summary>
        /// Combat perception interface for later integration. Returns sightline
        /// occlusion only, never a hit probability or permission to fire.
        /// Caller owns target recognition, faction rules and aiming animation.
        /// </summary>
        public bool HasClearSightline(Vector3 eye, Vector3 point,
            LayerMask mask, Transform targetRoot)
        {
            Vector3 delta = point - eye;
            float distance = delta.magnitude;
            if (distance <= 0.01f) return true;
            int hitCount = Physics.RaycastNonAlloc(eye, delta / distance,
                scanHits, distance, mask, QueryTriggerInteraction.Ignore);
            if (hitCount == scanHits.Length) return false;
            for (int i = 0; i < hitCount; ++i)
            {
                RaycastHit hit = scanHits[i];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(transform) ||
                    (targetRoot != null &&
                     hit.collider.transform.IsChildOf(targetRoot)))
                    continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Query the owner's recent observation without treating unknown space
        /// as free. Tactical cover uses this only to rank physically verified
        /// destinations; it never replaces route/geometry validation.
        /// </summary>
        public bool TryGetRecentObservation(Vector3 point, out bool blocked)
        {
            blocked = false;
            Observation observation;
            if (!cells.TryGetValue(Key(point), out observation)
                || Time.time - observation.seenAt > Lifetime
                || Mathf.Abs(observation.position.y - point.y) > 0.8f)
                return false;
            blocked = observation.blocked;
            return true;
        }

        /// <summary>
        /// Choose a sensed edge of explored space for exploration behavior.
        /// Unknown cells are never treated as navigable; movement to the
        /// observed frontier still requires an ordinary verified route.
        /// </summary>
        public bool TryGetExplorationFrontier(Vector3 origin, out Vector3 frontier)
        {
            frontier = Vector3.zero;
            float best = float.NegativeInfinity;
            foreach (KeyValuePair<Vector2Int, Observation> pair in cells)
            {
                Observation known = pair.Value;
                if (known.blocked || Time.time - known.seenAt > Lifetime)
                    continue;
                float distance = Vector3.Distance(origin, known.position);
                if (distance < 2.5f || distance > 9f ||
                    Mathf.Abs(origin.y - known.position.y) > 0.7f)
                    continue;
                int unknown = 0;
                Vector2Int k = pair.Key;
                if (!cells.ContainsKey(k + Vector2Int.up)) ++unknown;
                if (!cells.ContainsKey(k + Vector2Int.down)) ++unknown;
                if (!cells.ContainsKey(k + Vector2Int.left)) ++unknown;
                if (!cells.ContainsKey(k + Vector2Int.right)) ++unknown;
                if (unknown == 0) continue;
                // Do not send the same NPC back to an identical frontier
                // every time its routine requests a new destination.
                if (hasLastFrontier && k == lastFrontierKey &&
                    Time.time - lastFrontierChosenAt < 24f) continue;
                float score = unknown * 3f - Mathf.Abs(distance - 5f);
                if (score <= best) continue;
                best = score;
                frontier = known.position;
            }
            if (best > float.NegativeInfinity)
            {
                lastFrontierKey = Key(frontier);
                lastFrontierChosenAt = Time.time;
                hasLastFrontier = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns an observed, unobstructed intermediary, never a teleport.
        /// The route provider must still verify both legs using physical A*.
        /// </summary>
        public bool TryGetObservedIntermediate(Vector3 start, Vector3 goal,
            out Vector3 intermediate)
        {
            intermediate = Vector3.zero;
            float best = float.PositiveInfinity;
            foreach (KeyValuePair<Vector2Int, Observation> item in cells)
            {
                Observation o = item.Value;
                if (o.blocked || Time.time - o.seenAt > Lifetime ||
                    Mathf.Abs(o.position.y - start.y) > 0.85f) continue;
                float a = Vector3.Distance(start, o.position);
                float b = Vector3.Distance(o.position, goal);
                if (a < 2f || a > 10f || b >= Vector3.Distance(start, goal)) continue;
                float score = a + b;
                if (score < best)
                {
                    best = score;
                    intermediate = o.position;
                }
            }
            return best < float.PositiveInfinity;
        }
    }
}
