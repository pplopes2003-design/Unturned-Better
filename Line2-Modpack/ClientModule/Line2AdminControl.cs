using System;
using System.Globalization;
using System.IO;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Singleplayer-only settings and command parser. Does NOT instantiate
    /// NPCs until a verified Steam-side entity adapter is registered.
    /// </summary>
    internal sealed class Line2AdminControl : MonoBehaviour
    {
        [Serializable]
        private sealed class Settings
        {
            public bool npcEnabled = false;
            public bool naturalSpawn = false;
            public int maxNPCs = 8;
            public string combatMode = "normal";
            public bool hordesEnabled = false;
        }

        private Settings _settings = new Settings();
        private string _typed = "help";
        private string _feedback = "F7: Line 2 admin. Use !u2 help in local chat.";
        private bool _show;
        private float _lastCommandAt = -100f;
        private CursorLockMode _oldLock;
        private bool _oldCursor;
        private Rect _rect = new Rect(25, 55, 500, 310);
        private static readonly string ConfigPath =
            Path.Combine(Environment.CurrentDirectory, "Unturned2-Line2", "admin-settings.json");

        private static bool IsLocalHost()
        {
            // Never enable client-controlled features on public/dedicated servers.
            return Provider.isServer && !Dedicator.IsDedicatedServer;
        }

        private void Start()
        {
            Load();
            PushRuntimeSettings();
            ChatManager.onChatted += OnChatted;
            ProbeLogger.Log("LINE2_ADMIN_READY: F7 panel, chat prefix !u2. Prototype runtime is singleplayer-only, requires local cheats.");
        }

        private void OnDestroy()
        {
            ChatManager.onChatted -= OnChatted;
            if (_show) RestoreCursor();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                if (!IsLocalHost())
                {
                    ProbeLogger.Log("LINE2_ADMIN_DENIED: no local server authority.");
                    return;
                }
                _show = !_show;
                if (_show)
                {
                    _oldLock = Cursor.lockState;
                    _oldCursor = Cursor.visible;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else RestoreCursor();
            }
        }

        private void RestoreCursor()
        {
            Cursor.lockState = _oldLock;
            Cursor.visible = _oldCursor;
        }

        private void OnGUI()
        {
            if (!IsLocalHost()) return;
            if (_show)
                _rect = GUI.Window(879321, _rect, DrawPanel, "Unturned 2 - Controle local (F7)");
            else if (Time.realtimeSinceStartup - _lastCommandAt < 7f)
                GUI.Box(new Rect(22, 55, Mathf.Min(Screen.width - 44, 640f), 72),
                    "Line 2: " + _feedback);

        }

        private void DrawPanel(int id)
        {
            GUI.Label(new Rect(12, 24, 475, 25),
                "NPCs: " + _settings.npcEnabled + " | Spawn natural: " + _settings.naturalSpawn +
                " | Max: " + _settings.maxNPCs);
            GUI.Label(new Rect(12, 47, 475, 25),
                "Combate: " + _settings.combatMode + " | Hordas: " + _settings.hordesEnabled);
            GUI.Label(new Rect(12, 73, 475, 25),
                "Comandos: help, status, max 8, combat normal, natural on, spawn hostile");
            _typed = GUI.TextField(new Rect(12, 101, 460, 26), _typed, 100);
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Return)
            {
                Run(_typed);
                Event.current.Use();
            }
            if (GUI.Button(new Rect(12, 134, 100, 27), "Executar"))
                Run(_typed);
            if (GUI.Button(new Rect(122, 134, 100, 27), "Status"))
                Run("status");
            if (GUI.Button(new Rect(232, 134, 130, 27), "Salvar config"))
                Save();
            if (GUI.Button(new Rect(375, 134, 98, 27), "Fechar"))
            {
                _show = false;
                RestoreCursor();
            }
            GUI.TextArea(new Rect(12, 173, 460, 122), _feedback);
            GUI.DragWindow(new Rect(0, 0, 500, 23));
        }

        private void OnChatted(SteamPlayer player, EChatMode mode, ref Color color,
            ref bool isRich, string message, ref bool isVisible)
        {
            if (!IsLocalHost() || string.IsNullOrWhiteSpace(message)) return;
            // !u2 is used rather than /u2 to avoid colliding with native commands.
            string trimmed = message.Trim();
            if (!trimmed.StartsWith("!u2", StringComparison.OrdinalIgnoreCase)) return;
            if (trimmed.Length > 3 && !char.IsWhiteSpace(trimmed[3])) return;

            // Chat changes are disabled unless the chat came from the local player.
            Player local = Player.player;
            if (player == null || local == null ||
                player.playerID.steamID != local.channel.owner.playerID.steamID)
                return;

            isVisible = false;
            Run(trimmed.Length > 3 ? trimmed.Substring(3).Trim() : "help");
        }

        private void Run(string command)
        {
            if (!IsLocalHost())
            {
                _feedback = "Local singleplayer host only. No remote/server commands.";
                return;
            }
            string[] args = (command ?? "").Trim().Split(
                new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (args.Length == 0) return;
            string action = args[0].ToLowerInvariant();
            for (int i = 1; i < args.Length; i++) args[i] = args[i].ToLowerInvariant();
            switch (action)
            {
                case "help":
                    _feedback = "status | npc on/off | natural on/off | max 1..32 | " +
                        "combat passive/normal/total | hordes on/off | server | population | spawn friendly/neutral/hostile [1..5] | save. " +
                        "Spawn uses EXPERIMENTAL visual prototype only; no real damage, dialogue, radio, saves or multiplayer.";
                    break;
                case "status":
                    _feedback = "NPC enabled=" + _settings.npcEnabled +
                        ", natural=" + _settings.naturalSpawn + ", max=" + _settings.maxNPCs +
                        ", combat=" + _settings.combatMode + ", hordes=" + _settings.hordesEnabled +
                        ". PROTOTYPE NPCs=" + Line2NpcRuntimeRegistry.ActiveCount +
                        " (prototype connected=" + Line2NpcRuntimeRegistry.IsReady + ").";
                    break;
                case "population":
                    _feedback = "Population config max=" + _settings.maxNPCs +
                        ", natural=" + _settings.naturalSpawn +
                        ", no NPCs spawned until verified game adapter is installed.";
                    break;
                case "server":
                    _feedback = "Local host=" + IsLocalHost() +
                        ". Dedicated and multiplayer server administration is not implemented in Line 2.";
                    break;
                case "npc":
                case "natural":
                case "hordes":
                    bool value;
                    if (args.Length != 2 || !TryToggle(args[1], out value))
                    { _feedback = "Use " + action + " on/off"; break; }
                    if (action == "npc") _settings.npcEnabled = value;
                    else if (action == "natural") _settings.naturalSpawn = value;
                    else _settings.hordesEnabled = value;
                    Save();
                    PushRuntimeSettings();
                    _feedback = action + "=" + value +
                        ". NPC runtime ready=" + Line2NpcRuntimeRegistry.IsReady + ".";
                    break;
                case "max":
                    int limit;
                    if (args.Length != 2 ||
                        !int.TryParse(args[1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out limit) ||
                        limit < 1 || limit > 32)
                    { _feedback = "Use max 1..32"; break; }
                    _settings.maxNPCs = limit;
                    Save();
                    PushRuntimeSettings();
                    _feedback = "Max NPC population set to " + limit + ".";
                    break;
                case "combat":
                    if (args.Length != 2 ||
                        (args[1] != "passive" && args[1] != "normal" && args[1] != "total"))
                    { _feedback = "Use combat passive/normal/total"; break; }
                    _settings.combatMode = args[1];
                    Save();
                    PushRuntimeSettings();
                    _feedback = "Combat mode configured: " + _settings.combatMode +
                        ". Live NPC combat is not installed.";
                    break;
                case "spawn":
                    if (args.Length < 2 || args.Length > 3 ||
                        (args[1] != "friendly" && args[1] != "neutral" && args[1] != "hostile"))
                    { _feedback = "Use spawn friendly/neutral/hostile [1..5]"; break; }
                    int count = 1;
                    if (args.Length == 3 &&
                        (!int.TryParse(args[2], out count) || count < 1 || count > 5))
                    { _feedback = "Spawn batch limit is 1..5"; break; }
                    string spawnReason;
                    bool spawned = Line2NpcRuntimeRegistry.TrySpawn(args[1], count, out spawnReason);
                    _feedback = (spawned ? "PROTOTYPE_SPAWN_OK: " : "SPAWN_UNAVAILABLE: ") +
                        count + " " + args[1] + ": " + spawnReason;
                    break;
                case "save":
                    Save();
                    _feedback = "Settings saved; not a game world save.";
                    break;
                default:
                    _feedback = "Unknown command. Enter help.";
                    break;
            }
            _lastCommandAt = Time.realtimeSinceStartup;
            ProbeLogger.Log("LINE2_ADMIN: " + action + " => " + _feedback);
        }

        private void PushRuntimeSettings()
        {
            Line2NpcRuntimeRegistry.UpdateSettings(_settings.npcEnabled,
                _settings.naturalSpawn, _settings.maxNPCs, _settings.combatMode);
        }

        private static bool TryToggle(string value, out bool result)
        {
            if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
            { result = true; return true; }
            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            { result = false; return true; }
            result = false;
            return false;
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                Settings loaded = JsonUtility.FromJson<Settings>(File.ReadAllText(ConfigPath));
                if (loaded == null) return;
                loaded.maxNPCs = Mathf.Clamp(loaded.maxNPCs, 1, 32);
                if (loaded.combatMode != "passive" && loaded.combatMode != "normal" &&
                    loaded.combatMode != "total")
                    loaded.combatMode = "normal";
                _settings = loaded;
            }
            catch (Exception e)
            { ProbeLogger.Log("LINE2_CONFIG_LOAD_FAILED: " + e.Message); }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath, JsonUtility.ToJson(_settings, true));
            }
            catch (Exception e)
            { ProbeLogger.Log("LINE2_CONFIG_SAVE_FAILED: " + e.Message); }
        }
    }
}
