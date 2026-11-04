using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Server-owned, map-scoped fallback for corpse inventory the native ItemManager
    /// refused to spawn. Entries are committed to disk before the corpse relinquishes
    /// ownership. Never creates replacement/random items or client-side drops.
    /// Disk and native world persistence cannot be committed atomically together:
    /// retry removes an entry from disk BEFORE attempting the native insertion, favoring
    /// at-most-once delivery (possible loss on a crash in that narrow window) over dupes.
    /// </summary>
    internal static class MobileNPCCorpseLootJournal
    {
        private const int Version = 1;
        private const int MaximumEntries = 512;
        private const float RetrySeconds = 8f;
        private const float MaxAttemptsPerTick = 4f;
        [Serializable] private sealed class Entry
        {
            public string id;
            public int itemId, amount, quality;
            public byte[] state;
            public float x, y, z;
        }
        [Serializable] private sealed class Journal
        {
            public int version = Version;
            public string mapKey;
            public List<Entry> entries = new List<Entry>();
        }
        private static Journal journal;
        private static string activeKey;
        private static bool blocked;
        private static float nextRetry;

        internal static void Reset()
        {
            journal = null;
            activeKey = null;
            blocked = false;
            nextRetry = 0f;
        }

        private static string GetPath(string key)
        {
            return Path.Combine(Application.persistentDataPath, "MobileNPCs",
                key + ".corpse-loot.json");
        }

        private static bool EnsureLoaded()
        {
            if (!Provider.isServer || !Level.isLoaded) return false;
            string key = MobileNPCPersistence.GetCurrentMapKeyForJournalServer();
            if (string.IsNullOrEmpty(key)) return false;
            if (journal != null && string.Equals(activeKey, key, StringComparison.Ordinal))
                return !blocked;
            activeKey = key;
            journal = new Journal { mapKey = key };
            blocked = false;
            string path = GetPath(key);
            bool hasFiles = File.Exists(path) || File.Exists(path + ".bak");
            if (!hasFiles) return true;
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    Journal loaded = JsonUtility.FromJson<Journal>(File.ReadAllText(candidate));
                    if (loaded != null && loaded.version == Version && loaded.entries != null
                        && string.Equals(loaded.mapKey, key, StringComparison.Ordinal)
                        && loaded.entries.Count <= MaximumEntries)
                    {
                        journal = loaded;
                        if (candidate != path)
                        {
                            // If restoring backup, repair primary before any next write.
                            File.Copy(candidate, path, true);
                        }
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[MobileNPCCorpseLootJournal] Could not read "
                        + candidate + ": " + e.Message);
                }
            }
            blocked = true; // Preserve unparseable files, do not overwrite history.
            Debug.LogError("[MobileNPCCorpseLootJournal] Invalid journal; deposits and retries disabled for " + key);
            return false;
        }

        private static bool Commit()
        {
            if (blocked || journal == null || string.IsNullOrEmpty(activeKey)) return false;
            string path = GetPath(activeKey);
            string temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temp, JsonUtility.ToJson(journal, true));
                if (!File.Exists(path)) File.Move(temp, path);
                else
                {
                    try { File.Replace(temp, path, path + ".bak"); }
                    catch
                    {
                        // Do not use non-atomic delete-then-move fallback here:
                        // losing the entire journal would risk an unbounded loot loss.
                        throw;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[MobileNPCCorpseLootJournal] Disk commit failed: " + e);
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
            }
        }

        internal static bool TryAcceptServer(Item item, Vector3 position)
        {
            if (item == null || item.id == 0 || !EnsureLoaded()
                || journal.entries.Count >= MaximumEntries
                || float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)
                || float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                return false;
            Entry entry = new Entry
            {
                id = Guid.NewGuid().ToString("N"),
                itemId = item.id, amount = item.amount, quality = item.quality,
                state = item.state != null ? (byte[])item.state.Clone() : new byte[0],
                x = position.x, y = position.y, z = position.z
            };
            journal.entries.Add(entry);
            if (Commit()) return true;
            journal.entries.Remove(entry);
            return false;
        }

        internal static void TickServer()
        {
            if (!EnsureLoaded() || Time.time < nextRetry || journal.entries.Count == 0)
                return;
            nextRetry = Time.time + RetrySeconds;
            int attempts = Mathf.Min((int)MaxAttemptsPerTick, journal.entries.Count);
            for (int i = 0; i < attempts; i++)
            {
                Entry entry = journal.entries[0];
                // No unsafe/unknown assets or invalid amounts are materialized.
                ItemAsset asset = entry != null && entry.itemId > 0 && entry.itemId <= ushort.MaxValue
                    ? Assets.find(EAssetType.ITEM, (ushort)entry.itemId) as ItemAsset : null;
                if (entry == null || asset == null || asset.isPro || entry.amount < 0 || entry.amount > 255
                    || entry.quality < 0 || entry.quality > 255)
                {
                    // Rotate undeliverable entry, retain it durably for content restoration.
                    journal.entries.RemoveAt(0);
                    journal.entries.Add(entry);
                    continue;
                }
                journal.entries.RemoveAt(0);
                if (!Commit())
                {
                    journal.entries.Insert(0, entry);
                    return;
                }
                var item = new Item((ushort)entry.itemId, (byte)entry.amount,
                    (byte)entry.quality, entry.state ?? new byte[0]);
                Vector3 position = new Vector3(entry.x, entry.y, entry.z);
                if (!ItemManager.TryDropMobileNPCItemServer(item, position))
                {
                    // If re-commit fails after a refusal, the entry remains in-memory;
                    // a crash in this window can lose the item. Never replay an
                    // already delivered item from a stale on-disk entry.
                    journal.entries.Insert(0, entry);
                    if (!Commit())
                        Debug.LogError("[MobileNPCCorpseLootJournal] Could not persist rejected retry; at-risk entry " + entry.id);
                    return;
                }
            }
        }

        internal static int PendingCountServer => journal != null ? journal.entries.Count : 0;
    }
}
