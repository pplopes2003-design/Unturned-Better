using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// In-menu LAN host: starts a SECOND build of the SAME MODIFIED project,
    /// compiled with Unity's Windows64DedicatedServer target.
    /// Never runs a second Provider server inside the Unity client.
    /// No user-supplied executable arguments or shell are evaluated.
    /// </summary>
    internal static class MobileNPCIntegratedHostLauncher
    {
        internal const string ServerId = "UnturnedBetterFriends";
        private static Process ownedServer;
        private static string ownedServerDirectory;
        internal static bool IsRunning
        {
            get
            {
                try { return ownedServer != null && !ownedServer.HasExited; }
                catch { return false; }
            }
        }

        internal static string SuggestDirectory()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string parent = Directory.GetParent(root) != null
                ? Directory.GetParent(root).FullName : root;
            // Unity source build layout: Builds/Windows64 and
            // Builds/Windows64_Headless are peers. Do not silently pick
            // vanilla Steam U3DS, which has none of our MobileNPC code.
            string candidate = Path.Combine(parent, "Windows64_Headless");
            if (File.Exists(Path.Combine(candidate, "UnturnedBetterDedicated.marker")))
                return candidate;
            string projectBuildCandidate = Path.Combine(root, "Builds", "Windows64_Headless");
            return File.Exists(Path.Combine(projectBuildCandidate, "UnturnedBetterDedicated.marker"))
                ? projectBuildCandidate : "";
        }

        internal static bool TryStart(MobileNPCIntegratedHostConfig config, out string error)
        {
            error = null;
            if (config == null || !config.TryValidate(out error))
                return false;
            if (Provider.isConnected || Provider.isServer)
            {
                error = "Saia da partida atual antes de iniciar a hospedagem.";
                return false;
            }
            if (IsRunning)
            {
                error = "O servidor de amigos ja foi iniciado por este jogo.";
                return false;
            }
            try
            {
                string directory = string.IsNullOrWhiteSpace(config.DedicatedServerDirectory)
                    ? SuggestDirectory() : Path.GetFullPath(config.DedicatedServerDirectory);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    error = "Compile a build dedicada modificada (Builds/Windows64_Headless) e selecione sua pasta.";
                    return false;
                }
                // Accept only the official U3DS layout; never execute an
                // arbitrary path entered by a server name or Workshop metadata.
                string markerPath = Path.Combine(directory,
                    "UnturnedBetterDedicated.marker");
                if (!File.Exists(markerPath)
                    || File.ReadAllText(markerPath).Trim() != "UnturnedBetterDedicated-v1")
                {
                    error = "Servidor incompatível: use a build headless do Unturned Better, não o U3DS padrão.";
                    return false;
                }
                string executable = Path.Combine(directory, "Unturned.exe");
                if (!File.Exists(executable) || !Directory.Exists(Path.Combine(directory, "Unturned_Data")))
                {
                    error = "Build headless inválida: Unturned.exe / Unturned_Data ausentes.";
                    return false;
                }
                string saveDirectory = Path.Combine(directory, "Servers", ServerId);
                string commandDirectory = Path.Combine(saveDirectory, "Server");
                Directory.CreateDirectory(commandDirectory);
                string commands = Path.Combine(commandDirectory, "Commands.dat");
                // The dedicated server is separate: explicitly provide all
                // session options before starting, rather than mutating a
                // running server's config and losing the chosen map.
                StringBuilder lines = new StringBuilder();
                lines.Append("Map ").AppendLine(config.Map);
                lines.Append("Name ").AppendLine(config.ServerName);
                lines.Append("Port ").AppendLine(config.Port.ToString());
                lines.Append("MaxPlayers ").AppendLine(config.MaxPlayers.ToString());
                if (!string.IsNullOrEmpty(config.Password))
                    lines.Append("Password ").AppendLine(config.Password);
                File.WriteAllText(commands, lines.ToString(), new UTF8Encoding(false));
                ProcessStartInfo start = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = directory,
                    Arguments = "-batchmode -nographics +LanServer/" + ServerId,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                ownedServer = Process.Start(start);
                if (ownedServer == null)
                {
                    error = "O sistema operacional nao iniciou o U3DS.";
                    return false;
                }
                ownedServerDirectory = directory;
                UnityEngine.Debug.Log("[MobileNPCFriendsHost] Compatible modified dedicated LAN process launched. "
                    + config.CopyWithoutPassword() + " PID=" + ownedServer.Id);
                return true;
            }
            catch (Exception ex)
            {
                error = "Falha ao iniciar servidor local: " + ex.GetType().Name;
                UnityEngine.Debug.LogWarning("[MobileNPCFriendsHost] Start failed: "
                    + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        internal static bool TryStop(out string error)
        {
            error = null;
            if (!IsRunning || string.IsNullOrEmpty(ownedServerDirectory))
            {
                error = "Nao existe uma instancia de servidor local ativa.";
                return false;
            }
            try
            {
                string commandFolder = Path.Combine(ownedServerDirectory,
                    "Servers", ServerId, "Server");
                Directory.CreateDirectory(commandFolder);
                string temporary = Path.Combine(commandFolder,
                    "HostStopRequest.tmp");
                string request = Path.Combine(commandFolder,
                    "HostStopRequest.dat");
                File.WriteAllText(temporary, "SAVE_AND_SHUTDOWN_V1",
                    new UTF8Encoding(false));
                if (File.Exists(request))
                    File.Delete(request);
                File.Move(temporary, request);
                // Do not Kill(). The server performs native Save and
                // Shutdown on its next main-thread polling tick.
                UnityEngine.Debug.Log("[MobileNPCFriendsHost] Graceful local stop requested.");
                return true;
            }
            catch (Exception ex)
            {
                error = "Falha ao solicitar parada segura: " + ex.GetType().Name;
                return false;
            }
        }

        internal static void TryConnectLocal(MobileNPCIntegratedHostConfig config)
        {
            if (config == null || Provider.isConnected || !IsRunning)
                return;
            var ip = new IPv4Address("127.0.0.1");
            SteamConnectionInfo info = new SteamConnectionInfo(
                ip.value, (ushort)config.Port, config.Password);
            MenuPlayConnectUI.connect(info, false,
                MenuPlayServerInfoUI.EServerInfoOpenContext.CONNECT);
        }
    }
}
