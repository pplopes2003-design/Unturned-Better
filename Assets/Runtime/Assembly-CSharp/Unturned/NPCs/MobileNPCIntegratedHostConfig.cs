using System;
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
        internal bool PublicListing; // LAN-only in initial implementation
        internal string DedicatedServerDirectory = "";

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
            else if (MaxPlayers < 1 || MaxPlayers > 24)
                error = "Escolha entre 1 e 24 jogadores.";
            else if (Port < 1024 || Port > 65532)
                error = "A porta deve estar entre 1024 e 65532.";
            else if (PublicListing)
                error = "A hospedagem publica ainda nao esta habilitada; use LAN."; 
            return error == null;
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
                DedicatedServerDirectory = DedicatedServerDirectory
            };
        }

        public override string ToString()
        {
            // Never print credentials into Player.log or diagnostics.
            return "Map=" + Map + ", ServerName=" + ServerName
                + ", MaxPlayers=" + MaxPlayers + ", Port=" + Port
                + ", PublicListing=" + PublicListing;
        }
    }
}
