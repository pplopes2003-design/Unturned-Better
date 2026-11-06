using System;
using System.IO;
using SDG.Framework.Modules;
using SDG.Unturned;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unturned2.Line2
{
    // Diagnostic bootstrap only; intentionally does not modify world state.
    public sealed class Nexus : IModuleNexus
    {
        private static readonly object LogLock = new object();
        private static readonly Collider[] ProbeResults = new Collider[64];
        private bool _subscribed;

        public void initialize()
        {
            WriteLog("Module initialized. Active scene: " + SceneManager.GetActiveScene().name);
            Level.onLevelLoaded += OnLevelLoaded;
            _subscribed = true;
        }

        private void OnLevelLoaded(int level)
        {
            try
            {
                // First-pass diagnostic, not a navigation mesh. Query runs on Unity's main thread.
                string sceneName = SceneManager.GetActiveScene().name;
                int nearbyColliders = Physics.OverlapSphereNonAlloc(
                    Vector3.zero, 8f, ProbeResults);
                WriteLog("Level event=" + level + "; scene=" + sceneName +
                         "; sample_colliders_origin=" + nearbyColliders +
                         (nearbyColliders == ProbeResults.Length ? " (buffer full)" : ""));
                Array.Clear(ProbeResults, 0, ProbeResults.Length);
            }
            catch (Exception ex)
            {
                WriteLog("Level probe failed: " + ex);
            }
        }

        public void shutdown()
        {
            if (_subscribed)
            {
                Level.onLevelLoaded -= OnLevelLoaded;
                _subscribed = false;
            }
            WriteLog("Module shutdown.");
        }

        private static void WriteLog(string message)
        {
            string line = DateTime.UtcNow.ToString("o") + " [Unturned2.Line2] " + message;
            try
            {
                string dir = Path.Combine(Environment.CurrentDirectory, "Unturned2-Line2");
                Directory.CreateDirectory(dir);
                lock (LogLock)
                {
                    File.AppendAllText(Path.Combine(dir, "probe.log"), line + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Logging failures must not prevent normal game startup.
            }
            Debug.Log(line);
        }
    }
}
