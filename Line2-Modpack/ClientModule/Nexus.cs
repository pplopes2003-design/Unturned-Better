using System;
using SDG.Framework.Modules;
using SDG.Unturned;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unturned2.Line2
{
    public sealed class Nexus : IModuleNexus
    {
        private GameObject _host;
        private bool _subscribed;

        public void initialize()
        {
            ProbeLogger.Log("Module initialized. Active scene=" + SceneManager.GetActiveScene().name);
            _host = new GameObject("Unturned2.Line2.Diagnostics");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<WorldScanner>();
            _host.AddComponent<LocalAiBridge>();
            _host.AddComponent<NpcApiDiscovery>();
            Level.onLevelLoaded += OnLevelLoaded;
            _subscribed = true;
            ProbeLogger.Log("Diagnostics ready: F8 scans colliders, F9 probes AI Service, F10 inventories NPC APIs.");
        }

        private void OnLevelLoaded(int level)
        {
            ProbeLogger.Log("Level event=" + level + "; activeScene=" +
                            SceneManager.GetActiveScene().name + "; scanner waits for F8.");
        }

        public void shutdown()
        {
            if (_subscribed)
            {
                Level.onLevelLoaded -= OnLevelLoaded;
                _subscribed = false;
            }
            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host);
                _host = null;
            }
            ProbeLogger.Log("Module shutdown.");
        }
    }
}
