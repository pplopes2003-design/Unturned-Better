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
                RaycastHit[] hits = Physics.RaycastAll(origin, direction, 5f,
                    mask, QueryTriggerInteraction.Ignore);
                float nearest = 5f;
                foreach (RaycastHit hit in hits)
                {
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
                    Collider[] contacts = Physics.OverlapCapsule(low, high, 0.30f,
                        mask, QueryTriggerInteraction.Ignore);
                    bool occupied = false;
                    foreach (Collider contact in contacts)
                    {
                        if (contact == null ||
                            (owner != null && contact.transform.IsChildOf(owner)) ||
                            contact.bounds.max.y <= floor.point.y + 0.10f)
                            continue;
                        occupied = true;
                        break;
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
            RaycastHit[] hits = Physics.RaycastAll(eye, delta / distance,
                distance, mask, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
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
