using System;
using System.IO;
using Steamworks;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Local controller for a modified headless Unturned Better server.
    /// The hosting client requests shutdown by writing a single-purpose
    /// command file in the dedicated server's own save directory.
    /// No arbitrary command text or socket is accepted.
    /// </summary>
    internal sealed class MobileNPCFriendsHostControlServer : MonoBehaviour
    {
        private const string ServerId = "UnturnedBetterFriends";
        private float nextPoll;
        private bool stopAccepted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Dedicator.IsDedicatedServer)
                return;
            GameObject obj = new GameObject("MobileNPCFriendsHostControl");
            DontDestroyOnLoad(obj);
            obj.AddComponent<MobileNPCFriendsHostControlServer>();
        }

        private void Update()
        {
            if (stopAccepted || !Provider.isServer
                || !Dedicator.IsDedicatedServer || Dedicator.serverID != ServerId
                || Time.unscaledTime < nextPoll)
                return;
            nextPoll = Time.unscaledTime + 0.5f;
            try
            {
                string requestFile = Path.Combine(
                    Environment.CurrentDirectory, "Servers", ServerId,
                    "Server", "HostStopRequest.dat");
                if (!File.Exists(requestFile))
                    return;
                string request = File.ReadAllText(requestFile).Trim();
                File.Delete(requestFile);
                if (request != "SAVE_AND_SHUTDOWN_V1")
                {
                    Debug.LogWarning("[MobileNPCFriendsHost] Rejected unknown host command.");
                    return;
                }
                stopAccepted = true;
                // Native server commands save all world/player data, then
                // schedule a brief graceful shutdown. NPC-specific save
                // handlers still need verification in the dedicated build.
                bool saveAccepted = Commander.execute(CSteamID.Nil, "save");
                Debug.Log("[MobileNPCFriendsHost] Local save requested; "
                    + "nativeSaveCommandAccepted=" + saveAccepted);
                Commander.execute(CSteamID.Nil, "shutdown 5");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MobileNPCFriendsHost] Local stop request failed: "
                    + ex.GetType().Name);
            }
        }
    }
}
