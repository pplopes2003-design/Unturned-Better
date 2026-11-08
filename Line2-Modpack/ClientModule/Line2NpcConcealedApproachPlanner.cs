using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Port of Line1 BrainDriver.TryPlanConcealedApproach into the Steam
    /// physical geometry API. The proposed sheltered vantage is proved from
    /// nearby ground and solid world geometry; it is NEVER the presumed
    /// enemy's exact position. Vanilla/alternate A* still validates the route.
    /// </summary>
    internal sealed class Line2NpcConcealedApproachPlanner
    {
        private readonly RaycastHit[] _pathHits = new RaycastHit[16];
        private readonly RaycastHit[] _coverHits = new RaycastHit[16];

        internal bool TryPlan(Vector3 current, Vector3 lastObservedArea,
            Transform self, out Vector3 destination)
        {
            destination = Vector3.zero;
            if (!Level.isLoaded || self == null) return false;
            Vector3 forward = lastObservedArea - current;
            forward.y = 0f;
            if (forward.sqrMagnitude < 16f || forward.sqrMagnitude > 900f)
                return false;
            forward.Normalize();
            Vector3 best = Vector3.zero;
            float bestScore = float.NegativeInfinity;
            for (int i = -2; i <= 2; i++)
            {
                Vector3 approach = Quaternion.AngleAxis(i * 27f,
                    Vector3.up) * forward;
                Vector3 sample = current + approach * 5f;
                RaycastHit floor;
                if (!Physics.Raycast(sample + Vector3.up * 1.8f,
                    Vector3.down, out floor, 4.0f, RayMasks.GROUND2,
                    QueryTriggerInteraction.Ignore) ||
                    floor.collider == null || floor.normal.y < 0.78f)
                    continue;
                Vector3 foot = floor.point + Vector3.up * 0.06f;
                if (!Level.checkSafeIncludingClipVolumes(foot) ||
                    Mathf.Abs(foot.y - current.y) > 0.85f ||
                    Physics.CheckCapsule(foot + Vector3.up * 0.55f,
                        foot + Vector3.up * 1.55f, 0.33f,
                        RayMasks.BLOCK_COLLISION,
                        QueryTriggerInteraction.Ignore))
                    continue;
                Vector3 delta = foot - current;
                delta.y = 0f;
                float length = delta.magnitude;
                if (length < 2.4f || length > 7.2f) continue;
                int count = Physics.SphereCastNonAlloc(
                    current + Vector3.up, 0.28f,
                    delta / length, _pathHits, length,
                    RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
                if (count >= _pathHits.Length) continue;
                bool corridorOpen = true;
                for (int j = 0; j < count; j++)
                {
                    RaycastHit hit = _pathHits[j];
                    _pathHits[j] = default(RaycastHit);
                    if (hit.collider == null ||
                        hit.transform == self ||
                        hit.transform.IsChildOf(self)) continue;
                    if (hit.distance < length - 0.65f)
                    {
                        corridorOpen = false;
                        break;
                    }
                }
                if (!corridorOpen) continue;
                // A visible line from the last observed area to the candidate
                // is NOT sheltered. Only hard world geometry with a mostly
                // vertical face qualifies; moving actors do not act as cover.
                Vector3 eye = lastObservedArea + Vector3.up * 1.35f;
                Vector3 chest = foot + Vector3.up * 1.0f;
                Vector3 coverRay = chest - eye;
                float coverDistance = coverRay.magnitude;
                if (coverDistance < 0.5f) continue;
                count = Physics.RaycastNonAlloc(eye,
                    coverRay / coverDistance, _coverHits, coverDistance,
                    RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
                if (count >= _coverHits.Length) continue;
                bool sheltered = false;
                for (int j = 0; j < count; j++)
                {
                    RaycastHit hit = _coverHits[j];
                    _coverHits[j] = default(RaycastHit);
                    if (hit.collider == null ||
                        hit.transform == self ||
                        hit.transform.IsChildOf(self) ||
                        hit.collider.GetComponentInParent<Player>() != null ||
                        hit.collider.GetComponentInParent<Zombie>() != null ||
                        hit.collider.GetComponentInParent<PrototypeActor>() != null)
                        continue;
                    if (hit.distance < coverDistance - 0.7f &&
                        Vector3.Angle(hit.normal, Vector3.up) > 45f)
                    {
                        sheltered = true;
                        break;
                    }
                }
                if (!sheltered) continue;
                float score = -(lastObservedArea - foot).magnitude +
                    length * 0.2f;
                if (score <= bestScore) continue;
                bestScore = score;
                best = foot;
            }
            if (float.IsNegativeInfinity(bestScore)) return false;
            destination = best;
            return true;
        }
    }
}
