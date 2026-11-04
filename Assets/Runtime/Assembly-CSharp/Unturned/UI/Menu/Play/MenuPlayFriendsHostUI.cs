using Steamworks;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>Host LAN using the matching Unturned Better dedicated server build.</summary>
    internal sealed class MenuPlayFriendsHostUI
    {
        private static SleekFullscreenBox container;
        private static bool active;
        internal static bool IsActive => active;
        private static ISleekField folderField;
        private static ISleekField mapField;
        private static ISleekField nameField;
        private static ISleekField passwordField;
        private static ISleekUInt16Field playersField;
        private static ISleekUInt16Field portField;
        private static ISleekBox statusBox;
        private static ISleekBox codeBox;
        private static ISleekButton internetModeButton;
        private static ISleekButton npcModeButton;
        private static MobileNPCIntegratedHostConfig configuration =
            new MobileNPCIntegratedHostConfig();

        public static void Open()
        {
            if (active) return;
            active = true;
            container.AnimateIntoView();
            RefreshSteamCodeDisplay();
        }

        public static void Close()
        {
            if (!active) return;
            active = false;
            container.AnimateOutOfView(0, 1);
        }

        internal static void RefreshSteamCodeDisplay()
        {
            if (!active || codeBox == null)
                return;
            if (MobileNPCIntegratedHostLauncher.TryGetSteamServerCode(
                out string realCode))
                codeBox.Text = "Steam Server Code: " + realCode;
            else if (MobileNPCIntegratedHostLauncher.IsRunning
                && configuration.InternetFriends)
                codeBox.Text = "Steam Server Code: aguardando autenticacao...";
            else
                codeBox.Text = "Steam Server Code: indisponivel";
        }

        private static void OnInternetModeCycle(ISleekElement button)
        {
            if (MobileNPCIntegratedHostLauncher.IsRunning)
            {
                statusBox.Text = "Encerre o servidor antes de trocar LAN / Internet.";
                return;
            }
            configuration.InternetFriends = !configuration.InternetFriends;
            UpdateInternetModeLabel();
        }

        private static void UpdateInternetModeLabel()
        {
            if (internetModeButton != null)
                internetModeButton.Text = configuration.InternetFriends
                    ? "Rede: Internet privada (Steam Server Code)"
                    : "Rede: LAN (teste local)";
        }

        private static void OnCopySteamCode(ISleekElement button)
        {
            if (!MobileNPCIntegratedHostLauncher.TryGetSteamServerCode(
                out string realCode))
            {
                statusBox.Text = "Codigo indisponivel. Confira a autenticacao Steam no console.";
                return;
            }
            GUIUtility.systemCopyBuffer = realCode;
            statusBox.Text = "Codigo Steam copiado. Envie-o ao seu amigo pelo chat.";
        }

        private static void OnOpenSteamFriends(ISleekElement button)
        {
            if (!MobileNPCIntegratedHostLauncher.TryGetSteamServerCode(out string code))
            {
                statusBox.Text = "Inicie o servidor e aguarde o Server Code da Steam.";
                return;
            }
            // Native Steam invite dialog accepts a rich-presence connect
            // string. Unturned's CommandLine.TryGetSteamConnect knows
            // "+connect {SteamGameServerID}", so no custom URI is needed.
            // Do not embed the session password in rich presence / logs;
            // friends must enter the password manually when joining.
            SteamFriends.ActivateGameOverlayInviteDialogConnectString(
                "+connect " + code);
            statusBox.Text = "Convite Steam aberto. Compartilhe a senha separadamente.";
        }

        private static void OnNpcModeCycle(ISleekElement button)
        {
            int next = ((int)configuration.NpcMode + 1) % 3;
            configuration.NpcMode = (MobileNPCSettings.GameMode)next;
            UpdateNpcModeLabel();
        }

        private static void UpdateNpcModeLabel()
        {
            if (npcModeButton == null) return;
            switch (configuration.NpcMode)
            {
                case MobileNPCSettings.GameMode.Combat:
                    npcModeButton.Text = "Modo NPC: Combate";
                    break;
                case MobileNPCSettings.GameMode.CombatOnly:
                    npcModeButton.Text = "Modo NPC: Combate Total";
                    break;
                default:
                    npcModeButton.Text = "Modo NPC: Normal";
                    break;
            }
        }

        private static void OnBack(ISleekElement button)
        {
            Close();
            MenuPlayUI.open();
        }

        private static void OnLaunch(ISleekElement button)
        {
            configuration.DedicatedServerDirectory = folderField.Text;
            configuration.Map = mapField.Text;
            configuration.ServerName = nameField.Text;
            configuration.Password = passwordField.Text;
            configuration.MaxPlayers = playersField.Value;
            configuration.Port = portField.Value;
            if (MobileNPCIntegratedHostLauncher.TryStart(configuration, out string error))
                statusBox.Text = configuration.InternetFriends
                    ? "Servidor Steam iniciado. Aguarde o codigo aparecer abaixo."
                    : "Servidor LAN iniciado. Aguarde o mapa; depois conecte localmente.";
            else
                statusBox.Text = error;
        }

        private static void OnJoin(ISleekElement button)
        {
            if (!MobileNPCIntegratedHostLauncher.IsRunning)
            {
                statusBox.Text = "Nenhum servidor foi iniciado neste jogo.";
                return;
            }
            if (configuration.InternetFriends)
            {
                if (MobileNPCIntegratedHostLauncher.TryConnectBySteamCode(
                    configuration, out string error))
                    statusBox.Text = "Conectando ao Steam Server Code...";
                else
                    statusBox.Text = error;
            }
            else
            {
                MobileNPCIntegratedHostLauncher.TryConnectLocal(configuration);
                statusBox.Text = "Consultando servidor LAN local.";
            }
        }

        private static void OnStop(ISleekElement button)
        {
            if (MobileNPCIntegratedHostLauncher.TryStop(out string error))
                statusBox.Text = "Salvamento e encerramento solicitados ao servidor.";
            else
                statusBox.Text = error;
        }

        private static ISleekField AddField(string label, string initial,
            int y, bool secret, int maxLength)
        {
            ISleekField field = Glazier.Get().CreateStringField();
            field.PositionScale_X = 0.5f;
            field.PositionScale_Y = 0.5f;
            field.PositionOffset_X = -285;
            field.PositionOffset_Y = y;
            field.SizeOffset_X = 570;
            field.SizeOffset_Y = 30;
            field.MaxLength = maxLength;
            field.Text = initial;
            field.IsPasswordField = secret;
            field.AddLabel(label, ESleekSide.RIGHT);
            container.AddChild(field);
            return field;
        }

        public MenuPlayFriendsHostUI()
        {
            container = new SleekFullscreenBox();
            container.PositionOffset_X = 10;
            container.PositionOffset_Y = 10;
            container.PositionScale_Y = 1;
            container.SizeOffset_X = -20;
            container.SizeOffset_Y = -20;
            container.SizeScale_X = 1;
            container.SizeScale_Y = 1;
            MenuUI.container.AddChild(container);
            active = false;

            configuration.DedicatedServerDirectory =
                MobileNPCIntegratedHostLauncher.SuggestDirectory();
            internetModeButton = Glazier.Get().CreateButton();
            internetModeButton.PositionScale_X = 0.5f;
            internetModeButton.PositionScale_Y = 0.5f;
            internetModeButton.PositionOffset_X = -285;
            internetModeButton.PositionOffset_Y = -213;
            internetModeButton.SizeOffset_X = 570;
            internetModeButton.SizeOffset_Y = 32;
            internetModeButton.OnClicked += OnInternetModeCycle;
            UpdateInternetModeLabel();
            container.AddChild(internetModeButton);

            folderField = AddField("Build dedicada", configuration.DedicatedServerDirectory, -170, false, 255);
            mapField = AddField("Mapa", configuration.Map, -130, false, 64);
            nameField = AddField("Nome do servidor", configuration.ServerName, -90, false, 50);
            passwordField = AddField("Senha do servidor", configuration.Password, -50, true, 64);

            playersField = Glazier.Get().CreateUInt16Field();
            playersField.PositionScale_X = 0.5f;
            playersField.PositionScale_Y = 0.5f;
            playersField.PositionOffset_X = -285;
            playersField.PositionOffset_Y = -10;
            playersField.SizeOffset_X = 260;
            playersField.SizeOffset_Y = 30;
            playersField.Value = (ushort)configuration.MaxPlayers;
            playersField.AddLabel("Jogadores", ESleekSide.RIGHT);
            container.AddChild(playersField);

            portField = Glazier.Get().CreateUInt16Field();
            portField.PositionScale_X = 0.5f;
            portField.PositionScale_Y = 0.5f;
            portField.PositionOffset_X = 25;
            portField.PositionOffset_Y = -10;
            portField.SizeOffset_X = 260;
            portField.SizeOffset_Y = 30;
            portField.Value = (ushort)configuration.Port;
            portField.AddLabel("Porta", ESleekSide.RIGHT);
            container.AddChild(portField);

            npcModeButton = Glazier.Get().CreateButton();
            npcModeButton.PositionScale_X = 0.5f;
            npcModeButton.PositionScale_Y = 0.5f;
            npcModeButton.PositionOffset_X = -285;
            npcModeButton.PositionOffset_Y = 35;
            npcModeButton.SizeOffset_X = 570;
            npcModeButton.SizeOffset_Y = 30;
            npcModeButton.OnClicked += OnNpcModeCycle;
            npcModeButton.TooltipText = "Clique para alternar Normal / Combate / Combate Total.";
            UpdateNpcModeLabel();
            container.AddChild(npcModeButton);

            ISleekButton launch = Glazier.Get().CreateButton();
            launch.PositionScale_X = 0.5f;
            launch.PositionScale_Y = 0.5f;
            launch.PositionOffset_X = -285;
            launch.PositionOffset_Y = 73;
            launch.SizeOffset_X = 270;
            launch.SizeOffset_Y = 36;
            launch.Text = "Iniciar servidor";
            launch.OnClicked += OnLaunch;
            container.AddChild(launch);

            ISleekButton join = Glazier.Get().CreateButton();
            join.PositionScale_X = 0.5f;
            join.PositionScale_Y = 0.5f;
            join.PositionOffset_X = 15;
            join.PositionOffset_Y = 73;
            join.SizeOffset_X = 270;
            join.SizeOffset_Y = 36;
            join.Text = "Conectar ao meu servidor";
            join.OnClicked += OnJoin;
            container.AddChild(join);

            ISleekButton stop = Glazier.Get().CreateButton();
            stop.PositionScale_X = 0.5f;
            stop.PositionScale_Y = 0.5f;
            stop.PositionOffset_X = -285;
            stop.PositionOffset_Y = 115;
            stop.SizeOffset_X = 570;
            stop.SizeOffset_Y = 30;
            stop.Text = "Salvar e encerrar servidor";
            stop.OnClicked += OnStop;
            container.AddChild(stop);

            codeBox = Glazier.Get().CreateBox();
            codeBox.PositionScale_X = 0.5f;
            codeBox.PositionScale_Y = 0.5f;
            codeBox.PositionOffset_X = -285;
            codeBox.PositionOffset_Y = 153;
            codeBox.SizeOffset_X = 570;
            codeBox.SizeOffset_Y = 34;
            codeBox.Text = "Steam Server Code: indisponivel";
            container.AddChild(codeBox);

            ISleekButton copyCode = Glazier.Get().CreateButton();
            copyCode.PositionScale_X = 0.5f;
            copyCode.PositionScale_Y = 0.5f;
            copyCode.PositionOffset_X = -285;
            copyCode.PositionOffset_Y = 195;
            copyCode.SizeOffset_X = 270;
            copyCode.SizeOffset_Y = 34;
            copyCode.Text = "Copiar Server Code";
            copyCode.OnClicked += OnCopySteamCode;
            container.AddChild(copyCode);

            ISleekButton steamFriends = Glazier.Get().CreateButton();
            steamFriends.PositionScale_X = 0.5f;
            steamFriends.PositionScale_Y = 0.5f;
            steamFriends.PositionOffset_X = 15;
            steamFriends.PositionOffset_Y = 195;
            steamFriends.SizeOffset_X = 270;
            steamFriends.SizeOffset_Y = 34;
            steamFriends.Text = "Convidar amigos Steam";
            steamFriends.OnClicked += OnOpenSteamFriends;
            container.AddChild(steamFriends);

            statusBox = Glazier.Get().CreateBox();
            statusBox.PositionScale_X = 0.5f;
            statusBox.PositionScale_Y = 0.5f;
            statusBox.PositionOffset_X = -285;
            statusBox.PositionOffset_Y = 240;
            statusBox.SizeOffset_X = 570;
            statusBox.SizeOffset_Y = 44;
            statusBox.Text = "Internet via Steam Server Code. Cliente e servidor precisam da mesma build.";
            container.AddChild(statusBox);

            ISleekButton back = Glazier.Get().CreateButton();
            back.PositionScale_X = 0.5f;
            back.PositionScale_Y = 0.5f;
            back.PositionOffset_X = -285;
            back.PositionOffset_Y = 292;
            back.SizeOffset_X = 570;
            back.SizeOffset_Y = 32;
            back.Text = "Voltar";
            back.OnClicked += OnBack;
            container.AddChild(back);
        }
    }
}
