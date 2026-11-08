using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Time-sliced Line1 BrainDriver group-regroup adaptation. Teammate
    /// position is used ONLY after real local line of sight; the group's
    /// persistent ID is never a radar. A* remains the sole movement planner.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcSquadRegroup : MonoBehaviour
    {
        private const float TickInterval = 6.2f;
        private const float GroupCooldown = 23f;
        private const int MaximumRayHits = 16;
        private readonly RaycastHit[] _hits = new RaycastHit[MaximumRayHits];
        private readonly RaycastHit[] _floorHits = new RaycastHit[16];
        private PrototypeActor _actor;
        private Line2NPCGroupMember _group;
        private Line2NPCCombatPerception _perception;
        private float _nextAt;

        internal void Initialize(PrototypeActor actor,
            Line2NPCCombatPerception perception)
        {
            _actor = actor;
            _group = GetComponent<Line2NPCGroupMember>();
            _perception = perception;
            _nextAt = Time.time + UnityEngine.Random.Range(4f, 9f);
        }

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                (Provider.clients == null || Provider.clients.Count <= 1) &&
                Level.isLoaded && Provider.hasCheats;
        }

        private void Update()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + TickInterval;
            if (_actor == null || _actor.IsDead || _group == null ||
                !_group.Joined || !Authorized() ||
                _actor.RecentlyDamaged ||
                _actor.UnderVerifiedSuppression) return;
            PrototypeActor leader;
            if (!Line2NPCGroupMember.TryGetLivingMember(
                _group.GroupId, out leader) || leader == null ||
                leader == _actor || leader.IsDead ||
                leader.Disposition != _actor.Disposition)
                return;
            Line2NPCGroupMember leaderGroup =
                leader.GetComponent<Line2NPCGroupMember>();
            if (leaderGroup == null || !leaderGroup.Joined ||
                leaderGroup.GroupId != _group.GroupId ||
                leaderGroup.Faction != _group.Faction)
                return;
            if (_perception != null &&
                _perception.Kind !=
                    Line2NPCCombatPerception.TargetKind.None &&
                _perception.VisibleTarget != null)
                return;
            Vector3 toward = leader.transform.position -
                transform.position;
            if (Mathf.Abs(toward.y) > 1f ||
                toward.sqrMagnitude < 1f) return;
            Vector3 eyes = transform.position + Vector3.up * 1.35f;
            Vector3 target = leader.transform.position + Vector3.up * 1.2f;
            if (!FirsthandSight(eyes, target, leader.transform))
                return;
            Vector3 flat = toward;
            flat.y = 0f;
            Vector3 candidate = leader.transform.position -
                flat.normalized * 2.6f;
            // Original floor-only GROUND2 missed map interiors and
            // could pick overhead rooftops in multi-storey buildings.
            RaycastHit floor;
            Vector3 expected = new Vector3(
                candidate.x, transform.position.y, candidate.z);
            if (!Line2NpcFloorGeometry.TryFindStaticSupport(
                    expected, 2f, 4.5f, 0.7f, 0.72f,
                    _floorHits, out floor))
                return;
            Vector3 safe = floor.point + Vector3.up * 0.08f;
            if (Mathf.Abs(safe.y - transform.position.y) > 0.7f ||
                !Level.checkSafeIncludingClipVolumes(safe) ||
                Physics.CheckCapsule(safe + Vector3.up * 0.5f,
                    safe + Vector3.up * 1.60f, 0.32f,
                    RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore))
                return;

            bool eligible = Line2NpcSquadRegroupPolicy.MayRegroup(
                Authorized(), leader != _actor &&
                _group.GroupId > 0 &&
                _group.GroupId ==
                    leader.GetComponent<Line2NPCGroupMember>()?.GroupId,
                true,
                _perception != null &&
                    _perception.VisibleTarget != null,
                _actor.IsTacticallyRetreating ||
                    _actor.RecentlyDamaged ||
                    _actor.UnderVerifiedSuppression,
                _actor.IsRecruited,
                true,
                transform.position,
                leader.transform.position);
            if (!eligible || !_actor.TryStageObservedGroupRegroup(safe))
                return;
            _nextAt = Time.time + GroupCooldown;
            ProbeLogger.Log("NPC_SQUAD_REGROUP: member=" + _actor.Identity +
                " group=" + _group.GroupId + " leader=" +
                leader.Identity + " firsthand-LOS=true");
        }

        private bool FirsthandSight(Vector3 eye, Vector3 point,
            Transform teammate)
        {
            Vector3 ray = point - eye;
            if (ray.sqrMagnitude < 0.1f) return false;
            int n = Physics.RaycastNonAlloc(
                eye, ray.normalized, _hits, ray.magnitude,
                RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (n >= _hits.Length) return false;
            for (int i = 0; i < n; i++)
            {
                Transform hit = _hits[i].transform;
                _hits[i] = default(RaycastHit);
                if (hit == null || hit == transform ||
                    hit.IsChildOf(transform) ||
                    hit == teammate || hit.IsChildOf(teammate))
                    continue;
                return false;
            }
            return true;
        }
    }
}
