using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// A bounded cache of portals ACTUALLY verified by MobileNPCHumanoidMotor's
    /// facade scanner (continuous floor and unobstructed standing capsule).
    /// It deliberately never invents passages from rectangles or NavMesh bounds.
    /// Cache is per live scene; callers revalidate actual physical traversability.
    /// </summary>
    internal static class HybridNPCObservedPortalRegistry
    {
        internal struct Passage
        {
            internal Vector3 near;
            internal Vector3 far;
            internal float observedAt;
            // A passage may be physically sensed but not yet traversed.
            // Only real arrival at its far side promotes it to confirmed.
            internal bool crossed;
        }
        private static readonly List<Passage> entries = new List<Passage>(64);
        private const float Lifetime = 180f;
        private const float ConfirmedLifetime = 600f;
        private const int Capacity = 64;

        internal static void Reset() { entries.Clear(); }

        internal static void Remember(Vector3 near, Vector3 far)
        {
            for (int i = entries.Count - 1; i >= 0; --i)
            {
                if (Time.time - entries[i].observedAt >
                    (entries[i].crossed ? ConfirmedLifetime : Lifetime)) entries.RemoveAt(i);
                else if ((entries[i].near - near).sqrMagnitude < 0.5f &&
                    (entries[i].far - far).sqrMagnitude < 0.5f)
                {
                    entries[i] = new Passage { near = near, far = far,
                        observedAt = Time.time, crossed = entries[i].crossed };
                    return;
                }
            }
            if (entries.Count >= Capacity) entries.RemoveAt(0);
            entries.Add(new Passage { near = near, far = far, observedAt = Time.time });
        }

        /// <summary>
        /// Share only an actually traversed doorway as confirmed topology.
        /// The registry is an advisory graph; consumers still perform full
        /// ground and capsule route validation before moving.
        /// </summary>
        internal static void ConfirmCrossing(Vector3 entry, Vector3 exit)
        {
            for (int i = 0; i < entries.Count; ++i)
            {
                Passage existing = entries[i];
                bool same = (existing.near - entry).sqrMagnitude < 0.64f
                    && (existing.far - exit).sqrMagnitude < 0.64f;
                bool opposite = (existing.near - exit).sqrMagnitude < 0.64f
                    && (existing.far - entry).sqrMagnitude < 0.64f;
                if (!same && !opposite) continue;
                existing.crossed = true;
                existing.observedAt = Time.time;
                entries[i] = existing;
                return;
            }
            if (entries.Count >= Capacity) entries.RemoveAt(0);
            entries.Add(new Passage {
                near = entry, far = exit, observedAt = Time.time, crossed = true
            });
        }

        /// <summary>
        /// Select a nearby observed crossing, then compute exact ground route
        /// to its entry and from its exit to the destination. Never cross
        /// without validating the entire portal-to-portal path again.
        /// </summary>
        internal static bool TryRoute(Vector3 start, Vector3 goal,
            HybridNPCGridPlanner.Settings config, List<Vector3> output)
        {
            output.Clear();
            // Retry several actually observed openings. One inaccessible
            // entrance must not prevent a second doorway from being used.
            const int MaxCandidates = 4;
            int[] indices = { -1, -1, -1, -1 };
            bool[] reverse = new bool[MaxCandidates];
            float[] scores = { float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity, float.PositiveInfinity };
            for (int i = 0; i < entries.Count; ++i)
            {
                Passage p = entries[i];
                if (Time.time - p.observedAt >
                    (p.crossed ? ConfirmedLifetime : Lifetime)) continue;
                float forward = Vector3.Distance(start, p.near) +
                    Vector3.Distance(goal, p.far);
                float backward = Vector3.Distance(start, p.far) +
                    Vector3.Distance(goal, p.near);
                bool backwardPreferred = backward < forward;
                // Prefer successfully traversed connections without treating them
                // as collision-free: every leg is validated below.
                float score = Mathf.Min(forward, backward) - (p.crossed ? 2f : 0f);
                if (score > config.maxSearchRadius * 1.75f) continue;
                for (int slot = 0; slot < MaxCandidates; ++slot)
                {
                    if (score >= scores[slot]) continue;
                    for (int shift = MaxCandidates - 1; shift > slot; --shift)
                    {
                        scores[shift] = scores[shift - 1];
                        indices[shift] = indices[shift - 1];
                        reverse[shift] = reverse[shift - 1];
                    }
                    scores[slot] = score;
                    indices[slot] = i;
                    reverse[slot] = backwardPreferred;
                    break;
                }
            }

            var first = new List<Vector3>(32);
            var crossing = new List<Vector3>(16);
            var last = new List<Vector3>(32);
            for (int candidate = 0; candidate < MaxCandidates; ++candidate)
            {
                if (indices[candidate] < 0) break;
                Passage p = entries[indices[candidate]];
                Vector3 entry = reverse[candidate] ? p.far : p.near;
                Vector3 exit = reverse[candidate] ? p.near : p.far;
                first.Clear();
                crossing.Clear();
                last.Clear();
                // Every leg must be grounded and navigable. A doorway is
                // never treated as a teleport or an exception to collisions.
                if (!HybridNPCGridPlanner.TryFindPath(start, entry, config, first)
                    || !HybridNPCGridPlanner.TryFindPath(entry, exit, config, crossing)
                    || !HybridNPCGridPlanner.TryFindPath(exit, goal, config, last))
                    continue;

                float crossingLength = 0f;
                for (int i = 1; i < crossing.Count; ++i)
                    crossingLength += Vector3.Distance(crossing[i - 1], crossing[i]);
                if (crossingLength > Mathf.Max(2.5f,
                    Vector3.Distance(entry, exit) * 1.6f))
                    continue;
                output.Clear();
                Append(output, first);
                Append(output, crossing);
                Append(output, last);
                if (output.Count >= 2) return true;
            }
            output.Clear();
            return false;
        }

        private static void Append(List<Vector3> target, List<Vector3> source)
        {
            foreach (Vector3 point in source)
                if (target.Count == 0 ||
                    (target[target.Count - 1] - point).sqrMagnitude > 0.0025f)
                    target.Add(point);
        }
    }
}
