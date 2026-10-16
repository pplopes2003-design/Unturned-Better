////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Unturned Better mobile NPC source integration.             //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Real local merchant courier-to-vendor handoff. No imaginary
    /// stock, radio teleportation, or infinite vendor goods: only
    /// already-owned simulated items move between two nearby living
    /// members of the same actual trading group.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCInventory))]
    public sealed class MobileNPCTraderLogistics : MonoBehaviour
    {
        private MobileNPCGroupMember member;
        private MobileNPCInventory inventory;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCBrainDriver brain;
        private float nextDeliveryAt;

        private void Awake()
        {
            inventory = GetComponent<MobileNPCInventory>();
            // The inactive bootstrap installs GroupMember after other
            // components, so resolve it again on the first real update.
            member = GetComponent<MobileNPCGroupMember>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            brain = GetComponent<MobileNPCBrainDriver>();
            nextDeliveryAt = Time.time + Random.Range(1f, 2.5f);
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || MobileNPCSettings.CombatMode
                || Time.time < nextDeliveryAt)
                return;
            nextDeliveryAt = Time.time + 2f;
            if (member == null)
                member = GetComponent<MobileNPCGroupMember>();
            if (member == null || inventory == null || vitals == null || vitals.IsDead
                || brain == null || brain.Target != null
                || brain.CurrentGoal != MobileNPCTactics.Goal.None
                || !member.HasSettlement || inventory.FreeSlots > 2
                || MobileNPCArchetypePolicy.ForNPC(gameObject)
                    != MobileNPCArchetypePolicy.Preset.Trader
                || !member.TryGetLivingLeaderServer(out MobileNPCGroupMember vendor)
                || vendor == null || vendor == member
                || MobileNPCArchetypePolicy.ForNPC(vendor.gameObject)
                    != MobileNPCArchetypePolicy.Preset.Trader)
                return;

            MobileNPCHumanoidVitals vendorVitals = vendor.GetComponent<MobileNPCHumanoidVitals>();
            MobileNPCBrainDriver vendorBrain = vendor.GetComponent<MobileNPCBrainDriver>();
            MobileNPCInventory vendorStock = vendor.GetComponent<MobileNPCInventory>();
            if (vendorVitals == null || vendorVitals.IsDead || vendorStock == null
                || !vendorStock.HasSpace || vendorBrain == null
                || vendorBrain.Target != null
                || vendorBrain.CurrentGoal != MobileNPCTactics.Goal.None
                || (vendor.transform.position - transform.position).sqrMagnitude > 3.5f * 3.5f
                || (vendor.transform.position - member.GroupAnchor).sqrMagnitude > 9f * 9f)
                return;

            // One genuinely carried, transferable surplus good per delivery
            // interval. Preserve own nutrition, medicine and last defensive
            // weapon. Final goods taxonomy can be user-configured later.
            for (int slot = inventory.UsedSlots - 1; slot >= 0; --slot)
            {
                if (!inventory.TryGetItemSnapshot(slot, out Item owned) || owned == null)
                    continue;
                ItemAsset asset = Assets.find(EAssetType.ITEM, owned.id) as ItemAsset;
                if (asset == null || asset.type == EItemType.MEDICAL
                    || asset.type == EItemType.FOOD || asset.type == EItemType.WATER
                    || asset.type == EItemType.GENERATOR
                    || asset.type == EItemType.BARRICADE
                    || asset.type == EItemType.STORAGE
                    || asset.type == EItemType.STRUCTURE
                    || (asset.type == EItemType.GUN && inventory.CountItemType(EItemType.GUN) <= 1)
                    || (asset.type == EItemType.MELEE
                        && inventory.CountItemType(EItemType.MELEE) <= 1
                        && inventory.CountItemType(EItemType.GUN) == 0))
                    continue;

                // Reserve the destination FIRST, then remove from origin.
                // Any failed store is rolled back; neither NPC may conjure
                // a second copy or magically transfer from across the map.
                if (!vendorStock.TryReserveSlotServer())
                    return;
                if (!inventory.TryTakeItemServer(slot, out Item transferred))
                {
                    vendorStock.ReleaseReservedSlotServer();
                    continue;
                }
                if (!vendorStock.TryStoreReservedItemServer(transferred))
                {
                    vendorStock.ReleaseReservedSlotServer();
                    if (!inventory.TryStoreTransferredItem(transferred))
                        Debug.LogError("[MobileNPCTraderLogistics] CRITICAL: seller rejected item "
                            + "and courier rollback failed. Item=" + transferred.id
                            + ", courier=" + name, this);
                    return;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MobileNPCTraderLogistics] Real goods delivered"
                    + " courier=" + name + " seller=" + vendor.name
                    + " item=" + transferred.id
                    + " sellerStockSlots=" + vendorStock.UsedSlots, this);
#endif
                return;
            }
        }
    }
}
