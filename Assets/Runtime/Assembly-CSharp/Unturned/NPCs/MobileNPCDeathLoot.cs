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
        [SerializeField, Range(0, 100)] private int hostileNPCDefeatXP = 10;
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
            // XP is optional, but dropping legitimately owned items is part
            // of the death lifecycle. If stats/skills throw while awarding XP,
            // still transfer or retain the corpse's inventory for retry.
            try
            {
                AwardLegitimateHostileDefeatXPServer();
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[MobileNPCDeathLoot] XP reward failed for " + name
                    + " (corpse loot will still be attempted): " + exception, this);
            }
            finally
            {
                ReleaseRealInventoryServer();
            }
        }

        /// <summary>
        /// Optional modest reward through the actual vanilla experience system.
        /// A non-hostile survivor never awards farming XP. This is not a fake
        /// Player kill event, statistics entry or quest completion.
        /// </summary>
        private void AwardLegitimateHostileDefeatXPServer()
        {
            if (!Provider.isServer || hostileNPCDefeatXP <= 0 || vitals == null)
                return;
            MobileNPCHumanoidDisposition disposition = GetComponent<MobileNPCHumanoidDisposition>();
            if (disposition == null || disposition.CurrentRole != MobileNPCHumanoidDisposition.Role.Hostile)
                return;
            Transform attacker = vitals.LastAttacker;
            Player killer = attacker != null ? attacker.GetComponentInParent<Player>() : null;
            if (killer == null || killer.skills == null)
                return;
            killer.skills.askAward((uint)hostileNPCDefeatXP);
            Debug.Log("[MobileNPCDeathLoot] Awarded " + hostileNPCDefeatXP
                + " vanilla XP to direct player killer of hostile " + name + ".", this);
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
                // Keep ownership of currently unavailable Workshop assets too:
                // the journal can recover them after that content is installed.
                // Never materialize Pro-only or unknown items as ordinary drops.
                bool canDrop = asset != null && !asset.isPro;

                // Claim inventory ownership first. If the world refuses the drop,
                // restore EXACTLY the original item with amount, quality and state.
                if (!inventory.TryTakeItemServer(slot, out Item transferred))
                {
                    ++refused;
                    continue;
                }

                // Place the drop slightly beside the corpse rather than
                // through the new rigidbody capsule. This allows ItemManager's
                // downward ground probe to find real ground, not the corpse's
                // own dynamic collider immediately below its center.
                Vector3 dropAnchor = transform.position + transform.right * 0.9f
                    + Vector3.up * 0.6f;
                bool inserted = false;
                if (canDrop)
                {
                    try { inserted = ItemManager.TryDropMobileNPCItemServer(transferred, dropAnchor); }
                    catch (System.Exception exception)
                    {
                        Debug.LogError("[MobileNPCDeathLoot] Native drop failed: " + exception, this);
                    }
                }
                if (inserted)
                {
                    ++released;
                }
                else
                {
                    // Durable journal owns the refused item only after disk commit.
                    // Otherwise put the same item back into the dead inventory.
                    if (!MobileNPCCorpseLootJournal.TryAcceptServer(transferred, dropAnchor)
                        && !inventory.TryStoreTransferredItem(transferred))
                        Debug.LogError("[MobileNPCDeathLoot] Journal and rollback both failed for "
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
