////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt file for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Conservative server-owned native grenade throw: real ItemThrowableAsset,
    /// one consumed inventory instance, native prefab Rigidbody, native Grenade
    /// fuse/effects/DamageTool, and explicit NPC body splash extension.
    /// Standard fused explosive only, no imagined PlayerEquipment or infinite ammo.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCInventory))]
    [RequireComponent(typeof(MobileNPCBrainDriver))]
    public sealed class MobileNPCGrenadeThrower : MonoBehaviour
    {
        private const float CheckEvery = 1.8f;
        private const float Cooldown = 45f;
        private const float MinimumRange = 11f;
        private const float MaximumRange = 27f;
        private const float MinimumHealth = 0.34f;
        private static readonly List<MobileNPCBrainDriver> Active =
            new List<MobileNPCBrainDriver>(40);
        private static readonly RaycastHit[] ObstructionHits = new RaycastHit[16];
        private MobileNPCBrainDriver brain;
        private MobileNPCInventory inventory;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCHumanoidPerception perception;
        private float nextCheck;
        private float nextThrowAt;

        private void Awake()
        {
            brain = GetComponent<MobileNPCBrainDriver>();
            inventory = GetComponent<MobileNPCInventory>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            perception = GetComponent<MobileNPCHumanoidPerception>();
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || Time.time < nextCheck)
                return;
            nextCheck = Time.time + CheckEvery;
            if (Time.time < nextThrowAt || brain == null || brain.Target == null
                || brain.CurrentState != MobileNPCBrain.State.Pursuing
                || brain.CurrentGoal == MobileNPCTactics.Goal.Retreat
                || brain.CurrentGoal == MobileNPCTactics.Goal.SeekCover
                || brain.IsAmbushPreparing || vitals == null || vitals.IsDead
                || vitals.HealthFraction < MinimumHealth || inventory == null
                || perception == null)
                return;

            Transform enemy = brain.Target;
            bool genuinelyVisible = perception.VisiblePlayer != null
                    && enemy.IsChildOf(perception.VisiblePlayer.transform)
                || perception.VisibleNPC != null
                    && enemy.IsChildOf(perception.VisibleNPC.transform)
                || perception.VisibleZombie != null
                    && enemy.IsChildOf(perception.VisibleZombie.transform);
            if (!genuinelyVisible)
                return;

            Vector3 origin = transform.position + Vector3.up * 1.45f;
            Vector3 target = enemy.position + Vector3.up * 0.3f;
            Vector3 horizontal = target - origin;
            horizontal.y = 0f;
            float range = horizontal.magnitude;
            if (range < MinimumRange || range > MaximumRange)
                return;

            for (int i = 0; i < inventory.UsedSlots; i++)
            {
                if (!inventory.TryGetItemSnapshot(i, out Item item))
                    continue;
                ItemThrowableAsset asset = Assets.find(EAssetType.ITEM, item.id)
                    as ItemThrowableAsset;
                if (asset == null || !asset.isExplosive || asset.isSticky
                    || asset.explodeOnImpact || asset.throwable == null
                    || asset.range < 3f || asset.fuseLength < 1.5f
                    || asset.fuseLength > 5f)
                    continue;

                // Require predicted target far enough from the thrower AND
                // every other *living friendly NPC*. Fuses are not perfectly
                // deterministic, so this is a conservative no-throw filter.
                float unsafeRange = asset.range + 3.5f;
                if (range < unsafeRange + 3f || AlliesInBlastZone(target, unsafeRange))
                    continue;

                float travel = Mathf.Clamp(range / 13.5f, 0.8f,
                    Mathf.Min(2.1f, asset.fuseLength - 0.4f));
                Vector3 gravity = Physics.gravity;
                Vector3 velocity = (target - origin - 0.5f * gravity * travel * travel)
                    / travel;
                if (velocity.magnitude > 31f || !ArcInitiallyClear(origin,
                    velocity, gravity, travel * 0.8f, enemy))
                    continue;

                // Instantiate FIRST to guarantee a real prefab/Rigidbody
                // before debiting actual inventory. The new object is not
                // used until one owned item has been consumed.
                GameObject grenadeObject = Object.Instantiate(asset.throwable,
                    origin, Quaternion.LookRotation(velocity));
                if (grenadeObject == null)
                    return;
                Rigidbody body = grenadeObject.GetComponent<Rigidbody>();
                if (body == null || !inventory.TryConsumeSimpleGrenadeServer(
                    item.id, out ItemThrowableAsset spent))
                {
                    Object.Destroy(grenadeObject);
                    return;
                }

                grenadeObject.name = "MobileNPC_NativeGrenade";
                EffectManager.RegisterDebris(grenadeObject);
                Grenade native = grenadeObject.AddComponent<Grenade>();
                native.range = spent.range;
                native.playerDamage = spent.playerDamageMultiplier.damage;
                native.zombieDamage = spent.zombieDamageMultiplier.damage;
                native.animalDamage = spent.animalDamageMultiplier.damage;
                native.barricadeDamage = spent.barricadeDamage;
                native.structureDamage = spent.structureDamage;
                native.vehicleDamage = spent.vehicleDamage;
                native.resourceDamage = spent.resourceDamage;
                native.objectDamage = spent.objectDamage;
                native.explosionEffectGuid = spent.explosionEffectGuid;
                native.explosion = spent.explosion;
                native.fuseLength = spent.fuseLength;
                native.explosionLaunchSpeed = spent.explosionLaunchSpeed;

                // Native explosion damages Player, Zombie and world props.
                // MobileNPC bodies are not vanilla Player/Zombie objects.
                MobileNPCGrenadeNPCSplash splash =
                    grenadeObject.AddComponent<MobileNPCGrenadeNPCSplash>();
                splash.InitializeServer(transform, spent.range,
                    spent.playerDamageMultiplier.damage, spent.fuseLength);
                body.collisionDetectionMode = CollisionDetectionMode.Continuous;
                body.AddForce(velocity * body.mass, ForceMode.Impulse);
                nextThrowAt = Time.time + Cooldown;
                Debug.Log("[MobileNPCGrenadeThrower] " + name
                    + " threw 1 native consumed grenade after visual, arc and friendly-safety checks.", this);
                return;
            }
        }

        private bool AlliesInBlastZone(Vector3 blastPoint, float unsafeRange)
        {
            if ((blastPoint - transform.position).sqrMagnitude
                < unsafeRange * unsafeRange)
                return true;
            MobileNPCRegistry.CopyActiveDrivers(Active);
            MobileNPCFaction ownFaction = GetComponent<MobileNPCFaction>();
            MobileNPCGroupMember ownGroup = GetComponent<MobileNPCGroupMember>();
            for (int i = 0; i < Active.Count; i++)
            {
                MobileNPCBrainDriver candidate = Active[i];
                if (candidate == null || candidate == brain)
                    continue;
                MobileNPCHumanoidVitals alive =
                    candidate.GetComponent<MobileNPCHumanoidVitals>();
                if (alive == null || alive.IsDead)
                    continue;
                MobileNPCGroupMember otherGroup =
                    candidate.GetComponent<MobileNPCGroupMember>();
                MobileNPCFaction otherFaction = candidate.GetComponent<MobileNPCFaction>();
                bool allied = ownGroup != null && otherGroup != null
                    && ownGroup.GroupId > 0 && ownGroup.GroupId == otherGroup.GroupId
                    || ownFaction != null && otherFaction != null
                    && MobileNPCFaction.GetRelationship(ownFaction.CurrentFaction,
                        otherFaction.CurrentFaction) == MobileNPCFaction.Relationship.Allied;
                if (allied && (candidate.transform.position - blastPoint).sqrMagnitude
                    < unsafeRange * unsafeRange)
                    return true;
            }
            return false;
        }

        private bool ArcInitiallyClear(Vector3 origin, Vector3 velocity,
            Vector3 gravity, float time, Transform enemy)
        {
            Vector3 previous = origin;
            const int Segments = 9;
            for (int segment = 1; segment <= Segments; segment++)
            {
                float step = time * segment / Segments;
                Vector3 next = origin + velocity * step + gravity * (0.5f * step * step);
                Vector3 delta = next - previous;
                float distance = delta.magnitude;
                if (distance > 0.02f)
                {
                    int count = Physics.RaycastNonAlloc(previous, delta / distance,
                        ObstructionHits, distance, RayMasks.BLOCK_COLLISION,
                        QueryTriggerInteraction.Ignore);
                    if (count >= ObstructionHits.Length)
                        return false;
                    for (int hitIndex = 0; hitIndex < count; hitIndex++)
                    {
                        Collider obstacle = ObstructionHits[hitIndex].collider;
                        if (obstacle == null || obstacle.transform.IsChildOf(transform)
                            || obstacle.transform.IsChildOf(enemy))
                            continue;
                        return false;
                    }
                }
                previous = next;
            }
            return true;
        }
    }

    /// <summary>
    /// Single scheduled NPC-body splash. Do NOT replace native Grenade.Explode:
    /// native DamageTool handles players, zombies and world props separately.
    /// Only NPC-exclusive vitals are affected here, with range and LOS.
    /// </summary>
    public sealed class MobileNPCGrenadeNPCSplash : MonoBehaviour
    {
        private Transform instigator;
        private float radius;
        private float damage;
        private bool initialized;
        private static readonly List<MobileNPCBrainDriver> Active =
            new List<MobileNPCBrainDriver>(40);
        public void InitializeServer(Transform source, float blastRadius,
            float npcDamage, float fuse)
        {
            if (!Provider.isServer || initialized)
                return;
            initialized = true;
            instigator = source;
            radius = Mathf.Max(0f, blastRadius);
            damage = Mathf.Max(0f, npcDamage);
            // The native Grenade self-destroys after its own Invoke. Run this
            // just before to avoid native deferred-destruction race.
            Invoke(nameof(ApplyNPCSplashServer), Mathf.Max(0.03f, fuse - 0.03f));
        }

        private void ApplyNPCSplashServer()
        {
            if (!Provider.isServer || !initialized || radius <= 0f || damage <= 0f)
                return;
            MobileNPCRegistry.CopyActiveDrivers(Active);
            Vector3 center = transform.position;
            for (int i = 0; i < Active.Count; i++)
            {
                MobileNPCBrainDriver driver = Active[i];
                if (driver == null)
                    continue;
                MobileNPCHumanoidVitals vitals =
                    driver.GetComponent<MobileNPCHumanoidVitals>();
                if (vitals == null || vitals.IsDead)
                    continue;
                Vector3 target = driver.transform.position + Vector3.up;
                Vector3 difference = target - center;
                float d = difference.magnitude;
                if (d >= radius || Physics.Linecast(center, target,
                    RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore))
                    continue;
                float dealt = damage * Mathf.Clamp01(1f - d / radius);
                if (dealt > 0f)
                    vitals.ApplyDamage(dealt, difference.normalized * dealt * 0.35f,
                        instigator);
            }
            initialized = false;
        }
    }
}
