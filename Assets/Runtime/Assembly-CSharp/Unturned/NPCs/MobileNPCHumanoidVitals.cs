////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections;
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
		public event System.Action Died;
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
			if (!Provider.isServer)
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
			if (!Provider.isServer || isDead || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f
				|| injurySystem == null)
				return false;
			if (!injurySystem.ApplyInjuryEffectsServer(bodyPart, damage, causeBleeding, causeFracture, severity))
				return false;
			return ApplyDamage(damage, hitImpulse);
		}

		/// <summary>
		/// Apply validated damage and optionally provide the world-space impulse associated with the lethal hit.
		/// The impulse is only a presentation hint; callers must still validate the hit on the server.
		/// </summary>
		public bool ApplyDamage(float damage, Vector3 hitImpulse)
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

			CurrentHealth = Mathf.Max(0.0f, CurrentHealth - damage);
			brainDriver?.ReportDamage(CurrentHealth, maximumHealth);
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

		/// <summary>
		/// Visible local fallback for the current single-player prototype. The SDK's
		/// humanoid prefab does not guarantee a death clip or ragdoll, so a lethal hit
		/// must not leave the NPC standing upright after its AI has been disabled.
		/// </summary>
		private IEnumerator PlayDeathFallPresentation()
		{
			Quaternion startRotation = transform.rotation;
			Quaternion endRotation = Quaternion.AngleAxis(78.0f, transform.forward) * startRotation;
			const float duration = 0.45f;
			float elapsed = 0.0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				transform.rotation = Quaternion.Slerp(startRotation, endRotation, Mathf.Clamp01(elapsed / duration));
				yield return null;
			}
			transform.rotation = endRotation;
		}

		private void DieServer(Vector3 hitImpulse)
		{
			if (!Provider.isServer || isDead)
			{
				return;
			}

			isDead = true;
			brainDriver?.ResetServer();
			if (motor != null)
			{
				motor.enabled = false;
			}
			if (disableCombatOnDeath)
			{
				if (combat != null)
				{
					combat.enabled = false;
				}
				if (rangedCombat != null)
				{
					rangedCombat.enabled = false;
				}
				if (perception != null)
				{
					perception.enabled = false;
				}
				if (aim != null)
				{
					aim.enabled = false;
				}
				if (characterController != null)
				{
					characterController.enabled = false;
				}
			}
			DeathPresentationRequested?.Invoke(transform.position, transform.rotation, hitImpulse);
			StartCoroutine(PlayDeathFallPresentation());
			// Remove dead agents from active AI enumeration after publishing the final reset.
			if (brainDriver != null)
			{
				brainDriver.enabled = false;
			}
			Died?.Invoke();
		}
	}
}
