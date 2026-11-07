using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Steam-module adapter of the non-projectile branch of Line 1's
    /// MobileNPCHumanoidRangedCombat. Real owned ItemGunAsset, selected gun
    /// instance, 18-byte native magazine state and finite spare magazines.
    /// No UseableGun fake Player instance and no infinite bullets.
    ///
    /// Conservatively limited to short/medium-range direct fire while the
    /// custom Steam module is not yet battle-tested. Native damage pipeline
    /// handles players/zombies; a single vitals owner handles rival NPCs.
    /// </summary>
    internal sealed class Line2NPCRangedExecutor : MonoBehaviour
    {
        private const float HardMaximumRange = 65f;
        private const float MaximumSpreadDegrees = 12f;
        private const int MaxShotsPerFrame = 3;
        private readonly RaycastHit[] _hits = new RaycastHit[24];
        private static int _lastFrame = -1;
        private static int _shotsThisFrame;
        private PrototypeActor _actor;
        private Line2NPCCombatPerception _perception;
        private Line2MobileNPCInventory _inventory;
        private float _nextUpdateAt;
        private float _nextShotAt;
        private float _acquiredAt;
        private Transform _acquiredTarget;
        private uint _pendingReloadGun;
        private float _reloadCompleteAt;
        private float _nextLogAt;
        private int _burstRemaining;
        private bool _waitingBurstPause;

        internal bool HasUsableGun { get; private set; }
        internal bool Reloading { get { return _pendingReloadGun != 0; } }

        internal void Initialize(PrototypeActor actor,
            Line2NPCCombatPerception perception,
            Line2MobileNPCInventory inventory)
        {
            _actor = actor;
            _perception = perception;
            _inventory = inventory;
            _nextUpdateAt = Time.time + UnityEngine.Random.Range(0.1f, 0.36f);
            _nextShotAt = Time.time + UnityEngine.Random.Range(0.3f, 0.8f);
            _burstRemaining = UnityEngine.Random.Range(2, 6);
        }

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                Provider.hasCheats && Level.isLoaded && Player.player != null &&
                (Provider.clients == null || Provider.clients.Count <= 1);
        }

        private void Update()
        {
            if (Time.time < _nextUpdateAt) return;
            _nextUpdateAt = Time.time + 0.09f;
            HasUsableGun = false;
            if (_actor == null || _inventory == null || _perception == null ||
                !_actor.IsCombatEnabled || _actor.IsDead || !Authorized())
                return;

            // A pending reload is a DELAYED inventory transaction. If the
            // NPC dies during the timer, the magazine is still in inventory.
            if (_pendingReloadGun != 0)
            {
                if (Time.time < _reloadCompleteAt) return;
                FinishReload();
                return;
            }

            uint gunId;
            Item owned;
            ItemGunAsset gun;
            if (!_inventory.TryGetBestGun(out gunId, out owned, out gun) ||
                gun == null || owned == null || owned.state == null ||
                owned.state.Length < 18 || gun.projectile != null ||
                gun.ammoPerShot == 0 || gun.range <= 0f ||
                gun.playerDamageMultiplier == null ||
                gun.magazineCalibers == null)
                return;

            ushort magazineId =
                BitConverter.ToUInt16(owned.state, GunStateIndices.MAGAZINE_ID);
            ItemMagazineAsset loadedMagazine =
                Assets.find(EAssetType.ITEM, magazineId) as ItemMagazineAsset;
            if (loadedMagazine == null ||
                !loadedMagazine.CalibersContainAnyOfIds(gun.magazineCalibers))
                return;

            int rounds = owned.state[GunStateIndices.AMMO];
            if (rounds < gun.ammoPerShot)
            {
                StartReload(gunId, gun);
                return;
            }
            // Never accept corrupt gun state with more rounds than an
            // authentic native magazine could hold.
            if (!Line2NPCAmmoRules.CanFire(
                    rounds, gun.ammoPerShot, loadedMagazine.MaxAmountAsByte))
                return;
            HasUsableGun = true;

            Line2NPCCombatPerception.TargetKind kind = _perception.Kind;
            Transform target = _perception.VisibleTarget;
            if (kind == Line2NPCCombatPerception.TargetKind.None || target == null ||
                _actor.IsTacticallyRetreating || Time.time < _nextShotAt)
            {
                _acquiredTarget = null;
                return;
            }

            if (target != _acquiredTarget)
            {
                _acquiredTarget = target;
                _acquiredAt = Time.time;
                _nextShotAt = Time.time + UnityEngine.Random.Range(0.30f, 0.85f);
                return; // Original Line 1 reaction delay before the FIRST shot.
            }

            if (Time.time - _acquiredAt < 0.28f) return;
            float gunRange = Mathf.Min(gun.range, HardMaximumRange);
            if (!_perception.HasVerifiedSight(target, kind, gunRange))
                return;

            Vector3 origin = transform.position + Vector3.up * 1.45f;
            Vector3 offset = target.position + Vector3.up * 1.0f - origin;
            float length = offset.magnitude;
            if (length < 1f || length > gunRange) return;

            Vector3 facing = transform.forward;
            facing.y = 0f;
            Vector3 horizontal = new Vector3(offset.x, 0f, offset.z);
            if (horizontal.sqrMagnitude < 0.01f ||
                Vector3.Dot(facing.normalized, horizontal.normalized) < 0.72f)
                return;

            if (_lastFrame != Time.frameCount)
            {
                _lastFrame = Time.frameCount;
                _shotsThisFrame = 0;
            }
            if (_shotsThisFrame >= MaxShotsPerFrame) return;

            // Commit consumed ammunition BEFORE damage. If a duplicate gun
            // instance disappears, the shot fails closed rather than firing
            // for free. Rifle reload uses the original inventory transaction.
            int remaining = Line2NPCAmmoRules.RemainingAfterShot(
                rounds, gun.ammoPerShot, loadedMagazine.MaxAmountAsByte);
            if (remaining < 0) return;
            byte left = (byte)remaining;
            if (!_inventory.TryUpdateGunAmmoServer(gunId, left))
                return;
            _shotsThisFrame++;

            // Same idea as the original Line 1 humanized aim: first-shot
            // reaction, uncertain spread, movement penalty and escalating
            // inaccuracy across bursts. Never calculate guaranteed hits.
            float spread = Mathf.Clamp(
                2.8f + (length / 25f) * 0.75f +
                (HasMovement() ? 2.4f : 0f) +
                (5 - Mathf.Clamp(_burstRemaining, 1, 5)) * 0.70f,
                2f, MaximumSpreadDegrees);
            // Vanilla magazines may fire multiple pellets per spent shell.
            // Consume ammunition ONCE, then independently trace each pellet.
            // Cap untrusted custom asset data to a bounded hit budget.
            int pellets = Mathf.Clamp((int)loadedMagazine.pellets, 1, 16);
            int pelletHits = 0;
            for (int pellet = 0; pellet < pellets; pellet++)
            {
                Vector2 scatter = UnityEngine.Random.insideUnitCircle * spread;
                Quaternion deviation = Quaternion.Euler(scatter.y, scatter.x, 0f);
                Vector3 direction = deviation * offset.normalized;
                if (TryApplyNativeImpact(kind, target, origin, direction,
                    gunRange, gun, length))
                    pelletHits++;
            }
            if (_burstRemaining <= 1)
            {
                _burstRemaining = UnityEngine.Random.Range(2, 6);
                _waitingBurstPause = true;
                _nextShotAt = Time.time + UnityEngine.Random.Range(0.55f, 1.3f);
            }
            else
            {
                _burstRemaining--;
                _waitingBurstPause = false;
                _nextShotAt = Time.time + Mathf.Clamp(
                    (gun.firerate + 1f) / 50f, 0.12f, 1.8f) +
                    UnityEngine.Random.Range(0.035f, 0.14f);
            }

            if (Time.time >= _nextLogAt)
            {
                ProbeLogger.Log("NPC_RANGED_SHOT: actor=" + _actor.Identity +
                    " nativeGun=" + gun.id + " ammo=" + left +
                    " pelletHits=" + pelletHits + "/" + pellets +
                    " spreadDeg=" + spread.ToString("F1"));
                _nextLogAt = Time.time + 12f;
            }
        }

        private bool HasMovement()
        {
            // Only the observed actor velocity; no remote accuracy estimate.
            return _actor != null && _actor.IsMoving;
        }

        private bool TryApplyNativeImpact(
            Line2NPCCombatPerception.TargetKind kind, Transform intended,
            Vector3 origin, Vector3 direction, float range,
            ItemGunAsset gun, float intendedDistance)
        {
            int mask = RayMasks.DAMAGE_SERVER | RayMasks.ENTITY |
                       RayMasks.PLAYER | RayMasks.ENEMY;
            int count = Physics.RaycastNonAlloc(origin, direction, _hits,
                range, mask, QueryTriggerInteraction.Collide);
            if (count <= 0 || count >= _hits.Length) return false;
            Transform first = null;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                Transform hit = _hits[i].transform;
                if (hit == null || hit == transform || hit.IsChildOf(transform))
                    continue;
                if (_hits[i].distance < nearest)
                {
                    nearest = _hits[i].distance;
                    first = hit;
                }
            }
            if (first == null) return false; // no fabricated hits

            float falloffStart = gun.range * gun.damageFalloffRange;
            float falloffEnd = gun.range * gun.damageFalloffMaxRange;
            float falloff = Mathf.Lerp(1f, gun.damageFalloffMultiplier,
                Mathf.InverseLerp(falloffStart, falloffEnd, nearest));
            if (float.IsNaN(falloff) || float.IsInfinity(falloff) ||
                falloff <= 0f) return false;

            if (kind == Line2NPCCombatPerception.TargetKind.Player)
            {
                Player victim = first.GetComponentInParent<Player>();
                if (victim == null || victim.transform != intended ||
                    victim.life == null || victim.life.isDead ||
                    !Line2NPCCombatRules.CanEngagePlayer(_actor.Disposition,
                        _actor.CombatMode, _actor.PlayerVisible,
                        (victim.transform.position - transform.position).sqrMagnitude))
                    return false;
                var damage = new DamagePlayerParameters(victim);
                damage.cause = EDeathCause.GUN;
                damage.killer = Provider.server;
                damage.limb = ELimb.SPINE;
                damage.direction = direction;
                damage.damage = gun.playerDamageMultiplier.multiply(ELimb.SPINE) *
                    falloff;
                damage.respectArmor = true;
                EPlayerKill kill;
                if (damage.damage <= 0f) return false;
                DamageTool.damagePlayer(damage, out kill);
                return true;
            }

            if (kind == Line2NPCCombatPerception.TargetKind.Zombie)
            {
                Zombie victim = first.GetComponentInParent<Zombie>();
                Zombie expected = intended.GetComponentInParent<Zombie>();
                if (victim == null || victim != expected || victim.isDead)
                    return false;
                float damage = gun.zombieOrPlayerDamageMultiplier.multiply(ELimb.SPINE) *
                    falloff * victim.getBulletResistance();
                if (damage <= 0f || float.IsNaN(damage) ||
                    float.IsInfinity(damage)) return false;
                ushort applied = (ushort)Mathf.Clamp(Mathf.RoundToInt(damage),
                    1, ushort.MaxValue);
                EPlayerKill kill;
                uint xp;
                victim.askDamage(applied, direction * applied,
                    out kill, out xp, trackKill: false, dropLoot: true);
                return true;
            }

            if (kind == Line2NPCCombatPerception.TargetKind.Npc)
            {
                PrototypeActor victim = first.GetComponentInParent<PrototypeActor>();
                PrototypeActor expected = intended.GetComponentInParent<PrototypeActor>();
                if (victim == null || victim != expected || victim.IsDead ||
                    !Line2NPCCombatRules.AreOpposed(
                        _actor.Disposition, victim.Disposition, _actor.CombatMode))
                    return false;
                Line2MobileNPCInjurySystem.BodyPart part;
                if (!Line2MobileNPCBodyHitbox.TryResolve(first, out part))
                    return false;
                PrototypeNpcVitals vitals = victim.GetComponent<PrototypeNpcVitals>();
                if (vitals == null || vitals.IsDead) return false;
                ELimb limb = part == Line2MobileNPCInjurySystem.BodyPart.Head
                    ? ELimb.SKULL : ELimb.SPINE;
                float amount = gun.playerDamageMultiplier.multiply(limb) * falloff;
                return vitals.ApplyDamageToPart(amount, part, false, false,
                    Line2MobileNPCInjurySystem.BleedingSeverity.None,
                    direction, transform);
            }
            return false;
        }

        private void StartReload(uint gunInstanceId, ItemGunAsset gun)
        {
            int magazineSlot;
            ItemMagazineAsset replacement;
            if (!_inventory.TryFindCompatibleMagazine(gun, out magazineSlot,
                                                      out replacement))
                return;
            _pendingReloadGun = gunInstanceId;
            _reloadCompleteAt = Time.time +
                Mathf.Clamp(gun.reloadTime, 1.1f, 9f);
            _nextShotAt = _reloadCompleteAt + 0.3f;
        }

        private void FinishReload()
        {
            uint instance = _pendingReloadGun;
            _pendingReloadGun = 0;
            Item original;
            if (!_inventory.TryGetGunSnapshotServer(instance, out original) ||
                original == null || original.state == null ||
                original.state.Length < 18)
                return;

            ItemGunAsset gun = Assets.find(EAssetType.ITEM, original.id)
                as ItemGunAsset;
            if (gun == null || gun.projectile != null) return;
            int magazineSlot;
            ItemMagazineAsset spare;
            if (!_inventory.TryFindCompatibleMagazine(gun, out magazineSlot,
                                                      out spare))
                return;

            Item replacement;
            if (!_inventory.TryTakeItemServer(magazineSlot, out replacement) ||
                replacement == null) return;

            if (!Line2NPCAmmoRules.CanLoadReplacement(
                    replacement.amount, spare.MaxAmountAsByte))
            {
                if (!_inventory.TryStoreTransferredItem(replacement))
                    ProbeLogger.Log("NPC_RELOAD_INVALID_MAG_ROLLBACK_FAILED: actor=" +
                                    _actor.Identity);
                return;
            }

            ushort oldMagazineId =
                BitConverter.ToUInt16(original.state, GunStateIndices.MAGAZINE_ID);
            byte oldAmmo = original.state[GunStateIndices.AMMO];
            byte oldMagazineQuality = original.state[17];

            if (!_inventory.TryUpdateGunAmmoServer(instance, replacement.amount,
                    replacement.id, replacement.quality))
            {
                // Reverse the ownership transfer, no free ammunition.
                if (!_inventory.TryStoreTransferredItem(replacement))
                    ProbeLogger.Log("NPC_RELOAD_ROLLBACK_FAILED: actor=" +
                                    _actor.Identity);
                return;
            }

            // The previous magazine existed inside the gun, and may contain
            // remaining rounds. Return exactly those rounds, never a full mag.
            if (oldAmmo > 0 && oldMagazineId != 0)
            {
                ItemMagazineAsset oldAsset = Assets.find(EAssetType.ITEM,
                    oldMagazineId) as ItemMagazineAsset;
                if (oldAsset != null)
                {
                    var partial = new Item(oldMagazineId, oldAmmo,
                        oldMagazineQuality, new byte[0]);
                    if (!_inventory.TryStoreTransferredItem(partial))
                        ProbeLogger.Log("NPC_RELOAD_PARTIAL_MAG_FULL: actor=" +
                                        _actor.Identity);
                }
            }
            ProbeLogger.Log("NPC_RELOAD_OK: actor=" + _actor.Identity +
                " gun=" + original.id +
                " loaded=" + replacement.amount +
                " (consumed a real spare magazine)");
        }
    }
}
