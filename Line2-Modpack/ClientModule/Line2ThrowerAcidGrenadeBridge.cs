using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Dedicated LOCAL singleplayer extension of vanilla UseableThrowable:
    /// keep the authentic grenade fuse/physics/blue Thrower visual and add
    /// three weak server-authoritative corrosion ticks after detonation.
    /// Never replace native grenade damage or a SteamPlayer.
    /// </summary>
    internal sealed class Line2ThrowerAcidGrenadeBridge : MonoBehaviour
    {
        internal const ushort CustomAcidGrenadeId = 32615;

        private void OnEnable()
        {
            UseableThrowable.onThrowableSpawned += OnThrowableSpawned;
        }

        private void OnDisable()
        {
            UseableThrowable.onThrowableSpawned -= OnThrowableSpawned;
        }

        private static void OnThrowableSpawned(UseableThrowable useable, GameObject obj)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Provider.hasCheats || !Level.isLoaded || obj == null ||
                useable == null || useable.equippedThrowableAsset == null ||
                useable.equippedThrowableAsset.id != CustomAcidGrenadeId ||
                (Provider.clients != null && Provider.clients.Count > 1))
                return;

            Grenade native = obj.GetComponent<Grenade>();
            if (native == null)
            {
                ProbeLogger.Log("ACID_GRENADE_NO_NATIVE_EXPLOSION: item=" +
                    CustomAcidGrenadeId + " isExplosive=" +
                    useable.equippedThrowableAsset.isExplosive +
                    " (regenerate/reinstall asset; no fuse or splash component)");
                return;
            }
            if (obj.GetComponent<Line2ThrowerAcidGrenadeField>() != null)
                return;

            // Resolve the actual Thrower effect against the running Steam
            // asset registry. The base grenade may have inherited a missing
            // Explosion reference from a stale/old staging package.
            Guid acidEffect = new Guid("ae477aac40b64d3c8ce8e538daffecf5");
            EffectAsset resolvedEffect = Assets.FindEffectAssetByGuidOrLegacyId(acidEffect, 0);
            if (resolvedEffect != null &&
                Assets.FindEffectAssetByGuidOrLegacyId(
                    native.explosionEffectGuid, native.explosion) == null)
            {
                native.explosionEffectGuid = acidEffect;
                native.explosion = 0;
                ProbeLogger.Log("ACID_GRENADE_EFFECT_RESTORED: native Thrower effect available");
            }
            else if (resolvedEffect == null)
            {
                ProbeLogger.Log("ACID_GRENADE_EFFECT_UNAVAILABLE: Thrower effect GUID not loaded");
            }
            ProbeLogger.Log("ACID_GRENADE_NATIVE_CONFIG: radius=" +
                native.range.ToString("F1") + " zombieDamage=" +
                native.zombieDamage.ToString("F1") + " fuse=" +
                native.fuseLength.ToString("F1"));
            // The vanilla Grenade.Explode() still performs the initial
            // 3m splash + native visual. Leave its server object in place
            // until three delayed pulses complete, then clean it up.
            native.shouldDestroySelf = false;
            Line2ThrowerAcidGrenadeField field =
                obj.AddComponent<Line2ThrowerAcidGrenadeField>();
            field.Initialize(native);
            ProbeLogger.Log("ACID_GRENADE_ARMED: item=" + CustomAcidGrenadeId +
                " fuse=" + native.fuseLength.ToString("F1") +
                " (original physics, native acid visual, 3 weak corrosion pulses)");
        }
    }

    internal sealed class Line2ThrowerAcidGrenadeField : MonoBehaviour
    {
        private const float Radius = 2.3f;
        private const float PlayerTickDamage = 5.0f;
        private const float ZombieTickDamage = 8.0f;
        private const float NpcTickDamage = 6.0f;
        private const int PulseCount = 3;
        private readonly Collider[] _candidates = new Collider[64];
        private readonly HashSet<PrototypeNpcVitals> _affected =
            new HashSet<PrototypeNpcVitals>();
        private Grenade _native;
        private Vector3 _lastPoint;
        private float _firstPulse;
        private int _pulses;
        private bool _started;

        internal void Initialize(Grenade grenade)
        {
            _native = grenade;
            _lastPoint = transform.position;
            _firstPulse = Time.time + Mathf.Max(0f, grenade.fuseLength) + 0.15f;
            _started = true;
        }

        private void Update()
        {
            if (!_started) return;
            if (!Provider.isServer || Dedicator.IsDedicatedServer || !Level.isLoaded)
            {
                Destroy(gameObject);
                return;
            }
            if (Time.time < _firstPulse + _pulses * 1.0f)
                return;

            _lastPoint = transform.position;
            if (_pulses < PulseCount)
            {
                ++_pulses;
                ApplyAcidPulse(_lastPoint);
                if (_pulses == PulseCount)
                {
                    ProbeLogger.Log("ACID_GRENADE_PULSES_COMPLETE: location=" +
                        _lastPoint.ToString("F1"));
                }
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void ApplyAcidPulse(Vector3 at)
        {
            // This is the same native splash API used by the real acid
            // zombie, but scaled down and with zero buildable/vehicle
            // damage. No custom Player damage bypassing PvE rules.
            var kills = new List<EPlayerKill>();
            DamageTool.explode(at, Radius, EDeathCause.ACID,
                _native != null ? _native.killer : Steamworks.CSteamID.Nil,
                PlayerTickDamage, 0f, ZombieTickDamage, 0f,
                0f, 0f, 0f, 0f, out kills,
                EExplosionDamageType.ZOMBIE_ACID, 0f,
                damageOrigin: EDamageOrigin.Radioactive_Zombie_Explosion);

            // NPC_Client anatomy is not a vanilla IExplosionDamageable.
            // Apply once per NPC per pulse, never six times for six bones.
            int count = Physics.OverlapSphereNonAlloc(
                at, Radius, _candidates, RayMasks.ENTITY,
                QueryTriggerInteraction.Collide);
            _affected.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider collider = _candidates[i];
                _candidates[i] = null;
                if (collider == null ||
                    collider.GetComponent<Line2MobileNPCBodyHitbox>() == null)
                    continue;
                PrototypeNpcVitals npc =
                    collider.GetComponentInParent<PrototypeNpcVitals>();
                if (npc == null || npc.IsDead || !_affected.Add(npc))
                    continue;

                Vector3 eye = npc.transform.position + Vector3.up * 1.0f;
                Vector3 source = at + Vector3.up * 0.2f;
                float distance = Vector3.Distance(source, eye);
                if (distance > Radius + 0.35f ||
                    Physics.Linecast(source, eye, RayMasks.BLOCK_COLLISION,
                        QueryTriggerInteraction.Ignore))
                    continue;
                float falloff = Mathf.Clamp01(1f - distance / (Radius + 0.5f));
                float damage = NpcTickDamage * Mathf.Max(0.25f, falloff);
                Transform attacker = Player.player != null
                    ? Player.player.transform : null;
                npc.ApplyDamage(damage, (eye - source).normalized, attacker);
            }
            _affected.Clear();
            ProbeLogger.Log("ACID_GRENADE_PULSE: index=" + _pulses +
                " radius=" + Radius.ToString("F1"));
        }
    }
}
