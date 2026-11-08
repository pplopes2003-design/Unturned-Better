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
        private readonly List<PrototypeActor> _squad =
            new List<PrototypeActor>(32);
        private SteamNpcPrototypeRuntime _runtime;
        private PrototypeActor _actor;
        private Line2NpcRaidBreacher _breacher;
        private float _nextAt;
        private float _nextReconsider;
        private float _nextLogAt;

        internal void Initialize(SteamNpcPrototypeRuntime runtime,
            PrototypeActor actor)
        {
            _runtime = runtime;
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
                        (observed.instanceID != 0u &&
                         native.instanceID != observed.instanceID) ||
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
                    // Stage relative to the EXACT visually confirmed
                    // barricade, not the camp centroid. A 9m+ offset from
                    // the centroid left raiders permanently unable to
                    // approach native melee reach after rallying.
                    if (!TryFindStaging(candidate.position, out staging) ||
                        !_actor.TryStageObservedRaid(staging))
                        return;
                    _nextReconsider = Time.time + 42f;
                    int followers = InvitePersonallyHeardAllies(staging);
                    if (Time.time >= _nextLogAt)
                    {
                        _nextLogAt = Time.time + 15f;
                        ProbeLogger.Log("NPC_RAID_STAGING: observer=" +
                            _actor.Identity + " rivalGroup=" + camp.groupId +
                            " observedAsset=" + native.asset.id +
                            " directSpokenFollowers=" + followers +
                            " (physical object; waypoint only, no target grant)");
                    }
                    return;
                }
            }
        }

        private bool HasDirectGroupSpeechSight(Transform ally)
        {
            if (ally == null) return false;
            Vector3 origin = transform.position + Vector3.up * 1.4f;
            Vector3 destination = ally.position + Vector3.up * 1.25f;
            Vector3 ray = destination - origin;
            float length = ray.magnitude;
            if (length < 0.25f ||
                length > Line2NpcRaidGroupOrderPolicy.SpeakingRange + 1f)
                return false;
            int n = Physics.RaycastNonAlloc(origin, ray / length,
                _hits, length, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (n >= _hits.Length) return false;
            for (int i = 0; i < n; ++i)
            {
                Transform hit = _hits[i].transform;
                _hits[i] = default(RaycastHit);
                if (hit == null || hit == transform ||
                    hit.IsChildOf(transform) ||
                    hit == ally || hit.IsChildOf(ally))
                    continue;
                return false;
            }
            return true;
        }

        private bool CarriesRealRaidEquipment(PrototypeActor teammate)
        {
            Line2MobileNPCInventory inventory = teammate != null
                ? teammate.GetComponent<Line2MobileNPCInventory>() : null;
            if (inventory == null) return false;
            Item gunItem;
            ItemGunAsset gun;
            if (inventory.TryGetBestGun(out gunItem, out gun) &&
                gunItem != null && gunItem.state != null &&
                gunItem.state.Length > GunStateIndices.AMMO &&
                gunItem.state[GunStateIndices.AMMO] >= gun.ammoPerShot &&
                gun.ammoPerShot > 0)
                return true;
            Item meleeItem;
            ItemMeleeAsset melee;
            return inventory.TryGetBestMeleeServer(out meleeItem, out melee) &&
                meleeItem != null && melee != null &&
                meleeItem.quality > 0 && melee.barricadeDamage > 0f;
        }

        private bool TryGroundedFollowerRally(PrototypeActor ally,
            Vector3 leaderStage, out Vector3 safe)
        {
            safe = Vector3.zero;
            Vector3 away = ally.transform.position - leaderStage;
            away.y = 0f;
            if (away.sqrMagnitude < 0.1f)
                away = ally.transform.right;
            away.Normalize();
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                Vector3 rotated = Quaternion.Euler(0f,
                    (attempt - 1) * 30f, 0f) * away;
                Vector3 estimate = leaderStage + rotated * 2.8f;
                RaycastHit floor;
                if (!Physics.Raycast(estimate + Vector3.up * 1.8f,
                    Vector3.down, out floor, 4.2f,
                    RayMasks.GROUND2, QueryTriggerInteraction.Ignore) ||
                    floor.collider == null || floor.normal.y < 0.77f)
                    continue;
                Vector3 point = floor.point + Vector3.up * 0.06f;
                if (Mathf.Abs(point.y - ally.transform.position.y) > 1.1f ||
                    !Level.checkSafeIncludingClipVolumes(point) ||
                    Physics.CheckCapsule(
                        point + Vector3.up * 0.55f,
                        point + Vector3.up * 1.55f, 0.34f,
                        RayMasks.BLOCK_COLLISION,
                        QueryTriggerInteraction.Ignore))
                    continue;
                safe = point;
                return true;
            }
            return false;
        }

        /// <summary>
        /// A first-hand observer can propose a short spoken group rally to
        /// up to two friends who are really present and have LOS to the
        /// speaker. Nobody receives the enemy target or asset transform.
        /// Each recipient separately verifies its safe standing waypoint
        /// and lets the original native A* prove route connectivity.
        /// </summary>
        private int InvitePersonallyHeardAllies(Vector3 leaderStage)
        {
            if (_runtime == null || _actor == null ||
                _actor.IsDead || _actor.RecentlyDamaged)
                return 0;
            Line2NPCGroupMember ownGroup =
                GetComponent<Line2NPCGroupMember>();
            if (ownGroup == null || !ownGroup.Joined) return 0;
            PrototypeActor livingLeader;
            if (!Line2NPCGroupMember.TryGetLivingMember(
                ownGroup.GroupId, out livingLeader) ||
                livingLeader != _actor)
                return 0; // one physical leader assigns raid staging
            _runtime.CopyLiveActors(_squad, 32);
            int followers = 0;
            foreach (PrototypeActor ally in _squad)
            {
                if (followers >=
                    Line2NpcRaidGroupOrderPolicy.MaximumFollowers)
                    break;
                if (ally == null || ally == _actor || ally.IsDead)
                    continue;
                Line2NPCGroupMember allyGroup =
                    ally.GetComponent<Line2NPCGroupMember>();
                if (allyGroup == null || allyGroup.GroupId != ownGroup.GroupId ||
                    allyGroup.Faction != ownGroup.Faction)
                    continue;
                Line2NPCCombatPerception perception =
                    ally.GetComponent<Line2NPCCombatPerception>();
                Vector3 allyStage;
                bool standing = TryGroundedFollowerRally(
                    ally, leaderStage, out allyStage);
                bool visible = HasDirectGroupSpeechSight(ally.transform);
                bool enemyContact = perception != null &&
                    perception.Kind != Line2NPCCombatPerception.TargetKind.None &&
                    perception.VisibleTarget != null;
                if (!Line2NpcRaidGroupOrderPolicy.MayFollow(
                    _actor.IsCombatEnabled && ally.IsCombatEnabled,
                    true, visible, CarriesRealRaidEquipment(ally),
                    enemyContact, ally.IsTacticallyRetreating ||
                    ally.RecentlyDamaged, ally.IsRecruited, standing,
                    ally.HealthFraction, transform.position,
                    ally.transform.position, allyStage, followers))
                    continue;
                if (ally.TryStageObservedRaid(allyStage))
                    followers++;
            }
            _squad.Clear();
            return followers;
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
                // Stand just outside the observed obstacle, never inside
                // its collider. Physical floor/capsule and the native
                // pathfinder must still accept the exact candidate.
                float r = 2.2f + step * 1.1f;
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
