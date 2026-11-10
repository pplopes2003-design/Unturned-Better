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
        private const float HardMaximumRange = Line2NpcSniperPolicy.MaximumRange;
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
        private uint _activeGunInstance;
        private byte _activeGunFiremode;

        internal bool HasUsableGun { get; private set; }
        internal bool Reloading { get { return _pendingReloadGun != 0; } }

        // Presentation subscribes to COMMITTED actions only. Damage, stock
        // and cooldown remain wholly owned by this server executor.
        internal event Action<ItemGunAsset> ShotFired;
        internal event Action<ItemGunAsset> ReloadStarted;

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
            {
                // Unloaded native firearms can have MAGAZINE_ID=0 and
                // AMMO=0. They are still usable after acquiring a genuine
                // compatible spare; refusing here left a newly looted gun
                // permanently unable to participate in combat.
                if (magazineId == 0 &&
                    owned.state[GunStateIndices.AMMO] == 0)
                    StartReload(gunId, gun);
                return;
            }

            // Respect the actual native firemode encoded in this exact
            // owned gun instance. Safety is not an automatic weapon and
            // a semi-only rifle cannot magically fire multi-shot bursts.
            byte nativeMode = owned.state[GunStateIndices.FIREMODE];
            bool semi = nativeMode == (byte)EFiremode.SEMI && gun.hasSemi;
            bool burst = nativeMode == (byte)EFiremode.BURST && gun.hasBurst;
            bool automatic = nativeMode == (byte)EFiremode.AUTO && gun.hasAuto;
            int cycle = Line2NpcFireModePolicy.ShotsInCycle(
                semi, burst, automatic, gun.bursts,
                UnityEngine.Random.Range(2, 6));
            if (cycle == 0) return;
            if (_activeGunInstance != gunId ||
                _activeGunFiremode != nativeMode)
            {
                _activeGunInstance = gunId;
                _activeGunFiremode = nativeMode;
                _burstRemaining = cycle;
                _acquiredTarget = null; // New physical gun requires reaction.
            }

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
            if (!Line2NpcFireCadencePolicy.RetainTarget(
                    kind != Line2NPCCombatPerception.TargetKind.None &&
                    target != null, _actor.IsTacticallyRetreating))
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

            // Waiting for fire cadence must NOT discard the acquired
            // target. Previously, every 90ms update during cooldown reset
            // _acquiredTarget, so the next check restarted the first-shot
            // reaction delay forever and NPCs could never fire.
            if (!Line2NpcFireCadencePolicy.MayFire(
                    Time.time, _acquiredAt, _nextShotAt)) return;
            bool sniper = Line2NpcSniperPolicy.IsPrecisionWeapon(
                gun.hasSemi, gun.hasAuto, gun.range);
            float gunRange = sniper
                ? Mathf.Min(gun.range, HardMaximumRange)
                : Mathf.Min(gun.range, 65f);
            if (!_perception.HasVerifiedSight(target, kind, gunRange))
                return;
            if (_actor.ShouldHoldPreparedAmbushShot(target))
                return;

            Vector3 origin = transform.position + Vector3.up * 1.45f;
            float aimHeight = kind ==
                Line2NPCCombatPerception.TargetKind.Zombie ? 0.95f : 1.0f;
            if (kind == Line2NPCCombatPerception.TargetKind.Player)
            {
                Player victim = target.GetComponentInParent<Player>();
                if (victim != null && victim.stance != null)
                {
                    if (victim.stance.stance == EPlayerStance.CROUCH)
                        aimHeight = 0.80f;
                    else if (victim.stance.stance == EPlayerStance.PRONE)
                        aimHeight = 0.34f;
                }
            }
            Vector3 offset = target.position + Vector3.up * aimHeight - origin;
            float length = offset.magnitude;
            if (length < 1f || length > gunRange) return;
            if (sniper && Provider.modeConfigData != null &&
                Provider.modeConfigData.Gameplay.Ballistics)
            {
                float gravity = Mathf.Abs(Physics.gravity.y *
                    gun.bulletGravityMultiplier);
                float correction = Line2NpcSniperPolicy.VerticalDropCompensation(
                    length, gun.muzzleVelocity, gravity);
                offset += Vector3.up * correction;
            }
            if (sniper && !Line2NpcSniperPolicy.MayTakePrecisionShot(
                    true, true, HasMovement(),
                    _actor.IsTacticallyRetreating,
                    _actor.UnderVerifiedSuppression,
                    Time.time - _acquiredAt, length, gun.range))
                return;

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
            float spread = Line2NpcAimSpreadPolicy.ComputeDegrees(
                length, HasMovement(), _burstRemaining,
                _actor.RecentlyDamaged, _actor.UnderVerifiedSuppression,
                _actor.HealthFraction);
            if (sniper)
                spread = Line2NpcSniperPolicy.PrecisionSpread(
                    length, _actor.RecentlyDamaged ||
                        _actor.HealthFraction < 0.60f, spread);
            // Vanilla magazines may fire multiple pellets per spent shell.
            // Consume ammunition ONCE, then independently trace each pellet.
            // Cap untrusted custom asset data to a bounded hit budget.
            int pellets = Mathf.Clamp((int)loadedMagazine.pellets, 1, 16);
            int pelletHits = 0;
            for (int pellet = 0; pellet < pellets; pellet++)
            {
                Vector2 scatter = UnityEngine.Random.insideUnitCircle * spread;
                // Build the deviation in the actual AIM-LOCAL basis,
                // never global world axes (which distorted horizontal
                // spread depending on the NPC's facing direction).
                Quaternion deviation = Quaternion.LookRotation(offset.normalized) *
                    Quaternion.Euler(scatter.y, scatter.x, 0f);
                Vector3 direction = deviation * Vector3.forward;
                if (TryApplyNativeImpact(kind, target, origin, direction,
                    gunRange, gun, length))
                    pelletHits++;
            }
            // Suppression is NOT damage: only a physically debited shot
            // toward a previously sighted rival NPC may create pressure.
            // A miss cannot silently injure a target or grant omniscient
            // knowledge of the attacker's future location.
            bool suppressionCommitted = false;
            if (pelletHits == 0 &&
                kind == Line2NPCCombatPerception.TargetKind.Npc &&
                target != null)
            {
                PrototypeActor rival =
                    target.GetComponentInParent<PrototypeActor>();
                if (rival != null && !rival.IsDead)
                    suppressionCommitted =
                        rival.TryRegisterVerifiedSuppressingShot(
                            _actor, Time.time);
            }

            if (_burstRemaining <= 1)
            {
                _burstRemaining = Line2NpcFireModePolicy.ShotsInCycle(
                    semi, burst, automatic, gun.bursts,
                    UnityEngine.Random.Range(2, 6));
                // Semi mode has a human trigger-reset delay, while
                // native burst and auto modes regroup between volleys.
                _nextShotAt = Time.time + (sniper
                    ? Line2NpcSniperPolicy.NextShotDelay(gun.reloadTime) +
                        UnityEngine.Random.Range(0.12f, 0.65f)
                    : semi
                        ? UnityEngine.Random.Range(0.38f, 0.82f)
                        : UnityEngine.Random.Range(0.55f, 1.3f));
            }
            else
            {
                _burstRemaining--;
                _nextShotAt = Time.time + Mathf.Clamp(
                    (gun.firerate + 1f) / 50f, 0.12f, 1.8f) +
                    UnityEngine.Random.Range(0.035f, 0.14f);
            }

            try { ShotFired?.Invoke(gun); }
            catch (Exception error)
            {
                ProbeLogger.Log("NPC_SHOT_VISUAL_REJECTED: " +
                    error.GetType().Name);
            }
            if (Time.time >= _nextLogAt)
            {
                ProbeLogger.Log("NPC_RANGED_SHOT: actor=" + _actor.Identity +
                    " nativeGun=" + gun.id + " ammo=" + left +
                    " firemode=" + nativeMode +
                    " pelletHits=" + pelletHits + "/" + pellets +
                    " sniper=" + sniper +
                    " spreadDeg=" + spread.ToString("F1") +
                    " nearMissSuppressed=" + suppressionCommitted);
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
            Transform first;
            float nearest;
            if (!TryTraceFirstImpact(origin, direction, range, gun,
                out first, out nearest)) return false;
            
            float falloffStart = gun.range * gun.damageFalloffRange;
            float falloffEnd = gun.range * gun.damageFalloffMaxRange;
            float falloff = Mathf.Lerp(1f, gun.damageFalloffMultiplier,
                Mathf.InverseLerp(falloffStart, falloffEnd, nearest));
            if (!Line2NpcGunDamagePolicy.IsValidFalloff(falloff))
                return false;

            if (kind == Line2NPCCombatPerception.TargetKind.Player)
            {
                Player victim = first.GetComponentInParent<Player>();
                if (victim == null || victim.transform != intended ||
                    victim.life == null || victim.life.isDead ||
                    !Line2NPCCombatRules.CanEngagePlayer(_actor.Disposition,
                        _actor.CombatMode, _actor.PlayerVisible,
                        (victim.transform.position - transform.position).sqrMagnitude,
                        _actor.EffectiveVisualRange))
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
                if (!Line2NpcGunDamagePolicy.IsValid(damage.damage))
                    return false;
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
                if (!Line2NpcGunDamagePolicy.IsValid(damage))
                    return false;
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
                    !_perception.CanEngageNpcTarget(victim))
                    return false;
                Line2MobileNPCInjurySystem.BodyPart part;
                if (!Line2MobileNPCBodyHitbox.TryResolve(first, out part))
                    return false;
                PrototypeNpcVitals vitals = victim.GetComponent<PrototypeNpcVitals>();
                if (vitals == null || vitals.IsDead) return false;
                ELimb limb = part == Line2MobileNPCInjurySystem.BodyPart.Head
                    ? ELimb.SKULL : ELimb.SPINE;
                float amount = gun.playerDamageMultiplier.multiply(limb) * falloff;
                if (!Line2NpcGunDamagePolicy.IsValid(amount))
                    return false;
                return vitals.ApplyDamageToPart(amount, part, false, false,
                    Line2MobileNPCInjurySystem.BleedingSeverity.None,
                    direction, transform);
            }
            return false;
        }

        /// <summary>
        /// Locates the first PHYSICAL impact on the same bent ballistic
        /// trajectory as native U3's 20ms bullet simulation, when ballistics
        /// are enabled. No invented target, line-of-sight bypass or fake
        /// owner Player. This still commits the validated hit at shot time:
        /// projectile travel-time replication requires a separate adapter.
        /// </summary>
        private bool TryTraceFirstImpact(Vector3 origin, Vector3 direction,
            float range, ItemGunAsset gun, out Transform first,
            out float distance)
        {
            first = null;
            distance = 0f;
            // DAMAGE_SERVER omits CLIP and VEHICLE in the original
            // Unturned mask. Always include physical motor blockers so a
            // valid target behind an invisible clip or car is not exposed.
            int mask = RayMasks.DAMAGE_SERVER | RayMasks.BLOCK_COLLISION |
                       RayMasks.ENTITY | RayMasks.PLAYER | RayMasks.ENEMY;
            bool ballistic = Provider.modeConfigData.Gameplay.Ballistics;
            float speed = gun.muzzleVelocity;
            float gravity = Physics.gravity.y * gun.bulletGravityMultiplier;
            if (ballistic && (gun.ballisticSteps <= 0 ||
                float.IsNaN(speed) || float.IsInfinity(speed) ||
                speed < 1f || speed > 1500f ||
                float.IsNaN(gravity) || float.IsInfinity(gravity)))
                return false;
            int steps = ballistic ? Mathf.Min((int)gun.ballisticSteps, 96) : 1;
            const float dt = 0.02f; // Native UseableGun.BALLISTICS_DELTA_TIME
            Vector3 current = origin;
            Vector3 velocity = direction * speed;
            float traveled = 0f;
            for (int step = 0; step < steps && traveled < range; step++)
            {
                Vector3 advance = ballistic ? velocity * dt : direction * range;
                float advanceLength = advance.magnitude;
                if (advanceLength < 0.001f || float.IsNaN(advanceLength) ||
                    float.IsInfinity(advanceLength)) return false;
                float segment = Mathf.Min(advanceLength, range - traveled);
                Vector3 normalized = advance / advanceLength;
                int count = Physics.RaycastNonAlloc(current, normalized,
                    _hits, segment, mask, QueryTriggerInteraction.Collide);
                // Under a saturated result buffer, nearest-blocker proof
                // is unavailable. Refuse the hit rather than shoot through.
                if (count >= _hits.Length) return false;
                float closest = float.PositiveInfinity;
                Transform candidate = null;
                for (int index = 0; index < count; ++index)
                {
                    Transform hit = _hits[index].transform;
                    if (hit == null || hit == transform || hit.IsChildOf(transform))
                        continue;
                    if (_hits[index].distance < closest)
                    {
                        closest = _hits[index].distance;
                        candidate = hit;
                    }
                }
                if (candidate != null)
                {
                    first = candidate;
                    distance = traveled + closest;
                    return true;
                }
                traveled += segment;
                current += normalized * segment;
                if (ballistic) velocity.y += gravity * dt;
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
            try { ReloadStarted?.Invoke(gun); }
            catch (Exception error)
            {
                ProbeLogger.Log("NPC_RELOAD_VISUAL_REJECTED: " +
                    error.GetType().Name);
            }
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
