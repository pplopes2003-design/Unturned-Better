using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Session map-scoped accounting for authentic group barricades. This is
    /// NOT an additional Unity wall system: the physical objects belong to
    /// vanilla BarricadeManager, which persists/despawns them normally.
    /// A claimed anchor alone creates no structure and consumes no item.
    /// </summary>
    internal static class Line2NpcSettlementLedger
    {
        private const int MaxGroups = 16;
        private const int MaxPieces = 4;
        private static readonly Dictionary<int,
            Line2NpcPersistence.SettlementRecord> _sites =
            new Dictionary<int, Line2NpcPersistence.SettlementRecord>(MaxGroups);
        private static string _map;

        internal static void ResetSession()
        {
            _sites.Clear();
            _map = null;
        }

        internal static void Restore(string map,
            List<Line2NpcPersistence.SettlementRecord> snapshots)
        {
            _sites.Clear();
            _map = map;
            if (snapshots == null) return;
            foreach (Line2NpcPersistence.SettlementRecord record in snapshots)
                if (record != null && record.groupId > 0 &&
                    record.placed != null && _sites.Count < MaxGroups)
                    _sites[record.groupId] = Clone(record);
        }

        private static bool Authorized()
        {
            return !string.IsNullOrEmpty(_map) &&
                Line2NpcPersistence.CurrentMapKey == _map &&
                Provider.isServer && !Dedicator.IsDedicatedServer &&
                Level.isLoaded;
        }

        internal static List<Line2NpcPersistence.SettlementRecord> Capture()
        {
            var result = new List<Line2NpcPersistence.SettlementRecord>(_sites.Count);
            if (!Authorized()) return result;
            foreach (Line2NpcPersistence.SettlementRecord site in _sites.Values)
                result.Add(Clone(site));
            return result;
        }

        private static Line2NpcPersistence.SettlementRecord Clone(
            Line2NpcPersistence.SettlementRecord original)
        {
            var parts = new Line2NpcPersistence.PlacedBarricadeRecord[
                original.placed.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var old = original.placed[i];
                parts[i] = new Line2NpcPersistence.PlacedBarricadeRecord
                {
                    assetId = old.assetId,
                    x = old.x, y = old.y, z = old.z, yaw = old.yaw
                };
            }
            return new Line2NpcPersistence.SettlementRecord
            {
                groupId = original.groupId,
                role = original.role,
                x = original.x, y = original.y, z = original.z,
                placed = parts
            };
        }

        internal static bool TryGet(int groupId, string role,
            out Vector3 site, out int pieces)
        {
            site = Vector3.zero;
            pieces = 0;
            Line2NpcPersistence.SettlementRecord record;
            if (!Authorized() || groupId <= 0 ||
                !_sites.TryGetValue(groupId, out record) ||
                record.role != role) return false;
            site = record.Anchor;
            pieces = record.placed.Length;
            return true;
        }

        internal static bool TryClaim(int groupId, string role, Vector3 site)
        {
            if (!Authorized() || groupId <= 0 || groupId > 1000000 ||
                (role != "friendly" && role != "neutral" && role != "hostile") ||
                float.IsNaN(site.x) || float.IsNaN(site.y) ||
                float.IsNaN(site.z) ||
                !Level.checkSafeIncludingClipVolumes(site)) return false;
            Line2NpcPersistence.SettlementRecord current;
            if (_sites.TryGetValue(groupId, out current))
                return current.role == role;
            if (_sites.Count >= MaxGroups) return false;
            // Preserve separate faction camps and avoid NPC construction
            // inside an established rival's physical footprint.
            foreach (Line2NpcPersistence.SettlementRecord other in
                _sites.Values)
                if ((other.Anchor - site).sqrMagnitude < 25f * 25f)
                    return false;
            _sites.Add(groupId, new Line2NpcPersistence.SettlementRecord
            {
                groupId = groupId, role = role,
                x = site.x, y = site.y, z = site.z,
                placed = new Line2NpcPersistence.PlacedBarricadeRecord[0]
            });
            return true;
        }

        internal static bool CommitPlaced(int groupId, string role,
            ushort assetId, Vector3 actualSite, float yaw)
        {
            Vector3 baseSite;
            int used;
            if (!TryGet(groupId, role, out baseSite, out used) ||
                used >= MaxPieces || assetId == 0 ||
                (actualSite - baseSite).sqrMagnitude > 30f * 30f)
                return false;
            Line2NpcPersistence.SettlementRecord record = _sites[groupId];
            foreach (Line2NpcPersistence.PlacedBarricadeRecord old in record.placed)
                if (new Vector3(old.x - actualSite.x,
                    old.y - actualSite.y, old.z - actualSite.z).sqrMagnitude <
                    0.6f * 0.6f)
                    return false; // Do not double-count the same footprint.
            var next = new Line2NpcPersistence.PlacedBarricadeRecord[used + 1];
            Array.Copy(record.placed, next, used);
            next[used] = new Line2NpcPersistence.PlacedBarricadeRecord
            {
                assetId = assetId,
                x = actualSite.x, y = actualSite.y, z = actualSite.z,
                yaw = yaw
            };
            record.placed = next;
            return true;
        }
    }
}
