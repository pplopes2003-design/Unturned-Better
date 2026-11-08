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
