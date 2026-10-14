////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt file for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Individual, transient knowledge of a same-group RADIO rumor of a
    /// physically witnessed landed carepackage. A rumor is NOT proof a crate
    /// still exists and cannot be used as a direct loot target. An idle NPC may
    /// investigate an imprecise safe-ground location. The airdrop contest must
    /// then confirm a REAL crate visually before starting a loot objective.
    /// No global source/target registry, long-lived faction omniscience, or
    /// movement bypass.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCBrainDriver))]
    [RequireComponent(typeof(MobileNPCGroupMember))]
    [RequireComponent(typeof(MobileNPCInventory))]
    public sealed class MobileNPCAirdropRumor : MonoBehaviour
    {
        private const float ThinkInterval = 2.5f;
        private const float MaximumResponseRange = 105f;
        private const float CloseEnoughForSearch = 7f;
        private const float LocalVerificationRange = 28f;
        private const float ExpiredSiteRetryCooldown = 95f;
        private MobileNPCBrainDriver brain;
        private MobileNPCGroupMember group;
        private MobileNPCFaction faction;
        private MobileNPCInventory inventory;
        private MobileNPCHumanoidVitals vitals;
        private Vector3 reportedSector;
        private Vector3 approximateDestination;
        private Vector3 recentlySearchedSector;
        private float expiresAt;
        private float nextThinkAt;
        private float doNotReinvestigateUntil;
        private float arrivedAt;
        private int originalGroupId;
        private MobileNPCFaction.Id originalFaction;
        private bool hasRumor;
        private bool isInvestigating;

        private void Awake()
        {
            brain = GetComponent<MobileNPCBrainDriver>();
            group = GetComponent<MobileNPCGroupMember>();
            faction = GetComponent<MobileNPCFaction>();
            inventory = GetComponent<MobileNPCInventory>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
        }

        public bool IsInvestigatingAirdropServer => Provider.isServer && hasRumor
            && isInvestigating && Time.time < expiresAt
            && group != null && group.GroupId == originalGroupId
            && faction != null && faction.CurrentFaction == originalFaction
            && brain != null && brain.Target == null
            && brain.CurrentGoal == MobileNPCTactics.Goal.Investigate
            && (brain.Destination - approximateDestination).sqrMagnitude < 4f;

        /// <summary>
        /// Called ONLY after sender/receiver have both satisfied physical
        /// Unturned radio endpoint and same living group/faction/frequency checks.
        /// Deliberately coarsen exact transmitted crate coordinates. The listener
        /// never receives a crate transform or an authorized pickup instruction.
        /// </summary>
        public bool ReceiveAirdropReportServer(MobileNPCRadioMessage message)
        {
            if (!Provider.isServer || !Level.isLoaded
                || !MobileNPCSettings.AirdropContestEnabled || message == null
                || message.Type != MobileNPCRadioMessage.Kind.Airdrop
                || !message.IsFresh(Time.time) || message.Confidence < 0.35f
                || group == null || group.GroupId <= 0
                || group.GroupId != message.SenderGroupId
                || faction == null || faction.CurrentFaction != message.Faction
                || vitals != null && vitals.IsDead
                || !IsFinite(message.ReportedPosition))
                return false;

            // Preserve distinct individual investigation history. Several NPCs
            // cannot repeatedly refresh the same rumor into perpetual certainty.
            Vector3 coarseSector = new Vector3(
                Mathf.Round(message.ReportedPosition.x / 20f) * 20f,
                message.ReportedPosition.y,
                Mathf.Round(message.ReportedPosition.z / 20f) * 20f);
            if (Time.time < doNotReinvestigateUntil
                && (coarseSector - recentlySearchedSector).sqrMagnitude < 35f * 35f)
                return false;
            if (hasRumor && (coarseSector - reportedSector).sqrMagnitude < 35f * 35f)
                return false;

            Vector3 offset = new Vector3(Random.Range(-8f, 8f), 0f,
                Random.Range(-8f, 8f));
            Vector3 approximate = coarseSector + offset;
            Vector3 delta = approximate - transform.position;
            if (delta.sqrMagnitude > MaximumResponseRange * MaximumResponseRange)
                return false;

            // Ground validation is not path authorization. Existing travel /
            // tactical goal and motor own movement and collision safety.
            if (!MobileNPCSpawnService.TryFindSafeGroundPosition(approximate,
                12f, 9f, RayMasks.BLOCK_COLLISION, 36f,
                out Vector3 validatedGround, out _)
                || !Level.checkSafeIncludingClipVolumes(validatedGround))
                return false;

            reportedSector = coarseSector;
            approximateDestination = validatedGround;
            expiresAt = Mathf.Min(message.ExpiresAt, Time.time + 45f);
            originalGroupId = message.SenderGroupId;
            originalFaction = message.Faction;
            arrivedAt = 0f;
            hasRumor = true;
            isInvestigating = false;
            nextThinkAt = Time.time + Random.Range(0.4f, 2f);
            return true;
        }

        /// <summary>Called only when a real, landed, nonempty nearby crate has
        /// been visually confirmed by the game's local airdrop scan.</summary>
        public bool TryInvestigateConfirmedCrateServer(Vector3 genuineCratePosition)
        {
            if (!IsInvestigatingAirdropServer
                || (genuineCratePosition - transform.position).sqrMagnitude
                    > LocalVerificationRange * LocalVerificationRange
                || (genuineCratePosition - reportedSector).sqrMagnitude > 55f * 55f)
                return false;

            // Clear ONLY the investigation we own, then let the existing loot
            // goal approach and native inventory-transfer validation handle it.
            brain.ReportDestinationReached();
            bool started = brain.TryStartLoot(genuineCratePosition);
            if (started)
                FinishRumorServer();
            return started;
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || !hasRumor
                || Time.time < nextThinkAt)
                return;
            nextThinkAt = Time.time + ThinkInterval;

            if (!MobileNPCSettings.AirdropContestEnabled
                || Time.time >= expiresAt || group == null
                || group.GroupId != originalGroupId
                || faction == null || faction.CurrentFaction != originalFaction
                || vitals != null && vitals.IsDead || brain == null
                || inventory == null || !inventory.HasSpace)
            {
                FinishRumorServer();
                return;
            }

            if (isInvestigating)
            {
                // If combat, another goal or normal navigation has displaced
                // this optional investigation, never restore it automatically.
                if (!IsInvestigatingAirdropServer)
                {
                    FinishRumorServer();
                    return;
                }
                if ((transform.position - approximateDestination).sqrMagnitude
                    <= CloseEnoughForSearch * CloseEnoughForSearch)
                {
                    if (arrivedAt == 0f)
                        arrivedAt = Time.time;
                    // An ordinary airdrop scan gets several opportunities to
                    // visually verify a crate here, before the rumor lapses.
                    if (Time.time - arrivedAt > 8f)
                    {
                        if (IsInvestigatingAirdropServer)
                            brain.ReportDestinationReached();
                        FinishRumorServer();
                    }
                }
                return;
            }

            if (brain.Target != null
                || brain.CurrentGoal != MobileNPCTactics.Goal.None
                || brain.CurrentState != MobileNPCBrain.State.Idle
                || (approximateDestination - transform.position).sqrMagnitude
                    > MaximumResponseRange * MaximumResponseRange)
                return;

            if (brain.TryStartRememberedSupplySearch(approximateDestination))
            {
                isInvestigating = true;
                Debug.Log("[MobileNPCAirdropRumor] " + name
                    + " is investigating a radio-reported approximate airdrop sector, not a verified crate.", this);
            }
        }

        private void FinishRumorServer()
        {
            if (!hasRumor)
                return;
            // Expired/invalid hearsay must not leave an investigate destination
            // running long after the radio report has been forgotten. Clear
            // only our own matching tactical investigation, never combat,
            // escort orders or another component's newer objective.
            if (Provider.isServer && isInvestigating && brain != null
                && brain.CurrentGoal == MobileNPCTactics.Goal.Investigate
                && (brain.Destination - approximateDestination).sqrMagnitude < 4f)
                brain.ReportDestinationReached();
            recentlySearchedSector = reportedSector;
            doNotReinvestigateUntil = Time.time + ExpiredSiteRetryCooldown;
            hasRumor = false;
            isInvestigating = false;
            arrivedAt = 0f;
        }

        private void OnDisable()
        {
            hasRumor = false;
            isInvestigating = false;
            arrivedAt = 0f;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }
    }
}
