////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using Steamworks;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Project-specific, physical base radio assembly, NOT a vanilla broadcast
    /// interactable. A living group operates a real 1445 walkie stored in its
    /// placed central storage; native map electricity or a live fueled,
    /// switched-on generator must power this stationary operator position.
    ///
    /// Does not generate items, grant long-distance NPC knowledge, transmit
    /// native voice packets, consume phantom batteries, or work from a vehicle.
    /// Physical NPC operator must stand next to the station to send/receive.
    /// </summary>
    internal static class MobileNPCBaseRadioStation
    {
        private const float MaximumOperatorDistance = 3.0f;
        private const float MaximumStorageAnchorDistance = 3.5f;

        internal static bool CanOperateServer(MobileNPCGroupMember member, Vector3 position)
        {
            if (!Provider.isServer || !Level.isLoaded || member == null
                || member.GroupId <= 0 || !member.HasSettlement
                || !member.TryGetLivingLeaderServer(out MobileNPCGroupMember leader)
                || leader == null || !leader.HasSettlement)
                return false;

            MobileNPCSettlementBuilder builder = leader.GetComponent<MobileNPCSettlementBuilder>();
            if (builder == null || !builder.TryGetCentralStorageForRadioServer(
                out InteractableStorage storage) || storage == null
                || !storage.gameObject.activeInHierarchy || storage.items == null
                || storage.owner != CSteamID.Nil || storage.group != CSteamID.Nil
                || (storage.transform.position - member.GroupAnchor).sqrMagnitude
                    > MaximumStorageAnchorDistance * MaximumStorageAnchorDistance
                || (storage.transform.position - position).sqrMagnitude
                    > MaximumOperatorDistance * MaximumOperatorDistance)
                return false;

            // Real vanilla item 1445 must remain inside the physical, saved
            // base container. Removing or looting it instantly disables the
            // improvised station for the next channel operation.
            bool hasStoredWalkie = false;
            for (int i = 0; i < storage.items.getItemCount(); i++)
            {
                ItemJar jar = storage.items.getItem((byte)i);
                if (jar != null && jar.item != null
                    && jar.item.id == MobileNPCRadioOutbox.VanillaWalkieTalkieID)
                {
                    hasStoredWalkie = true;
                    break;
                }
            }
            if (!hasStoredWalkie)
                return false;

            // Native power conditions. Map-wide global electricity is a
            // vanilla supported setting; otherwise ONLY powered+fueled native
            // world generators within their real wiring range qualify.
            bool mapHasPower = Level.info != null && Level.info.configData != null
                && Level.info.configData.Has_Global_Electricity;
            return mapHasPower
                || InteractableGenerator.IsWorldPositionPowered(storage.transform.position);
        }
    }
}
