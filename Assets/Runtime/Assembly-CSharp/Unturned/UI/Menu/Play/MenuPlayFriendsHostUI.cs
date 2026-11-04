using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>Simple first-party "Host LAN" menu backed by official U3DS.</summary>
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
        private static MobileNPCIntegratedHostConfig configuration =
            new MobileNPCIntegratedHostConfig();

        public static void Open()
        {
            if (active) return;
            active = true;
            container.AnimateIntoView();
        }

        public static void Close()
        {
            if (!active) return;
            active = false;
            container.AnimateOutOfView(0, 1);
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
                statusBox.Text = "Servidor LAN iniciado. Aguarde carregar o mapa, depois clique Conectar.";
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
            MobileNPCIntegratedHostLauncher.TryConnectLocal(configuration);
            statusBox.Text = "Consultando servidor local. Se ainda carregar, tente novamente.";
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
            folderField = AddField("Pasta U3DS", configuration.DedicatedServerDirectory, -170, false, 255);
            mapField = AddField("Mapa", configuration.Map, -130, false, 64);
            nameField = AddField("Nome do servidor", configuration.ServerName, -90, false, 80);
            passwordField = AddField("Senha (LAN)", configuration.Password, -50, true, 64);

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

            ISleekButton launch = Glazier.Get().CreateButton();
            launch.PositionScale_X = 0.5f;
            launch.PositionScale_Y = 0.5f;
            launch.PositionOffset_X = -285;
            launch.PositionOffset_Y = 35;
            launch.SizeOffset_X = 270;
            launch.SizeOffset_Y = 36;
            launch.Text = "Iniciar servidor LAN";
            launch.OnClicked += OnLaunch;
            container.AddChild(launch);

            ISleekButton join = Glazier.Get().CreateButton();
            join.PositionScale_X = 0.5f;
            join.PositionScale_Y = 0.5f;
            join.PositionOffset_X = 15;
            join.PositionOffset_Y = 35;
            join.SizeOffset_X = 270;
            join.SizeOffset_Y = 36;
            join.Text = "Conectar ao meu servidor";
            join.OnClicked += OnJoin;
            container.AddChild(join);

            statusBox = Glazier.Get().CreateBox();
            statusBox.PositionScale_X = 0.5f;
            statusBox.PositionScale_Y = 0.5f;
            statusBox.PositionOffset_X = -285;
            statusBox.PositionOffset_Y = 85;
            statusBox.SizeOffset_X = 570;
            statusBox.SizeOffset_Y = 44;
            statusBox.Text = "LAN: amigos na mesma rede. Instale U3DS pelo Steam (Ferramentas).";
            container.AddChild(statusBox);

            ISleekButton back = Glazier.Get().CreateButton();
            back.PositionScale_X = 0.5f;
            back.PositionScale_Y = 0.5f;
            back.PositionOffset_X = -285;
            back.PositionOffset_Y = 146;
            back.SizeOffset_X = 570;
            back.SizeOffset_Y = 32;
            back.Text = "Voltar";
            back.OnClicked += OnBack;
            container.AddChild(back);
        }
    }
}
