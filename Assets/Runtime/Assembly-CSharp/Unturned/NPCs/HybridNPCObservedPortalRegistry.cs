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
        }
        private static readonly List<Passage> entries = new List<Passage>(64);
        private const float Lifetime = 180f;
        private const int Capacity = 64;

        internal static void Reset() { entries.Clear(); }

        internal static void Remember(Vector3 near, Vector3 far)
        {
            for (int i = entries.Count - 1; i >= 0; --i)
            {
                if (Time.time - entries[i].observedAt > Lifetime) entries.RemoveAt(i);
                else if ((entries[i].near - near).sqrMagnitude < 0.5f &&
                    (entries[i].far - far).sqrMagnitude < 0.5f)
                {
                    entries[i] = new Passage { near = near, far = far, observedAt = Time.time };
                    return;
                }
            }
            if (entries.Count >= Capacity) entries.RemoveAt(0);
            entries.Add(new Passage { near = near, far = far, observedAt = Time.time });
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
            // Only one checked candidate per request to bound CPU cost.
            int chosen = -1;
            bool reverse = false;
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < entries.Count; ++i)
            {
                Passage p = entries[i];
                if (Time.time - p.observedAt > Lifetime) continue;
                float forward = Vector3.Distance(start, p.near) +
                    Vector3.Distance(goal, p.far);
                float backward = Vector3.Distance(start, p.far) +
                    Vector3.Distance(goal, p.near);
                float score = Mathf.Min(forward, backward);
                if (score < lowest && score <= config.maxSearchRadius * 1.75f)
                {
                    lowest = score;
                    chosen = i;
                    reverse = backward < forward;
                }
            }
            if (chosen < 0) return false;
            Passage chosenPassage = entries[chosen];
            Vector3 entry = reverse ? chosenPassage.far : chosenPassage.near;
            Vector3 exit = reverse ? chosenPassage.near : chosenPassage.far;
            var first = new List<Vector3>();
            var transition = new List<Vector3>();
            var last = new List<Vector3>();
            if (!HybridNPCGridPlanner.TryFindPath(start, entry, config, first)
                || !HybridNPCGridPlanner.TryFindPath(entry, exit, config, transition)
                || !HybridNPCGridPlanner.TryFindPath(exit, goal, config, last))
                return false;
            // Reject a detour masquerading as a doorway crossing.
            float crossingLength = 0f;
            for (int i = 1; i < transition.Count; ++i)
                crossingLength += Vector3.Distance(transition[i - 1], transition[i]);
            if (crossingLength > Mathf.Max(2.5f, Vector3.Distance(entry, exit) * 1.6f))
                return false;
            Append(output, first);
            Append(output, transition);
            Append(output, last);
            return output.Count >= 2;
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
