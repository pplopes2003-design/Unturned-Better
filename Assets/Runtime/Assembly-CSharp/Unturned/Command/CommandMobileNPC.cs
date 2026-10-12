////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using Steamworks;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>Developer-only local single-player chat command for nearby mobile NPC smoke tests.</summary>
    public sealed class CommandMobileNPC : Command
    {
        protected override void execute(CSteamID executorID, string parameter)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer)
            {
                CommandWindow.LogError("The mobile NPC spawn chat command is available only in local single-player.");
                return;
            }

            // Use Unturned's native cheats switch as the single gate for manual test tools.
            if (!Provider.hasCheats)
            {
                CommandWindow.LogError("Enable cheats in Unturned before using mobile NPC test commands.");
                return;
            }

            string[] parts = (parameter ?? string.Empty).Split(new[] { '/', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], "spawn", System.StringComparison.OrdinalIgnoreCase))
            {
                CommandWindow.Log("Usage: /mobilenpc spawn/hostile (or spawn/friendly, spawn/neutral)");
                return;
            }

            MobileNPCHumanoidDisposition.Role role = MobileNPCSettings.DefaultRole;
            if (parts.Length > 2)
            {
                CommandWindow.LogError("Usage: /mobilenpc spawn [hostile|friendly|neutral]");
                return;
            }

            if (parts.Length == 2)
            {
                switch (parts[1].ToLowerInvariant())
                {
                    case "hostile":
                        role = MobileNPCHumanoidDisposition.Role.Hostile;
                        break;
                    case "friendly":
                        role = MobileNPCHumanoidDisposition.Role.Friendly;
                        break;
                    case "neutral":
                        role = MobileNPCHumanoidDisposition.Role.Neutral;
                        break;
                    default:
                        CommandWindow.LogError("Unknown NPC role. Use hostile, friendly, or neutral.");
                        return;
                }
            }

            MobileNPCPopulationDirector director = Object.FindObjectOfType<MobileNPCPopulationDirector>();
            if (director == null)
            {
                CommandWindow.LogError("Mobile NPC population director was not found. Ensure the mobile NPC bootstrap is active.");
                return;
            }

            if (director.TrySpawnNPCNearPlayer(role, true))
            {
                CommandWindow.Log("Spawned one nearby mobile NPC with role: " + role + ".");
            }
            else
            {
                CommandWindow.LogError("Could not spawn a mobile NPC. Check the game log for the reason (population cap, disabled setting, missing prefab, or unsafe ground).");
            }
        }

        public CommandMobileNPC(Local newLocalization)
        {
            localization = newLocalization;
            _command = "mobilenpc";
            _info = "Spawn a nearby mobile NPC in local single-player.";
            _help = "spawn [hostile|friendly|neutral]";
        }
    }
}