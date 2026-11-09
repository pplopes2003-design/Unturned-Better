using System;
using System.Collections.Generic;
using System.Reflection;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Complements the native UseableGun server hit event for LOCAL
    /// singleplayer shots whose custom Entity-layer NPC_Client trigger
    /// colliders are classified as ERaycastInfoType.NONE by vanilla Unturned.
    ///
    /// Uses original Line 1's first-physical-hit validator, native gun
    /// damage multipliers / distance falloff, and one shared NPC health owner.
    /// Never edits the client's hit, creates bullets or changes vanilla
    /// PLAYER/ZOMBIE/ANIMAL damage; never runs with remote clients.
    /// </summary>
    internal sealed class Line2NpcNativeGunHitBridge : MonoBehaviour
    {
        private static readonly MethodInfo NativeBulletMultiplier =
            typeof(UseableGun).GetMethod("getBulletDamageMultiplier",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(BulletInfo).MakeByRefType() }, null);

        private readonly HashSet<BulletInfo> _processed = new HashSet<BulletInfo>();
        private float _nextResetAt;
        private float _nextLogAt;

        private void OnEnable()
        {
            UseableGun.onBulletHit += OnBulletHit;
            ProbeLogger.Log("NPC_GUN_HIT_BRIDGE_READY: native server ray validator armed; " +
                            "vanilla bullet multiplier=" + (NativeBulletMultiplier != null));
        }

        private void OnDisable()
        {
            UseableGun.onBulletHit -= OnBulletHit;
            _processed.Clear();
        }

        private void OnBulletHit(UseableGun gun, BulletInfo bullet,
                                 InputInfo hit, ref bool shouldAllow)
        {
            if (!shouldAllow || gun == null || bullet == null || hit == null ||
                !Provider.isServer || Dedicator.IsDedicatedServer ||
                !Provider.hasCheats || !Level.isLoaded ||
                (Provider.clients != null && Provider.clients.Count > 1) ||
                Player.player == null || Player.player.life == null ||
                Player.player.life.isDead ||
                Player.player.equipment == null ||
                Player.player.equipment.useable != gun ||
                NativeBulletMultiplier == null)
                return;

            var asset = Player.player.equipment.asset as ItemGunAsset;
            if (asset == null || asset.playerDamageMultiplier == null ||
                asset.range <= 0f || asset.range > 3000f)
                return;
            Vector3 start = bullet.origin;
            Vector3 reported = hit.point;
            Vector3 displacement = reported - start;
            float distance = displacement.magnitude;
            if (distance < 0.1f || distance > asset.range + 1f ||
                float.IsNaN(distance) || float.IsInfinity(distance))
                return;

            // Host-only: the server independently traces FIRST contact from
            // the actual bullet origin. This rejects claimed hits behind
            // walls, barricades, vehicles and other blocking targets.
            Transform realHit;
            float physicalDistance;
            if (!Line2MobileNPCDamageBridge.TryResolvePhysicalServerHit(
                start, reported, asset.range, out realHit,
                out physicalDistance))
                return;
            Line2MobileNPCInjurySystem.BodyPart part;
            if (!Line2MobileNPCBodyHitbox.TryResolve(realHit, out part))
                return;

            // No hitmarker-verified NPC volume: no damage. References are
            // bounded and never persisted to game saves.
            if (Time.time >= _nextResetAt || _processed.Count >= 512)
            {
                _processed.Clear();
                _nextResetAt = Time.time + 15f;
            }
            if (!_processed.Add(bullet))
                return;

            try
            {
                var args = new object[] { bullet };
                object raw = NativeBulletMultiplier.Invoke(gun, args);
                if (!(raw is float)) return;
                float times = (float)raw;
                float startFalloff = asset.range * asset.damageFalloffRange;
                float endFalloff = asset.range * asset.damageFalloffMaxRange;
                float alpha = Mathf.InverseLerp(startFalloff, endFalloff, physicalDistance);
                times *= Mathf.Lerp(1.0f, asset.damageFalloffMultiplier, alpha);
                if (float.IsNaN(times) || float.IsInfinity(times) || times <= 0f)
                    return;

                ELimb limb;
                switch (part)
                {
                    case Line2MobileNPCInjurySystem.BodyPart.Head:
                        limb = ELimb.SKULL; break;
                    case Line2MobileNPCInjurySystem.BodyPart.LeftArm:
                        limb = ELimb.LEFT_ARM; break;
                    case Line2MobileNPCInjurySystem.BodyPart.RightArm:
                        limb = ELimb.RIGHT_ARM; break;
                    case Line2MobileNPCInjurySystem.BodyPart.LeftLeg:
                        limb = ELimb.LEFT_LEG; break;
                    case Line2MobileNPCInjurySystem.BodyPart.RightLeg:
                        limb = ELimb.RIGHT_LEG; break;
                    default:
                        limb = ELimb.SPINE; break;
                }
                // Original weapon asset's exact player-vs-limb multiplier.
                float damage = asset.playerDamageMultiplier.multiply(limb) * times;
                if (float.IsNaN(damage) || float.IsInfinity(damage) ||
                    damage <= 0f || damage > 50000f)
                    return;

                Collider collider = realHit.GetComponent<Collider>();
                if (collider == null) return;
                Vector3 shotDirection = displacement / distance;
                bool applied = Line2MobileNPCDamageBridge.TryApplyValidatedHitToPart(
                    collider, damage, part,
                    false, false, Line2MobileNPCInjurySystem.BleedingSeverity.None,
                    shotDirection, Player.player.transform);
                if (applied && Time.time >= _nextLogAt)
                {
                    ProbeLogger.Log("NPC_NATIVE_GUN_DAMAGE: local gun=" + asset.id +
                        " part=" + part + " damage=" + damage.ToString("F1") +
                        " (server ray, no native damage duplication)");
                    _nextLogAt = Time.time + 5f;
                }
                // NPC collider was the FIRST authoritative physical hit.
                // Prevent the native hit report (potentially a wall/player
                // behind this custom trigger) from applying a SECOND hit.
                if (applied)
                    shouldAllow = false;
            }
            catch (Exception ex)
            {
                if (Time.time >= _nextLogAt)
                {
                    ProbeLogger.Log("NPC_GUN_HIT_UNAVAILABLE: " +
                        ex.GetType().Name + " " + ex.Message);
                    _nextLogAt = Time.time + 5f;
                }
            }
        }
    }
}
