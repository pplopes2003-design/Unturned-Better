using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Adapted directly from the one-time, authentic owned-kit provisioning
    /// in Line 1 MobileNPCCombatOnlyPolicy. Vanilla assets are verified
    /// against the loaded catalog and magazine calibers. No invented ammo.
    /// Restored actors must never run this method.
    /// </summary>
    internal static class Line2NPCStartingEquipment
    {
        private struct Pair
        {
            internal ushort gun;
            internal ushort magazine;
            internal string name;
            internal Pair(ushort g, ushort m, string n)
            { gun = g; magazine = m; name = n; }
        }

        // Exact vanilla pairings from original Line 1 CombatOnlyPolicy.
        private static readonly Pair[] VanillaGuns =
        {
            new Pair(363, 6, "Maplestrike military"),
            new Pair(122, 123, "Zubeknakov ranger"),
            new Pair(112, 113, "Bluntforce 12 gauge"),
            new Pair(129, 130, "Snayperskya"),
            new Pair(297, 298, "Grizzly")
        };

        private static bool TryGetCompatible(Pair pair,
            out ItemGunAsset gun, out ItemMagazineAsset magazine)
        {
            gun = Assets.find(EAssetType.ITEM, pair.gun) as ItemGunAsset;
            magazine = Assets.find(EAssetType.ITEM, pair.magazine)
                as ItemMagazineAsset;
            return gun != null && magazine != null &&
                !gun.isPro && !magazine.isPro &&
                gun.projectile == null && gun.ammoPerShot > 0 &&
                gun.magazineCalibers != null &&
                magazine.CalibersContainAnyOfIds(gun.magazineCalibers);
        }

        internal static bool GiveFreshKitServer(
            Line2MobileNPCInventory inventory, string disposition,
            string combatMode, out string description)
        {
            description = "not eligible";
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                Player.player == null || !Level.isLoaded ||
                (Provider.clients != null && Provider.clients.Count > 1) ||
                inventory == null)
                return false;

            bool total = combatMode == "total";
            inventory.SetModeServer(total);
            // In normal mode only some NPCs are armed; no weapon is conjured
            // from thin air in subsequent frames/reloads.
            if (!total && disposition == "neutral")
                return false;
            if (!total && disposition == "friendly" &&
                UnityEngine.Random.value > 0.35f)
                return false;
            if (!total && disposition == "hostile" &&
                UnityEngine.Random.value > 0.70f)
                return false;

            // Optional *actual installed* Auto 24. If users have not installed
            // their custom content, it cannot become an NPC's phantom gun.
            Pair choice = VanillaGuns[UnityEngine.Random.Range(0, VanillaGuns.Length)];
            if (total && UnityEngine.Random.value < 0.045f)
            {
                var special = new Pair(32610, 32611, "Auto 24 custom");
                ItemGunAsset optionalGun;
                ItemMagazineAsset optionalMagazine;
                if (TryGetCompatible(special, out optionalGun, out optionalMagazine))
                    choice = special;
            }

            ItemGunAsset gun;
            ItemMagazineAsset magazine;
            if (!TryGetCompatible(choice, out gun, out magazine))
            {
                choice = VanillaGuns[0];
                if (!TryGetCompatible(choice, out gun, out magazine))
                {
                    description = "no verified native gun/magazine asset pair";
                    return false;
                }
            }

            Item native = new Item(choice.gun, EItemOrigin.ADMIN);
            if (native.state == null || native.state.Length < 18)
            {
                description = "missing native 18-byte gun state";
                return false;
            }
            // An actual compatible loaded magazine belongs to the gun. Spare
            // magazines are separate instances and are consumed on reload.
            byte[] magId = BitConverter.GetBytes(choice.magazine);
            native.state[GunStateIndices.MAGAZINE_ID] = magId[0];
            native.state[GunStateIndices.MAGAZINE_ID + 1] = magId[1];
            native.state[GunStateIndices.AMMO] = magazine.MaxAmountAsByte;
            if (native.state[GunStateIndices.AMMO] < gun.ammoPerShot)
            {
                description = "magazine too small for one trigger pull";
                return false;
            }
            if (!inventory.TryStoreTransferredItem(native))
            {
                description = "owned storage rejected gun";
                return false;
            }

            // Issue exactly TWO physical spares, not limitless reserves.
            for (int i = 0; i < 2; ++i)
            {
                Item spare = new Item(choice.magazine, EItemOrigin.ADMIN);
                if (!inventory.TryStoreTransferredItem(spare))
                    break;
            }

            // Adapt the original starting medical kit; no automatic treatment
            // is invented here before the medical executor is integrated.
            for (int i = 0; i < 2; ++i)
                GiveIfLoaded(inventory, 394); // Dressing
            GiveIfLoaded(inventory, 95);      // Bandage
            GiveIfLoaded(inventory, 96);      // Splint
            GiveIfLoaded(inventory, 15);      // Medkit
            GiveIfLoaded(inventory, 121);     // Military knife
            description = choice.name + ": finite loaded gun, 2 owned spare magazines";
            return true;
        }

        private static void GiveIfLoaded(Line2MobileNPCInventory inventory, ushort id)
        {
            ItemAsset asset = Assets.find(EAssetType.ITEM, id) as ItemAsset;
            if (asset != null && !asset.isPro)
                inventory.TryStoreTransferredItem(new Item(id, EItemOrigin.ADMIN));
        }
    }
}
