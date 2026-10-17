////////////////////////////////////////////////////////////////////////////////////////
// Combat-only sandbox configuration. Deliberately does not alter vanilla items,      //
// normal A-Life professions, or existing single-player world snapshots.             //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    internal static class MobileNPCCombatOnlyPolicy
    {
        // Native IDs confirmed in original game bundles / official item table:
        // Maplestrike 363 -> Military Magazine 6
        // Zubeknakov 122 -> Ranger Magazine 123
        // Military Knife 121; Dressing 394; Bandage 95;
        // Splint 96; Medkit 15. NEVER generate mod-only fake bullets.
        private const ushort Maplestrike = 363;
        private const ushort MilitaryMagazine = 6;
        private const ushort Zubeknakov = 122;
        private const ushort RangerMagazine = 123;
        private const ushort MilitaryKnife = 121;
        private const ushort Dressing = 394;
        private const ushort Bandage = 95;
        private const ushort Splint = 96;
        private const ushort Medkit = 15;

        /// <summary>
        /// Called exactly once after the cloned actor is initialized, before
        /// the spawn service returns. On save restoration only configure its
        /// role/AI and restore its actual saved items; never gift another kit.
        /// </summary>
        internal static void ConfigureSpawnServer(GameObject npc, bool freshSpawn)
        {
            if (!Provider.isServer || npc == null || !MobileNPCSettings.CombatOnly)
                return;

            MobileNPCHumanoidDisposition disposition =
                npc.GetComponent<MobileNPCHumanoidDisposition>();
            if (disposition != null
                && disposition.CurrentRole != MobileNPCHumanoidDisposition.Role.Hostile)
                disposition.SetRoleServer(MobileNPCHumanoidDisposition.Role.Hostile);

            // Disable optional civilian subsystems, not the tactical brain,
            // physical locomotion, first-hand perception, wound treatment,
            // group combat or death/loot persistence.
            Disable<MobileNPCSurvivalNeeds>(npc);
            Disable<MobileNPCLooting>(npc);
            Disable<MobileNPCInteriorSurvey>(npc);
            Disable<MobileNPCSettlementPlanner>(npc);
            Disable<MobileNPCSettlementBuilder>(npc);
            Disable<MobileNPCTraderLogistics>(npc);
            Disable<MobileNPCBaseGeneratorOperator>(npc);
            Disable<MobileNPCAirdropRumor>(npc);
            Disable<MobileNPCUsefulLootRumor>(npc);
            Disable<MobileNPCCompanionContract>(npc);
            Disable<MobileNPCTerritoryRaidPlanner>(npc);
            Disable<MobileNPCUnknownBaseIntel>(npc);

            if (freshSpawn)
                GrantStartingKitServer(npc);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MobileNPCInventory inventory = npc.GetComponent<MobileNPCInventory>();
            Debug.Log("[MobileNPCCombatOnly] Spawn npc=" + npc.name
                + " hostile=" + (disposition != null
                    && disposition.CurrentRole == MobileNPCHumanoidDisposition.Role.Hostile)
                + " kit=" + (freshSpawn ? "issued-once" : "restore-from-JSON")
                + " inventorySlots=" + (inventory != null ? inventory.UsedSlots : -1),
                npc);
#endif
        }

        private static void Disable<T>(GameObject npc) where T : Behaviour
        {
            T behaviour = npc.GetComponent<T>();
            if (behaviour != null)
                behaviour.enabled = false;
        }

        private static void GrantStartingKitServer(GameObject npc)
        {
            MobileNPCInventory inventory = npc.GetComponent<MobileNPCInventory>();
            if (inventory == null)
            {
                Debug.LogError("[MobileNPCCombatOnly] Missing native NPC inventory.", npc);
                return;
            }

            // Fresh world-spawn only. Admin-origin state supplies an actual
            // compatible loaded magazine and full quality, not infinite ammo.
            bool useRanger = Random.value < 0.5f;
            ushort primary = useRanger ? Zubeknakov : Maplestrike;
            ushort spareMagazine = useRanger ? RangerMagazine : MilitaryMagazine;
            ItemGunAsset gunAsset = Assets.find(EAssetType.ITEM, primary) as ItemGunAsset;
            ItemMagazineAsset magazineAsset =
                Assets.find(EAssetType.ITEM, spareMagazine) as ItemMagazineAsset;
            if (gunAsset == null || magazineAsset == null
                || gunAsset.projectile != null
                || gunAsset.magazineCalibers == null
                || !magazineAsset.CalibersContainAnyOfIds(gunAsset.magazineCalibers))
            {
                Debug.LogWarning("[MobileNPCCombatOnly] Native gun/magazine missing "
                    + "or incompatible; trying Military Maplestrike fallback.", npc);
                primary = Maplestrike;
                spareMagazine = MilitaryMagazine;
                gunAsset = Assets.find(EAssetType.ITEM, primary) as ItemGunAsset;
                magazineAsset =
                    Assets.find(EAssetType.ITEM, spareMagazine) as ItemMagazineAsset;
            }
            if (gunAsset != null && magazineAsset != null
                && gunAsset.projectile == null
                && gunAsset.magazineCalibers != null
                && magazineAsset.CalibersContainAnyOfIds(gunAsset.magazineCalibers))
            {
                // Item(.., ADMIN) invokes native gun-state generation: its
                // initialized ammo/magazine state is read by the existing
                // MobileNPCHumanoidRangedCombat on its regular refresh.
                Item loadedGun = new Item(primary, EItemOrigin.ADMIN);
                if (loadedGun.state == null
                    || loadedGun.state.Length < GunStateIndices.AMMO + 1)
                {
                    Debug.LogWarning("[MobileNPCCombatOnly] Gun has no valid "
                        + "native loaded magazine state: " + primary, npc);
                }
                else if (!inventory.TryStoreTransferredItem(loadedGun))
                {
                    Debug.LogError("[MobileNPCCombatOnly] Could not store gun "
                        + primary + " on fresh spawn.", npc);
                }
                for (int i = 0; i < 3; ++i)
                    GiveIfAvailable(inventory, spareMagazine, npc);
            }

            // Real server inventory items, actually consumed on damage,
            // dressing bleeding, fractures and healing missing HP.
            GiveIfAvailable(inventory, MilitaryKnife, npc);
            GiveIfAvailable(inventory, Dressing, npc);
            GiveIfAvailable(inventory, Dressing, npc);
            GiveIfAvailable(inventory, Bandage, npc);
            GiveIfAvailable(inventory, Splint, npc);
            GiveIfAvailable(inventory, Medkit, npc);
        }

        private static void GiveIfAvailable(MobileNPCInventory inventory,
            ushort itemID, GameObject npc)
        {
            ItemAsset asset = Assets.find(EAssetType.ITEM, itemID) as ItemAsset;
            if (asset == null)
            {
                Debug.LogWarning("[MobileNPCCombatOnly] Vanilla item unavailable: "
                    + itemID, npc);
                return;
            }
            if (!inventory.TryStoreTransferredItem(new Item(itemID, EItemOrigin.ADMIN)))
                Debug.LogWarning("[MobileNPCCombatOnly] Combat kit storage full; "
                    + "item=" + itemID + ".", npc);
        }
    }
}
