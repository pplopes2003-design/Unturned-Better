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

        internal float HealthFraction { get { return Mathf.Clamp01(_health / MaximumHealth); } }
        internal float Health { get { return _health; } }
        internal bool IsDead { get { return _dead; } }
        internal float LastDamageAt { get { return _lastDamageAt; } }
        internal Vector3 LastDamageOrigin { get; private set; }
        internal bool HasExplosionOrigin { get; private set; }
        internal event Action<float, Vector3> Damaged;
        internal event Action Died;

        public bool IsEligibleForExplosionDamage
        {
            get { return !_dead && enabled && gameObject.activeInHierarchy; }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
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
            ApplyServerDamage(damage, delta.sqrMagnitude > 0.001f
                ? delta.normalized : Vector3.up);
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
                ProbeLogger.Log("NPC_EXPLOSION_DAMAGE: actor=" + gameObject.name +
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
