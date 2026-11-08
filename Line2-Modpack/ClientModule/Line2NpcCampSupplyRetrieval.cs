using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Completes a finite A-Life food/water/medicine loop: a needy living
    /// NPC can physically visit its OWN native camp chest and withdraw an
    /// existing ItemJar. Never duplicates an item in the NPC save or
    /// invents contents from a saved settlement manifest.
    ///
    /// Native Items.removeItem is the source commit. After it, check the
    /// exact ItemJar reference (callbacks can throw before/after removal),
    /// then credit ONE reserved recipient inventory slot. On rejection
    /// restore the original Item into native Items. Never dropItem or
    /// fabricate a second native instance.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcCampSupplyRetrieval : MonoBehaviour
    {
        private const float CheckSeconds = 5.5f;
        private const float TripCooldown = 65f;
        private const int MaxRayHits = 16;
        private readonly RaycastHit[] _hits = new RaycastHit[MaxRayHits];
        private readonly Line2NpcNativeCampRegistry _native =
            new Line2NpcNativeCampRegistry();
        private PrototypeActor _actor;
        private SteamNpcPrototypeRuntime _runtime;
        private Line2NPCGroupMember _membership;
        private Line2MobileNPCInventory _inventory;
        private Line2NpcNeeds _needs;
        private Line2MobileNPCInjurySystem _injuries;
        private Line2NPCCombatPerception _sight;
        private float _nextCheckAt;
        private float _nextTripAt;
        private float _nextLogAt;

        internal void Initialize(SteamNpcPrototypeRuntime runtime,
            PrototypeActor actor, Line2NPCCombatPerception sight)
        {
            _runtime = runtime;
            _actor = actor;
            _sight = sight;
            _membership = GetComponent<Line2NPCGroupMember>();
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _needs = GetComponent<Line2NpcNeeds>();
            _injuries = GetComponent<Line2MobileNPCInjurySystem>();
            _nextCheckAt = Time.time + UnityEngine.Random.Range(2f, 6f);
            _nextTripAt = Time.time + 14f;
        }

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                Level.isLoaded && Provider.hasCheats &&
                Player.player != null &&
                (Provider.clients == null || Provider.clients.Count <= 1);
        }

        private bool NeedsFood()
        {
            return _needs != null && _needs.Hunger < 40f;
        }

        private bool NeedsWater()
        {
            return _needs != null && _needs.Thirst < 40f;
        }

        private bool NeedsMedicine()
        {
            return (_injuries != null &&
                _injuries.NeedsMedicalAttention) ||
                (_actor != null && _actor.HealthFraction < 0.55f);
        }

        private bool SafeToRequest()
        {
            return Authorized() && _actor != null && !_actor.IsDead &&
                !_actor.IsRecruited && _actor.CombatMode != "total" &&
                !_actor.IsTacticallyRetreating &&
                _membership != null && _membership.Joined &&
                _inventory != null && _inventory.HasSpace &&
                (NeedsFood() || NeedsWater() || NeedsMedicine()) &&
                (_sight == null ||
                 _sight.Kind == Line2NPCCombatPerception.TargetKind.None ||
                 _sight.VisibleTarget == null);
        }

        private bool IsUseful(Item item)
        {
            if (item == null || item.id == 0 || item.amount == 0)
                return false;
            ItemAsset asset = Assets.find(EAssetType.ITEM, item.id)
                as ItemAsset;
            if (asset == null || asset.isPro) return false;
            if (NeedsMedicine() &&
                (item.id == 393 || item.id == 95 ||
                 item.id == 394 || item.id == 96)) return true;
            if (NeedsWater() && asset is ItemWaterAsset)
                return ((ItemWaterAsset)asset).water > 0f;
            if (NeedsFood() && asset is ItemFoodAsset)
                return ((ItemFoodAsset)asset).food > 0f;
            return false;
        }

        private bool TryGetOwnStorage(
            out Line2NpcPersistence.PlacedBarricadeRecord record,
            out InteractableStorage chest)
        {
            record = null;
            chest = null;
            if (!SafeToRequest() ||
                _membership.Faction !=
                    Line2NPCFactionPolicy.DefaultForRole(
                        _actor.Disposition) ||
                !Line2NpcSettlementLedger.TryGetCentralStorage(
                    _membership.GroupId, _actor.Disposition,
                    out record))
                return false;
            // This checks the REAL server BarricadeManager drop (matching
            // saved instance ID, asset ID, position and zero owner/group).
            // Player-placed chests with the same asset do not qualify.
            return _native.TryGetRealStorage(record, out chest) &&
                chest != null && chest.items != null &&
                !chest.isOpen && chest.opener == null;
        }

        /// <summary>
        /// Offer ONE A* travel objective to a known group depot. The item
        /// content is not visible at long distance: actual stock is only
        /// inspected after arriving. A failed route must be abandoned by
        /// the world's physical-progress monitor.
        /// </summary>
        internal bool TryGetEmergencySupplyGoalServer(out Vector3 goal)
        {
            goal = Vector3.zero;
            if (Time.time < _nextTripAt || !SafeToRequest())
                return false;
            Line2NpcPersistence.PlacedBarricadeRecord record;
            InteractableStorage chest;
            if (!TryGetOwnStorage(out record, out chest))
                return false;
            Vector3 center = chest.transform.position;
            Vector3 delta = transform.position - center;
            delta.y = 0f;
            float separation = delta.magnitude;
            if (separation < 4.2f || separation > 60f ||
                Mathf.Abs(center.y - transform.position.y) > 1.3f)
                return false;

            // Never plan a path *through the solid chest*. Select a nearby
            // supported standing point on this side of it, with no portal
            // invention, collider phasing or arbitrary high-floor snap.
            Vector3 proposed = center +
                delta / separation * 2.1f;
            RaycastHit floor;
            if (!Physics.Raycast(proposed + Vector3.up * 2.0f,
                Vector3.down, out floor, 4.5f, RayMasks.GROUND2,
                QueryTriggerInteraction.Ignore) ||
                floor.collider == null || floor.normal.y < 0.74f)
                return false;
            Vector3 foot = floor.point + Vector3.up * 0.07f;
            if (Mathf.Abs(foot.y - transform.position.y) > 0.8f ||
                !Level.checkSafeIncludingClipVolumes(foot) ||
                Physics.CheckCapsule(foot + Vector3.up * 0.55f,
                    foot + Vector3.up * 1.55f, 0.32f,
                    RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore))
                return false;

            _nextTripAt = Time.time + TripCooldown;
            goal = foot;
            return true;
        }

        internal void ReportRouteFailureServer()
        {
            _nextTripAt = Mathf.Max(_nextTripAt, Time.time + 55f);
        }

        private bool HasDirectAccess(Transform chest)
        {
            if (chest == null) return false;
            Vector3 eye = transform.position + Vector3.up * 1.2f;
            Vector3 aim = chest.position + Vector3.up * 0.75f;
            Vector3 toward = aim - eye;
            float length = toward.magnitude;
            if (length < 0.15f || length > 3.35f) return false;
            int count = Physics.RaycastNonAlloc(eye, toward / length,
                _hits, length, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (count >= MaxRayHits) return false;
            for (int i = 0; i < count; i++)
            {
                Transform hit = _hits[i].transform;
                _hits[i] = default(RaycastHit);
                if (hit == null || hit == transform ||
                    hit.IsChildOf(transform) || hit == chest ||
                    hit.IsChildOf(chest))
                    continue;
                return false;
            }
            return true;
        }

        private void Update()
        {
            if (Time.time < _nextCheckAt) return;
            _nextCheckAt = Time.time + CheckSeconds;
            if (!SafeToRequest() || _runtime == null) return;

            Line2NpcPersistence.PlacedBarricadeRecord record;
            InteractableStorage chest;
            if (!TryGetOwnStorage(out record, out chest))
                return;
            float meters = Vector3.Distance(
                transform.position, chest.transform.position);
            if (meters > 3.35f) return;
            // Inventory is ordinary vanilla Items. The saved NPC manifest
            // does not contain a mirror of this chest's contents.
            Items nativeItems = chest.items;
            int limit = Mathf.Min(30, nativeItems.getItemCount());
            for (int index = 0; index < limit; index++)
            {
                ItemJar jar = nativeItems.getItem((byte)index);
                if (jar == null || jar.item == null ||
                    !IsUseful(jar.item)) continue;
                bool same = _membership.Faction ==
                    Line2NPCFactionPolicy.DefaultForRole(
                        _actor.Disposition);
                bool sight = HasDirectAccess(chest.transform);
                bool allowed = Line2NpcCampWithdrawalPolicy.MayWithdraw(
                    Authorized(), same && _membership.GroupId > 0,
                    true, chest.owner.m_SteamID == 0ul &&
                        chest.group.m_SteamID == 0ul,
                    sight, IsUseful(jar.item),
                    _sight == null || _sight.Kind ==
                        Line2NPCCombatPerception.TargetKind.None,
                    _inventory.HasSpace, nativeItems.containsItem(jar),
                    meters);
                if (!allowed || !_inventory.TryReserveSlotServer())
                    return;

                Item exact = new Item(jar.item.id, jar.item.amount,
                    jar.item.quality, jar.item.state != null ?
                    (byte[])jar.item.state.Clone() : new byte[0]);
                bool removed = false;
                bool stored = false;
                try
                {
                    try { nativeItems.removeItem((byte)index); }
                    catch (Exception e)
                    {
                        // Items.removeItem calls mod hooks before removing
                        // the Jar, and onStateUpdated afterwards. Inspect
                        // the actual Jar reference rather than assuming the
                        // exception means no mutation occurred.
                        ProbeLogger.Log("NPC_CAMP_WITHDRAW_NATIVE_CALLBACK: " +
                            e.GetType().Name);
                    }
                    removed = !nativeItems.containsItem(jar);
                    if (!removed) continue;
                    stored = _inventory.TryStoreReservedItemServer(exact);
                    if (!stored)
                    {
                        RestoreToNative(nativeItems, exact);
                        continue;
                    }
                    if (_runtime != null)
                        _runtime.RequestPriorityCheckpoint();
                    ProbeLogger.Log("NPC_CAMP_WITHDRAW: actor=" +
                        _actor.Identity + " group=" +
                        _membership.GroupId + " item=" + exact.id +
                        " amount=" + exact.amount +
                        " nativeStorage=" + record.instanceID);
                    return;
                }
                catch (Exception error)
                {
                    if (removed && !stored)
                        RestoreToNative(nativeItems, exact);
                    if (Time.time >= _nextLogAt)
                    {
                        _nextLogAt = Time.time + 10f;
                        ProbeLogger.Log("NPC_CAMP_WITHDRAW_FAILED: " +
                            error.GetType().Name + " group=" +
                            _membership.GroupId);
                    }
                    return;
                }
                finally
                {
                    // The reservation was consumed by success. No other
                    // inventory reservation should be released.
                    if (!stored)
                        _inventory.ReleaseReservedSlotServer();
                }
            }
        }

        private void RestoreToNative(Items nativeItems, Item exact)
        {
            bool restored = false;
            int before = nativeItems.getItemCount();
            try { restored = nativeItems.tryAddItem(exact, true); }
            catch (Exception error)
            {
                restored = nativeItems.getItemCount() > before;
                ProbeLogger.Log("NPC_CAMP_WITHDRAW_ROLLBACK_CALLBACK: " +
                    error.GetType().Name + " restored=" + restored);
            }
            if (!restored)
                ProbeLogger.Log("NPC_CAMP_WITHDRAW_ROLLBACK_FAILED: " +
                    "manual recovery required, item=" + exact.id +
                    " actor=" + _actor.Identity);
        }
    }
}
