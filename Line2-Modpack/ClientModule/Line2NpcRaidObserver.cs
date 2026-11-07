using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Bounded first-hand rival-camp reconnaissance. Knows only camps whose
    /// EXACT manifest-listed native BarricadeDrop is physically in view.
    /// Internal settlement ledger is a lookup index, never permission to
    /// teleport, fabricate targets or damage unknown buildings.
    /// Staging is tactical movement only: grenade/weapon adapters must still
    /// commit damage through their native real-ammunition paths.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcRaidObserver : MonoBehaviour
    {
        private const int MaxReports = 16;
        private const int MaxPhysicalHits = 16;
        private readonly List<RegionCoordinate> _regions =
            new List<RegionCoordinate>(9);
        private readonly List<Transform> _worldBarricades =
            new List<Transform>(40);
        private readonly RaycastHit[] _hits = new RaycastHit[MaxPhysicalHits];
        private PrototypeActor _actor;
        private Line2NpcRaidBreacher _breacher;
        private float _nextAt;
        private float _nextReconsider;
        private float _nextLogAt;

        internal void Initialize(PrototypeActor actor)
        {
            _actor = actor;
            _breacher = GetComponent<Line2NpcRaidBreacher>();
            _nextAt = Time.time + UnityEngine.Random.Range(4f, 9f);
        }

        private void Update()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + 5.2f;
            if (_actor == null || !_actor.IsCombatEnabled ||
                _actor.IsDead || _actor.IsRecruited ||
                _actor.IsTacticallyRetreating ||
                !Level.isLoaded || BarricadeManager.instance == null ||
                Time.time < _nextReconsider) return;
            var camps = Line2NpcSettlementLedger.Capture();
            if (camps == null || camps.Count == 0) return;
            Vector3 eye = transform.position + Vector3.up * 1.45f;
            int checkedCamps = 0;
            foreach (Line2NpcPersistence.SettlementRecord camp in camps)
            {
                if (++checkedCamps > MaxReports) break;
                if (camp == null || camp.placed == null ||
                    camp.placed.Length == 0 ||
                    !Line2NPCCombatRules.AreOpposed(
                        _actor.Disposition, camp.role, _actor.CombatMode) ||
                    (camp.Anchor - transform.position).sqrMagnitude > 34f * 34f)
                    continue;

                _regions.Clear();
                _worldBarricades.Clear();
                Regions.getRegionsInRadius(camp.Anchor, 7f, _regions);
                BarricadeManager.getBarricadesInRadius(
                    camp.Anchor, 7f * 7f, _regions, _worldBarricades);
                int inspected = 0;
                foreach (Transform candidate in _worldBarricades)
                {
                    if (++inspected > 40) break;
                    if (candidate == null) continue;
                    Line2NpcPersistence.PlacedBarricadeRecord observed = null;
                    foreach (var part in camp.placed)
                        if ((candidate.position -
                            new Vector3(part.x, part.y, part.z)).sqrMagnitude <
                            0.95f * 0.95f)
                        {
                            observed = part;
                            break;
                        }
                    if (observed == null) continue;
                    byte x,y;
                    ushort plant;
                    BarricadeRegion region;
                    if (!BarricadeManager.tryGetRegion(candidate, out x,
                        out y, out plant, out region) || region == null)
                        continue;
                    BarricadeDrop native =
                        region.FindBarricadeByRootTransform(candidate);
                    if (native == null || native.asset == null ||
                        native.asset.id != observed.assetId ||
                        native.GetServersideData() == null ||
                        native.GetServersideData().owner != 0ul ||
                        native.GetServersideData().group != 0ul ||
                        !HasUnobstructedSight(eye,
                            candidate.position + Vector3.up * 0.8f,
                            candidate))
                        continue;
                    // After physically observing the original rival base
                    // piece, a nearby raider with a real melee item can
                    // attack that exact native barricade. The vanilla
                    // manager owns armor, callbacks and final destruction.
                    if (_breacher != null &&
                        _breacher.TryStrikeVerifiedRivalServer(
                            camp.groupId, native, candidate))
                    {
                        _nextAt = Time.time + 1.8f;
                        return;
                    }
                    Vector3 staging;
                    if (!TryFindStaging(camp.Anchor, out staging) ||
                        !_actor.TryStageObservedRaid(staging))
                        return;
                    _nextReconsider = Time.time + 42f;
                    if (Time.time >= _nextLogAt)
                    {
                        _nextLogAt = Time.time + 15f;
                        ProbeLogger.Log("NPC_RAID_STAGING: observer=" +
                            _actor.Identity + " rivalGroup=" + camp.groupId +
                            " observedAsset=" + native.asset.id +
                            " (physical object, no omniscient hit)");
                    }
                    return;
                }
            }
        }

        private bool HasUnobstructedSight(Vector3 origin,
            Vector3 destination, Transform target)
        {
            Vector3 ray = destination - origin;
            float length = ray.magnitude;
            if (length < 0.5f || length > 35f) return false;
            int n = Physics.RaycastNonAlloc(origin, ray / length,
                _hits, length, ~0, QueryTriggerInteraction.Ignore);
            if (n >= MaxPhysicalHits) return false;
            float nearestDistance = float.PositiveInfinity;
            Transform nearest = null;
            for (int i = 0; i < n; i++)
            {
                var hit = _hits[i];
                _hits[i] = default(RaycastHit);
                if (hit.transform == null ||
                    hit.transform == transform ||
                    hit.transform.IsChildOf(transform)) continue;
                if (hit.distance < nearestDistance)
                {
                    nearest = hit.transform;
                    nearestDistance = hit.distance;
                }
            }
            return nearest == null || nearest == target ||
                nearest.IsChildOf(target);
        }

        private bool TryFindStaging(Vector3 camp, out Vector3 result)
        {
            result = Vector3.zero;
            Vector3 away = transform.position - camp;
            away.y = 0f;
            if (away.sqrMagnitude < 1f) return false;
            away.Normalize();
            for (int step = 0; step < 3; step++)
            {
                float r = 9f + step * 2.5f;
                Vector3 estimated = camp + away * r;
                RaycastHit ground;
                if (!Physics.Raycast(estimated + Vector3.up * 2f,
                    Vector3.down, out ground, 4.2f, RayMasks.GROUND2,
                    QueryTriggerInteraction.Ignore) ||
                    ground.normal.y < 0.75f)
                    continue;
                Vector3 point = ground.point + Vector3.up * 0.07f;
                if (!Level.checkSafeIncludingClipVolumes(point) ||
                    Physics.CheckCapsule(point + Vector3.up * 0.55f,
                        point + Vector3.up * 1.6f, 0.36f,
                        RayMasks.BLOCK_COLLISION,
                        QueryTriggerInteraction.Ignore))
                    continue;
                result = point;
                return true;
            }
            return false;
        }
    }
}
