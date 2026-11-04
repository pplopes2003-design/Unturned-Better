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
        private float nextPublish;
        private string lastPublishedCode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!Dedicator.IsDedicatedServer)
                return;
            GameObject obj = new GameObject("MobileNPCFriendsHostControl");
            DontDestroyOnLoad(obj);
            obj.AddComponent<MobileNPCFriendsHostControlServer>();
        }

        private string GetServerCodePath()
        {
            return Path.Combine(Environment.CurrentDirectory, "Servers",
                ServerId, "Server", "ServerCode.dat");
        }

        private void PublishRealSteamServerCode()
        {
            if (Dedicator.serverVisibility != ESteamServerVisibility.Internet)
                return;
            CSteamID id = SteamGameServer.GetSteamID();
            if (!id.IsValid() || !id.BGameServerAccount())
                return;
            string code = id.m_SteamID.ToString();
            if (string.Equals(code, lastPublishedCode, StringComparison.Ordinal))
                return;
            try
            {
                string file = GetServerCodePath();
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string temporary = file + ".tmp";
                File.WriteAllText(temporary, code, new System.Text.UTF8Encoding(false));
                if (File.Exists(file))
                    File.Delete(file);
                File.Move(temporary, file);
                lastPublishedCode = code;
                Debug.Log("[MobileNPCFriendsHost] Steam server code available: " + code);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MobileNPCFriendsHost] Could not publish Steam code: "
                    + ex.GetType().Name);
            }
        }

        private void OnDisable()
        {
            if (Dedicator.IsDedicatedServer && Dedicator.serverID == ServerId)
            {
                try
                {
                    string file = GetServerCodePath();
                    if (File.Exists(file)) File.Delete(file);
                }
                catch (Exception) { }
            }
        }

        private void Update()
        {
            if (!Dedicator.IsDedicatedServer || Dedicator.serverID != ServerId)
                return;
            if (Provider.isServer && Level.isLoaded
                && Time.unscaledTime >= nextPublish)
            {
                nextPublish = Time.unscaledTime + 1f;
                PublishRealSteamServerCode();
            }
            if (stopAccepted || !Provider.isServer
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
