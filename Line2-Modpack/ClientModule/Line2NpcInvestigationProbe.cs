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
        internal bool TryValidate(Vector3 estimate, Vector3 actualPosition,
            out Vector3 verified)
        {
            verified = Vector3.zero;
            if (!Level.isLoaded ||
                !Level.checkSafeIncludingClipVolumes(estimate) ||
                Mathf.Abs(estimate.y - actualPosition.y) > 1.4f)
                return false;
            RaycastHit floor;
            if (!Physics.Raycast(estimate + Vector3.up * 1.3f,
                Vector3.down, out floor, 3f, RayMasks.GROUND2,
                QueryTriggerInteraction.Ignore) ||
                floor.collider == null ||
                floor.normal.y < 0.74f ||
                Mathf.Abs(floor.point.y - actualPosition.y) > 0.85f)
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
