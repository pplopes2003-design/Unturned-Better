using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Bounded physical A-Life aid between members of the SAME living group.
    /// Transfers exactly one already-owned native item from a donor's actual
    /// inventory into a nearby needy member's reserved slot. Does not create
    /// free food/medicine, reach across walls, access the player's inventory,
    /// drain the donor's only emergency resource, or relay through radio.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcSquadSupplySharing : MonoBehaviour
    {
        private const int MaxNeighborColliders = 48;
        private const int MaxLineHits = 16;
        private const float SampleInterval = 7.5f;
        private static float nextSharedTransferAt;
        private readonly Collider[] _neighbors =
            new Collider[MaxNeighborColliders];
        private readonly RaycastHit[] _hits = new RaycastHit[MaxLineHits];
        private PrototypeActor _owner;
        private SteamNpcPrototypeRuntime _runtime;
        private Line2NPCGroupMember _group;
        private Line2MobileNPCInventory _inventory;
        private Line2NpcNeeds _needs;
        private Line2MobileNPCInjurySystem _injury;
        private Line2NPCCombatPerception _perception;
        private float _nextAt;

        internal static void ResetSession()
        {
            nextSharedTransferAt = 0f;
        }

        internal void Initialize(SteamNpcPrototypeRuntime runtime,
            PrototypeActor owner, Line2NPCCombatPerception perception)
        {
            _runtime = runtime;
            _owner = owner;
            _perception = perception;
            _group = GetComponent<Line2NPCGroupMember>();
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _needs = GetComponent<Line2NpcNeeds>();
            _injury = GetComponent<Line2MobileNPCInjurySystem>();
            _nextAt = Time.time + UnityEngine.Random.Range(3f, 8f);
        }

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                Level.isLoaded && Player.player != null &&
                (Provider.clients == null || Provider.clients.Count <= 1);
        }

        private static bool IsBusy(Line2NPCCombatPerception perception,
            PrototypeActor actor)
        {
            return actor == null || actor.IsDead ||
                actor.IsTacticallyRetreating ||
                perception != null &&
                perception.Kind !=
                    Line2NPCCombatPerception.TargetKind.None &&
                perception.VisibleTarget != null;
        }

        private bool HasDirectSight(Transform other)
        {
            Vector3 eye = transform.position + Vector3.up * 1.1f;
            Vector3 target = other.position + Vector3.up * 1.1f;
            Vector3 ray = target - eye;
            if (ray.sqrMagnitude < 0.01f) return false;
            int n = Physics.RaycastNonAlloc(eye, ray.normalized, _hits,
                ray.magnitude, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (n >= MaxLineHits) return false;
            for (int i = 0; i < n; i++)
            {
                Transform hit = _hits[i].transform;
                _hits[i] = default(RaycastHit);
                if (hit == null || hit == transform ||
                    hit.IsChildOf(transform) ||
                    hit == other || hit.IsChildOf(other))
                    continue;
                return false;
            }
            return true;
        }

        private bool TryFindShareableItem(PrototypeActor recipient,
            out int ownedSlot, out ushort itemID)
        {
            ownedSlot = -1;
            itemID = 0;
            if (_inventory == null || _needs == null ||
                recipient == null || _owner == null) return false;
            Line2NpcNeeds hungry = recipient.GetComponent<Line2NpcNeeds>();
            Line2MobileNPCInjurySystem injured =
                recipient.GetComponent<Line2MobileNPCInjurySystem>();
            PrototypeNpcVitals vitals =
                recipient.GetComponent<PrototypeNpcVitals>();
            if (hungry == null || vitals == null) return false;

            // Medical first for a genuinely bleeding/fractured ally.
            // Next, prioritize the specific survival need actually low.
            bool medical = injured != null &&
                injured.NeedsMedicalAttention &&
                (_injury == null || !_injury.NeedsMedicalAttention) &&
                _owner.HealthFraction > 0.70f;
            bool water = hungry.Thirst < 38f && _needs.Thirst > 65f;
            bool food = hungry.Hunger < 38f && _needs.Hunger > 65f;
            if (!medical && !water && !food) return false;

            for (int slot = 0; slot < _inventory.UsedSlots; slot++)
            {
                Item item;
                if (!_inventory.TryGetItemSnapshot(slot, out item) ||
                    item == null || item.id == 0 ||
                    item.amount != 1) continue;
                ItemAsset asset = Assets.find(EAssetType.ITEM,
                    item.id) as ItemAsset;
                if (asset == null || asset.isPro) continue;

                bool suitable = medical &&
                    (item.id == 393 || item.id == 95 ||
                     item.id == 394 || item.id == 96);
                if (!suitable && water && asset is ItemWaterAsset)
                    suitable = ((ItemWaterAsset)asset).water > 0f;
                if (!suitable && food && asset is ItemFoodAsset)
                    suitable = ((ItemFoodAsset)asset).food > 0f;
                if (!suitable) continue;

                // Do not donate the donor's only emergency item. Count
                // INVENTORY slots, not fabricated hypothetical supplies.
                if (_inventory.CountItemType(asset.type) < 2)
                    continue;
                ownedSlot = slot;
                itemID = item.id;
                return true;
            }
            return false;
        }

        private void Update()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + SampleInterval;
            if (!Authorized() || Time.time < nextSharedTransferAt ||
                IsBusy(_perception, _owner) || _group == null ||
                !_group.Joined || _inventory == null || _needs == null)
                return;
            int count = Physics.OverlapSphereNonAlloc(transform.position,
                2.75f, _neighbors, ~0,
                QueryTriggerInteraction.Collide);
            // Failure closed on a crowd with a saturated physics buffer.
            if (count >= MaxNeighborColliders) return;
            for (int i = 0; i < count; i++)
            {
                Collider body = _neighbors[i];
                if (body == null) continue;
                PrototypeActor neighbor = body.GetComponentInParent<PrototypeActor>();
                if (neighbor == null || neighbor == _owner || neighbor.IsDead)
                    continue;
                Line2NPCGroupMember otherGroup =
                    neighbor.GetComponent<Line2NPCGroupMember>();
                Line2MobileNPCInventory receiver =
                    neighbor.GetComponent<Line2MobileNPCInventory>();
                if (otherGroup == null || receiver == null) continue;
                Line2NPCCombatPerception otherPerception =
                    neighbor.GetComponent<Line2NPCCombatPerception>();
                float range = Vector3.Distance(transform.position,
                    neighbor.transform.position);
                bool sameGroup = _group.GroupId > 0 &&
                    otherGroup.GroupId == _group.GroupId &&
                    otherGroup.Faction == _group.Faction;
                bool clearSight = sameGroup && HasDirectSight(neighbor.transform);
                int slot;
                ushort itemID;
                bool donorSafe = TryFindShareableItem(neighbor,
                    out slot, out itemID);
                if (!Line2NpcSquadSupplyPolicy.MayShare(
                    Authorized(), sameGroup, true, clearSight,
                    IsBusy(otherPerception, neighbor),
                    donorSafe, donorSafe, receiver.HasSpace, range))
                    continue;

                // Reserve the RECEIVER first, then remove the EXACT source
                // slot. A failed transfer can restore the original donor
                // inventory slot because removing it freed one slot.
                if (!receiver.TryReserveSlotServer()) continue;
                Item transferred = null;
                bool debited = false;
                bool stored = false;
                bool receiverReservationOutstanding = true;
                try
                {
                    Item latest;
                    if (!_inventory.TryGetItemSnapshot(slot, out latest) ||
                        latest == null || latest.id != itemID ||
                        latest.amount != 1)
                        continue;
                    debited = _inventory.TryTakeItemServer(slot,
                        out transferred);
                    if (!debited || transferred == null ||
                        transferred.id != itemID ||
                        transferred.amount != 1)
                        continue;
                    stored = receiver.TryStoreReservedItemServer(transferred);
                    if (!stored)
                    {
                        if (!_inventory.TryStoreTransferredItem(transferred))
                            ProbeLogger.Log("NPC_SQUAD_TRANSFER_ROLLBACK_FAILED: id=" +
                                itemID + " source=" + _owner.Identity);
                        debited = false; // rollback attempted, do not repeat
                        continue;
                    }
                    receiverReservationOutstanding = false;
                    nextSharedTransferAt = Time.time + 5f;
                    if (_runtime != null)
                        _runtime.RequestPriorityCheckpoint();
                    ProbeLogger.Log("NPC_SQUAD_SUPPLY_TRANSFER: donor=" +
                        _owner.Identity + " recipient=" +
                        neighbor.Identity + " item=" + itemID +
                        " (native finite inventory; local physical handoff)");
                    break;
                }
                catch (Exception error)
                {
                    if (debited && !stored && transferred != null &&
                        !_inventory.TryStoreTransferredItem(transferred))
                        ProbeLogger.Log("NPC_SQUAD_TRANSFER_ROLLBACK_FAILED: id=" +
                            itemID + " source=" + _owner.Identity);
                    ProbeLogger.Log("NPC_SQUAD_SUPPLY_FAILED: " +
                        error.GetType().Name + " donor=" + _owner.Identity);
                    // A real successful receiver store remains authoritative;
                    // we do not credit another item to the donor.
                }
                finally
                {
                    // TryStoreReservedItemServer already consumes our slot
                    // reservation on success. Do not release any unrelated
                    // reservation held by another native inventory action.
                    if (receiverReservationOutstanding)
                        receiver.ReleaseReservedSlotServer();
                }
            }
            Array.Clear(_neighbors, 0, _neighbors.Length);
        }
    }
}
