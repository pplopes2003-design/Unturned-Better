using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Server-owned health and native explosion reception for a local NPC
    /// prototype. DamageTool finds this by querying a physical Collider on
    /// its AGENT layer and calling GetComponentInParent<IExplosionDamageable>.
    ///
    /// This deliberately does not claim native firearm/melee hit support:
    /// those require a distinct validated server hit-report integration.
    /// </summary>
    internal sealed class PrototypeNpcVitals : MonoBehaviour, IExplosionDamageable
    {
        private const float MaximumHealth = 100f;
        private const float CorpseSeconds = 12f;
        private float _health = MaximumHealth;
        private float _lastDamageAt = -1000f;
        private CharacterController _controller;
        private bool _dead;
        private float _nextDiagnosticAt;
        private Line2MobileNPCInjurySystem _injuries;

        internal float HealthFraction { get { return Mathf.Clamp01(_health / MaximumHealth); } }
        internal float Health { get { return _health; } }
        internal bool IsDead { get { return _dead; } }
        internal float LastDamageAt { get { return _lastDamageAt; } }
        internal Vector3 LastDamageOrigin { get; private set; }
        internal bool HasExplosionOrigin { get; private set; }
        // Backwards-compatible alias: either an observed blast center OR
        // server-validated gun attacker can now provide a defense direction.
        internal bool HasVerifiedDamageOrigin { get { return HasExplosionOrigin; } }
        internal event Action<float, Vector3> Damaged;
        internal event Action Died;

        public bool IsEligibleForExplosionDamage
        {
            get { return !_dead && enabled && gameObject.activeInHierarchy; }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _injuries = GetComponent<Line2MobileNPCInjurySystem>();
        }

        public bool Equals(IExplosionDamageable other)
        {
            return ReferenceEquals(this, other);
        }

        public Vector3 GetClosestPointToExplosion(Vector3 center)
        {
            if (_controller != null && _controller.enabled)
                return _controller.ClosestPoint(center);
            return transform.position + Vector3.up;
        }

        public void ApplyExplosionDamage(in ExplosionParameters parameters,
                                         ref ExplosionDamageParameters result)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || !IsEligibleForExplosionDamage ||
                parameters.damageRadius <= 0f ||
                float.IsNaN(parameters.damageRadius) ||
                float.IsInfinity(parameters.damageRadius))
                return;

            // This humanoid lives on AGENT for DamageTool's zombie/animal
            // overlap, and uses the native player-damage scaling when allowed.
            if (!result.shouldAffectZombies && !result.shouldAffectAnimals &&
                !result.shouldAffectPlayers)
                return;

            // Do not duplicate firearm damage. DamageTool invokes this once
            // per NPC due to its HashSet<IExplosionDamageable> deduplication.
            float amount = result.canDealPlayerDamage && parameters.playerDamage > 0f
                ? parameters.playerDamage : parameters.zombieDamage;
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
                return;

            Vector3 impactPoint = result.closestPoint;
            Vector3 delta = impactPoint - parameters.point;
            float range = delta.magnitude;
            if (float.IsNaN(range) || float.IsInfinity(range) ||
                range > parameters.damageRadius)
                return;

            // Use the same obstruction mask chosen by native DamageTool.
            // Its LOS helper is internal to Assembly-CSharp, so equivalent
            // non-mutating physics must be performed in this mod assembly.
            if (range > 0.05f)
            {
                RaycastHit obstruction;
                if (Physics.Raycast(parameters.point, delta / range, out obstruction,
                    Mathf.Max(0f, range - 0.015f), result.obstructionMask,
                    QueryTriggerInteraction.Ignore) && obstruction.transform != null &&
                    obstruction.transform != transform &&
                    !obstruction.transform.IsChildOf(transform))
                    return;
            }

            float damage = NpcExplosionMath.Calculate(
                amount, parameters.damageRadius, range);
            if (damage < 0.05f) return;
            LastDamageOrigin = parameters.point;
            HasExplosionOrigin = true;
            if (_injuries == null)
                _injuries = GetComponent<Line2MobileNPCInjurySystem>();
            if (_injuries != null)
                _injuries.ApplyInjuryEffectsServer(
                    Line2MobileNPCInjurySystem.BodyPart.Torso, damage,
                    false, false,
                    Line2MobileNPCInjurySystem.BleedingSeverity.None);
            ApplyServerDamage(damage, delta.sqrMagnitude > 0.001f
                ? delta.normalized : Vector3.up);
        }

        // Compatibility boundary for the original Line 1 injury system.
        // All actual health mutations still have exactly one owner.
        public bool ApplyDamage(float amount)
        {
            return ApplyServerDamage(amount, Vector3.zero);
        }

        public bool Heal(float amount)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                _dead || float.IsNaN(amount) || float.IsInfinity(amount) ||
                amount <= 0f)
                return false;
            float previous = _health;
            _health = Mathf.Min(MaximumHealth, _health + amount);
            return _health > previous;
        }

        // Legacy validated-hit contract. Attribution will be surfaced in
        // death/loot systems after a native Steam damage hook is attached.
        public bool ApplyDamage(float amount, Vector3 impulse, Transform attacker)
        {
            return ApplyServerDamage(amount, impulse);
        }

        public bool ApplyDamageToPart(float damage,
            Line2MobileNPCInjurySystem.BodyPart part,
            bool causeBleeding, bool causeFracture,
            Line2MobileNPCInjurySystem.BleedingSeverity severity,
            Vector3 impulse, Transform attacker)
        {
            if (attacker != null)
            {
                LastDamageOrigin = attacker.position;
                HasExplosionOrigin = true;
            }
            return ApplyDamageToPart(damage, part, causeBleeding, causeFracture,
                                    severity, impulse);
        }

        public bool ApplyDamageToPart(float damage,
            Line2MobileNPCInjurySystem.BodyPart part,
            bool causeBleeding, bool causeFracture,
            Line2MobileNPCInjurySystem.BleedingSeverity severity,
            Vector3 impulse)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer || _dead ||
                damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage))
                return false;

            if (_injuries == null)
                _injuries = GetComponent<Line2MobileNPCInjurySystem>();
            // Limb metadata is recorded, but NEVER damages health itself.
            // ApplyServerDamage below deducts the total damage only once.
            if (_injuries != null &&
                !_injuries.ApplyInjuryEffectsServer(part, damage,
                    causeBleeding, causeFracture, severity))
                return false;
            return ApplyServerDamage(damage, impulse);
        }

        internal bool ApplyServerDamage(float amount, Vector3 direction)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer || _dead ||
                float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
                return false;

            _health = Mathf.Max(0f, _health - amount);
            _lastDamageAt = Time.time;
            if (_nextDiagnosticAt <= Time.time)
            {
                ProbeLogger.Log("NPC_DAMAGE: actor=" + gameObject.name +
                    " damage=" + amount.ToString("F1") +
                    " remaining=" + _health.ToString("F1"));
                _nextDiagnosticAt = Time.time + 0.5f;
            }
            Damaged?.Invoke(amount, direction);
            if (_health > 0f) return true;

            _dead = true;
            // Reject any subsequent duplicate explosion candidates immediately.
            if (_controller != null) _controller.enabled = false;
            foreach (Collider collider in GetComponentsInChildren<Collider>())
                collider.enabled = false;
            Died?.Invoke();
            ProbeLogger.Log("NPC_PROTOTYPE_DIED: actor=" + gameObject.name +
                " (no native corpse/loot/persistence yet)");
            Destroy(gameObject, CorpseSeconds);
            return true;
        }
    }
}
