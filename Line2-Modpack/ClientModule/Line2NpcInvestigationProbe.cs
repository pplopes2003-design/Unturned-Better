using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Steam adapter of Line 1 BrainDriver's ValidateInvestigationSweepServer:
    /// the three original brain search points and later evidence hypotheses
    /// require an actual supported floor and a clear standing capsule.
    /// This is only ENDPOINT verification; original A* must still prove a
    /// connected route. No movement, teleport, destination knowledge,
    /// player Transform or Unity pathfinder is created here.
    /// </summary>
    internal sealed class Line2NpcInvestigationProbe
    {
        private readonly RaycastHit[] _floorHits = new RaycastHit[16];

        internal bool TryValidate(Vector3 estimate, Vector3 actualPosition,
            out Vector3 verified)
        {
            verified = Vector3.zero;
            if (!Level.isLoaded ||
                !Level.checkSafeIncludingClipVolumes(estimate) ||
                Mathf.Abs(estimate.y - actualPosition.y) > 1.4f)
                return false;
            // Radio/Combat Total hypotheses must stand on the ACTUAL
            // current floor. GROUND2 alone misses native map interiors;
            // a single ray may accidentally choose an upper roof or stair.
            RaycastHit floor;
            Vector3 expectedFloor = new Vector3(
                estimate.x, actualPosition.y, estimate.z);
            if (!Line2NpcFloorGeometry.TryFindStaticSupport(
                    expectedFloor, 1.3f, 3f, 0.85f, 0.74f,
                    _floorHits, out floor))
                return false;
            Vector3 foot = floor.point + Vector3.up * 0.06f;
            if (!Level.checkSafeIncludingClipVolumes(foot) ||
                Physics.CheckCapsule(foot + Vector3.up * 0.55f,
                    foot + Vector3.up * 1.55f, 0.33f,
                    RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore))
                return false;
            verified = foot;
            return true;
        }
    }
}
