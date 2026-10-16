////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Real vanilla RPG/projectile-breach service for a personally confirmed
    /// HOSTILE NPC base in Combat mode. Consumes an owned launcher magazine's
    /// genuine ammo, then instantiates the exact native projectile prefab
    /// and configures a vanilla Rocket with authored structure/barricade damage.
    /// Never casts a magic "breach" ray, creates ammunition, sees through
    /// walls, guesses enemy-owned coordinates or attacks the player's base
    /// merely because a player placed a structure nearby.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCInventory))]
    [RequireComponent(typeof(MobileNPCTerritoryRaidPlanner))]
    public sealed class MobileNPCRaidLauncher : MonoBehaviour
    {
        private const float MaxBreachSight = 38f;
        private const float ReloadPause = 3.7f;
        private const float FirePause = 9f;
        private static readonly RaycastHit[] InitialObstacles = new RaycastHit[16];
        private readonly List<MobileNPCBrainDriver> active = new List<MobileNPCBrainDriver>(40);
        private MobileNPCTerritoryRaidPlanner planner;
        private MobileNPCInventory inventory;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCFaction faction;
        private MobileNPCGroupMember group;
        private MobileNPCBrainDriver brain;
        private float nextAttemptAt;

        private void Awake()
        {
            planner = GetComponent<MobileNPCTerritoryRaidPlanner>();
            inventory = GetComponent<MobileNPCInventory>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            faction = GetComponent<MobileNPCFaction>();
            group = GetComponent<MobileNPCGroupMember>();
            brain = GetComponent<MobileNPCBrainDriver>();
            nextAttemptAt = Time.time + Random.Range(4f, 9f);
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || !MobileNPCSettings.CombatMode
                || Time.time < nextAttemptAt || planner == null || inventory == null
                || vitals == null || vitals.IsDead || vitals.HealthFraction < 0.35f
                || faction == null || group == null || brain == null
                || !group.CanPlanSettlement)
                return;
            nextAttemptAt = Time.time + Random.Range(2.5f, 4.5f);
            if (!planner.TryGetVisibleHostileBaseServer(out Vector3 impact))
                return;
            if (brain.CurrentGoal == MobileNPCTactics.Goal.Retreat
                || brain.CurrentGoal == MobileNPCTactics.Goal.SeekCover
                || brain.IsAmbushPreparing)
                return;

            if (!inventory.TryGetLoadedRaidLauncherServer(
                out uint gunInstance, out Item held, out ItemGunAsset gun,
                out ItemMagazineAsset magazine))
            {
                if (inventory.TryReloadOwnedRaidLauncherServer())
                    nextAttemptAt = Time.time + ReloadPause;
                return;
            }
            GameObject prefab = magazine != null && magazine.ProjectilePrefabOverride != null
                ? magazine.ProjectilePrefabOverride : gun.projectile;
            if (prefab == null || gun.ballisticForce <= 0f) return;

            Vector3 origin = transform.position + Vector3.up * 1.45f;
            Vector3 delta = impact + Vector3.up * 0.5f - origin;
            float range = delta.magnitude;
            float blastRadius = gun.range * (magazine != null
                ? magazine.projectileBlastRadiusMultiplier : 1f);
            if (range < Mathf.Max(16f, blastRadius + 8f)
                || range > MaxBreachSight) return;
            // Real projectile flight and a randomized point of impact.
            // This is not the future position of any moving defender.
            Vector3 imperfectPoint = impact + Vector3.up * 0.5f
                + new Vector3(Random.Range(-1.5f, 1.5f),
                    Random.Range(-0.7f, 0.9f), Random.Range(-1.5f, 1.5f));
            Vector3 direction = (imperfectPoint - origin).normalized;
            if (!ClearInitialFlight(origin, direction, range)
                || AlliesNearImpact(impact, blastRadius + 5f))
                return;

            // Exactly the native prefab and Rocket fields used by UseableGun.
            // Instantiate as an inert setup object before debiting; if any
            // prerequisites fail it is destroyed without consuming rockets.
            GameObject launched = Object.Instantiate(prefab,
                origin + direction * 0.85f,
                Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f));
            if (launched == null) return;
            Rigidbody body = launched.GetComponent<Rigidbody>();
            if (body == null)
            {
                Object.Destroy(launched);
                return;
            }
            int cost = Mathf.Max(1, gun.ammoPerShot);
            int remaining = held.state[GunStateIndices.AMMO] - cost;
            if (remaining < 0 || !inventory.TryUpdateGunAmmoServer(gunInstance,
                (byte)Mathf.Clamp(remaining, 0, byte.MaxValue)))
            {
                Object.Destroy(launched);
                return;
            }
            launched.name = "MobileNPC_NativeRaidRocket";
            Rocket rocket = launched.AddComponent<Rocket>();
            rocket.ignoreTransform = transform;
            rocket.killer = CSteamID.Nil;
            float damageScale = magazine != null ? magazine.projectileDamageMultiplier : 1f;
            rocket.range = gun.range * (magazine != null
                ? magazine.projectileBlastRadiusMultiplier : 1f);
            rocket.playerDamage = gun.playerDamageMultiplier.damage * damageScale;
            rocket.zombieDamage = gun.zombieDamageMultiplier.damage * damageScale;
            rocket.animalDamage = gun.animalDamageMultiplier.damage * damageScale;
            rocket.barricadeDamage = gun.barricadeDamage * damageScale;
            rocket.structureDamage = gun.structureDamage * damageScale;
            rocket.vehicleDamage = gun.vehicleDamage * damageScale;
            rocket.resourceDamage = gun.resourceDamage * damageScale;
            rocket.objectDamage = gun.objectDamage * damageScale;
            rocket.penetrateBuildables = gun.projectilePenetrateBuildables;
            rocket.explosionLaunchSpeed = gun.projectileExplosionLaunchSpeed;
            if (magazine != null && !magazine.IsExplosionEffectRefNull())
            {
                rocket.explosionEffectGuid = magazine.explosionEffectGuid;
                rocket.explosion = magazine.explosion;
            }
            else
            {
                rocket.explosionEffectGuid = gun.projectileExplosionEffectGuid;
                rocket.explosion = gun.explosion;
            }
            Grenade nativeGrenade = launched.GetComponent<Grenade>();
            if (nativeGrenade != null) nativeGrenade.killer = CSteamID.Nil;
            EffectManager.RegisterDebris(launched);
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.AddForce(direction * gun.ballisticForce
                * (magazine != null ? magazine.projectileLaunchForceMultiplier : 1f));
            Object.Destroy(launched, Mathf.Max(1f, gun.projectileLifespan));
            // Native engine owns the real explosion and all buildable damage.
            if (gun.alertRadius > 0f)
            {
                MobileNPCRegistry.NotifyGunfire(transform.position, gun.alertRadius,
                    null, transform);
                AlertTool.alert(transform.position, gun.alertRadius);
            }
            nextAttemptAt = Time.time + FirePause + Random.Range(0f, 4f);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCRaidLauncher] Fired one native owned rocket at a"
                + " freshly visible hostile settlement, gun=" + gun.id
                + ", ammo remaining=" + remaining + ".", this);
