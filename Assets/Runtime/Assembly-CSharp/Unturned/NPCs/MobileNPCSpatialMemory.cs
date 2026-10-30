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
            // Record footing under actual standing position, not a distant map tile.
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
                // Record only actually observed samples, not guessed passages.
                for (float d = 1f; d < nearest - 0.2f; d += 1f)
                    Remember(feet + direction * d, false);
                if (nearest < 5f)
                    Remember(feet + direction * nearest, true);
            }
            scanPhase = 1 - scanPhase;
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
