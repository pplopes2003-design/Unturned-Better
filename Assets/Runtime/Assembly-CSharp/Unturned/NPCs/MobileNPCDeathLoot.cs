////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// On server-authoritative death, convert only ACTUALLY OWNED NPC inventory
    /// items into vanilla world drops. No synthetic loot, random gun, or XP.
    /// The ItemManager helper acknowledges a successful region insertion;
    /// a refused drop is restored to the corpse and retried at a bounded rate.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCHumanoidVitals))]
    [RequireComponent(typeof(MobileNPCInventory))]
    public sealed class MobileNPCDeathLoot : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float retryIntervalSeconds = 4f;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCInventory inventory;
        private bool deathObserved;
        private float nextRetryAt;

        private void Awake()
        {
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            inventory = GetComponent<MobileNPCInventory>();
        }

        private void OnEnable()
        {
            if (vitals == null)
                vitals = GetComponent<MobileNPCHumanoidVitals>();
            if (inventory == null)
                inventory = GetComponent<MobileNPCInventory>();
            if (vitals != null)
                vitals.Died += OnDied;
        }

        private void OnDisable()
        {
            if (vitals != null)
                vitals.Died -= OnDied;
        }

        private void OnDied()
        {
            if (!Provider.isServer || deathObserved)
                return;
            deathObserved = true;
            ReleaseRealInventoryServer();
        }

        private void Update()
        {
            if (!Provider.isServer || !deathObserved || vitals == null
                || !vitals.IsDead || inventory == null || inventory.UsedSlots == 0
                || Time.time < nextRetryAt)
                return;
            ReleaseRealInventoryServer();
        }

        private void ReleaseRealInventoryServer()
        {
            if (!Provider.isServer || vitals == null || !vitals.IsDead || inventory == null)
                return;
            nextRetryAt = Time.time + Mathf.Max(1f, retryIntervalSeconds);

            // Iterate backward so removing a slot never skips the next item.
            // A pro-only or absent asset is never spawned into ordinary world loot.
            int released = 0;
            int refused = 0;
            for (int slot = inventory.UsedSlots - 1; slot >= 0; --slot)
            {
                if (!inventory.TryGetItemSnapshot(slot, out Item snapshot) || snapshot == null)
                    continue;
                ItemAsset asset = snapshot.GetAsset();
                if (asset == null || asset.isPro)
                {
                    ++refused;
                    continue;
                }

                // Claim inventory ownership first. If the world refuses the drop,
                // restore EXACTLY the original item with amount, quality and state.
                if (!inventory.TryTakeItemServer(slot, out Item transferred))
                {
                    ++refused;
                    continue;
                }

                if (ItemManager.TryDropMobileNPCItemServer(transferred,
                    transform.position + Vector3.up * 0.6f))
                {
                    ++released;
                }
                else
                {
                    if (!inventory.TryStoreTransferredItem(transferred))
                        Debug.LogError("[MobileNPCDeathLoot] Could not roll back refused corpse loot for "
                            + name + ", item=" + transferred.id + ". Inspect inventory reservations.", this);
                    ++refused;
                }
            }
            if (released > 0)
                Debug.Log("[MobileNPCDeathLoot] Dropped " + released
                    + " genuinely owned item slot(s) from " + name + ".", this);
            if (refused > 0)
                Debug.LogWarning("[MobileNPCDeathLoot] Retained " + refused
                    + " item slot(s) in corpse for retry or because the asset cannot drop: "
                    + name + ".", this);
        }
    }
}
