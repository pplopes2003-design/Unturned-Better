////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Combat-only FIRST stage of A-Life territorial raids: physically SEE a
    /// rival faction's living defender near its actual native-built central
    /// storage, remember its approximate site, and lead nearby armed friends
    /// to a grounded staging point. No omniscient group lookup, teleport,
    /// automatic damage, ownership transfer, or invented structures.
    ///
    /// Once the group arrives, regular firsthand perception, squad tactics
    /// and projectile combat decide the fight. Territorial capture/claiming
    /// is intentionally a later system.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCGroupMember))]
    [RequireComponent(typeof(MobileNPCBrainDriver))]
    public sealed class MobileNPCTerritoryRaidPlanner : MonoBehaviour
    {
        private const float VisualReconRange = 38f;
        private const float MaximumSpokenOrderRange = 14f;
        private readonly List<MobileNPCBrainDriver> nearbyDrivers =
            new List<MobileNPCBrainDriver>(40);
        private readonly RaycastHit[] sightHits = new RaycastHit[20];

        private MobileNPCGroupMember group;
        private MobileNPCFaction faction;
        private MobileNPCBrainDriver brain;
        private MobileNPCHumanoidPerception perception;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCHumanoidRangedCombat gun;
        private float nextObserveAt;
        private float nextPlanAt;
        private float lastRaidAt = -1000f;
        private Vector3 knownRivalBase;
        private float knowledgeExpiresAt;
        private int lastRivalGroupId;
        private InteractableStorage lastPersonallySeenHostileStorage;

        private void Awake()
        {
            group = GetComponent<MobileNPCGroupMember>();
            faction = GetComponent<MobileNPCFaction>();
            brain = GetComponent<MobileNPCBrainDriver>();
            perception = GetComponent<MobileNPCHumanoidPerception>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            gun = GetComponent<MobileNPCHumanoidRangedCombat>();
            nextObserveAt = Time.time + Random.Range(2f, 6f);
            nextPlanAt = Time.time + Random.Range(4f, 8f);
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || !MobileNPCSettings.CombatMode
                || group == null || !group.CanPlanSettlement || faction == null
                || brain == null || perception == null || vitals == null || vitals.IsDead)
                return;
            if (Time.time >= nextObserveAt)
            {
                nextObserveAt = Time.time + Random.Range(5f, 8f);
                ObserveRealRivalSettlementServer();
            }
            if (Time.time < nextPlanAt) return;
            nextPlanAt = Time.time + Random.Range(5f, 9f);
            MobileNPCInventory supplies = GetComponent<MobileNPCInventory>();
            bool ordinaryArmed = gun != null && gun.SelectedGunAsset != null
                && gun.RoundsRemaining >= 3;
            bool breachArmed = supplies != null
                && (supplies.HasUsableRaidExplosiveServer()
                    || supplies.HasUsableRaidChargeServer());
            if (Time.time >= knowledgeExpiresAt || Time.time < lastRaidAt + 100f
                || brain.Target != null || brain.CurrentGoal != MobileNPCTactics.Goal.None
                || brain.CurrentState != MobileNPCBrain.State.Idle
                || (!ordinaryArmed && !breachArmed)
                || vitals.HealthFraction < 0.42f)
                return;

            // Validated approximate vantage from the PERSONAL last sighting.
            // Stop outside the rival building, not magically at its doorway.
            Vector3 retreatAxis = transform.position - knownRivalBase;
            retreatAxis.y = 0f;
            if (retreatAxis.sqrMagnitude < 1f) return;
            Vector3 proposed = knownRivalBase + retreatAxis.normalized * 17f;
            if ((proposed - transform.position).sqrMagnitude > 26f * 26f)
                return;
            if (!MobileNPCSpawnService.TryFindSafeGroundPosition(proposed,
                3f, 3f, RayMasks.BLOCK_COLLISION | RayMasks.GROUND2,
                40f, out Vector3 stage, out _)
                || Mathf.Abs(stage.y - transform.position.y) > 1.0f
                || !Level.checkSafeIncludingClipVolumes(stage))
                return;

            MobileNPCRegistry.CopyActiveDrivers(nearbyDrivers);
            int nearbyArmed = 1;
            // A real raid is not launched with mere rifles. At least one
            // surviving teammate must carry a usable native launcher rocket
            // or buildable-damaging explosive. If absent, continue scavenging.
            bool hasBreachEquipment = breachArmed;
            for (int i = 0; i < nearbyDrivers.Count; ++i)
            {
                MobileNPCBrainDriver allyBrain = nearbyDrivers[i];
                if (allyBrain == null || allyBrain == brain) continue;
                MobileNPCGroupMember ally = allyBrain.GetComponent<MobileNPCGroupMember>();
                MobileNPCHumanoidVitals allyVitals = allyBrain.GetComponent<MobileNPCHumanoidVitals>();
                MobileNPCHumanoidRangedCombat allyGun =
                    allyBrain.GetComponent<MobileNPCHumanoidRangedCombat>();
                MobileNPCInventory allyInventory = allyBrain.GetComponent<MobileNPCInventory>();
                bool allyBreachReady = allyInventory != null
                    && (allyInventory.HasUsableRaidExplosiveServer()
                        || allyInventory.HasUsableRaidChargeServer());
                bool allyRifleReady = allyGun != null && allyGun.SelectedGunAsset != null
                    && allyGun.RoundsRemaining >= 2;
                if (ally == null || ally.GroupId != group.GroupId
                    || allyVitals == null || allyVitals.IsDead || allyVitals.HealthFraction < 0.35f
                    || (!allyRifleReady && !allyBreachReady)
                    || !CanSpeakDirectlyTo(ally.transform))
                    continue;
                ++nearbyArmed;
                hasBreachEquipment |= allyBreachReady;
            }
            if (nearbyArmed < 2 || !hasBreachEquipment
                || !brain.TryStartRaidStagingServer(stage))
                return;
            lastRaidAt = Time.time;

            // The leader can share only a short physical rendezvous order with
            // actual teammates in talking range. They do NOT receive target
            // objects, attack permission, or hidden enemy/base positions.
            int following = 0;
            for (int i = 0; i < nearbyDrivers.Count; ++i)
            {
                MobileNPCBrainDriver allyBrain = nearbyDrivers[i];
                if (allyBrain == null || allyBrain == brain) continue;
                MobileNPCGroupMember ally = allyBrain.GetComponent<MobileNPCGroupMember>();
                MobileNPCHumanoidVitals allyVitals = allyBrain.GetComponent<MobileNPCHumanoidVitals>();
                MobileNPCHumanoidRangedCombat allyGun =
                    allyBrain.GetComponent<MobileNPCHumanoidRangedCombat>();
                if (ally == null || ally.GroupId != group.GroupId
                    || allyVitals == null || allyVitals.IsDead
                    || allyGun == null || allyGun.RoundsRemaining < 2
                    || !CanSpeakDirectlyTo(ally.transform))
                    continue;
                Vector3 away = ally.transform.position - stage;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -transform.forward;
                Vector3 rendezvous = stage + away.normalized * 2.5f;
                if (MobileNPCSpawnService.TryFindSafeGroundPosition(rendezvous,
                    2f, 2f, RayMasks.BLOCK_COLLISION | RayMasks.GROUND2,
                    40f, out Vector3 allyStage, out _)
                    && allyBrain.TryStartRaidStagingServer(allyStage))
                    ++following;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCTerritoryRaidPlanner] Group " + group.GroupId
                + " left for observed rival defensive sector (not captured): "
                + knownRivalBase + ", rivalGroup=" + lastRivalGroupId
                + ", audible followers=" + following + ".", this);
