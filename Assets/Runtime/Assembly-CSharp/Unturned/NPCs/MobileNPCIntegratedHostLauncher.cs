using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// In-menu LAN host: starts the official, SEPARATELY INSTALLED U3DS.
    /// Never runs a second Provider server inside the Unity client.
    /// No user-supplied executable arguments or shell are evaluated.
    /// </summary>
    internal static class MobileNPCIntegratedHostLauncher
    {
        internal const string ServerId = "UnturnedBetterFriends";
        private static Process ownedServer;
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
            string candidate = Path.Combine(parent, "U3DS");
            return Directory.Exists(candidate) ? candidate : "";
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
                    error = "Instale o Unturned Dedicated Server (U3DS) pela Steam e selecione sua pasta.";
                    return false;
                }
                // Accept only the official U3DS layout; never execute an
                // arbitrary path entered by a server name or Workshop metadata.
                string executable = Path.Combine(directory, "Unturned.exe");
                if (!File.Exists(executable) || !Directory.Exists(Path.Combine(directory, "Unturned_Data")))
                {
                    error = "Pasta U3DS invalida: Unturned.exe / Unturned_Data ausentes.";
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
                UnityEngine.Debug.Log("[MobileNPCFriendsHost] U3DS LAN process launched. "
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
