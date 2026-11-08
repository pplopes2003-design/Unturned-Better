using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Bounded cover and retreat candidate search after a verified hit.
    /// Ground is checked locally; A* route follower must still independently
    /// verify reachability before the actor can actually reach the target.
    /// Does NOT open doors or invent player positions.
    /// </summary>
    internal sealed class PrototypeCoverPlanner : MonoBehaviour
    {
        private const int CandidatesPerSearch = 6;
        private const float SearchCooldown = 2.5f;
        private Line2MobileNPCAdaptiveMemory _experience;
        internal void Initialize(Line2MobileNPCAdaptiveMemory experience)
        {
            _experience = experience;
        }

        private float _nextSearchAt;
        private Vector3 _lastSource;
        private Vector3 _lastCover;
        private Vector3 _lastRetreat;
        private bool _hasCover;
        private bool _hasRetreat;
        private float _resultExpires;

        internal bool TryGetDefense(Vector3 threat, out Vector3 cover,
                                    out Vector3 retreat)
        {
            cover = _lastCover;
            retreat = _lastRetreat;
            if (Time.time < _nextSearchAt &&
                Time.time < _resultExpires &&
                (threat - _lastSource).sqrMagnitude < 9f)
                return _hasCover || _hasRetreat;

            _nextSearchAt = Time.time + SearchCooldown;
            _resultExpires = Time.time + 3.0f;
            _lastSource = threat;
            _hasCover = false;
            _hasRetreat = false;

            Vector3 away = transform.position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.2f)
                away = transform.forward;
            away.Normalize();

            float bestCoverScore = -999999f;
            float bestRetreatScore = -999999f;
            Vector3 eye = threat + Vector3.up * 1.25f;

            for (int i = 0; i < CandidatesPerSearch; i++)
            {
                float angle = (i - 2.5f) * 25f;
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * away;
                float distance = i % 2 == 0 ? 6f : 10f;
                Vector3 point = transform.position + dir * distance;
                Vector3 ground = point + Vector3.up * 2.4f;
                RaycastHit hit;
                if (!Physics.Raycast(ground, Vector3.down, out hit, 5f,
                                     Line2NpcFloorGeometry.SupportMask, QueryTriggerInteraction.Ignore) ||
                    hit.normal.y < 0.72f ||
                    Mathf.Abs(hit.point.y - transform.position.y) > 1.4f)
                    continue;

                point = hit.point + Vector3.up * 0.08f;
                if (Physics.CheckCapsule(point + Vector3.up * 0.55f,
                         point + Vector3.up * 1.55f, 0.32f,
                         RayMasks.BLOCK_COLLISION,
                         QueryTriggerInteraction.Ignore))
                    continue;

                Vector3 candidateEye = point + Vector3.up * 1.3f;
                Vector3 ray = candidateEye - eye;
                RaycastHit blocking;
                bool protectedFromThreat = ray.sqrMagnitude > 0.1f &&
                    Physics.Raycast(eye, ray.normalized, out blocking,
                        ray.magnitude - 0.1f, RayMasks.BLOCK_EXPLOSION,
                        QueryTriggerInteraction.Ignore);

                float awayScore = Vector3.Dot(dir, away) * 4f +
                    (point - threat).sqrMagnitude * 0.02f;
                float rememberedCoverUtility = _experience != null
                    ? _experience.GetCoverAccessAdjustment(point) : 0f;
                float coverScore = awayScore + rememberedCoverUtility;
                if (protectedFromThreat && coverScore > bestCoverScore)
                {
                    bestCoverScore = coverScore;
                    _lastCover = point;
                    _hasCover = true;
                }
                if (awayScore > bestRetreatScore)
                {
                    bestRetreatScore = awayScore;
                    _lastRetreat = point;
                    _hasRetreat = true;
                }
            }

            cover = _lastCover;
            retreat = _lastRetreat;
            return _hasCover || _hasRetreat;
        }

        internal bool HasCover { get { return _hasCover && Time.time < _resultExpires; } }
        internal bool HasRetreat { get { return _hasRetreat && Time.time < _resultExpires; } }
    }
}
