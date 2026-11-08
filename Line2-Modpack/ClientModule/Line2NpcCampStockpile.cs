using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Port of Line 1's central storage delivery into the Steam module.
    /// Unlike a serialized 'virtual stash', this writes to the ORIGINAL
    /// InteractableStorage.items of a fully registered native barricade.
    /// Only the leader's group may deliver AUTHENTIC spare carried items.
    ///
    /// The persisted BarricadeManager instanceID, asset ID, exact location
    /// and NIL owner/group are all rechecked before transferring an item.
    /// A player-built chest near the base is never a substitute.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcCampStockpile : MonoBehaviour
    {
        private const float DeliveryInterval = 7.5f;
        private const int MaxLineHits = 16;
        private static float _nextWorldTransferAt;
        private PrototypeActor _actor;
        private SteamNpcPrototypeRuntime _runtime;
        private Line2NPCGroupMember _group;
        private Line2MobileNPCInventory _inventory;
        private Line2NpcNeeds _needs;
        private Line2MobileNPCInjurySystem _injury;
        private Line2NPCCombatPerception _sight;
        private readonly Line2NpcNativeCampRegistry _nativeCamp =
            new Line2NpcNativeCampRegistry();
        private readonly RaycastHit[] _sightHits =
            new RaycastHit[MaxLineHits];
        private float _nextAt;
        private float _nextDeliveryTripAt;

        internal static void ResetSession()
        {
            _nextWorldTransferAt = 0f;
        }

        internal void Initialize(SteamNpcPrototypeRuntime runtime,
            PrototypeActor actor, Line2NPCCombatPerception sight)
        {
            _runtime = runtime;
            _actor = actor;
            _sight = sight;
            _group = GetComponent<Line2NPCGroupMember>();
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _needs = GetComponent<Line2NpcNeeds>();
            _injury = GetComponent<Line2MobileNPCInjurySystem>();
            _nextAt = Time.time + UnityEngine.Random.Range(8f, 14f);
            _nextDeliveryTripAt = Time.time + 65f;
        }

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                Level.isLoaded && Provider.hasCheats &&
                Player.player != null &&
                (Provider.clients == null || Provider.clients.Count <= 1);
        }

        private bool Safe()
        {
            return Authorized() && _group != null && _group.Joined &&
                _actor != null && !_actor.IsDead &&
                !_actor.IsRecruited &&
                _inventory != null && _inventory.UsedSlots > 1 &&
                !_actor.IsTacticallyRetreating &&
                (_sight == null ||
                 _sight.Kind == Line2NPCCombatPerception.TargetKind.None ||
                 _sight.VisibleTarget == null);
        }

        private bool HasPhysicalAccess(Transform storageRoot)
        {
            if (storageRoot == null) return false;
            Vector3 eye = transform.position + Vector3.up * 1.25f;
            Vector3 point = storageRoot.position + Vector3.up * 0.85f;
            Vector3 toward = point - eye;
            float length = toward.magnitude;
            if (length < 0.12f || length > 4.0f) return false;
            int hits = Physics.RaycastNonAlloc(eye, toward / length,
                _sightHits, length,
                RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
            if (hits >= MaxLineHits) return false;
            for (int i = 0; i < hits; i++)
            {
                Transform hit = _sightHits[i].transform;
                _sightHits[i] = default(RaycastHit);
                if (hit == null || hit == transform ||
                    hit.IsChildOf(transform) ||
                    hit == storageRoot || hit.IsChildOf(storageRoot))
                    continue;
                return false;
            }
            return true;
        }

        private bool ShouldTransfer(Item item, ItemAsset asset)
        {
            if (item == null || item.amount == 0 || asset == null ||
                asset.isPro || _inventory == null)
                return false;
            // Keep equipped combat tools, buildable pieces, ownerless
            // containers, trade identity items and native ammo state in
            // personal custody. Only survival and spare supplies qualify.
            bool useful = asset.type == EItemType.FOOD ||
                asset.type == EItemType.WATER ||
                asset.type == EItemType.MEDICAL ||
                asset.type == EItemType.SUPPLY;
            if (!useful || _inventory.CountItemType(asset.type) < 2)
                return false;
            if (asset.type == EItemType.FOOD &&
                _needs != null && _needs.Hunger < 68f) return false;
            if (asset.type == EItemType.WATER &&
                _needs != null && _needs.Thirst < 68f) return false;
            if (asset.type == EItemType.MEDICAL &&
                _injury != null && _injury.NeedsMedicalAttention) return false;
            return true;
        }

        /// <summary>
        /// A group member remembers the location of its OWN previously
        /// built central stockpile. It may ask A-Life to travel there with
        /// spare supplies, but neither the manifest nor this goal authorizes
        /// depositing into an unobserved/absent or player-owned container.
        /// Physical registry validation still happens at delivery time.
        /// </summary>
        internal bool TryGetDeliveryGoalServer(out Vector3 goal)
        {
            goal = Vector3.zero;
            if (Time.time < _nextDeliveryTripAt || !Safe() ||
                _actor.CombatMode == "total")
                return false;
            Line2NpcPersistence.PlacedBarricadeRecord record;
            if (!Line2NpcSettlementLedger.TryGetCentralStorage(
                _group.GroupId, _actor.Disposition, out record))
                return false;
            Vector3 depot = new Vector3(record.x, record.y, record.z);
            if (!Level.checkSafeIncludingClipVolumes(depot) ||
                Mathf.Abs(transform.position.y - depot.y) > 1.4f)
                return false;
            float length = Vector3.Distance(transform.position, depot);
            if (length <= 4.3f || length >= 60f)
                return false;
            bool hasSurplus = false;
            for (int i = 0; i < _inventory.UsedSlots; ++i)
            {
                Item own;
                if (!_inventory.TryGetItemSnapshot(i, out own) ||
                    own == null) continue;
                ItemAsset asset = Assets.find(EAssetType.ITEM,
                    own.id) as ItemAsset;
                if (!ShouldTransfer(own, asset)) continue;
                hasSurplus = true;
                break;
            }
            if (!hasSurplus) return false;
            // Finite trip lease. The existing actor/world survey follows A*
            // and autonomously abandons a disconnected building route.
            _nextDeliveryTripAt = Time.time + 115f;
            goal = depot;
            return true;
        }

        private void Update()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + DeliveryInterval;
            if (Time.time < _nextWorldTransferAt || !Safe() ||
                _runtime == null) return;

            // A living group leader must still exist, but its location
            // cannot by itself grant stockpile ownership.
            PrototypeActor leader;
            if (!Line2NPCGroupMember.TryGetLivingMember(_group.GroupId,
                out leader) || leader == null || leader.IsDead)
                return;
            Line2NpcPersistence.PlacedBarricadeRecord record;
            if (!Line2NpcSettlementLedger.TryGetCentralStorage(
                _group.GroupId, _actor.Disposition, out record))
                return;
            InteractableStorage storage;
            if (!_nativeCamp.TryGetRealStorage(record, out storage) ||
                storage.items == null) return;

            float distance = Vector3.Distance(transform.position,
                storage.transform.position);
            bool same = _group.Faction ==
                Line2NPCFactionPolicy.DefaultForRole(_actor.Disposition);
            if (!Line2NpcCampSupplyPolicy.MayDeliver(Authorized(), same,
                true, HasPhysicalAccess(storage.transform),
                false, true, false, false, distance))
                return;

            for (int i = _inventory.UsedSlots - 1; i >= 0; --i)
            {
                Item snapshot;
                if (!_inventory.TryGetItemSnapshot(i, out snapshot))
                    continue;
                ItemAsset asset = Assets.find(EAssetType.ITEM, snapshot.id)
                    as ItemAsset;
                if (!ShouldTransfer(snapshot, asset)) continue;

                // Native Items.tryAddItem debits no source; take exactly
                // one complete inventory slot first, preserving its real
                // ammo/quality/state bytes. Never create a copy in both.
                Item removed;
                if (!_inventory.TryTakeItemServer(i, out removed) ||
                    removed == null) continue;
                bool stored = false;
                byte before = storage.items.getItemCount();
                try
                {
                    stored = storage.items.tryAddItem(removed, false);
                }
                catch (Exception error)
                {
                    // A callback might throw after the native Jar was
                    // inserted. Comparing the real native item count is
                    // safer than minting a duplicate into donor custody.
                    stored = storage.items.getItemCount() > before;
                    ProbeLogger.Log("NPC_CAMP_STORE_CALLBACK: " +
                        error.GetType().Name + " nativeCommitted=" + stored);
                }

                if (!stored)
                {
                    if (!_inventory.TryStoreTransferredItem(removed))
                        ProbeLogger.Log("NPC_CAMP_STORE_ROLLBACK_FAILED: item=" +
                            removed.id + " donor=" + _actor.Identity);
                    continue;
                }

                // The original Items manager owns the container state and
                // its normal vanilla save. Never serialize stock twice in
                // the NPC checkpoint. Callback failure after commit must
                // NOT create a donor refund.
                try { storage.items.onStateUpdated?.Invoke(); }
                catch (Exception error)
                {
                    ProbeLogger.Log("NPC_CAMP_STORE_EVENT_FAILED: " +
                        error.GetType().Name + " (item retained natively)");
                }
                _nextWorldTransferAt = Time.time + 4f;
                _runtime.RequestPriorityCheckpoint();
                ProbeLogger.Log("NPC_CAMP_STOCK_DELIVERED: group=" +
                    _group.GroupId + " donor=" + _actor.Identity +
                    " item=" + removed.id + " amount=" + removed.amount +
                    " nativeStorage=" + record.instanceID);
                break;
            }
        }
    }
}
