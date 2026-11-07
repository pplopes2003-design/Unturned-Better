namespace Unturned2.Line2
{
    /// <summary>
    /// Steam game-side NPC runtime contract. An implementation must own game
    /// entity creation, hitboxes, AI lifecycle and replication; no fake actors.
    /// All operations must execute on the Unity main thread and in an
    /// authoritative local/server context.
    /// </summary>
    internal interface ILine2NpcRuntime
    {
        int ActiveCount { get; }
        bool TrySpawn(string disposition, int count, out string reason);
        void UpdateSettings(bool enabled, bool naturalSpawn, int maxNPCs,
            string combatMode);
    }

    /// <summary>
    /// Explicit registration prevents the admin UI from claiming NPCs exist
    /// before the actual Unturned adapter is ready.
    /// </summary>
    internal static class Line2NpcRuntimeRegistry
    {
        private static ILine2NpcRuntime _runtime;
        private static bool _enabled;
        private static bool _natural;
        private static int _maxNPCs = 8;
        private static string _mode = "normal";

        internal static bool IsReady { get { return _runtime != null; } }
        internal static int ActiveCount
        {
            get { return _runtime == null ? 0 : _runtime.ActiveCount; }
        }

        internal static void Register(ILine2NpcRuntime runtime)
        {
            if (runtime == null)
                throw new System.ArgumentNullException("runtime");
            if (_runtime != null && !object.ReferenceEquals(_runtime, runtime))
                throw new System.InvalidOperationException("NPC runtime already registered");
            _runtime = runtime;
            _runtime.UpdateSettings(_enabled, _natural, _maxNPCs, _mode);
        }

        internal static void Unregister(ILine2NpcRuntime runtime)
        {
            if (object.ReferenceEquals(_runtime, runtime)) _runtime = null;
        }

        internal static bool TrySpawn(string disposition, int count, out string reason)
        {
            if (_runtime == null)
            {
                reason = "Steam NPC runtime not installed; no entity created.";
                return false;
            }
            return _runtime.TrySpawn(disposition, count, out reason);
        }

        internal static void UpdateSettings(bool enabled, bool natural, int max,
            string mode)
        {
            _enabled = enabled;
            _natural = natural;
            _maxNPCs = max;
            _mode = mode;
            if (_runtime != null)
                _runtime.UpdateSettings(enabled, natural, max, mode);
        }
    }
}
