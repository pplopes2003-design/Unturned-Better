using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Common native walkable-floor candidate layers for real Steam assets.
    /// GROUND2 alone misses ordinary terrain and many LARGE/MEDIUM map
    /// building floors. STRUCTURE permits player-built floors to be sampled,
    /// but never grants passage through doors, roofs or collision volumes:
    /// each caller must still check slope, local height, capsule clearance,
    /// connected edges and native A* route availability.
    /// </summary>
    internal static class Line2NpcFloorGeometry
    {
        internal static readonly int SupportMask = RayMasks.GROUND |
            RayMasks.GROUND2 | RayMasks.LARGE | RayMasks.MEDIUM |
            RayMasks.ENVIRONMENT | RayMasks.STRUCTURE;

        /// <summary>
        /// Reject moving platforms and unrelated actors as static floor.
        /// A ray hitting a stable horizontal level-object collider can
        /// support a step; no fake NAVMESH surface is created.
        /// </summary>
        /// <summary>
        /// A single downward Raycast returns only the first ceiling, roof or
        /// loft hit. Buildings may have multiple horizontal layers above one
        /// another, so inspect a bounded native hit buffer and choose the
        /// support nearest the caller's EXPECTED floor height. Fail closed on
        /// saturation; an omitted closer obstacle cannot silently become a
        /// navigable floor.
        /// </summary>
        internal static bool TryFindStaticSupport(Vector3 estimate,
            float castAbove, float castLength, float maxHeightError,
            float minimumUpNormal, RaycastHit[] hits,
            out RaycastHit selected)
        {
            selected = default(RaycastHit);
            if (hits == null || hits.Length < 2 ||
                !IsFinite(estimate) || !IsFinite(castAbove) ||
                !IsFinite(castLength) || !IsFinite(maxHeightError) ||
                !IsFinite(minimumUpNormal) || castAbove <= 0f ||
                castLength <= 0f || castLength > 8f ||
                maxHeightError < 0f || maxHeightError > 2.5f ||
                minimumUpNormal < 0.5f || minimumUpNormal > 1f)
                return false;

            int count = Physics.RaycastNonAlloc(
                estimate + Vector3.up * castAbove, Vector3.down,
                hits, castLength, SupportMask,
                QueryTriggerInteraction.Ignore);
            if (count <= 0 || count >= hits.Length)
                return false;

            float bestError = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                RaycastHit hit = hits[i];
                if (!IsStaticFloorCandidate(hit.collider) ||
                    !IsFinite(hit.point) || !IsFinite(hit.normal.y) ||
                    hit.normal.y < minimumUpNormal)
                    continue;
                float error = Mathf.Abs(hit.point.y - estimate.y);
                if (error > maxHeightError || error >= bestError)
                    continue;
                bestError = error;
                selected = hit;
            }
            return bestError < float.PositiveInfinity;
        }

        private static bool IsFinite(float n)
        {
            return !float.IsNaN(n) && !float.IsInfinity(n);
        }

        private static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        internal static bool IsStaticFloorCandidate(Collider candidate)
        {
            return candidate != null &&
                candidate.enabled &&
                (candidate.attachedRigidbody == null ||
                 candidate.attachedRigidbody.isKinematic) &&
                candidate.GetComponentInParent<InteractableVehicle>() == null &&
                candidate.GetComponentInParent<PrototypeActor>() == null &&
                candidate.GetComponentInParent<Player>() == null &&
                candidate.GetComponentInParent<Zombie>() == null;
        }
    }
}
