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
		// Reserve lethal transition before publishing callbacks, which might themselves
		// attempt healing or damage. Only the original lethal hit can finalize death.
		private bool lethalTransitionReserved;

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
			if (!Provider.isServer || initialized || isDead || lethalTransitionReserved)
			{
				return;
			}

			maximumHealth = Mathf.Max(1.0f, maximumHealth);
			CurrentHealth = maximumHealth;
			isDead = false;
			lethalTransitionReserved = false;
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
			if (!Provider.isServer || isDead || lethalTransitionReserved || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f
				|| !IsFinite(hitImpulse.x) || !IsFinite(hitImpulse.y) || !IsFinite(hitImpulse.z)
				|| injurySystem == null)
				return false;

			// Initialize before recording injury so the first hit on a newly activated NPC
			// cannot create limb effects while the health state is still uninitialized.
			if (!initialized)
				InitializeServer();
			if (!initialized || isDead || lethalTransitionReserved)
				return false;

			if (!injurySystem.ApplyInjuryEffectsServer(bodyPart, damage, causeBleeding, causeFracture, severity))
				return false;
			return ApplyDamage(damage, hitImpulse, attacker);
		}

		/// <summary>
		/// Resolve a hit against the actual upright mobile-NPC hit volume. Vanilla
		/// RaycastInfo.limb belongs to Player/Zombie hitboxes and may be the
		/// default SPINE when the target is our custom Entity-layer collider.
		/// A single capsule cannot identify exact anatomy, so these bounded
		/// regions are an approximation until anatomical colliders are authored.
		/// </summary>
		public MobileNPCInjurySystem.BodyPart ResolveBodyPartAtWorldPoint(Vector3 impactPoint, Transform hitTransform = null)
		{
			if (!IsFinite(impactPoint.x) || !IsFinite(impactPoint.y)
				|| !IsFinite(impactPoint.z))
				return MobileNPCInjurySystem.BodyPart.Torso;
			if (hitTransform != null
				&& hitTransform.GetComponentInParent<MobileNPCHumanoidVitals>() == this
				&& MobileNPCBodyHitbox.TryResolve(hitTransform, out MobileNPCInjurySystem.BodyPart markedPart))
				return markedPart;
			// Backward-compatible approximation if an old prefab has only one
			// undifferentiated hit volume. The new 6-collider prefab is explicit.
			Vector3 local = transform.InverseTransformPoint(impactPoint);
			if (local.y > 1.49f)
				return MobileNPCInjurySystem.BodyPart.Head;
			if (local.y < 0.78f)
				return local.x < 0f ? MobileNPCInjurySystem.BodyPart.LeftLeg
					: MobileNPCInjurySystem.BodyPart.RightLeg;
			if (Mathf.Abs(local.x) > 0.24f && local.y > 0.92f)
				return local.x < 0f ? MobileNPCInjurySystem.BodyPart.LeftArm
					: MobileNPCInjurySystem.BodyPart.RightArm;
			return MobileNPCInjurySystem.BodyPart.Torso;
		}

		/// <summary>
		/// Server-verified player weapon impact: one health debit, one body-region
		/// injury result, and the actual attacker for later death attribution.
		/// Physical collision/range/ammunition validation remains in UseableGun/
		/// UseableMelee. This does not damage a Player or create fake XP.
		/// </summary>
		public bool ApplyValidatedWeaponImpactServer(float damage, Vector3 impactPoint,
			Vector3 hitDirection, Transform attacker, bool isFirearm, Transform hitTransform = null)
		{
			if (!Provider.isServer || isDead || lethalTransitionReserved
				|| !IsFinite(damage) || damage <= 0f
				|| !IsFinite(hitDirection.x) || !IsFinite(hitDirection.y)
				|| !IsFinite(hitDirection.z) || !IsFinite(impactPoint.x)
				|| !IsFinite(impactPoint.y) || !IsFinite(impactPoint.z))
				return false;

			MobileNPCInjurySystem.BodyPart part = ResolveBodyPartAtWorldPoint(impactPoint, hitTransform);
			// Probability uses final damage after the weapon's validated range/
			// falloff multiplier. Melee and firearms have distinct wound profiles.
			float bleedProbability = Mathf.Clamp01(
				(isFirearm ? 0.2f : 0.07f) + damage * (isFirearm ? 0.012f : 0.007f));
			bool bleed = damage >= (isFirearm ? 6f : 9f)
				&& UnityEngine.Random.value < bleedProbability;
			bool fracture = (part == MobileNPCInjurySystem.BodyPart.LeftLeg
				|| part == MobileNPCInjurySystem.BodyPart.RightLeg)
				&& damage >= 16f && UnityEngine.Random.value < 0.18f;
			MobileNPCInjurySystem.BleedingSeverity severity =
				damage > 32f && UnityEngine.Random.value < 0.4f
				? MobileNPCInjurySystem.BleedingSeverity.Heavy
				: MobileNPCInjurySystem.BleedingSeverity.Light;
			return ApplyDamageToPart(damage, part, bleed, fracture, severity,
				hitDirection * Mathf.Min(damage, 100f), attacker);
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
			if (!Provider.isServer || isDead || lethalTransitionReserved || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0.0f
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
			if (!initialized || isDead || lethalTransitionReserved)
			{
				return false;
			}

			float previousHealth = CurrentHealth;
			// Record even environmental hits so prior attackers are not credited for death.
			LastAttacker = attacker;
			LastDamageTime = Time.time;
			CurrentHealth = Mathf.Max(0.0f, CurrentHealth - damage);
			// Reserve a lethal transition *before* DamageReceived/HealthChanged handlers
			// run. Without this, a listener could reenter ApplyDamage/Heal or change
			// the zero-health state before DieServer executes, causing duplicated or
			// missing death handling and incorrect last-hit attribution.
			if (CurrentHealth <= 0.0f)
				lethalTransitionReserved = true;
			if (logDamageDiagnostics)
			{
				string attackerName = attacker != null ? attacker.name : "<environment>";
				Debug.Log("[MobileNPCHumanoidVitals] Damage accepted: npc=" + name
					+ ", attacker=" + attackerName + ", damage=" + damage.ToString("F1")
					+ ", health=" + previousHealth.ToString("F1") + "->" + CurrentHealth.ToString("F1")
					+ ", lethal=" + (CurrentHealth <= 0.0f) + ".", this);
			}
			// HP is already authoritative here. No observer, AI behavior or
			// HUD callback is allowed to strand a lethal 0-HP actor in the
			// reserved-but-not-dead state when it throws an exception.
			bool lethal = CurrentHealth <= 0f;
			try
			{
				SafeDeathStage("brain damage reaction", () =>
				{
					if (brainDriver != null)
						brainDriver.ReportDamage(CurrentHealth, maximumHealth);
				});
				SafeDeathStage("validated aggression", () =>
				{
					MobileNPCHumanoidDisposition disposition =
						GetComponent<MobileNPCHumanoidDisposition>();
					if (disposition != null)
						disposition.RecordValidatedAttackServer(attacker);
				});
				if (attacker != null && !lethal)
					SafeDeathStage("incoming fire reaction", () =>
					{
						if (brainDriver != null)
							brainDriver.ReportIncomingFire(attacker.position, attacker);
					});
				SafeDeathStage("damage received event", () =>
				{
					if (DamageReceived != null)
						DamageReceived(attacker, damage);
				});
				SafeDeathStage("health changed event", () =>
				{
					if (HealthChanged != null)
						HealthChanged(CurrentHealth, maximumHealth);
				});
			}
			finally
			{
				if (lethal)
					DieServer(hitImpulse);
			}

			return true;
		}

		/// <summary>Restore health from a validated server-side healing action.</summary>
		public bool Heal(float amount)
		{
			if (!Provider.isServer || !initialized || isDead || lethalTransitionReserved || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0.0f)
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

		/// <summary>
		/// Callback isolation is intentionally narrow. Damage validation still
		/// decides whether HP changes; failures in presentation, memory, or
		/// subscribers must NEVER interrupt the committed lethal transition.
		/// All failures are visible in Player.log with their stage name.
		/// </summary>
		private void SafeDeathStage(string stage, System.Action action)
		{
			try
			{
				action();
			}
			catch (System.Exception exception)
			{
				Debug.LogError("[MobileNPCHumanoidVitals] Failed stage '" + stage
					+ "' for " + name + ": " + exception, this);
			}
		}

		private void PublishDiedSafely()
		{
			System.Action handlers = Died;
			if (handlers == null)
				return;
			// A misbehaving loot listener cannot block the GroupMember listener,
			// and vice versa; one death still publishes each subscription once.
			foreach (System.Delegate handler in handlers.GetInvocationList())
			{
				System.Action callback = (System.Action)handler;
				SafeDeathStage("Died subscriber", callback);
			}
		}

		private void DieServer(Vector3 hitImpulse)
		{
			if (!Provider.isServer || isDead)
				return;

			// Set the terminal state FIRST. If a notification reenters damage,
			// healing or another death attempt, the second attempt is rejected.
			isDead = true;
			lethalTransitionReserved = true;
			CurrentHealth = 0f;

			if (logDamageDiagnostics)
				Debug.Log("[MobileNPCHumanoidVitals] Death transition: npc=" + name
					+ ", lastAttacker=" + (LastAttacker != null ? LastAttacker.name : "<unknown>")
					+ ", lastDamageTime=" + LastDamageTime.ToString("F2") + ".", this);

			SafeDeathStage("forget adaptive memory", () =>
			{
				MobileNPCAdaptiveMemory memory = GetComponent<MobileNPCAdaptiveMemory>();
				if (memory != null) memory.ForgetOnDeathServer();
			});
			SafeDeathStage("forget travel memory", () =>
			{
				MobileNPCTravelMemory memory = GetComponent<MobileNPCTravelMemory>();
				if (memory != null) memory.ForgetOnDeathServer();
			});
			SafeDeathStage("forget supply knowledge", () =>
			{
				MobileNPCSupplyKnowledge knowledge = GetComponent<MobileNPCSupplyKnowledge>();
				if (knowledge != null) knowledge.ForgetOnDeathServer();
			});

			SafeDeathStage("stop tactical brain", () =>
			{
				if (brainDriver != null) brainDriver.ResetServer();
			});
			// Each behavior must be stopped independently. One invalid/unloaded
			// Unity reference does not permit another behavior to keep acting.
			SafeDeathStage("stop motor", () => { if (motor != null) motor.enabled = false; });
			SafeDeathStage("stop melee combat", () => { if (combat != null) combat.enabled = false; });
			SafeDeathStage("stop ranged combat", () => { if (rangedCombat != null) rangedCombat.enabled = false; });
			SafeDeathStage("stop perception", () => { if (perception != null) perception.enabled = false; });
			SafeDeathStage("stop aiming", () => { if (aim != null) aim.enabled = false; });
			SafeDeathStage("stop walking collider", () =>
			{
				if (characterController != null) characterController.enabled = false;
			});

			SafeDeathStage("stop loot", () => DisableOnDeath<MobileNPCLooting>());
			SafeDeathStage("release A-Life job", () => DisableOnDeath<MobileNPCAutonomyPlanner>());
			SafeDeathStage("stop medical use", () => DisableOnDeath<MobileNPCMedicalUse>());
			SafeDeathStage("stop survival needs", () => DisableOnDeath<MobileNPCSurvivalNeeds>());
			SafeDeathStage("stop settlement planner", () => DisableOnDeath<MobileNPCSettlementPlanner>());
			SafeDeathStage("stop settlement builder", () => DisableOnDeath<MobileNPCSettlementBuilder>());
			SafeDeathStage("stop ranged presentation", () => DisableOnDeath<MobileNPCRangedPresentation>());
			SafeDeathStage("stop tactical combat", () => DisableOnDeath<MobileNPCTacticalCombatBehavior>());
			SafeDeathStage("stop companion contract", () => DisableOnDeath<MobileNPCCompanionContract>());
			SafeDeathStage("stop medical behavior", () => DisableOnDeath<MobileNPCMedicalBehavior>());
			SafeDeathStage("stop injury ticking", () => DisableOnDeath<MobileNPCInjurySystem>());
			SafeDeathStage("stop nameplate", () => DisableOnDeath<MobileNPCNameplate>());
			SafeDeathStage("stop visual guard", () => DisableOnDeath<MobileNPCVisualGuard>());

			SafeDeathStage("disable anatomical hit volumes", () =>
			{
				Transform hitboxRoot = transform.Find("MobileNPCHitbox");
				if (hitboxRoot == null) return;
				Collider[] hitboxes = hitboxRoot.GetComponentsInChildren<Collider>(true);
				foreach (Collider collider in hitboxes)
					if (collider != null) collider.enabled = false;
			});
			SafeDeathStage("activate corpse ragdoll", () => ActivateAuthoredRagdoll(hitImpulse));
			SafeDeathStage("death visual observers", () =>
			{
				System.Action<Vector3, Quaternion, Vector3> handlers = DeathPresentationRequested;
				if (handlers == null) return;
				foreach (System.Delegate handler in handlers.GetInvocationList())
				{
					System.Action<Vector3, Quaternion, Vector3> callback =
						(System.Action<Vector3, Quaternion, Vector3>)handler;
					SafeDeathStage("death presentation subscriber", () =>
						callback(transform.position, transform.rotation, hitImpulse));
				}
			});
			SafeDeathStage("unregister brain", () =>
			{
				if (brainDriver != null) brainDriver.enabled = false;
			});
			// Group, loot and other death subscribers retain their original
			// contracts and receive their signals even if ragdoll fails.
			PublishDiedSafely();
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
