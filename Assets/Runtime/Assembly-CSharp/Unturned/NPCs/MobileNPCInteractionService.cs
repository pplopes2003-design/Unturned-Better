////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Minimal local interaction entry point for the experimental mobile NPCs.
    /// Dialogue is presented through the vanilla custom-message UI. The barter service
    /// exists separately, but a stock-selection UI is not yet connected to this flow.
    /// </summary>
    public static class MobileNPCInteractionService
    {
        private static float nextInteractionTime;

        public static bool InteractLocal(Player player, MobileNPCHumanoidDisposition disposition)
        {
            if (player == null || disposition == null || !player.channel.IsLocalPlayer)
            {
                return false;
            }

            if (!Provider.isServer)
            {
                PlayerUI.message(EPlayerMessage.NPC_CUSTOM,
                    "Este NPC ainda não está disponível para interação fora do modo single-player.");
                return false;
            }

            if (!player.life.IsAlive || disposition.GetComponent<MobileNPCHumanoidVitals>() is MobileNPCHumanoidVitals vitals
                && vitals.IsDead)
            {
                return false;
            }

            Vector3 offset = player.transform.position - disposition.transform.position;
            if (offset.sqrMagnitude > 4.0f * 4.0f)
            {
                PlayerUI.message(EPlayerMessage.NPC_CUSTOM, "Chegue mais perto para conversar.");
                return false;
            }

            if (Time.time < nextInteractionTime)
            {
                return false;
            }
            nextInteractionTime = Time.time + 0.35f;

            switch (disposition.CurrentRole)
            {
                case MobileNPCHumanoidDisposition.Role.Hostile:
                    PlayerUI.message(EPlayerMessage.NPC_CUSTOM, "O NPC é hostil e não aceita conversa.");
                    return false;

                case MobileNPCHumanoidDisposition.Role.Friendly:
                    PlayerUI.message(EPlayerMessage.NPC_CUSTOM,
                        "Sobrevivente: Olá. Quero negociar, mas a interface de troca ainda não foi conectada.");
                    return true;

                default:
                    PlayerUI.message(EPlayerMessage.NPC_CUSTOM,
                        "Sobrevivente: Não quero briga. Mantenha distância e não aponte sua arma para mim.");
                    return true;
            }
        }
    }
}
