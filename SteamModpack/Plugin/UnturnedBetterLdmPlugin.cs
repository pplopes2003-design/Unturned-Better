// Compile as an external Rocket/LDM plugin with references to the installed
// Unturned/Rocket.Core assemblies, Newtonsoft.Json and System.Net.Http.
// This is a bootstrap ONLY: no humanoid AI actor has been ported into stock
// Steam Unturned yet. A game-side ILocalAIAgentSource must be registered.
using Rocket.Core.Logging;
using Rocket.Core.Plugins;
using UnityEngine;

namespace UnturnedBetter.SteamModpack
{
    public sealed class UnturnedBetterLdmPlugin : RocketPlugin
    {
        private static UnturnedBetterLdmPlugin active;
        private ILocalAIAgentSource agentSource;
        private LocalAIHostPump pump;

        protected override void Load()
        {
            active = this;
            Logger.Log("[UnturnedBetter] Steam modpack bridge loaded. " +
                "Waiting for a game-side NPC agent adapter. Local AI is optional.");
        }

        /// <summary>
        /// Called by a separately ported NPC runtime on the Unity game thread.
        /// Never grant an HTTP service direct access to world mutation.
        /// </summary>
        public static void RegisterAgentSource(ILocalAIAgentSource source)
        {
            if (active == null || source == null) return;
            active.pump?.Dispose();
            active.agentSource = source;
            active.pump = new LocalAIHostPump(source);
            Logger.Log("[UnturnedBetter] NPC observation adapter registered.");
        }

        private void Update()
        {
            if (pump != null && agentSource != null && agentSource.IsServerReady)
                pump.Tick(Time.time);
        }

        protected override void Unload()
        {
            pump?.Dispose();
            pump = null;
            agentSource = null;
            if (active == this) active = null;
        }
    }
}
