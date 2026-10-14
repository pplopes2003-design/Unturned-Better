////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt file for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Per-individual uncertain memory of a physically seen world item reported
    /// on the same group's REAL radio endpoint. It never stores a world item
    /// instance/Transform, or authorizes pickup before close first-hand LOS.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCBrainDriver))]
    [RequireComponent(typeof(MobileNPCInventory))]
    public sealed class MobileNPCUsefulLootRumor : MonoBehaviour
    {
        private const float ThinkInterval = 2f;
        private const float MaximumResponseRange = 100f;
        private const float MinimumResponseRange = 12f;
        private const float SearchArrivalRadius = 8f;
        private const float MaximumConfirmedItemSectorRadius = 34f;
        private const float RetrySameSectorAfter = 110f;
        private const float MinimumUsefulNeed = 70f;
        private MobileNPCBrainDriver brain;
        private MobileNPCInventory inventory;
        private MobileNPCGroupMember group;
        private MobileNPCFaction faction;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCInjurySystem injuries;
        private MobileNPCSurvivalNeeds survival;
        private Vector3 reportedSector;
        private Vector3 destination;
        private Vector3 recentlyVisitedSector;
        private float expiresAt;
        private float nextThinkAt;
        private float arrivedAt;
        private float suppressSameSectorUntil;
        private ushort expectedItemID;
        private ushort recentlyVisitedItemID;
        private int sourceGroup;
        private MobileNPCFaction.Id sourceFaction;
        private bool hasRumor;
        private bool traveling;

        private void Awake()
        {
            brain = GetComponent<MobileNPCBrainDriver>();
            inventory = GetComponent<MobileNPCInventory>();
            group = GetComponent<MobileNPCGroupMember>();
            faction = GetComponent<MobileNPCFaction>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            injuries = GetComponent<MobileNPCInjurySystem>();
            survival = GetComponent<MobileNPCSurvivalNeeds>();
        }

        public bool IsActivelySearchingServer
        {
            get
            {
                if (!Provider.isServer || !Level.isLoaded || !hasRumor
                    || !traveling || Time.time >= expiresAt
                    || group == null || group.GroupId != sourceGroup
                    || faction == null || faction.CurrentFaction != sourceFaction
                    || vitals == null || vitals.IsDead || inventory == null
                    || !inventory.HasSpace || brain == null || brain.Target != null)
                    return false;
                if (brain.CurrentGoal == MobileNPCTactics.Goal.Investigate)
                    return (brain.Destination - destination).sqrMagnitude < 4f;
                // Native motor may already clear Investigate at actual arrival.
                return brain.CurrentGoal == MobileNPCTactics.Goal.None
                    && (transform.position - destination).sqrMagnitude
                        <= SearchArrivalRadius * SearchArrivalRadius;
            }
        }

        public ushort ExpectedItemID => IsActivelySearchingServer ? expectedItemID : (ushort)0;

        public bool ReceiveUsefulLootReportServer(MobileNPCRadioMessage message)
        {
            if (!Provider.isServer || !Level.isLoaded || message == null
                || message.Type != MobileNPCRadioMessage.Kind.UsefulLoot
                || !message.IsFresh(Time.time) || message.Confidence < 0.40f
                || message.ReportedItemID == 0
                || group == null || group.GroupId <= 0
                || message.SenderGroupId != group.GroupId
                || faction == null || message.Faction != faction.CurrentFaction
                || vitals == null || vitals.IsDead
                || inventory == null || !inventory.HasSpace
                || !IsFinite(message.ReportedPosition)
                || traveling)
                return false;

            float need = GetNeedScoreServer(message.ReportedItemID);
            if (need < MinimumUsefulNeed)
                return false;

            Vector3 coarse = new Vector3(
                Mathf.Round(message.ReportedPosition.x / 15f) * 15f,
                message.ReportedPosition.y,
                Mathf.Round(message.ReportedPosition.z / 15f) * 15f);
            if (Time.time < suppressSameSectorUntil
                && recentlyVisitedItemID == message.ReportedItemID
                && (coarse - recentlyVisitedSector).sqrMagnitude < 36f * 36f)
                return false;
            if (hasRumor && expectedItemID == message.ReportedItemID
                && (coarse - reportedSector).sqrMagnitude < 36f * 36f)
                return false;

            float distance = Vector3.Distance(coarse, transform.position);
            if (distance < MinimumResponseRange || distance > MaximumResponseRange)
                return false;
            // Severe open injuries disqualify distant speculative loot trips.
            if (injuries != null
                && injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None
                && distance > 55f)
                return false;

            Vector2 error = Random.insideUnitCircle * 6f;
            Vector3 approximate = coarse + new Vector3(error.x, 0f, error.y);
            if (!MobileNPCSpawnService.TryFindSafeGroundPosition(approximate,
                12f, 8f, RayMasks.BLOCK_COLLISION, 38f,
                out Vector3 grounded, out _)
                || (grounded - approximate).sqrMagnitude > 10f * 10f
                || !Level.checkSafeIncludingClipVolumes(grounded))
                return false;

            reportedSector = coarse;
            destination = grounded;
            expectedItemID = message.ReportedItemID;
            sourceGroup = message.SenderGroupId;
            sourceFaction = message.Faction;
            expiresAt = Mathf.Min(message.ExpiresAt, Time.time + 62f);
            hasRumor = true;
            traveling = false;
            arrivedAt = -1f;
            nextThinkAt = Time.time + Random.Range(0.4f, 1.5f);
            return true;
        }

        /// <summary>
        /// The ordinary local ItemManager scan supplies a REAL world drop.
        /// This function never fetches items by radio or performs direct loot;
        /// it only allows the normal loot component to use its existing
        /// instance-ID, path, ownership, and transactional pickup code.
        /// </summary>
        public bool TryConfirmLocallySeenItemServer(InteractableItem item)
        {
            if (!IsActivelySearchingServer || item == null
                || item.jar == null || item.jar.item == null
                || item.jar.item.id != expectedItemID
                || (item.transform.position - reportedSector).sqrMagnitude >
                    MaximumConfirmedItemSectorRadius * MaximumConfirmedItemSectorRadius
                || !MobileNPCUsefulLootVision.CanSeeWorldDropServer(
                    item, transform, 17f))
                return false;

            // The original seek destination is uncertain. Only an actual
            // matching visible drop can supersede it with normal Goal.Loot.
            if (brain.CurrentGoal == MobileNPCTactics.Goal.Investigate
                && (brain.Destination - destination).sqrMagnitude < 4f)
                brain.ReportDestinationReached();

            bool started = brain.TryStartLoot(item.transform.position);
            if (started)
                FinishRumorServer();
            return started;
        }

        private float GetNeedScoreServer(ushort itemID)
        {
            return MobileNPCSupplyKnowledge.GetCurrentRadioItemNeedServer(itemID,
                inventory, vitals, injuries, survival);
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || !hasRumor
                || Time.time < nextThinkAt)
                return;
            nextThinkAt = Time.time + ThinkInterval;

            if (Time.time >= expiresAt || group == null
                || group.GroupId != sourceGroup
                || faction == null || faction.CurrentFaction != sourceFaction
                || vitals == null || vitals.IsDead
                || inventory == null || !inventory.HasSpace
                || brain == null || brain.Target != null
                || GetNeedScoreServer(expectedItemID) < MinimumUsefulNeed)
            {
                FinishRumorServer();
                return;
            }

            if (!traveling)
            {
                if (brain.CurrentGoal != MobileNPCTactics.Goal.None
                    || brain.CurrentState != MobileNPCBrain.State.Idle)
                    return;
                if (brain.TryStartRememberedSupplySearch(destination))
                {
                    traveling = true;
                    Debug.Log("[MobileNPCUsefulLootRumor] " + name
                        + " investigating heard report of item " + expectedItemID
                        + " in an uncertain region (no item acquired yet).", this);
                }
                return;
            }

            if (brain.CurrentGoal == MobileNPCTactics.Goal.Investigate
                && (brain.Destination - destination).sqrMagnitude < 4f)
            {
                if ((transform.position - destination).sqrMagnitude <=
                    SearchArrivalRadius * SearchArrivalRadius)
                {
                    if (arrivedAt < 0f)
                        arrivedAt = Time.time;
                    // Leave the ordinary motor free to acknowledge arrival;
                    // the local loot scan can still confirm a visible item.
                    brain.ReportDestinationReached();
                }
                return;
            }

            // The motor itself may clear this same goal as it reaches the
            // destination before this component's next 2-second update.
            if (brain.CurrentGoal == MobileNPCTactics.Goal.None
                && (transform.position - destination).sqrMagnitude <=
                    SearchArrivalRadius * SearchArrivalRadius)
            {
                if (arrivedAt < 0f)
                    arrivedAt = Time.time;
                if (Time.time - arrivedAt >= 8f)
                    FinishRumorServer();
                return;
            }
            // Combat, a follow order, a replacement goal or impossible
            // navigation has removed this speculative trip.
            FinishRumorServer();
        }

        private void FinishRumorServer()
        {
            if (!hasRumor)
                return;
            if (Provider.isServer && traveling && brain != null
                && brain.CurrentGoal == MobileNPCTactics.Goal.Investigate
                && (brain.Destination - destination).sqrMagnitude < 4f)
                brain.ReportDestinationReached();
            recentlyVisitedSector = reportedSector;
            recentlyVisitedItemID = expectedItemID;
            suppressSameSectorUntil = Time.time + RetrySameSectorAfter;
            hasRumor = false;
            traveling = false;
            arrivedAt = -1f;
            expectedItemID = 0;
        }

        private void OnDisable()
        {
            hasRumor = false;
            traveling = false;
            expectedItemID = 0;
        }

        private static bool IsFinite(Vector3 p)
        {
            return !float.IsNaN(p.x) && !float.IsInfinity(p.x)
                && !float.IsNaN(p.y) && !float.IsInfinity(p.y)
                && !float.IsNaN(p.z) && !float.IsInfinity(p.z);
        }
    }

    /// <summary>
    /// Real direct first-hand sight of an ItemManager-backed world drop.
    /// Does not use a POI loot table, group/world item registry, or hearing.
    /// </summary>
    internal static class MobileNPCUsefulLootVision
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        public static float IntrinsicImportance(ItemAsset asset)
        {
            if (asset == null)
                return 0f;
            if (asset.id == MobileNPCRadioOutbox.VanillaWalkieTalkieID)
                return 85f;
            switch (asset.type)
            {
                case EItemType.MEDICAL: return 100f;
                case EItemType.GUN: return 95f;
                case EItemType.MAGAZINE: return 92f;
                case EItemType.WATER: return 91f;
                case EItemType.FOOD: return 85f;
                case EItemType.MELEE: return 75f;
                case EItemType.BACKPACK: return 66f;
                case EItemType.VEST: return 63f;
                default: return 0f;
            }
        }

        public static bool CanSeeWorldDropServer(InteractableItem item,
            Transform observer, float maximumDistance)
        {
            if (!Provider.isServer || !Level.isLoaded || item == null
                || observer == null || item.transform == null
                || !item.gameObject.activeInHierarchy
                || item.asset == null || item.jar == null
                || item.jar.item == null || item.jar.item.id != item.asset.id
                || item.jar.item.amount == 0
                || IntrinsicImportance(item.asset) <= 0f
                || !ItemManager.TryGetWorldItemInstanceID(item, out uint id)
                || id == 0)
                return false;

            Vector3 from = observer.position + Vector3.up * 1.45f;
            Vector3 to = item.transform.position + Vector3.up * 0.23f;
            Vector3 offset = to - from;
            float length = offset.magnitude;
            if (length < 0.25f || length > maximumDistance)
                return false;
            int n = Physics.RaycastNonAlloc(from, offset / length,
                Hits, length, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (n >= Hits.Length)
                return false;
            for (int i = 0; i < n; i++)
            {
                Collider hit = Hits[i].collider;
                if (hit == null || hit.transform == observer
                    || hit.transform.IsChildOf(observer)
                    || hit.transform == item.transform
                    || hit.transform.IsChildOf(item.transform))
                    continue;
                return false;
            }
            return true;
        }
    }
}
