////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Combat-only native demolition charge: physically approach a personally
    /// recognized hostile NPC storage, own an ItemChargeAsset AND an actual
    /// ItemDetonatorAsset, attach ONE vanilla InteractableCharge to the visible
    /// storage surface, withdraw on a motor-validated path, then detonate using
    /// InteractableCharge.Detonate(null). No fake Player object or custom
    /// structure damage. Cannot harm a remembered or unseen base.
    ///
    /// A charge belongs to the vanilla world once planted; if the breacher
    /// dies before triggering it the world owns the unresolved explosive.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCInventory))]
    [RequireComponent(typeof(MobileNPCTerritoryRaidPlanner))]
    public sealed class MobileNPCNativeChargeBreacher : MonoBehaviour
    {
        private readonly List<MobileNPCBrainDriver> nearby =
            new List<MobileNPCBrainDriver>(40);
        private MobileNPCInventory inventory;
        private MobileNPCTerritoryRaidPlanner scout;
        private MobileNPCBrainDriver brain;
        private MobileNPCHumanoidMotor motor;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCGroupMember group;
        private MobileNPCFaction faction;
        private InteractableCharge plantedCharge;
        private Transform plantedNativeRoot;
        private Vector3 plantedPosition;
        private float plantedAt;
        private float nextEvaluateAt;
        private Vector3 plannedWithdrawal;

        private void Awake()
        {
            inventory = GetComponent<MobileNPCInventory>();
            scout = GetComponent<MobileNPCTerritoryRaidPlanner>();
            brain = GetComponent<MobileNPCBrainDriver>();
            motor = GetComponent<MobileNPCHumanoidMotor>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            group = GetComponent<MobileNPCGroupMember>();
            faction = GetComponent<MobileNPCFaction>();
            nextEvaluateAt = Time.time + Random.Range(3f, 7f);
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || !MobileNPCSettings.CombatMode
                || Time.time < nextEvaluateAt || inventory == null
                || scout == null || brain == null || motor == null || vitals == null
                || vitals.IsDead || group == null || group.GroupId <= 0
                || faction == null)
                return;
            nextEvaluateAt = Time.time + 1.4f;

            if (plantedCharge != null)
            {
                // Never order instant remote detonation. Vanilla explosion
                // must pass shooter distance AND all known ally safety checks.
                ItemChargeAsset deployed = FindOwnedChargeAssetForPlacedNative();
                if (deployed != null && Time.time > plantedAt + 2.8f
                    && (transform.position - plantedPosition).sqrMagnitude
                        > Mathf.Pow(deployed.range2 + 5f, 2f)
                    && !AlliesOrPlayerNear(plantedPosition, deployed.range2 + 4f))
                {
                    InteractableCharge charge = plantedCharge;
                    plantedCharge = null;
                    if (charge != null && charge.gameObject.activeInHierarchy)
                        charge.Detonate(null); // native DamageTool.explode, no false Player
                    nextEvaluateAt = Time.time + 28f;
                }
                // If the NPC cannot safely retreat, leave the native charge
                // in the world. A later authorized detonation is a separate task.
                return;
            }

            if (vitals.HealthFraction < 0.54f
                || brain.CurrentGoal == MobileNPCTactics.Goal.Retreat
                || brain.CurrentGoal == MobileNPCTactics.Goal.SeekCover
                || !scout.TryGetVisibleHostileStorageServer(out InteractableStorage depot))
                return;
            int chargeSlot = -1;
            ItemChargeAsset chargeAsset = null;
            bool hasDetonator = false;
            for (int i = 0; i < inventory.UsedSlots; ++i)
            {
                if (!inventory.TryGetItemSnapshot(i, out Item item)
                    || item == null || item.amount == 0) continue;
                ItemAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemAsset;
                if (asset is ItemDetonatorAsset) hasDetonator = true;
                if (chargeSlot < 0 && asset is ItemChargeAsset charge
                    && charge.build == EBuild.CHARGE && charge.barricade != null
                    && charge.barricade.GetComponentInChildren<InteractableCharge>(true) != null
                    && (charge.structureDamage > 0f || charge.barricadeDamage > 0f))
                { chargeSlot = i; chargeAsset = charge; }
            }
            if (!hasDetonator || chargeSlot < 0 || chargeAsset == null)
                return;
            Vector3 eye = transform.position + Vector3.up * 1.45f;
            Vector3 target = depot.transform.position + Vector3.up * 0.65f;
            Vector3 toward = target - eye;
            float distance = toward.magnitude;
            if (distance > Mathf.Min(3.1f, chargeAsset.range)
                || distance < 0.65f)
                return;
            if (!Physics.SphereCast(eye, 0.1f, toward.normalized,
                out RaycastHit hit, distance + 0.35f, RayMasks.BARRICADE_INTERACT,
                QueryTriggerInteraction.Ignore)
                || hit.transform == null
                || hit.transform != depot.transform
                    && !hit.transform.IsChildOf(depot.transform))
                return;
            Vector3 position = hit.point + hit.normal * chargeAsset.offset;
            Vector3 retreatDirection = transform.position - position;
            retreatDirection.y = 0f;
            if (retreatDirection.sqrMagnitude < 0.1f) return;
            retreatDirection.Normalize();
            float retreatDistance = Mathf.Max(8f, chargeAsset.range2 + 6f);
            if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
                transform.position + retreatDirection * retreatDistance,
                2f, 2f, RayMasks.BLOCK_COLLISION | RayMasks.GROUND2,
                45f, out Vector3 retreat, out _)
                || Mathf.Abs(retreat.y - transform.position.y) > 0.9f
                || (retreat - position).sqrMagnitude
                    < Mathf.Pow(chargeAsset.range2 + 5f, 2f)
                || !motor.CanReachNearbyCoverServer(retreat)
                || AlliesOrPlayerNear(position, chargeAsset.range2 + 3.5f))
                return;
            Quaternion align = Quaternion.LookRotation(hit.normal);
            Vector3 angles = align.eulerAngles;

            // Consume the real item BEFORE native placement. The prefab
            // was prechecked to contain the real charge component.
            if (!inventory.TryTakeItemServer(chargeSlot, out Item committed))
                return;
            if (committed == null || committed.id != chargeAsset.id)
            {
                if (committed != null) inventory.TryStoreTransferredItem(committed);
                return;
            }
            Transform nativeRoot = BarricadeManager.dropBarricade(
                new Barricade(chargeAsset), null, position,
                angles.x, angles.y, angles.z, 0UL, 0UL);
            if (nativeRoot == null)
            {
                if (!inventory.TryStoreTransferredItem(committed))
                    UnturnedLog.warn("[MobileNPCCharge] Failed to restore an unplaced charge.");
                return;
            }
            InteractableCharge placed = nativeRoot.GetComponentInChildren<InteractableCharge>();
            if (placed == null)
            {
                // Fail closed rather than triggering any unrelated world
                // charge through an approximate location.
                UnturnedLog.warn("[MobileNPCCharge] Native placed prefab lacks InteractableCharge: manual inspection required.");
                return;
            }
            if (committed.amount > 1)
            {
                Item surplus = new Item(committed.id, (byte)(committed.amount - 1),
                    committed.quality, committed.state != null
                        ? (byte[])committed.state.Clone() : new byte[0]);
                if (!inventory.TryStoreTransferredItem(surplus))
                    UnturnedLog.warn("[MobileNPCCharge] Failed to return remaining stacked charges.");
            }
            plantedCharge = placed;
            plantedNativeRoot = nativeRoot;
            plantedPosition = position;
            plantedAt = Time.time;
            plannedWithdrawal = retreat;
            // Hard priority: a planted charge is not a reason to stand
            // fighting in its blast radius. Use validated vanilla tactics.
            if (!brain.TryStartChargeWithdrawalServer(plannedWithdrawal))
                UnturnedLog.warn("[MobileNPCCharge] Native charge planted but NPC retreat unavailable; detonation remains gated by safety.");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCCharge] Native charge placed at a presently visible hostile base. Withdrawal required before detonation.", this);
