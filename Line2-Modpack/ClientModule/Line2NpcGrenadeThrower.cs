using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Adaptation of Line 1 MobileNPCGrenadeThrower to the original Steam
    /// UseableThrowable.toss pipeline: real owned throwable, native prefab
    /// Rigidbody.AddForce, native Grenade fuse and DamageTool explosion.
    /// The AI computes its own conservative trajectory because it has no
    /// PlayerLook; it does not create a fake Player or a second explosion.
    /// PrototypeNpcVitals/IExplosionDamageable handles NPC blast damage.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcGrenadeThrower : MonoBehaviour
    {
        private const float CheckEvery = 1.75f;
        private const float Cooldown = 45f;
        private const float MinimumRange = 11f;
        private const float MaximumRange = 27f;
        private const float MinimumHealth = 0.34f;
        private const int MaximumCollisions = 16;
        private readonly List<PrototypeActor> _actors = new List<PrototypeActor>(32);
        private readonly RaycastHit[] _obstacles = new RaycastHit[MaximumCollisions];
        private SteamNpcPrototypeRuntime _runtime;
        private PrototypeActor _actor;
        private Line2NPCCombatPerception _sight;
        private Line2MobileNPCInventory _inventory;
        private float _nextCheck;
        private float _nextThrow;

        internal void Initialize(SteamNpcPrototypeRuntime runtime,
            PrototypeActor actor, Line2NPCCombatPerception perception,
            Line2MobileNPCInventory inventory)
        {
            _runtime = runtime;
            _actor = actor;
            _sight = perception;
            _inventory = inventory;
            _nextCheck = Time.time + UnityEngine.Random.Range(0.5f, CheckEvery);
        }

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                (Provider.clients == null || Provider.clients.Count <= 1) &&
                Provider.hasCheats && Level.isLoaded && Player.player != null;
        }

        private void Update()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckEvery;
            if (!Authorized() || _actor == null || !_actor.IsCombatEnabled ||
                _actor.IsDead || _actor.IsTacticallyRetreating ||
                _actor.HealthFraction < MinimumHealth ||
                _inventory == null || _sight == null || _runtime == null ||
                Time.time < _nextThrow) return;

            Line2NPCCombatPerception.TargetKind kind = _sight.Kind;
            Transform target = _sight.VisibleTarget;
            if (kind == Line2NPCCombatPerception.TargetKind.None || target == null ||
                !_sight.HasVerifiedSight(target, kind, MaximumRange))
                return;
            if (_actor.ShouldHoldPreparedAmbushShot(target))
                return;
            Vector3 origin = transform.position + Vector3.up * 1.55f +
                transform.forward * 0.65f;
            Vector3 aim = target.position + Vector3.up * 0.4f;
            Vector3 horizontal = aim - origin;
            horizontal.y = 0f;
            float range = horizontal.magnitude;
            if (range < MinimumRange || range > MaximumRange)
                return;

            for (int i = 0; i < _inventory.UsedSlots; ++i)
            {
                Item item;
                if (!_inventory.TryGetItemSnapshot(i, out item) ||
                    item == null || item.amount == 0) continue;
                ItemThrowableAsset asset = Assets.find(EAssetType.ITEM,
                    item.id) as ItemThrowableAsset;
                if (asset == null || !asset.isExplosive || asset.isSticky ||
                    asset.explodeOnImpact || asset.throwable == null ||
                    asset.range < 3f || asset.fuseLength < 1.5f ||
                    asset.fuseLength > 5f) continue;

                float unsafeRadius = asset.range + 3.5f;
                if (range <= unsafeRadius + 3f ||
                    AlliesInBlastZone(aim, kind, unsafeRadius)) continue;
                float travel = Mathf.Clamp(range / 13.5f, 0.8f,
                    Mathf.Min(2.1f, asset.fuseLength - 0.4f));
                if (travel <= 0f) continue;
                Vector3 velocity = (aim - origin -
                    Physics.gravity * (0.5f * travel * travel)) / travel;
                if (velocity.sqrMagnitude > 31f * 31f ||
                    !ArcInitiallyClear(origin, velocity, travel * 0.8f,
                        target)) continue;

                // Native player throw has a Rigidbody.AddForce, not a fake
                // raycast grenade. Use the same ForceMode.Force impulse over
                // one fixed physics tick, bounded by this authentic asset's
                // strong throw force. Failed calculations do NOT debit ammo.
                GameObject projectile = null;
                try
                {
                    projectile = UnityEngine.Object.Instantiate(
                        asset.throwable, origin, Quaternion.LookRotation(velocity));
                    if (projectile == null) return;
                    Rigidbody body = projectile.GetComponent<Rigidbody>();
                    if (body == null || body.mass <= 0f ||
                        Time.fixedDeltaTime <= 0f)
                    {
                        UnityEngine.Object.Destroy(projectile);
                        return;
                    }
                    Vector3 throwForce =
                        velocity * (body.mass / Time.fixedDeltaTime);
                    if (asset.strongThrowForce <= 0f ||
                        throwForce.magnitude > asset.strongThrowForce * 1.05f)
                    {
                        UnityEngine.Object.Destroy(projectile);
                        return;
                    }

                    Grenade native = projectile.AddComponent<Grenade>();
                    native.range = asset.range;
                    native.playerDamage = asset.playerDamageMultiplier.damage;
                    native.zombieDamage = asset.zombieDamageMultiplier.damage;
                    native.animalDamage = asset.animalDamageMultiplier.damage;
                    native.barricadeDamage = asset.barricadeDamage;
                    native.structureDamage = asset.structureDamage;
                    native.vehicleDamage = asset.vehicleDamage;
                    native.resourceDamage = asset.resourceDamage;
                    native.objectDamage = asset.objectDamage;
                    native.explosionEffectGuid = asset.explosionEffectGuid;
                    native.explosion = asset.explosion;
                    native.fuseLength = asset.fuseLength;
                    native.explosionLaunchSpeed = asset.explosionLaunchSpeed;

                    ItemThrowableAsset consumed;
                    if (!_inventory.TryConsumeSimpleGrenadeServer(
                        item.id, out consumed) || consumed == null)
                    {
                        UnityEngine.Object.Destroy(projectile);
                        return;
                    }
                    projectile.name = "Line2_NativeGrenade";
                    EffectManager.RegisterDebris(projectile);
                    body.collisionDetectionMode = CollisionDetectionMode.Continuous;
                    body.AddForce(throwForce); // same original UseableThrowable call
                    _nextThrow = Time.time + Cooldown;
                    ProbeLogger.Log("NPC_GRENADE_THROWN: actor=" + _actor.Identity +
                        " item=" + consumed.id + " range=" +
                        range.ToString("F1") + " nativeFuse=" +
                        asset.fuseLength.ToString("F1") +
                        " (one consumed item; native splash)");
                    return;
                }
                catch (Exception error)
                {
                    if (projectile != null)
                        UnityEngine.Object.Destroy(projectile);
                    _nextThrow = Time.time + 12f;
                    ProbeLogger.Log("NPC_GRENADE_FAILED: actor=" + _actor.Identity +
                        " error=" + error.GetType().Name);
                    return;
                }
            }
        }

        private bool AlliesInBlastZone(Vector3 point,
            Line2NPCCombatPerception.TargetKind targetKind,
            float radius)
        {
            float squared = radius * radius;
            if ((point - transform.position).sqrMagnitude <= squared)
                return true;
            Player player = Player.player;
            if (player != null && targetKind !=
                Line2NPCCombatPerception.TargetKind.Player &&
                (point - player.transform.position).sqrMagnitude <= squared)
                return true;
            _runtime.CopyLiveActors(_actors, 32);
            foreach (PrototypeActor other in _actors)
            {
                if (other == null || other == _actor || other.IsDead ||
                    (other.transform.position - point).sqrMagnitude > squared)
                    continue;
                // Treat neutral actors as noncombatants too. Opposed enemies
                // are the only accepted collateral NPC targets.
                if (!Line2NPCCombatRules.AreOpposed(
                    _actor.Disposition, other.Disposition,
                    _actor.CombatMode)) return true;
            }
            return false;
        }

        private bool ArcInitiallyClear(Vector3 origin, Vector3 velocity,
            float horizon, Transform target)
        {
            Vector3 previous = origin;
            const int Segments = 10;
            for (int step = 1; step <= Segments; ++step)
            {
                float time = horizon * step / Segments;
                Vector3 next = origin + velocity * time +
                    Physics.gravity * (0.5f * time * time);
                Vector3 delta = next - previous;
                float length = delta.magnitude;
                if (length > 0.02f)
                {
                    int n = Physics.RaycastNonAlloc(
                        previous, delta / length, _obstacles, length,
                        RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
                    if (n >= MaximumCollisions) return false;
                    for (int i = 0; i < n; i++)
                    {
                        Collider hit = _obstacles[i].collider;
                        _obstacles[i] = default(RaycastHit);
                        if (hit == null ||
                            hit.transform == transform ||
                            hit.transform.IsChildOf(transform) ||
                            (target != null && (hit.transform == target ||
                                hit.transform.IsChildOf(target))))
                            continue;
                        return false;
                    }
                }
                previous = next;
            }
            return true;
        }
    }
}
