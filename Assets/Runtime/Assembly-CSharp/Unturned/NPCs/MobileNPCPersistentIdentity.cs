using System;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Save-scoped identity of one living NPC. Unlike Unity instance IDs or
    /// display names, this is stable across server restarts and unique per
    /// spawn. Only the authoritative server restores/writes identity.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class MobileNPCPersistentIdentity : MonoBehaviour
    {
        [SerializeField] private string persistentId;

        private void Awake()
        {
            if (string.IsNullOrEmpty(persistentId) && Provider.isServer)
                persistentId = Guid.NewGuid().ToString("N");
        }

        internal string ExportServer()
        {
            if (!Provider.isServer) return null;
            if (string.IsNullOrEmpty(persistentId))
                persistentId = Guid.NewGuid().ToString("N");
            return persistentId;
        }

        internal void RestoreServer(string savedId)
        {
            if (!Provider.isServer || string.IsNullOrEmpty(savedId))
                return;
            if (Guid.TryParseExact(savedId, "N", out Guid parsed))
                persistentId = parsed.ToString("N");
        }

        internal int AppearanceSeedServer()
        {
            string key = ExportServer();
            unchecked
            {
                uint hash = 2166136261u;
                if (key != null)
                    for (int i = 0; i < key.Length; ++i)
                    {
                        hash ^= key[i];
                        hash *= 16777619u;
                    }
                return (int)hash;
            }
        }
    }
}