#endif
        }

        // Vanilla charge asset is NOT reconstructed from coordinates. A native
        // InteractableCharge's own registered prefab is the only owner.
        private ItemChargeAsset FindOwnedChargeAssetForPlacedNative()
        {
            if (plantedCharge == null) return null;
            BarricadeDrop native = plantedNativeRoot != null
                ? BarricadeDrop.FindByTransformFastMaybeNull(plantedNativeRoot) : null;
            return native != null && native.asset is ItemChargeAsset charge
                ? charge : null;
        }

        private bool AlliesOrPlayerNear(Vector3 site, float safetyRadius)
        {
            float limit = safetyRadius * safetyRadius;
            if ((transform.position - site).sqrMagnitude <= limit) return true;
            // Avoid unexpectedly blowing up the local user at a custom
            // NPC base in the absence of native player/NPC faction attribution.
            Player local = Player.player;
            if (local != null && local.life != null && !local.life.isDead
                && (local.transform.position - site).sqrMagnitude <= limit)
                return true;
            MobileNPCRegistry.CopyActiveDrivers(nearby);
            for (int i = 0; i < nearby.Count; ++i)
            {
                MobileNPCBrainDriver other = nearby[i];
                if (other == null || other == brain) continue;
                MobileNPCHumanoidVitals life = other.GetComponent<MobileNPCHumanoidVitals>();
                if (life == null || life.IsDead) continue;
                MobileNPCGroupMember theirGroup = other.GetComponent<MobileNPCGroupMember>();
                MobileNPCFaction theirFaction = other.GetComponent<MobileNPCFaction>();
                bool allied = theirGroup != null && theirGroup.GroupId > 0
                    && theirGroup.GroupId == group.GroupId
                    || theirFaction != null && MobileNPCFaction.GetRelationship(
                        faction.CurrentFaction, theirFaction.CurrentFaction)
                        == MobileNPCFaction.Relationship.Allied;
                if (allied && (other.transform.position - site).sqrMagnitude < limit)
                    return true;
            }
            return false;
        }

        private void OnDisable()
        {
            // Physically placed charges remain vanilla world objects. A new
            // survivor must not inherit the dead operator's remote detonation.
            plantedCharge = null;
            plantedNativeRoot = null;
            nearby.Clear();
        }
    }
}
