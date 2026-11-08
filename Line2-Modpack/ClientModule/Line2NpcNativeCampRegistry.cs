using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Maps SAVED camp manifest entries back to actual registered
    /// BarricadeManager objects. No FindObjectsOfType, ownership spoofing,
    /// synthetic drop, or player storage access. A legacy record without
    /// instanceID requires a UNIQUE native candidate of the exact asset at
    /// the original saved position; otherwise it is not trusted.
    /// </summary>
    internal sealed class Line2NpcNativeCampRegistry
    {
        private readonly List<RegionCoordinate> _regions =
            new List<RegionCoordinate>(9);
        private readonly List<Transform> _nearby =
            new List<Transform>(32);

        internal bool TryIdentifyPlaced(Transform worldRoot, ushort assetId,
            out uint instanceID)
        {
            instanceID = 0u;
            if (worldRoot == null || assetId == 0 ||
                !Provider.isServer || BarricadeManager.instance == null)
                return false;
            BarricadeDrop found;
            if (!TryGetNative(worldRoot, out found) ||
                found.asset == null || found.asset.id != assetId ||
                found.instanceID == 0) return false;
            instanceID = found.instanceID;
            return true;
        }

        internal bool TryFindSavedPiece(
            Line2NpcPersistence.PlacedBarricadeRecord entry,
            out BarricadeDrop native)
        {
            native = null;
            if (!Provider.isServer || !Level.isLoaded ||
                BarricadeManager.instance == null || entry == null ||
                entry.assetId <= 0 || entry.assetId > ushort.MaxValue)
                return false;
            Vector3 point = new Vector3(entry.x, entry.y, entry.z);
            _regions.Clear();
            _nearby.Clear();
            Regions.getRegionsInRadius(point, 2.0f, _regions);
            BarricadeManager.getBarricadesInRadius(
                point, 2.0f * 2.0f, _regions, _nearby);
            int foundCount = 0;
            for (int i = 0; i < _nearby.Count && i < 32; ++i)
            {
                Transform candidate = _nearby[i];
                if (candidate == null ||
                    (candidate.position - point).sqrMagnitude > 0.85f * 0.85f)
                    continue;
                BarricadeDrop next;
                if (!TryGetNative(candidate, out next) || next.asset == null ||
                    next.asset.id != entry.assetId)
                    continue;
                if (entry.instanceID != 0 &&
                    next.instanceID != entry.instanceID)
                    continue;
                ++foundCount;
                native = next;
                if (foundCount > 1) break;
            }
            _nearby.Clear();
            _regions.Clear();
            if (foundCount != 1) { native = null; return false; }
            // Reconciliation checks physical EXISTENCE, not whether the
            // native InteractableStorage child finished initializing in
            // this frame. The separate access path requires a fully
            // instantiated inventory before anyone deposits real items.
            if (entry.isStorage && native.asset.build != EBuild.STORAGE)
            {
                native = null;
                return false;
            }
            return true;
        }

        private static bool TryGetNative(Transform worldRoot,
            out BarricadeDrop result)
        {
            result = null;
            byte x, y;
            ushort plant;
            BarricadeRegion region;
            if (!BarricadeManager.tryGetRegion(
                worldRoot, out x, out y, out plant, out region) ||
                region == null)
                return false;
            BarricadeDrop found =
                region.FindBarricadeByRootTransform(worldRoot);
            BarricadeData info = found != null ?
                found.GetServersideData() : null;
            if (found == null || found.model == null || info == null ||
                info.owner != 0ul || info.group != 0ul ||
                info.barricade == null || info.barricade.isDead)
                return false;
            result = found;
            return true;
        }

        internal bool TryGetRealStorage(
            Line2NpcPersistence.PlacedBarricadeRecord record,
            out InteractableStorage storage)
        {
            storage = null;
            if (record == null || !record.isStorage) return false;
            BarricadeDrop found;
            if (!TryFindSavedPiece(record, out found) ||
                found.model == null) return false;
            storage = found.model.GetComponentInChildren<InteractableStorage>(true);
            // Must be fully initialized native inventory. Unknown player
            // storage at the same coordinates is never a valid substitute.
            if (storage == null || !storage.gameObject.activeInHierarchy ||
                storage.items == null)
            {
                storage = null;
                return false;
            }
            return true;
        }
    }
}
