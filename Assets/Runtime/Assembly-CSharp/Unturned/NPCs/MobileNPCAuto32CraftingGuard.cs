////////////////////////////////////////////////////////////////////////////////////////
// Unturned Better optional Auto-32 ammo conservation rule. PlayerCrafting V2 hook.
// Applies ONLY to the dedicated 59011 drum blueprint.
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    internal static class MobileNPCAuto32CraftingGuard
    {
        private const ushort DrumId = 59011;
        private const ushort VanillaShellsId = 113;
        private const byte FullShellPack = 8;
        private const int PacksPerDrum = 4;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            PlayerCrafting.OnCraftBlueprintRequestedV2 -= VerifyFullShellPacks;
            PlayerCrafting.OnCraftBlueprintRequestedV2 += VerifyFullShellPacks;
        }

        /// <summary>
        /// A legacy "Supply" blueprint counts items, not ammunition, so four
        /// 1-round magazines could otherwise manufacture 32 shells.
        /// Fail closed on ANY partial 12-gauge pack because the native
        /// crafting inventory sorter may consume it ahead of a full pack.
        /// The actual four items are still removed by native PlayerCrafting.
        /// </summary>
        private static void VerifyFullShellPacks(PlayerCrafting crafting,
            ref Blueprint blueprint, ref bool shouldAllow)
        {
            if (!shouldAllow || !Provider.isServer || blueprint == null
                || blueprint.GetOwnerAsset() == null
                || blueprint.GetOwnerAsset().id != DrumId)
                return;
            Player player = crafting != null ? crafting.player : null;
            if (player == null || player.inventory == null)
            {
                shouldAllow = false;
                return;
            }

            List<InventorySearch> found = new List<InventorySearch>(8);
            player.inventory.search(found, VanillaShellsId, true, true);
            int full = 0;
            bool partial = false;
            for (int i = 0; i < found.Count; ++i)
            {
                Item item = found[i].jar != null ? found[i].jar.item : null;
                if (item == null) continue;
                if (item.amount == FullShellPack) ++full;
                else partial = true;
            }
            shouldAllow = full >= PacksPerDrum && !partial;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!shouldAllow)
                Debug.Log("[MobileNPCAuto32] Drum crafting denied: requires 4 full"
                    + " vanilla 8-shell magazines; partial shell packs must"
                    + " be moved out of player inventory before assembly.");
#endif
        }
    }
}
