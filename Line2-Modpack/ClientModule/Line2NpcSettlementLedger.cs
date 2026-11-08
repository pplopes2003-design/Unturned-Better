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
        private const int MaxPieces = 5; // central native storage plus four perimeter pieces
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
                {
                    Line2NPCGroupMember.ReserveHistoricalGroupId(record.groupId);
                    _sites[record.groupId] = Clone(record);
                }
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
                    instanceID = old.instanceID,
                    isStorage = old.isStorage,
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

        internal static bool HasCentralStorage(int groupId, string role)
        {
            Line2NpcPersistence.SettlementRecord record;
            if (!Authorized() || !_sites.TryGetValue(groupId, out record) ||
                record.role != role) return false;
            foreach (Line2NpcPersistence.PlacedBarricadeRecord part in record.placed)
                if (part.isStorage) return true;
            return false;
        }

        internal static bool TryGetCentralStorage(int groupId, string role,
            out Line2NpcPersistence.PlacedBarricadeRecord storage)
        {
            storage = null;
            Line2NpcPersistence.SettlementRecord record;
            if (!Authorized() || !_sites.TryGetValue(groupId, out record) ||
                record.role != role) return false;
            foreach (Line2NpcPersistence.PlacedBarricadeRecord part in record.placed)
                if (part.isStorage)
                {
                    storage = new Line2NpcPersistence.PlacedBarricadeRecord
                    {
                        assetId = part.assetId,
                        instanceID = part.instanceID,
                        isStorage = true,
                        x = part.x, y = part.y, z = part.z, yaw = part.yaw
                    };
                    return true;
                }
            return false;
        }

        /// <summary>
        /// Remove ONLY individually VERIFIED missing native structures from
        /// a previously saved camp. Caller is responsible for proving that
        /// the nearby vanilla BarricadeManager is fully loaded and each
        /// matching object really does NOT exist. A manifest is not a wall:
        /// this does not destroy, spawn, or award any native barricade.
        /// </summary>
        internal static bool ReconcileObservedPieces(int groupId, string role,
            Func<Line2NpcPersistence.PlacedBarricadeRecord, bool> stillExists)
        {
            Line2NpcPersistence.SettlementRecord record;
            if (!Authorized() || stillExists == null ||
                !_sites.TryGetValue(groupId, out record) ||
                record.role != role || record.placed.Length == 0)
                return false;
            var kept = new List<Line2NpcPersistence.PlacedBarricadeRecord>(
                record.placed.Length);
            try
            {
                foreach (Line2NpcPersistence.PlacedBarricadeRecord piece in record.placed)
                    if (stillExists(piece)) kept.Add(piece);
            }
            catch (Exception error)
            {
                // If a native registration lookup unexpectedly throws, we
                // lack positive evidence of a destroyed structure. Keep
                // EVERY saved piece; otherwise a partial scan could delete
                // the group's real base and incorrectly reuse its slot.
                ProbeLogger.Log("NPC_CAMP_RECONCILE_DEFERRED: " +
                    error.GetType().Name + " group=" + groupId);
                return false;
            }
            if (kept.Count == record.placed.Length) return false;
            record.placed = kept.ToArray();
            return true;
        }

        /// <summary>
        /// A confirmed extinct NPC group ceases to own a settlement SLOT,
        /// but the actual BarricadeManager objects are left intact as world
        /// ruins. The caller must prove the full restore queue has no pending
        /// surviving member, and no living group member remains. Delayed
        /// replacement tickets inherit only disposition, never a dead camp.
        /// </summary>
        internal static bool RetireExtinctGroup(int groupId,
            bool verifiedNoLivingMembersOrPendingRestore)
        {
            if (!Authorized() || groupId <= 0 ||
                !verifiedNoLivingMembersOrPendingRestore)
                return false;
            // Group ID remains historically reserved by the runtime; there
            // is no right to resurrect a destroyed group's saved property.
            return _sites.Remove(groupId);
        }

        internal static bool IsPositionOccupied(int groupId, string role,
            Vector3 requested, float radius)
        {
            Line2NpcPersistence.SettlementRecord record;
            if (!Authorized() || !_sites.TryGetValue(groupId, out record) ||
                record.role != role || radius < 0.1f) return false;
            float d2 = radius * radius;
            foreach (Line2NpcPersistence.PlacedBarricadeRecord part in record.placed)
                if ((new Vector3(part.x, part.y, part.z) - requested)
                    .sqrMagnitude <= d2) return true;
            return false;
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
            return CommitPlaced(groupId, role, assetId, actualSite, yaw,
                0u, false);
        }

        internal static bool CommitPlaced(int groupId, string role,
            ushort assetId, Vector3 actualSite, float yaw,
            uint instanceID, bool isStorage)
        {
            Vector3 baseSite;
            int used;
            if (!TryGet(groupId, role, out baseSite, out used) ||
                used >= MaxPieces || assetId == 0 ||
                (actualSite - baseSite).sqrMagnitude > 30f * 30f)
                return false;
            Line2NpcPersistence.SettlementRecord record = _sites[groupId];
            foreach (Line2NpcPersistence.PlacedBarricadeRecord old in record.placed)
            {
                if (isStorage && old.isStorage)
                    return false; // Only one central native container.
                if (instanceID != 0 && old.instanceID == instanceID)
                    return false; // One native object belongs to one slot.
                if (new Vector3(old.x - actualSite.x,
                    old.y - actualSite.y, old.z - actualSite.z).sqrMagnitude <
                    0.6f * 0.6f)
                    return false; // Do not double-count the same footprint.
            }
            var next = new Line2NpcPersistence.PlacedBarricadeRecord[used + 1];
            Array.Copy(record.placed, next, used);
            next[used] = new Line2NpcPersistence.PlacedBarricadeRecord
            {
                assetId = assetId,
                instanceID = instanceID,
                isStorage = isStorage,
                x = actualSite.x, y = actualSite.y, z = actualSite.z,
                yaw = yaw
            };
            record.placed = next;
            return true;
        }
    }
}