#endif
        }

        public bool HasRecentHostileBaseIntelServer =>
            Provider.isServer && MobileNPCSettings.CombatMode
            && group != null && group.CanPlanSettlement
            && Time.time < knowledgeExpiresAt;

        /// <summary>Native charge planting may target only the concrete
        /// vanilla storage that the raider can independently see now.</summary>
        public bool TryGetVisibleHostileStorageServer(out InteractableStorage storage)
        {
            storage = null;
            if (!TryGetVisibleHostileBaseServer(out _)
                || lastPersonallySeenHostileStorage == null
                || !lastPersonallySeenHostileStorage.gameObject.activeInHierarchy)
                return false;
            storage = lastPersonallySeenHostileStorage;
            return true;
        }

        /// <summary>A launcher may breach ONLY a structure whose defender
        /// and registered native storage the raider is SEEING right now.
        /// A rumour/old position never permits a rocket launch.</summary>
        public bool TryGetVisibleHostileBaseServer(out Vector3 impactPoint)
        {
            impactPoint = Vector3.zero;
            lastPersonallySeenHostileStorage = null;
            if (!Provider.isServer || !MobileNPCSettings.CombatMode
                || !Level.isLoaded || group == null || group.GroupId <= 0
                || perception == null || faction == null || vitals == null
                || vitals.IsDead)
                return false;
            MobileNPCHumanoidDisposition seen = perception.VisibleNPC;
            if (seen == null || !seen.gameObject.activeInHierarchy) return false;
            MobileNPCFaction rivalFaction = seen.GetComponent<MobileNPCFaction>();
            MobileNPCGroupMember rivalMember = seen.GetComponent<MobileNPCGroupMember>();
            MobileNPCHumanoidVitals rivalVitals = seen.GetComponent<MobileNPCHumanoidVitals>();
            if (rivalFaction == null || rivalMember == null || rivalVitals == null
                || rivalVitals.IsDead || rivalMember.GroupId == group.GroupId
                || !faction.IsHostileTo(rivalFaction)
                || !rivalMember.TryGetLivingLeaderServer(out MobileNPCGroupMember rivalLeader))
                return false;
            MobileNPCSettlementBuilder builder =
                rivalLeader.GetComponent<MobileNPCSettlementBuilder>();
            if (builder == null || !builder.TryGetCentralStorageForRadioServer(
                out InteractableStorage storage) || storage == null
                || storage.owner != CSteamID.Nil || storage.group != CSteamID.Nil)
                return false;
            Vector3 basePosition = storage.transform.position;
            // The enemy is personally seen, and its constructed position
            // must ALSO be nearby and personally visible; nobody scans
            // global group anchors or private save positions.
            if ((seen.transform.position - basePosition).sqrMagnitude > 16f * 16f
                || (transform.position - basePosition).sqrMagnitude
                    > VisualReconRange * VisualReconRange
                || !HasCleanSightToStorage(storage.transform, basePosition))
                return false;
            lastPersonallySeenHostileStorage = storage;
            impactPoint = basePosition;
            knownRivalBase = basePosition;
            lastRivalGroupId = rivalMember.GroupId;
            knowledgeExpiresAt = Time.time + 100f;
            return true;
        }

        private void ObserveRealRivalSettlementServer()
        {
            TryGetVisibleHostileBaseServer(out _);
        }

        private bool HasCleanSightToStorage(Transform storage, Vector3 position)
        {
            Vector3 eye = transform.position + Vector3.up * 1.45f;
            Vector3 offset = position + Vector3.up * 0.6f - eye;
            float distance = offset.magnitude;
            if (distance < 1f || distance > VisualReconRange) return false;
            int hits = Physics.RaycastNonAlloc(eye, offset / distance, sightHits,
                distance, RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
            if (hits == sightHits.Length) return false;
            for (int i = 0; i < hits; ++i)
            {
                Transform obstacle = sightHits[i].transform;
                if (obstacle == null || obstacle.IsChildOf(transform)
                    || obstacle == storage || obstacle.IsChildOf(storage)
                    // Ignore only this storage's OWN immediate native prefab
                    // collider; ignoring arbitrary ancestors of storage could
                    // incorrectly hide whole buildings between observer/base.
                    || (storage.parent == obstacle
                        && obstacle.GetComponentInChildren<InteractableStorage>(true) == storage)
                    || obstacle.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
                    continue;
                return false;
            }
            return true;
        }

        private bool CanSpeakDirectlyTo(Transform ally)
        {
            Vector3 origin = transform.position + Vector3.up * 1.45f;
            Vector3 destination = ally.position + Vector3.up * 1.45f;
            Vector3 delta = destination - origin;
            float distance = delta.magnitude;
            if (distance < 0.1f || distance > MaximumSpokenOrderRange)
                return false;
            int hits = Physics.RaycastNonAlloc(origin, delta / distance,
                sightHits, distance, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (hits == sightHits.Length) return false;
            for (int i = 0; i < hits; ++i)
            {
                Transform obstacle = sightHits[i].transform;
                if (obstacle == null || obstacle.IsChildOf(transform)
                    || obstacle.IsChildOf(ally)
                    || obstacle.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
                    continue;
                return false;
            }
            return true;
        }

        private void OnDisable()
        {
            knowledgeExpiresAt = 0f;
            nearbyDrivers.Clear();
        }
    }
}
