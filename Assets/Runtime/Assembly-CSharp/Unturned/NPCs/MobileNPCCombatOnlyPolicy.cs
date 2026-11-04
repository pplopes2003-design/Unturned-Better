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
        private const ushort Bluntforce = 112;
        // Optional content pack IDs; never select if the custom assets
        // were not loaded and validated by Unturned's native registry.
        private const ushort BluntforceAuto32 = 59010;
        private const ushort BluntforceAuto32Drum = 59011;
        private const ushort TwelveGaugeShells = 113;
        private const ushort Snayperskya = 129;
        private const ushort SnayperskyaMagazine = 130;
        private const ushort Grizzly = 297;
        private const ushort GrizzlyMagazine = 298;
        private const ushort FragmentationGrenade = 254;
        private const ushort MilitaryKnife = 121;
        private const ushort Dressing = 394;
        private const ushort Bandage = 95;
        private const ushort Splint = 96;
        private const ushort Medkit = 15;

        // Genuine vanilla clothes with military-grade protection. Prefer
        // Washington desert, then PEI forest if a map omits desert assets.
        private const ushort DesertHelmet = 1010;
        private const ushort DesertTop = 1011;
        private const ushort DesertBottom = 1012;
        private const ushort DesertVest = 1013;
        private const ushort ForestHelmet = 309;
        private const ushort ForestTop = 307;
        private const ushort ForestBottom = 308;
        private const ushort ForestVest = 310;
        private const ushort Alicepack = 253;

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

            // Restored NPCs retain their real saved grenade items; the
            // executor must exist for BOTH fresh spawns and JSON restores.
            // GetComponent avoids duplicate throwers on repeated setup.
            if (npc.GetComponent<MobileNPCGrenadeThrower>() == null)
                npc.AddComponent<MobileNPCGrenadeThrower>();

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
            // Four gameplay roles; selected by weight only on FIRST spawn.
            // Every pair below must be verified against the actual asset
            // catalog and caliber before it is installed in a real item.
            int roll = Random.Range(0, 100);
            ushort primary;
            ushort spareMagazine;
            string combatRole;
            bool auto32Available =
                Assets.find(EAssetType.ITEM, BluntforceAuto32) is ItemGunAsset
                && Assets.find(EAssetType.ITEM, BluntforceAuto32Drum)
                    is ItemMagazineAsset;
            if (roll >= 94 && auto32Available)
            {
                primary = BluntforceAuto32;
                spareMagazine = BluntforceAuto32Drum;
                combatRole = "automatic-shotgun-32";
            }
            else if (roll < 30)
            {
                primary = Maplestrike;
                spareMagazine = MilitaryMagazine;
                combatRole = "assault-military";
            }
            else if (roll < 55)
            {
                primary = Zubeknakov;
                spareMagazine = RangerMagazine;
                combatRole = "assault-ranger";
            }
            else if (roll < 78)
            {
                primary = Bluntforce;
                spareMagazine = TwelveGaugeShells;
                combatRole = "shotgun";
            }
            else if (roll < 93)
            {
                primary = Snayperskya;
                spareMagazine = SnayperskyaMagazine;
                combatRole = "marksman";
            }
            else
            {
                primary = Grizzly;
                spareMagazine = GrizzlyMagazine;
                combatRole = "heavy-sniper";
            }
            ItemGunAsset gunAsset = Assets.find(EAssetType.ITEM, primary) as ItemGunAsset;
            ItemMagazineAsset magazineAsset =
                Assets.find(EAssetType.ITEM, spareMagazine) as ItemMagazineAsset;
            if (gunAsset == null || magazineAsset == null
                || gunAsset.projectile != null || gunAsset.ammoPerShot == 0
                || gunAsset.magazineCalibers == null
                || !magazineAsset.CalibersContainAnyOfIds(gunAsset.magazineCalibers))
            {
                Debug.LogWarning("[MobileNPCCombatOnly] Native gun/magazine missing "
                    + "or incompatible; trying Military Maplestrike fallback.", npc);
                primary = Maplestrike;
                spareMagazine = MilitaryMagazine;
                combatRole = "assault-fallback";
                gunAsset = Assets.find(EAssetType.ITEM, primary) as ItemGunAsset;
                magazineAsset =
                    Assets.find(EAssetType.ITEM, spareMagazine) as ItemMagazineAsset;
            }
            if (gunAsset != null && magazineAsset != null
                && gunAsset.projectile == null && gunAsset.ammoPerShot > 0
                && gunAsset.magazineCalibers != null
                && magazineAsset.CalibersContainAnyOfIds(gunAsset.magazineCalibers))
            {
                // Item(.., ADMIN) invokes native gun-state generation: its
                // initialized ammo/magazine state is read by the existing
                // MobileNPCHumanoidRangedCombat on its regular refresh.
                Item loadedGun = new Item(primary, EItemOrigin.ADMIN);
                if (loadedGun.state == null
                    || loadedGun.state.Length < GunStateIndices.AMMO + 1
                    || loadedGun.state.Length < GunStateIndices.MAGAZINE_ID + 2)
                {
                    Debug.LogWarning("[MobileNPCCombatOnly] Gun has no valid "
                        + "native loaded magazine state: " + primary, npc);
                }
                else
                {
                    // Ensure the combat loadout has a real, compatible,
                    // fully loaded magazine in the gun's native state.
                    // Spare magazines remain separate physical inventory
                    // items and ordinary reload/shot code consumes them.
                    ushort installedMagazine = System.BitConverter.ToUInt16(
                        loadedGun.state, GunStateIndices.MAGAZINE_ID);
                    ItemMagazineAsset installed = Assets.find(EAssetType.ITEM,
                        installedMagazine) as ItemMagazineAsset;
                    if (installed == null
                        || !installed.CalibersContainAnyOfIds(gunAsset.magazineCalibers)
                        || loadedGun.state[GunStateIndices.AMMO] == 0)
                    {
                        byte[] idBytes = System.BitConverter.GetBytes(spareMagazine);
                        loadedGun.state[GunStateIndices.MAGAZINE_ID] = idBytes[0];
                        loadedGun.state[GunStateIndices.MAGAZINE_ID + 1] = idBytes[1];
                        loadedGun.state[GunStateIndices.AMMO] =
                            magazineAsset.MaxAmountAsByte;
                    }
                    if (!inventory.TryStoreTransferredItem(loadedGun))
                        Debug.LogError("[MobileNPCCombatOnly] Could not store gun "
                            + primary + " on fresh spawn.", npc);
                }
                for (int i = 0; i < 3; ++i)
                    GiveIfAvailable(inventory, spareMagazine, npc);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MobileNPCCombatLoadout] role=" + combatRole
                    + " gun=" + primary + " magazine=" + spareMagazine
                    + " npc=" + npc.name, npc);
