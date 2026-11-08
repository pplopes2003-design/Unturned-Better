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
            public bool recruited;
            public int groupId;
            public float health;
            public bool hasNeeds;
            public float hunger, thirst;
            public Line2MobileNPCInjurySystem.InjurySnapshot injuries;
            public float x, y, z, homeX, homeY, homeZ;
            public ItemRecord[] items;
            internal Vector3 Position { get { return new Vector3(x, y, z); } }
            internal Vector3 Home { get { return new Vector3(homeX, homeY, homeZ); } }
        }
        [Serializable]
        internal sealed class ReplacementRecord
        {
            public string role;
            public int formerGroupId;
            public long readyAtUtcTicks;
        }
        [Serializable]
        internal sealed class PlacedBarricadeRecord
        {
            public int assetId;
            // Serialized by Unity JsonUtility; zero denotes legacy
            // manifests created before stable native object tracking.
            public uint instanceID;
            public bool isStorage;
            public float x, y, z, yaw;
        }
        [Serializable]
        internal sealed class SettlementRecord
        {
            public int groupId;
            public string role;
            public float x, y, z;
            public PlacedBarricadeRecord[] placed;
            internal Vector3 Anchor { get { return new Vector3(x, y, z); } }
        }
        [Serializable]
        private sealed class SaveFile
        {
            public int schema = Schema;
            public string map;
            public ActorRecord[] actors;
            public ReplacementRecord[] pending;
            public SettlementRecord[] settlements;
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
            List<ReplacementRecord> ignored;
            return TryRead(map, out actors, out ignored, out reason);
        }

        internal static bool TryRead(string map, out List<ActorRecord> actors,
            out List<ReplacementRecord> pending, out string reason)
        {
            List<SettlementRecord> ignored;
            return TryRead(map, out actors, out pending, out ignored, out reason);
        }

        internal static bool TryRead(string map, out List<ActorRecord> actors,
            out List<ReplacementRecord> pending,
            out List<SettlementRecord> settlements, out string reason)
        {
            actors = new List<ActorRecord>();
            pending = new List<ReplacementRecord>();
            settlements = new List<SettlementRecord>();
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
                ValidateActors(data.actors);
                ValidatePending(data.pending);
                ValidateSettlements(data.settlements);
                actors.AddRange(data.actors);
                if (data.pending != null) pending.AddRange(data.pending);
                if (data.settlements != null) settlements.AddRange(data.settlements);
                reason = "loaded=" + actors.Count + " pending=" + pending.Count +
                    " settlements=" + settlements.Count;
                return true;
            }
            catch (Exception error)
            {
                actors.Clear();
                pending.Clear();
                settlements.Clear();
                reason = "rejected:" + error.GetType().Name + ": " + error.Message;
                return false; // Don't overwrite a damaged save with empty data.
            }
        }
        private static void ValidateActors(ActorRecord[] actors)
        {
            if (actors == null || actors.Length > 32)
                throw new InvalidDataException("invalid actor count");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var groupSize = new Dictionary<int, int>();
            var groupRole = new Dictionary<int, Line2NPCFactionPolicy.Id>();
            foreach (ActorRecord a in actors)
            {
                if (a == null || string.IsNullOrEmpty(a.identity) ||
                    a.identity.Length > 96 || !seen.Add(a.identity) ||
                    (a.role != "hostile" && a.role != "neutral" && a.role != "friendly") ||
                    a.groupId < 0 || a.groupId > 1000000 ||
                    a.displayName == null || a.displayName.Length > 80 ||
                    !Finite(a.health) || a.health <= 0f || a.health > 100f ||
                    !Finite(a.x) || !Finite(a.y) || !Finite(a.z) ||
                    !Finite(a.homeX) || !Finite(a.homeY) || !Finite(a.homeZ) ||
                    a.items == null || a.items.Length > 18 ||
                    (a.hasNeeds && (!Finite(a.hunger) || !Finite(a.thirst) ||
                        a.hunger < 0f || a.hunger > 100f ||
                        a.thirst < 0f || a.thirst > 100f)) ||
                    (a.injuries != null &&
                     (a.injuries.condition == null ||
                      a.injuries.condition.Length != 6 ||
                      a.injuries.bleedingSeverity < 0 ||
                      a.injuries.bleedingSeverity > 2 ||
                      a.injuries.bleedingPart < 0 ||
                      a.injuries.bleedingPart > 5)))
                    throw new InvalidDataException("invalid actor");
                if (a.groupId != 0)
                {
                    int count;
                    groupSize.TryGetValue(a.groupId, out count);
                    if (++count > 4) throw new InvalidDataException("group exceeds cap");
                    groupSize[a.groupId] = count;
                    var faction = Line2NPCFactionPolicy.DefaultForRole(a.role);
                    Line2NPCFactionPolicy.Id existingFaction;
                    if (groupRole.TryGetValue(a.groupId, out existingFaction) &&
                        existingFaction != faction)
                        throw new InvalidDataException("mixed-faction group");
                    groupRole[a.groupId] = faction;
                }
                if (a.injuries != null)
                    foreach (float partCondition in a.injuries.condition)
                        if (!Finite(partCondition) || partCondition < 0f ||
                            partCondition > 100f)
                            throw new InvalidDataException("invalid injury");
                foreach (ItemRecord item in a.items)
                    if (item == null || item.id < 1 || item.id > ushort.MaxValue ||
                        item.amount < 0 || item.amount > byte.MaxValue ||
                        item.quality < 0 || item.quality > byte.MaxValue ||
                        item.state == null || item.state.Length > 2048)
                        throw new InvalidDataException("invalid item");
            }
        }

        internal static bool TryWrite(string map, List<ActorRecord> actors, out string reason)
        {
            return TryWrite(map, actors, new List<ReplacementRecord>(), out reason);
        }

        private static void ValidatePending(ReplacementRecord[] records)
        {
            if (records == null) return; // Optional in older schema-one saves.
            if (records.Length > 32)
                throw new InvalidDataException("too many replacement tickets");
            foreach (ReplacementRecord record in records)
                if (record == null ||
                    (record.role != "hostile" && record.role != "friendly" &&
                     record.role != "neutral") ||
                    record.formerGroupId < 0 || record.formerGroupId > 1000000 ||
                    record.readyAtUtcTicks < 630822816000000000L ||
                    record.readyAtUtcTicks > DateTime.MaxValue.Ticks)
                    throw new InvalidDataException("invalid replacement");
        }

        internal static bool TryWrite(string map, List<ActorRecord> actors,
            List<ReplacementRecord> pending, out string reason)
        {
            return TryWrite(map, actors, pending,
                new List<SettlementRecord>(), out reason);
        }

        private static void ValidateSettlements(SettlementRecord[] data)
        {
            if (data == null) return; // Original schema-one checkpoints.
            if (data.Length > 16)
                throw new InvalidDataException("too many group bases");
            var groups = new HashSet<int>();
            foreach (SettlementRecord site in data)
            {
                if (site == null || site.groupId <= 0 ||
                    site.groupId > 1000000 || !groups.Add(site.groupId) ||
                    (site.role != "hostile" && site.role != "neutral" &&
                     site.role != "friendly") ||
                    !Finite(site.x) || !Finite(site.y) || !Finite(site.z) ||
                    site.placed == null || site.placed.Length > 5)
                    throw new InvalidDataException("invalid settlement site");
                bool alreadyHasStorage = false;
                var instanceIDs = new HashSet<uint>();
                foreach (PlacedBarricadeRecord entry in site.placed)
                {
                    if (entry != null && entry.isStorage)
                    {
                        if (alreadyHasStorage)
                            throw new InvalidDataException("multiple central storages");
                        alreadyHasStorage = true;
                    }
                    if (entry != null && entry.instanceID != 0 &&
                        !instanceIDs.Add(entry.instanceID))
                        throw new InvalidDataException("duplicate native barricade instance");
                    if (entry == null || entry.assetId <= 0 ||
                        entry.assetId > ushort.MaxValue ||
                        !Finite(entry.x) || !Finite(entry.y) ||
                        !Finite(entry.z) || !Finite(entry.yaw) ||
                        Math.Abs(entry.x - site.x) > 30 ||
                        Math.Abs(entry.z - site.z) > 30 ||
                        Math.Abs(entry.y - site.y) > 4)
                        throw new InvalidDataException("invalid placed barricade");
                }
            }
        }

        internal static bool TryWrite(string map, List<ActorRecord> actors,
            List<ReplacementRecord> pending,
            List<SettlementRecord> settlements, out string reason)
        {
            reason = "denied";
            if (string.IsNullOrEmpty(map) || actors == null || actors.Count > 32 ||
                pending == null || pending.Count > 32 ||
                settlements == null || settlements.Count > 16 ||
                !Provider.isServer || Dedicator.IsDedicatedServer) return false;
            string temp = null;
            try
            {
                ActorRecord[] validated = actors.ToArray();
                ValidateActors(validated);
                ReplacementRecord[] tickets = pending.ToArray();
                ValidatePending(tickets);
                SettlementRecord[] sites = settlements.ToArray();
                ValidateSettlements(sites);
                string json = JsonUtility.ToJson(
                    new SaveFile { map = map, actors = validated,
                        pending = tickets, settlements = sites });
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
