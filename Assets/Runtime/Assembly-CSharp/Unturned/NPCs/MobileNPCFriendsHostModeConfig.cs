using System;
using System.IO;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Dedicated servers do not share the client's PlayerPrefs. Apply the
    /// selected in-menu NPC mode before population restoration/spawning.
    /// </summary>
    internal static class MobileNPCFriendsHostModeConfig
    {
        internal static void ApplyServerMode()
        {
            if (!Provider.isServer || !Dedicator.IsDedicatedServer
                || Dedicator.serverID != MobileNPCIntegratedHostLauncher.ServerId)
                return;

            try
            {
                string path = Path.Combine(Environment.CurrentDirectory,
                    "Servers", Dedicator.serverID, "Server", "MobileNPCGameMode.dat");
                if (!File.Exists(path))
                {
                    Debug.LogWarning("[MobileNPCFriendsHost] NPC game mode config missing; "
                        + "using dedicated server settings.");
                    return;
                }
                if (!int.TryParse(File.ReadAllText(path).Trim(), out int value)
                    || value < 0 || value > 2)
                {
                    Debug.LogWarning("[MobileNPCFriendsHost] Invalid NPC mode; "
                        + "dedicated server preferences remain in effect.");
                    return;
                }
                MobileNPCSettings.CurrentGameMode = (MobileNPCSettings.GameMode)value;
                Debug.Log("[MobileNPCFriendsHost] Applied authoritative NPC mode: "
                    + MobileNPCSettings.CurrentGameMode);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MobileNPCFriendsHost] Failed to read NPC mode: "
                    + ex.GetType().Name);
            }
        }
    }
}
