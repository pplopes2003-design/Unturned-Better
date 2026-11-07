using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Drops ONLY a dead survivor's already-owned items into native Steam
    /// ItemManager regions. No item is generated from a guessed loot table,
    /// no drops are taken from the environment, and no corpse identity is
    /// inherited by later replacement NPCs. Successful dead NPCs remain
    /// absent from the survivor snapshot.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcDeathLoot : MonoBehaviour
    {
        private PrototypeNpcVitals _vitals;
        private Line2MobileNPCInventory _inventory;
        private bool _resolved;

        private void Awake()
        {
            _vitals = GetComponent<PrototypeNpcVitals>();
            _inventory = GetComponent<Line2MobileNPCInventory>();
            if (_vitals != null)
                _vitals.Died += OnDied;
        }

        private void OnDestroy()
        {
            if (_vitals != null) _vitals.Died -= OnDied;
        }

        private void OnDied()
        {
            if (_resolved) return;
            _resolved = true;
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || _inventory == null ||
                (Provider.clients != null && Provider.clients.Count > 1))
                return;
            int count = Mathf.Min(18, _inventory.UsedSlots);
            int dropped = 0;
            int rejected = 0;
            // Before consuming owned inventory, validate that the target
            // world region exists and has a floor; never create drops in the
            // invalid outskirts or while the map is unloading.
            Vector3 center = transform.position;
            byte regionX, regionY;
            if (!Regions.tryGetCoordinate(center, out regionX, out regionY) ||
                !Level.checkSafeIncludingClipVolumes(center))
            {
                ProbeLogger.Log("NPC_DEATH_LOOT_SKIPPED: unsafe map region actor=" + name);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                // Slot zero advances as each item is transferred out.
                Item owned;
                if (!_inventory.TryGetItemSnapshot(0, out owned))
                    break;
                ItemAsset asset = Assets.find(EAssetType.ITEM, owned.id) as ItemAsset;
                if (asset == null || asset.isPro || owned.id == 0)
                {
                    rejected++;
                    // Unusable or PRO items are never minted as world drops.
                    Item ignored;
                    _inventory.TryTakeItemServer(0, out ignored);
                    continue;
                }
                Item transferred;
                if (!_inventory.TryTakeItemServer(0, out transferred))
                {
                    rejected++;
                    break;
                }
                try
                {
                    float angle = (i * 2.399963f);
                    float r = 0.25f + 0.075f * (i % 4);
                    Vector3 offset = new Vector3(
                        Mathf.Cos(angle) * r, 0.65f, Mathf.Sin(angle) * r);
                    // Native ItemManager handles region registry, physical
                    // placement, persistence and client visibility.
                    ItemManager.dropItem(transferred, center + offset,
                        playEffect: false, isDropped: true, wideSpread: false);
                    dropped++;
                }
                catch (Exception error)
                {
                    rejected++;
                    ProbeLogger.Log("NPC_DEATH_LOOT_DROP_FAILED: item=" +
                        transferred.id + " error=" + error.GetType().Name);
                    // Once killed, NPC inventory must never be resurrected,
                    // even if the native item API rejected the world drop.
                }
            }
            ProbeLogger.Log("NPC_DEATH_LOOT: actor=" + name +
                " dropped=" + dropped + " skipped=" + rejected);
        }
    }
}
