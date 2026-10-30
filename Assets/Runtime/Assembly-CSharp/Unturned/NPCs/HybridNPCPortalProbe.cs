using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Probes actual openings on the boundary of a known building/room region.
    /// A doorway is a free humanoid capsule corridor, not a door GameObject.
    /// The caller supplies verified region bounds and an explicit collision mask.
    /// Run incrementally or at level setup, never for every NPC every frame.
    /// </summary>
    public static class HybridNPCPortalProbe
    {
        public struct Candidate
        {
            public Vector3 outside;
            public Vector3 inside;
            public float width;
        }

        public static int FindOpenings(Bounds interior, LayerMask geometryMask,
            float radius, float height, float spacing, float depth, float maxStep,
            float maxSlope, List<Candidate> result, int maxCandidates = 24)
        {
            if (result == null) return 0;
            result.Clear();
            if (radius <= 0f || height <= 2f * radius || spacing <= 0f ||
                depth <= radius || maxCandidates <= 0) return 0;
            // Rectangular boundary sampling is a first-pass heuristic; for
            // irregular/multistory interiors, supply per-room/per-floor bounds.
            float minX = interior.min.x, maxX = interior.max.x;
            float minZ = interior.min.z, maxZ = interior.max.z;
            float y = interior.min.y + 0.25f;
            ScanEdge(new Vector3(minX, y, minZ), new Vector3(maxX, y, minZ),
                Vector3.back, geometryMask, radius, height, spacing, depth,
                maxStep, maxSlope, result, maxCandidates);
            ScanEdge(new Vector3(minX, y, maxZ), new Vector3(maxX, y, maxZ),
                Vector3.forward, geometryMask, radius, height, spacing, depth,
                maxStep, maxSlope, result, maxCandidates);
            ScanEdge(new Vector3(minX, y, minZ), new Vector3(minX, y, maxZ),
                Vector3.left, geometryMask, radius, height, spacing, depth,
                maxStep, maxSlope, result, maxCandidates);
            ScanEdge(new Vector3(maxX, y, minZ), new Vector3(maxX, y, maxZ),
                Vector3.right, geometryMask, radius, height, spacing, depth,
                maxStep, maxSlope, result, maxCandidates);
            return result.Count;
        }

        private static void ScanEdge(Vector3 a, Vector3 b, Vector3 outward,
            LayerMask mask, float radius, float height, float spacing, float depth,
            float maxStep, float maxSlope, List<Candidate> result, int limit)
        {
            if (result.Count >= limit) return;
            float length = Vector3.Distance(a, b);
            int count = Mathf.Max(1, Mathf.CeilToInt(length / spacing));
            // Coalesce samples from a single opening into one candidate.
            bool previousWasOpen = false;
            for (int i = 0; i <= count && result.Count < limit; ++i)
            {
                Vector3 center = Vector3.Lerp(a, b, i / (float)count);
                Vector3 inside = center - outward * depth;
                Vector3 outside = center + outward * depth;
                Vector3 groundInside, groundOutside;
                bool valid = GroundAt(inside, center.y, mask, maxStep, maxSlope, out groundInside)
                    && GroundAt(outside, center.y, mask, maxStep, maxSlope, out groundOutside)
                    && Mathf.Abs(groundInside.y - groundOutside.y) <= maxStep
                    && ClearAt(groundInside, mask, radius, height)
                    && ClearAt(groundOutside, mask, radius, height)
                    && ClearCorridor(groundInside, groundOutside, mask, radius, height);
                if (valid && !previousWasOpen)
                {
                    result.Add(new Candidate { inside = groundInside,
                        outside = groundOutside, width = spacing });
                }
                previousWasOpen = valid;
            }
        }

        private static bool GroundAt(Vector3 point, float referenceY, LayerMask mask,
            float maxStep, float maxSlope, out Vector3 ground)
        {
            ground = Vector3.zero;
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(point.x, referenceY + maxStep + 0.5f, point.z),
                Vector3.down, out hit, maxStep * 2f + 1f, mask, QueryTriggerInteraction.Ignore))
                return false;
            if (Vector3.Angle(hit.normal, Vector3.up) > maxSlope) return false;
            ground = hit.point;
            return true;
        }

        private static bool ClearAt(Vector3 point, LayerMask mask, float radius, float height)
        {
            Vector3 bottom = point + Vector3.up * (radius + 0.08f);
            Vector3 top = point + Vector3.up * (height - radius - 0.02f);
            return !Physics.CheckCapsule(bottom, top, radius, mask,
                QueryTriggerInteraction.Ignore);
        }

        private static bool ClearCorridor(Vector3 from, Vector3 to, LayerMask mask,
            float radius, float height)
        {
            Vector3 delta = to - from;
            Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) return false;
            Vector3 bottom = from + Vector3.up * (radius + 0.08f);
            Vector3 top = from + Vector3.up * (height - radius - 0.02f);
            return !Physics.CapsuleCast(bottom, top, radius, flat.normalized,
                flat.magnitude, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