#endif
            }

            // Real server inventory items, actually consumed on damage,
            // dressing bleeding, fractures and healing missing HP.
            GiveIfAvailable(inventory, MilitaryKnife, npc);
            GiveIfAvailable(inventory, Dressing, npc);
            GiveIfAvailable(inventory, Dressing, npc);
            GiveIfAvailable(inventory, Bandage, npc);
            GiveIfAvailable(inventory, Splint, npc);
            GiveIfAvailable(inventory, Medkit, npc);
            // Real, finite, vanilla explosive: the existing thrower uses
            // sight, arc, friendly-safety, inventory debit and cooldown.
            // Most combatants carry one; some retain a second grenade.
            if (Random.value < 0.70f)
            {
                ItemThrowableAsset grenade = Assets.find(EAssetType.ITEM,
                    FragmentationGrenade) as ItemThrowableAsset;
                if (grenade != null && grenade.isExplosive
                    && grenade.throwable != null && !grenade.isSticky
                    && !grenade.explodeOnImpact && grenade.fuseLength >= 1.5f)
                {
                    GiveIfAvailable(inventory, FragmentationGrenade, npc);
                    if (Random.value < 0.20f)
                        GiveIfAvailable(inventory, FragmentationGrenade, npc);
                }
            }

            // Physical NPC armor, not a skin-only override: the existing
            // MobileNPCEquipment scans these item categories from inventory,
            // renders native HumanClothes and applies real quality-dependent
            // head/torso/limb mitigation on the server. Five owned garments
            // fit together with the ten weapon/medical items in 18 slots.
            GiveClothing(inventory, EItemType.HAT, DesertHelmet, ForestHelmet, npc);
            GiveClothing(inventory, EItemType.SHIRT, DesertTop, ForestTop, npc);
            GiveClothing(inventory, EItemType.PANTS, DesertBottom, ForestBottom, npc);
            GiveClothing(inventory, EItemType.VEST, DesertVest, ForestVest, npc);
            GiveClothing(inventory, EItemType.BACKPACK, Alicepack, 0, npc);
        }

        private static void GiveClothing(MobileNPCInventory inventory,
            EItemType category, ushort preferred, ushort alternate, GameObject npc)
        {
            ushort selected = 0;
            ItemClothingAsset first = Assets.find(EAssetType.ITEM, preferred)
                as ItemClothingAsset;
            if (first != null && first.type == category && !first.isPro)
                selected = preferred;
            if (selected == 0 && alternate != 0)
            {
                ItemClothingAsset backup = Assets.find(EAssetType.ITEM, alternate)
                    as ItemClothingAsset;
                if (backup != null && backup.type == category && !backup.isPro)
                    selected = alternate;
            }
            if (selected == 0)
            {
                Debug.LogWarning("[MobileNPCCombatOnly] Missing authentic clothing "
                    + "category=" + category + " preferred=" + preferred, npc);
                return;
            }
            GiveIfAvailable(inventory, selected, npc);
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
