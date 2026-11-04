using System;
using System.Collections.Generic;
using System.Text;

namespace SDG.Unturned
{
    /// <summary>
    /// Validated, side-effect-free configuration for a future in-menu Host Game
    /// workflow. This class does NOT start a server, open ports or modify Provider.
    /// Dedicated server startup must be audited against native Unturned APIs
    /// before binding a UI button to any process/network operation.
    /// </summary>
    internal sealed class MobileNPCIntegratedHostConfig
    {
        internal string Map = "Washington";
        internal string ServerName = "Unturned 2 - Amigos";
        internal string Password = "";
        internal int MaxPlayers = 4;
        internal int Port = 27015;
        internal MobileNPCSettings.GameMode NpcMode = MobileNPCSettings.GameMode.Normal;
        // Internet sessions use the native Steam server code and relay.
        // LAN remains available for offline/local diagnostics.
        internal bool InternetFriends = true;
        internal bool PublicListing; // Do not request public discovery
        internal string DedicatedServerDirectory = "";
        // Comma, semicolon or whitespace-separated Steam Workshop published file IDs.
        internal string WorkshopFileIds = "";

        internal bool TryValidate(out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(Map) || Map.Length > 64
                || !IsSafeIdentifier(Map))
                error = "O mapa deve ter de 1 a 64 caracteres validos.";
            else if (string.IsNullOrWhiteSpace(ServerName) || ServerName.Length > 50
                || ContainsControlCharacters(ServerName))
                error = "O nome do servidor deve ter de 1 a 50 caracteres sem controles.";
            else if (Password == null || Password.Length > 64
                || ContainsControlCharacters(Password))
                error = "A senha deve ter no maximo 64 caracteres sem controles.";
            else if (InternetFriends && string.IsNullOrWhiteSpace(Password))
                error = "Para hospedar pela internet, defina uma senha para seus amigos.";
            else if (MaxPlayers < 1 || MaxPlayers > 24)
                error = "Escolha entre 1 e 24 jogadores.";
            else if (Port < 1024 || Port > 65532)
                error = "A porta deve estar entre 1024 e 65532.";
            else if ((int)NpcMode < 0 || (int)NpcMode > 2)
                error = "Modo NPC invalido.";
            else if (!TryParseWorkshopIds(out _, out error))
                return false;
            else if (PublicListing)
                error = "A hospedagem publica ainda nao esta habilitada; use LAN."; 
            return error == null;
        }

        internal bool TryParseWorkshopIds(out List<ulong> ids, out string error)
        {
            ids = new List<ulong>();
            error = null;
            if (string.IsNullOrWhiteSpace(WorkshopFileIds)) return true;
            string[] parts = WorkshopFileIds.Split(new[] { ',', ';', ' ', '\\t', '\\r', '\\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 64)
            {
                error = "Selecione no maximo 64 itens da Workshop.";
                return false;
            }
            HashSet<ulong> seen = new HashSet<ulong>();
            foreach (string part in parts)
            {
                if (!ulong.TryParse(part, out ulong id) || id == 0)
                {
                    error = "Workshop: informe apenas IDs numericos positivos separados por virgulas.";
                    return false;
                }
                if (seen.Add(id)) ids.Add(id);
            }
            return true;
        }

        private static bool IsSafeIdentifier(string value)
        {
            foreach (char c in value)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' '))
                    return false;
            }
            return true;
        }

        private static bool ContainsControlCharacters(string value)
        {
            foreach (char c in value)
                if (char.IsControl(c)) return true;
            return false;
        }

        internal MobileNPCIntegratedHostConfig CopyWithoutPassword()
        {
            return new MobileNPCIntegratedHostConfig
            {
                Map = Map, ServerName = ServerName, Password = "",
                MaxPlayers = MaxPlayers, Port = Port, PublicListing = PublicListing,
                NpcMode = NpcMode, InternetFriends = InternetFriends,
                DedicatedServerDirectory = DedicatedServerDirectory,
                WorkshopFileIds = WorkshopFileIds
            };
        }

        public override string ToString()
        {
            // Never print credentials into Player.log or diagnostics.
            return "Map=" + Map + ", ServerName=" + ServerName
                + ", NPCMode=" + NpcMode + ", MaxPlayers=" + MaxPlayers + ", Port=" + Port
                + ", InternetFriends=" + InternetFriends
                + ", PublicListing=" + PublicListing
                + ", WorkshopIDs=" + (string.IsNullOrWhiteSpace(WorkshopFileIds) ? 0 : WorkshopFileIds.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length);
        }
    }
}
