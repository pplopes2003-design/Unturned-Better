////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Physically bounded generator maintenance for a living base group.
    /// Only real ownerless native world generators already registered by that
    /// settlement are eligible. Refueling consumes an actual ItemFuelAsset
    /// canister, preserving its native remaining-fuel state, then calls the
    /// same native generator fill and replication APIs as UseableFuel.
    /// No fake fuel, remote generator toggles or invented interaction clips.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCInventory))]
    [RequireComponent(typeof(MobileNPCGroupMember))]
    public sealed class MobileNPCBaseGeneratorOperator : MonoBehaviour
    {
        private const float ActionInterval = 12f;
        private const float MaximumOperationDistance = 3.3f;
        private const float MaximumStorageAccessDistance = 3.5f;
        private MobileNPCInventory inventory;
        private MobileNPCGroupMember member;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCBrainDriver driver;
        private float nextActionAt;
        private readonly RaycastHit[] interactionHits = new RaycastHit[16];

        private void Awake()
        {
            inventory = GetComponent<MobileNPCInventory>();
            member = GetComponent<MobileNPCGroupMember>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            driver = GetComponent<MobileNPCBrainDriver>();
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || Time.time < nextActionAt)
                return;
            nextActionAt = Time.time + ActionInterval;

            // Under fire, pursuing, or actively occupied NPCs should not
            // interrupt an urgent behavior to operate stationary machinery.
            if (inventory == null || member == null || member.GroupId <= 0
                || !member.HasSettlement || vitals == null || vitals.IsDead
                || driver == null || driver.Target != null
                || driver.CurrentGoal != MobileNPCTactics.Goal.None
                || !member.TryGetLivingLeaderServer(out MobileNPCGroupMember leader))
                return;

            MobileNPCSettlementBuilder builder =
                leader.GetComponent<MobileNPCSettlementBuilder>();
            if (builder == null || !builder.TryGetManagedGeneratorForPowerServer(
                out InteractableGenerator generator) || generator == null
                || !generator.gameObject.activeInHierarchy
                || (generator.transform.position - transform.position).sqrMagnitude
                    > MaximumOperationDistance * MaximumOperationDistance
                || !HasClearInteractionPath(generator.transform))
                return;

            // Native global electricity doesn't require operating a generator.
            bool hasGlobalElectricity = Level.info != null
                && Level.info.configData != null
                && Level.info.configData.Has_Global_Electricity;
            if (hasGlobalElectricity)
                return;

            if (generator.capacity > generator.fuel && generator.capacity > 0)
            {
                // Top up only when below half to avoid needless transactions
                // and log spam. A completely empty generator is eligible.
                if (generator.fuel <= generator.capacity / 2)
                {
                    ushort requested = (ushort)(generator.capacity - generator.fuel);
                    ushort spent = 0;

                    // Real central group storage is the first source: its
                    // inventory is a vanilla-saved physical container.
                    if (builder.TryGetCentralStorageForRadioServer(
                        out InteractableStorage storage)
                        && storage != null && storage.items != null
                        && (storage.transform.position - transform.position).sqrMagnitude
                            <= MaximumStorageAccessDistance * MaximumStorageAccessDistance
                        && HasClearInteractionPath(storage.transform))
                        spent = SpendStoredFuelServer(storage.items, requested);

                    // Otherwise use an actually owned fuel canister.
                    if (spent == 0)
                        inventory.TrySpendGeneratorFuelServer(requested, out spent);

                    if (spent > 0)
                    {
                        generator.askFill(spent);
                        BarricadeManager.sendFuel(generator.transform, generator.fuel);
                        Debug.Log("[MobileNPCBaseGeneratorOperator] Real fuel transferred: group="
                            + member.GroupId + ", units=" + spent
                            + ", generatorFuel=" + generator.fuel + "/"
                            + generator.capacity + ".", this);
                    }
                }
            }

            // Only activate a genuinely fueled machine; native server call
            // validates the actual barricade region and updates save/network
            // state. Never change private powered flags via reflection.
            if (!generator.isPowered && generator.fuel > 0)
            {
                if (BarricadeManager.ServerSetGeneratorPowered(generator, true))
                    Debug.Log("[MobileNPCBaseGeneratorOperator] Native generator activated for group="
                        + member.GroupId + " after nearby physical operation.", this);
            }
        }

        /// <summary>
        /// Distance alone does not grant interaction through locked rooms or
        /// constructed walls. Exclude the machine's own physical collider and
        /// the operator's NPC-only hitbox, but fail closed on any other wall.
        /// Non-allocating, only sampled on the 12-second maintenance cadence.
        /// </summary>
        private bool HasClearInteractionPath(Transform target)
        {
            if (target == null)
                return false;
            Vector3 origin = transform.position + Vector3.up;
            Vector3 endpoint = target.position + Vector3.up * 0.7f;
            Vector3 offset = endpoint - origin;
            float distance = offset.magnitude;
            if (distance < 0.25f || distance > MaximumStorageAccessDistance + 1f)
                return false;
            int count = Physics.RaycastNonAlloc(origin, offset / distance,
                interactionHits, distance, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (count >= interactionHits.Length)
                return false;
            for (int i = 0; i < count; i++)
            {
                Collider hit = interactionHits[i].collider;
                if (hit == null || hit.transform == target
                    || hit.transform.IsChildOf(target))
                    continue;
                return false;
            }
            return true;
        }

        private static ushort SpendStoredFuelServer(Items items, ushort requested)
        {
            if (!Provider.isServer || items == null || requested == 0)
                return 0;

            for (int i = 0; i < items.getItemCount(); i++)
            {
                ItemJar jar = items.getItem((byte)i);
                Item stored = jar != null ? jar.item : null;
                ItemFuelAsset asset = stored != null
                    ? stored.GetAsset<ItemFuelAsset>() : null;
                if (stored == null || stored.amount != 1 || asset == null
                    || asset.fuel == 0 || stored.state == null
                    || stored.state.Length < 2)
                    continue;

                ushort available = System.BitConverter.ToUInt16(stored.state, 0);
                if (available == 0 || available > asset.fuel)
                    continue;
                ushort amount = (ushort)Mathf.Min((int)requested, (int)available);
                if (amount == 0)
                    continue;

                ushort remaining = (ushort)(available - amount);
                if (remaining == 0 && asset.shouldDeleteAfterFillingTarget)
                {
                    items.removeItem((byte)i);
                }
                else
                {
                    byte[] changed = (byte[])stored.state.Clone();
                    byte[] bits = System.BitConverter.GetBytes(remaining);
                    changed[0] = bits[0];
                    changed[1] = bits[1];
                    items.updateState((byte)i, changed);
                }
                return amount;
            }
            return 0;
        }
    }
}
