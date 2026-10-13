////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-authoritative health state for a mobile humanoid NPC.
	/// Weapon/projectile hit detection must call ApplyDamage on the server.
	/// This component deliberately does not patch Player-only DamageTool APIs.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	[RequireComponent(typeof(MobileNPCInjurySystem))]
	public sealed class MobileNPCHumanoidVitals : MonoBehaviour
	{
		[SerializeField, Min(1.0f)] private float maximumHealth = 100.0f;
		[SerializeField] private bool disableCombatOnDeath = true;
		[SerializeField] private bool logDamageDiagnostics = true;

		private MobileNPCBrainDriver brainDriver;
		private MobileNPCInjurySystem injurySystem;
		private MobileNPCHumanoidMotor motor;
		private MobileNPCHumanoidCombat combat;
		private MobileNPCHumanoidRangedCombat rangedCombat;
		private MobileNPCHumanoidPerception perception;
		private MobileNPCHumanoidAim aim;
		private CharacterController characterController;
		private bool initialized;
		private bool isDead;

		public event System.Action<float, float> HealthChanged;
		/// <summary>Raised after validated server damage; attacker may be null for environmental damage.</summary>
		public event System.Action<Transform, float> DamageReceived;
		public event System.Action Died;
		public Transform LastAttacker { get; private set; }
		public float LastDamageTime { get; private set; } = -1f;
		/// <summary>Server-side death snapshot for a future entity replication/presentation layer. This event does not itself send a network message.</summary>
		public event System.Action<Vector3, Quaternion, Vector3> DeathPresentationRequested;

		public float MaximumHealth => maximumHealth;
		public float CurrentHealth { get; private set; }
		public bool IsDead => isDead;
		public float HealthFraction => maximumHealth > 0.0f ? Mathf.Clamp01(CurrentHealth / maximumHealth) : 0.0f;

		private void Awake()
		{
			brainDriver = GetComponent<MobileNPCBrainDriver>();
			injurySystem = GetComponent<MobileNPCInjurySystem>();
			motor = GetComponent<MobileNPCHumanoidMotor>();
			combat = GetComponent<MobileNPCHumanoidCombat>();
			rangedCombat = GetComponent<MobileNPCHumanoidRangedCombat>();
			perception = GetComponent<MobileNPCHumanoidPerception>();
			aim = GetComponent<MobileNPCHumanoidAim>();
			characterController = GetComponent<CharacterController>();
		}

		private void Start()
		{
			if (Provider.isServer && !initialized)
			{
				InitializeServer();
			}
		}

		public void InitializeServer()
		{
			// Initialization may be requested by both spawn adapters and Unity Start.
			// Re-entering must never heal or resurrect an already damaged/dead NPC.
			if (!Provider.isServer || initialized || isDead)
			{
				return;
			}

			maximumHealth = Mathf.Max(1.0f, maximumHealth);
			CurrentHealth = maximumHealth;
			isDead = false;
			initialized = true;
			// Ensure mobile humanoids receive the optional server-side medical behavior.
			// RequireComponent on that behavior adds the NPC inventory if the prefab omitted it.
			if (GetComponent<MobileNPCMedicalBehavior>() == null)
			{
				gameObject.AddComponent<MobileNPCMedicalBehavior>();
			}
			if (motor != null && !motor.IsInitialized)
			{
				motor.InitializeServer();
			}
			brainDriver?.ReportHealth(CurrentHealth, maximumHealth);
			HealthChanged?.Invoke(CurrentHealth, maximumHealth);
		}

		/// <summary>
		/// Apply damage after the server has validated the hit, attacker and damage type.
		/// Returns false for client calls, invalid damage, or an already-dead NPC.
		/// </summary>
		public bool ApplyDamage(float damage)
		{
			return ApplyDamage(damage, Vector3.zero);
		}

		/// <summary>
		/// Applies one validated hit to a known body region. Damage is deducted exactly
		/// once; the injury component records limb condition, bleeding, and fractures.
		/// The weapon/hit adapter must supply the actual region and injury flags.
		/// </summary>
		public bool ApplyDamageToPart(float damage, MobileNPCInjurySystem.BodyPart bodyPart,
			bool causeBleeding, bool causeFracture,
			MobileNPCInjurySystem.BleedingSeverity severity = MobileNPCInjurySystem.BleedingSeverity.Light,
			Vector3 hitImpulse = default(Vector3))
		{
			return ApplyDamageToPart(damage, bodyPart, causeBleeding, causeFracture, severity, hitImpulse, null);
		}

		/// <summary>Body-region damage with the validated attacker preserved for kill attribution.</summary>
		public bool ApplyDamageToPart(float damage, MobileNPCInjurySystem.BodyPart bodyPart,
			bool causeBleeding, bool causeFracture, MobileNPCInjurySystem.BleedingSeverity severity,
			Vector3 hitImpulse, Transform attacker)
		{
			if (!Provider.isServer || isDead || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f
				|| !IsFinite(hitImpulse.x) || !IsFinite(hitImpulse.y) || !IsFinite(hitImpulse.z)
				|| injurySystem == null)
				return false;

			// Initialize before recording injury so the first hit on a newly activated NPC
			// cannot create limb effects while the health state is still uninitialized.
			if (!initialized)
				InitializeServer();
			if (!initialized || isDead)
				return false;

			if (!injurySystem.ApplyInjuryEffectsServer(bodyPart, damage, causeBleeding, causeFracture, severity))
				return false;
			return ApplyDamage(damage, hitImpulse, attacker);
		}

		/// <summary>
		/// Apply validated damage and optionally provide the world-space impulse associated with the lethal hit.
		/// The impulse is only a presentation hint; callers must still validate the hit on the server.
		/// </summary>
		public bool ApplyDamage(float damage, Vector3 hitImpulse)
		{
			return ApplyDamage(damage, hitImpulse, null);
		}

		/// <summary>
		/// Apply damage after server-side hit validation and preserve the source for kill credit,
		/// faction consequences, and investigation behavior. Null is valid for world damage.
		/// </summary>
		public bool ApplyDamage(float damage, Vector3 hitImpulse, Transform attacker)
		{
			if (!Provider.isServer || isDead || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0.0f
				|| !IsFinite(hitImpulse.x) || !IsFinite(hitImpulse.y) || !IsFinite(hitImpulse.z))
			{
				return false;
			}

			// A hit can arrive on the same frame the spawned template is activated.
			// Initialize synchronously rather than silently discarding the first damage event.
			if (!initialized)
			{
				InitializeServer();
			}
			if (!initialized || isDead)
			{
				return false;
			}

			float previousHealth = CurrentHealth;
			// Record even environmental hits so prior attackers are not credited for death.
			LastAttacker = attacker;
			LastDamageTime = Time.time;
			CurrentHealth = Mathf.Max(0.0f, CurrentHealth - damage);
			if (logDamageDiagnostics)
			{
				string attackerName = attacker != null ? attacker.name : "<environment>";
				Debug.Log("[MobileNPCHumanoidVitals] Damage accepted: npc=" + name
					+ ", attacker=" + attackerName + ", damage=" + damage.ToString("F1")
					+ ", health=" + previousHealth.ToString("F1") + "->" + CurrentHealth.ToString("F1")
					+ ", lethal=" + (CurrentHealth <= 0.0f) + ".", this);
			}
			brainDriver?.ReportDamage(CurrentHealth, maximumHealth);
			DamageReceived?.Invoke(attacker, damage);
			HealthChanged?.Invoke(CurrentHealth, maximumHealth);

			if (CurrentHealth <= 0.0f)
			{
				DieServer(hitImpulse);
			}

			return true;
		}

		/// <summary>Restore health from a validated server-side healing action.</summary>
		public bool Heal(float amount)
		{
			if (!Provider.isServer || !initialized || isDead || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0.0f)
			{
				return false;
			}

			float previousHealth = CurrentHealth;
			CurrentHealth = Mathf.Min(maximumHealth, CurrentHealth + amount);
			if (Mathf.Approximately(previousHealth, CurrentHealth))
			{
				return false;
			}

			brainDriver?.ReportHealth(CurrentHealth, maximumHealth);
			HealthChanged?.Invoke(CurrentHealth, maximumHealth);
			return true;
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}

		private void DieServer(Vector3 hitImpulse)
		{
			if (!Provider.isServer || isDead)
			{
				return;
			}

			isDead = true;
			// Private experience does not survive death or pass to the replacement NPC.
			MobileNPCAdaptiveMemory adaptiveMemory = GetComponent<MobileNPCAdaptiveMemory>();
			if (adaptiveMemory != null)
				adaptiveMemory.ForgetOnDeathServer();
			if (logDamageDiagnostics)
			{
				Debug.Log("[MobileNPCHumanoidVitals] Death transition: npc=" + name
					+ ", lastAttacker=" + (LastAttacker != null ? LastAttacker.name : "<unknown>")
					+ ", lastDamageTime=" + LastDamageTime.ToString("F2") + ".", this);
			}
			brainDriver?.ResetServer();
			if (motor != null)
			{
				motor.enabled = false;
			}
			// Death always stops gameplay components; retain the serialized field for prefab compatibility.
			{
				if (combat != null)
					combat.enabled = false;
				if (rangedCombat != null)
					rangedCombat.enabled = false;
				if (perception != null)
					perception.enabled = false;
				if (aim != null)
					aim.enabled = false;
				if (characterController != null)
					characterController.enabled = false;

				// These server-side behaviors can still run after the brain is stopped.
				// Explicitly end item acquisition, healing, survival ticking and construction
				// so the corpse cannot resume work or mutate inventory/world state.
				DisableOnDeath<MobileNPCLooting>();
				DisableOnDeath<MobileNPCMedicalUse>();
				DisableOnDeath<MobileNPCSurvivalNeeds>();
				DisableOnDeath<MobileNPCSettlementPlanner>();
				DisableOnDeath<MobileNPCSettlementBuilder>();
				DisableOnDeath<MobileNPCRangedPresentation>();
				DisableOnDeath<MobileNPCTacticalCombatBehavior>();
				DisableOnDeath<MobileNPCCompanionContract>();
				DisableOnDeath<MobileNPCMedicalBehavior>();
				DisableOnDeath<MobileNPCInjurySystem>();
				DisableOnDeath<MobileNPCNameplate>();
				DisableOnDeath<MobileNPCVisualGuard>();
			}
			// Stop the living hit volume from intercepting later damage rays. The
			// authored ragdoll colliders below become the physical corpse instead.
			Transform hitboxRoot = transform.Find("MobileNPCHitbox");
			if (hitboxRoot != null)
			{
				Collider damageHitbox = hitboxRoot.GetComponent<Collider>();
				if (damageHitbox != null)
					damageHitbox.enabled = false;
			}
			// NPCs are local single-player entities, not native Player/Zombie instances.
			// Activate the physical ragdoll already authored in NPC_Client instead of
			// calling PlayerLife/RagdollTool with an incompatible entity or leaving the
			// humanoid animation standing over a zero-health state.
			ActivateAuthoredRagdoll(hitImpulse);
			DeathPresentationRequested?.Invoke(transform.position, transform.rotation, hitImpulse);
			// Remove dead agents from active AI enumeration after publishing the final reset.
			if (brainDriver != null)
			{
				brainDriver.enabled = false;
			}
			Died?.Invoke();
		}
		private void DisableOnDeath<T>() where T : Behaviour
		{
			T component = GetComponent<T>();
			if (component != null)
				component.enabled = false;
		}

		/// <summary>
		/// A physical, non-articulated corpse when NPC_Client contains no complete bone
		/// ragdoll. This does not pretend to be a skeletal ragdoll; it ensures the dead
		/// character actually falls instead of standing forever without collision.
		/// </summary>
		private void ActivateRigidCorpseFallback(Transform visualRoot, Vector3 hitImpulse,
			Rigidbody[] incompleteBodies, Collider[] incompleteColliders)
		{
			for (int i = 0; i < incompleteColliders.Length; ++i)
			{
				if (incompleteColliders[i] != null)
					incompleteColliders[i].enabled = false;
			}
			for (int i = 0; i < incompleteBodies.Length; ++i)
			{
				if (incompleteBodies[i] == null)
					continue;
				incompleteBodies[i].isKinematic = true;
				incompleteBodies[i].detectCollisions = false;
			}

			MobileNPCVisualGuard visualGuard = GetComponent<MobileNPCVisualGuard>();
			if (visualGuard != null)
				visualGuard.enabled = false;

			// The proxy uses the world-upright NPC capsule frame, rather than the
			// -90 degree rotated visual rig frame. It stays inside the NPC hierarchy
			// so the existing corpse cleanup removes it with the dead instance.
			GameObject corpse = new GameObject("MobileNPCCorpsePhysics");
			corpse.layer = LayerMasks.AGENT;
			corpse.transform.SetParent(transform, false);
			corpse.transform.localPosition = Vector3.zero;
			corpse.transform.localRotation = Quaternion.identity;
			visualRoot.SetParent(corpse.transform, true);

			CapsuleCollider corpseCollider = corpse.AddComponent<CapsuleCollider>();
			corpseCollider.direction = 1;
			corpseCollider.center = new Vector3(0f, 0.9f, 0f);
			corpseCollider.height = 1.8f;
			corpseCollider.radius = 0.34f;
			Rigidbody corpseBody = corpse.AddComponent<Rigidbody>();
			corpseBody.mass = 70f;
			corpseBody.useGravity = true;
			corpseBody.isKinematic = false;
			corpseBody.interpolation = RigidbodyInterpolation.Interpolate;
			corpseBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

			Vector3 direction = hitImpulse.sqrMagnitude > 0.001f
				? hitImpulse.normalized : transform.forward;
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.001f)
				direction = transform.forward;
			direction.Normalize();
			// An off-centre push causes the corpse to tip naturally instead of
			// remaining an upright, static-looking capsule after death.
			corpseBody.AddForceAtPosition(direction * Mathf.Max(60f, hitImpulse.magnitude),
				corpse.transform.position + Vector3.up * 1.5f, ForceMode.Impulse);
			corpseBody.AddTorque(Vector3.Cross(Vector3.up, direction) * 35f, ForceMode.Impulse);
			Debug.LogWarning("[MobileNPCHumanoidVitals] Incomplete authored skeletal ragdoll; "
				+ "activated physical capsule corpse fallback for " + name
				+ ". This is a rigid-body fall, not articulated bone ragdoll.", this);
		}

		private void ActivateAuthoredRagdoll(Vector3 hitImpulse)
		{
			Transform visualRoot = transform.Find("HumanoidVisual");
			if (visualRoot == null)
			{
				Debug.LogError("[MobileNPCHumanoidVitals] Death presentation failed: HumanoidVisual root is missing.", this);
				return;
			}

			// Stop all animation writers before releasing the authored bone rigidbodies.
			Animation[] animations = visualRoot.GetComponentsInChildren<Animation>(true);
			for (int i = 0; i < animations.Length; ++i)
			{
				if (animations[i] != null)
					animations[i].enabled = false;
			}
			Animator[] animators = visualRoot.GetComponentsInChildren<Animator>(true);
			for (int i = 0; i < animators.Length; ++i)
			{
				if (animators[i] != null)
					animators[i].enabled = false;
			}

			Collider[] colliders = visualRoot.GetComponentsInChildren<Collider>(true);
			Rigidbody[] authoredBodies = visualRoot.GetComponentsInChildren<Rigidbody>(true);
			Joint[] authoredJoints = visualRoot.GetComponentsInChildren<Joint>(true);
			int usableColliders = 0;
			for (int i = 0; i < colliders.Length; ++i)
			{
				if (colliders[i] != null && !colliders[i].isTrigger)
					usableColliders++;
			}
			if (authoredBodies.Length == 0 || authoredJoints.Length == 0 || usableColliders == 0)
			{
				ActivateRigidCorpseFallback(visualRoot, hitImpulse, authoredBodies, colliders);
				return;
			}
			int enabledColliders = 0;
			for (int i = 0; i < colliders.Length; ++i)
			{
				Collider bodyCollider = colliders[i];
				if (bodyCollider == null || bodyCollider.isTrigger)
					continue;
				bodyCollider.enabled = true;
				++enabledColliders;
			}

			Rigidbody[] bodies = visualRoot.GetComponentsInChildren<Rigidbody>(true);
			Joint[] joints = visualRoot.GetComponentsInChildren<Joint>(true);
			int releasedBodies = 0;
			Rigidbody impulseBody = null;
			float nearestSqrDistance = float.PositiveInfinity;
			Vector3 hitPoint = transform.position;
			if (hitImpulse.sqrMagnitude > 0.0001f)
			{
				// The hit impulse carries direction/magnitude, not a hit point. Use the
				// nearest authored body to the NPC root as a stable fallback application point.
				hitPoint = visualRoot.position;
			}
			for (int i = 0; i < bodies.Length; ++i)
			{
				Rigidbody body = bodies[i];
				if (body == null)
					continue;
				body.detectCollisions = true;
				body.isKinematic = false;
				body.WakeUp();
				++releasedBodies;
				float sqrDistance = (body.worldCenterOfMass - hitPoint).sqrMagnitude;
				if (sqrDistance < nearestSqrDistance)
				{
					nearestSqrDistance = sqrDistance;
					impulseBody = body;
				}
			}

			if (impulseBody != null && hitImpulse.sqrMagnitude > 0.0001f)
				impulseBody.AddForce(hitImpulse, ForceMode.Impulse);

			if (releasedBodies == 0 || enabledColliders == 0 || joints.Length == 0)
			{
				Debug.LogError("[MobileNPCHumanoidVitals] NPC_Client death ragdoll may be incomplete: released rigidbodies="
					+ releasedBodies + ", enabled colliders=" + enabledColliders + ", joints=" + joints.Length
					+ ". Check the NPC_Client prefab's authored ragdoll hierarchy.", this);
			}
			else
			{
				Debug.Log("[MobileNPCHumanoidVitals] Death ragdoll activated: rigidbodies="
					+ releasedBodies + ", colliders=" + enabledColliders + ", joints=" + joints.Length + ".", this);
			}
		}

	}
}
