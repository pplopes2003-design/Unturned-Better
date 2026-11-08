using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Steam-module adapter of Line 1's humanoid melee combat.
    /// Uses actual finite, owned ItemMeleeAsset damage, range and quality
    /// when available; otherwise falls back to ordinary unarmed punches.
    /// Requires an actually visible, locally verified strike in front of
    /// the humanoid. NPC victims use one shared PrototypeNpcVitals owner;
    /// vanilla zombies and players retain native server damage pipelines.
    /// </summary>
    internal sealed class Line2NPCMeleeExecutor : MonoBehaviour
    {
        private const float AttackReach = 1.75f;
        private const float Cooldown = 1.4f;
        private const float BasePunchDamage = 12.0f;
        private float _nextAttackAt;
        private float _nextThink;
        private float _nextLogAt;
        private Line2NPCCombatPerception _perception;
        private PrototypeActor _actor;
        private PrototypeAnimation _animation;
        private Line2MobileNPCInventory _inventory;

        internal void Initialize(PrototypeActor actor,
            Line2NPCCombatPerception perception)
        {
            _actor = actor;
            _perception = perception;
            _animation = GetComponent<PrototypeAnimation>();
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _nextAttackAt = Time.time + UnityEngine.Random.Range(0.15f, 0.5f);
        }

        private static bool IsLocalAuthorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                   (Provider.clients == null || Provider.clients.Count <= 1) &&
                   Provider.hasCheats && Level.isLoaded && Player.player != null;
        }

        private void Update()
        {
            if (Time.time < _nextThink) return;
            _nextThink = Time.time + 0.12f;
            if (!IsLocalAuthorized() || _actor == null || _perception == null ||
                !_actor.IsCombatEnabled || _actor.IsDead || Time.time < _nextAttackAt)
                return;

            Line2NPCCombatPerception.TargetKind kind = _perception.Kind;
            Transform target = _perception.VisibleTarget;
            if (kind == Line2NPCCombatPerception.TargetKind.None || target == null)
                return;

            // An actor under an emergency tactical retreat will not interrupt
            // withdrawal for an ordinary fight. Zombie point-blank defense is
            // allowed, same as the Line 1 combat executor.
            if (_actor.IsTacticallyRetreating &&
                kind != Line2NPCCombatPerception.TargetKind.Zombie)
                return;

            // Melee reach and injury must come from an actually owned item,
            // never an inferred equipped weapon or a forged Player object.
            Item heldWeapon = null;
            ItemMeleeAsset meleeAsset = null;
            bool armed = _inventory != null &&
                _inventory.TryGetBestMeleeServer(
                    kind != Line2NPCCombatPerception.TargetKind.Zombie,
                    out heldWeapon, out meleeAsset);
            // Explicit initialization also handles the short-circuit path.
            float reach = armed ? Mathf.Clamp(meleeAsset.range, 1f, 3.2f)
                : AttackReach;
            float damage = BasePunchDamage;
            if (armed)
            {
                float quality = heldWeapon.quality / 100f;
                float condition = quality < 0.5f ? 0.5f + quality : 1f;
                float nativeDamage = kind == Line2NPCCombatPerception.TargetKind.Zombie
                    ? meleeAsset.zombieOrPlayerDamageMultiplier.multiply(ELimb.SPINE)
                    : meleeAsset.playerDamageMultiplier.multiply(ELimb.SPINE);
                damage = nativeDamage * condition;
                if (damage <= 0f || float.IsNaN(damage) ||
                    float.IsInfinity(damage))
                    return;
            }
            bool witnessedContact =
                _perception.ValidateStrike(kind, target, reach);
            if (witnessedContact &&
                _actor.ShouldHoldPreparedAmbushShot(target))
                return;
            if (!Line2NPCCombatRules.MayCommitHit(
                    _actor.Disposition, _actor.CombatMode,
                    !_actor.IsDead, IsLocalAuthorized(), witnessedContact))
                return;
            Vector3 direction = target.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) return;
            direction.Normalize();

            bool committed = false;
            switch (kind)
            {
                case Line2NPCCombatPerception.TargetKind.Player:
                    Player player = target.GetComponentInParent<Player>();
                    if (player == null || player.life == null ||
                        player.life.isDead || _actor.Disposition != "hostile")
                        return;

                    DamagePlayerParameters parameters =
                        new DamagePlayerParameters(player);
                    parameters.cause = armed ? EDeathCause.MELEE : EDeathCause.PUNCH;
                    parameters.limb = ELimb.SPINE;
                    // The original Line 1 unarmed implementation attributes
                    // NPC host kills to the listen-server owner.
                    parameters.killer = Provider.server;
                    parameters.direction = direction;
                    parameters.damage = damage;
                    parameters.respectArmor = true;
                    EPlayerKill playerKill;
                    DamageTool.damagePlayer(parameters, out playerKill);
                    committed = true;
                    break;

                case Line2NPCCombatPerception.TargetKind.Zombie:
                    Zombie zombie = target.GetComponentInParent<Zombie>();
                    if (zombie == null || zombie.isDead) return;
                    // Line 1's native unarmed spine profile:
                    // 15 base * 0.6 spine = 9 damage.
                    ushort zombieDamage = armed
                        ? (ushort)Mathf.Clamp(Mathf.RoundToInt(damage), 1, ushort.MaxValue)
                        : (ushort)9;
                    EPlayerKill zombieKill;
                    uint xp;
                    zombie.askDamage(zombieDamage,
                        direction * zombieDamage, out zombieKill, out xp,
                        trackKill: false, dropLoot: true);
                    committed = true;
                    break;

                case Line2NPCCombatPerception.TargetKind.Npc:
                    PrototypeActor rival =
                        target.GetComponentInParent<PrototypeActor>();
                    PrototypeNpcVitals victim =
                        rival != null ? rival.GetComponent<PrototypeNpcVitals>() : null;
                    if (rival == null || victim == null || victim.IsDead ||
                        !_perception.CanEngageNpcTarget(rival))
                        return;
                    // No guessed headshots on melee: a validated torso
                    // strike may cause damage/condition loss once.
                    committed = victim.ApplyDamageToPart(
                        damage,
                        Line2MobileNPCInjurySystem.BodyPart.Torso,
                        false, false,
                        Line2MobileNPCInjurySystem.BleedingSeverity.None,
                        direction, transform);
                    break;
            }

            if (!committed) return;
            if (armed)
                _inventory.TryWearOwnedMeleeServer(heldWeapon.id);
            // Native weak attack interval, bounded to avoid unreasonably
            // fast custom workshop weapons or a frame-by-frame damage loop.
            float cadence = armed
                ? Mathf.Clamp(meleeAsset.weak + 0.2f, 0.65f, 1.9f)
                : Cooldown;
            _nextAttackAt = Time.time + cadence +
                UnityEngine.Random.Range(-0.1f, 0.16f);
            if (_animation != null) _animation.PlayMeleeSwing();
            if (Time.time >= _nextLogAt)
            {
                ProbeLogger.Log("NPC_MELEE_HIT: actor=" + _actor.Identity +
                    " type=" + kind +
                    " weapon=" + (armed ? heldWeapon.id.ToString() : "fists") +
                    " (native server damage; verified LOS)");
                _nextLogAt = Time.time + 9f;
            }
        }

    }
}