#endif
        }

        private bool ClearInitialFlight(Vector3 origin, Vector3 direction, float range)
        {
            // The muzzle must be clear and the target's wall/storage itself
            // may be struck. No bullet can depart directly through cover.
            int count = Physics.RaycastNonAlloc(origin, direction, InitialObstacles,
                Mathf.Min(2f, range), RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (count >= InitialObstacles.Length) return false;
            for (int i = 0; i < count; ++i)
            {
                Transform t = InitialObstacles[i].transform;
                if (t != null && !t.IsChildOf(transform)) return false;
            }
            return true;
        }

        private bool AlliesNearImpact(Vector3 point, float unsafeRadius)
        {
            if ((transform.position - point).sqrMagnitude < unsafeRadius * unsafeRadius)
                return true;
            MobileNPCRegistry.CopyActiveDrivers(active);
            for (int i = 0; i < active.Count; ++i)
            {
                MobileNPCBrainDriver other = active[i];
                if (other == null || other == brain) continue;
                MobileNPCHumanoidVitals alive = other.GetComponent<MobileNPCHumanoidVitals>();
                if (alive == null || alive.IsDead) continue;
                MobileNPCFaction otherFaction = other.GetComponent<MobileNPCFaction>();
                MobileNPCGroupMember otherGroup = other.GetComponent<MobileNPCGroupMember>();
                bool allied = otherFaction != null
                    && MobileNPCFaction.GetRelationship(faction.CurrentFaction,
                        otherFaction.CurrentFaction) == MobileNPCFaction.Relationship.Allied
                    || otherGroup != null && otherGroup.GroupId > 0
                    && otherGroup.GroupId == group.GroupId;
                if (allied && (other.transform.position - point).sqrMagnitude
                    < unsafeRadius * unsafeRadius)
                    return true;
            }
            return false;
        }
    }
}
