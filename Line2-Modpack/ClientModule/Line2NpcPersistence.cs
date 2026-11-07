using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>Bounded, map-keyed snapshots for surviving local NPCs.
    /// No world drops are minted, removed or transferred by this store.</summary>
    internal static class Line2NpcPersistence
    {
        private const int Schema = 1;
        private const int MaximumBytes = 131072;
        [Serializable]
        internal sealed class ItemRecord
        {
            public int id;
            public int amount;
            public int quality;
            public byte[] state;
        }
        [Serializable]
        internal sealed class ActorRecord
        {
            public string identity;
            public string role;
            public string displayName;
            public int groupId;
            public float health;
            public float x, y, z, homeX, homeY, homeZ;
            public ItemRecord[] items;
            internal Vector3 Position { get { return new Vector3(x, y, z); } }
            internal Vector3 Home { get { return new Vector3(homeX, homeY, homeZ); } }
        }
        [Serializable]
        private sealed class SaveFile
        {
            public int schema = Schema;
            public string map;
            public ActorRecord[] actors;
        }
        internal static string CurrentMapKey
        {
            get { return Level.info != null ? Level.info.name : null; }
        }
        private static bool Finite(float n)
        {
            return !float.IsNaN(n) && !float.IsInfinity(n) && Math.Abs(n) < 100000f;
        }
        private static string GetPath(string map)
        {
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(map));
            var name = new StringBuilder("map-");
            for (int i = 0; i < 12; i++) name.Append(digest[i].ToString("x2"));
            return Path.Combine(Environment.CurrentDirectory,
                "Unturned2-Line2", "npc-saves", name + ".json");
        }
        internal static bool TryRead(string map, out List<ActorRecord> actors, out string reason)
        {
            actors = new List<ActorRecord>();
            reason = "denied";
            if (string.IsNullOrEmpty(map) || !Provider.isServer ||
                Dedicator.IsDedicatedServer || !Level.isLoaded) return false;
            string path = GetPath(map);
            if (!File.Exists(path))
            {
                reason = "new_map";
                return true;
            }
            try
            {
                if (new FileInfo(path).Length > MaximumBytes)
                    throw new InvalidDataException("oversized save");
                SaveFile data = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
                if (data == null || data.schema != Schema || data.map != map ||
                    data.actors == null || data.actors.Length > 32)
                    throw new InvalidDataException("invalid map/schema/count");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (ActorRecord a in data.actors)
                {
                    if (a == null || string.IsNullOrEmpty(a.identity) ||
                        a.identity.Length > 96 || !seen.Add(a.identity) ||
                        (a.role != "hostile" && a.role != "neutral" && a.role != "friendly") ||
                        a.groupId < 0 || a.groupId > 1000000 ||
                        a.displayName == null || a.displayName.Length > 80 ||
                        !Finite(a.health) || a.health <= 0f || a.health > 100f ||
                        !Finite(a.x) || !Finite(a.y) || !Finite(a.z) ||
                        !Finite(a.homeX) || !Finite(a.homeY) || !Finite(a.homeZ) ||
                        a.items == null || a.items.Length > 18)
                        throw new InvalidDataException("invalid actor");
                    foreach (ItemRecord item in a.items)
                        if (item == null || item.id < 1 || item.id > ushort.MaxValue ||
                            item.amount < 0 || item.amount > byte.MaxValue ||
                            item.quality < 0 || item.quality > byte.MaxValue ||
                            item.state == null || item.state.Length > 2048)
                            throw new InvalidDataException("invalid item");
                    actors.Add(a);
                }
                reason = "loaded=" + actors.Count;
                return true;
            }
            catch (Exception error)
            {
                actors.Clear();
                reason = "rejected:" + error.GetType().Name + ": " + error.Message;
                return false; // Don't overwrite a damaged save with empty data.
            }
        }
        internal static bool TryWrite(string map, List<ActorRecord> actors, out string reason)
        {
            reason = "denied";
            if (string.IsNullOrEmpty(map) || actors == null || actors.Count > 32 ||
                !Provider.isServer || Dedicator.IsDedicatedServer) return false;
            string temp = null;
            try
            {
                string json = JsonUtility.ToJson(
                    new SaveFile { map = map, actors = actors.ToArray() });
                if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
                    throw new InvalidDataException("serialized data exceeds cap");
                string path = GetPath(map);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                temp = path + ".tmp";
                File.WriteAllText(temp, json, Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                reason = "saved=" + actors.Count;
                return true;
            }
            catch (Exception error)
            {
                reason = "save_failed:" + error.GetType().Name + ": " + error.Message;
                return false;
            }
            finally
            {
                if (temp != null && File.Exists(temp))
                    try { File.Delete(temp); } catch (IOException) { }
            }
        }
    }
}
