////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Conservative prototype for self-treatment using medical-category items stored by
	/// the mobile NPC. It does not call Player-only vanilla healing APIs.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCHumanoidVitals))]
	[RequireComponent(typeof(MobileNPCInventory))]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	public sealed class MobileNPCMedicalUse : MonoBehaviour
	{
		[SerializeField, Range(0.1f, 0.9f)] private float healThreshold = 0.55f;
		[SerializeField, Range(0.05f, 0.75f)] private float healFraction = 0.3f;
		[SerializeField, Min(1f)] private float retryInterval = 3f;
		[SerializeField, Min(1f)] private float successfulUseCooldown = 12f;

		private MobileNPCHumanoidVitals vitals;
		private MobileNPCInventory inventory;
		private MobileNPCBrainDriver driver;
		private float nextAttemptTime;
		private float nextAllowedUseTime;

		private void Awake()
		{
			vitals = GetComponent<MobileNPCHumanoidVitals>();
			inventory = GetComponent<MobileNPCInventory>();
			driver = GetComponent<MobileNPCBrainDriver>();
		}

		private void Update()
		{
			if (!Provider.isServer || !Level.isLoaded || vitals == null || inventory == null || driver == null
				|| vitals.IsDead || vitals.HealthFraction <= 0f || vitals.HealthFraction >= healThreshold
				|| Time.time < nextAttemptTime || Time.time < nextAllowedUseTime)
			{
				return;
			}

			// The wound-care component exclusively owns active bleeding treatment.
			// Avoid burning a specific rag/dressing as generic HP medicine while
			// blood loss still needs to be stopped.
			MobileNPCInjurySystem injuries = GetComponent<MobileNPCInjurySystem>();
			if (injuries != null
				&& injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None)
				return;

			// Do not interrupt an active fight or a higher-priority tactical action.
			if (driver.Target != null || driver.CurrentGoal != MobileNPCTactics.Goal.None)
			{
				nextAttemptTime = Time.time + retryInterval;
				return;
			}

			nextAttemptTime = Time.time + retryInterval;
			if (!inventory.TryConsumeFirstItemOfType(EItemType.MEDICAL, out Item medicalItem))
			{
				return;
			}

			float healing = Mathf.Max(1f, vitals.MaximumHealth * healFraction);
			if (vitals.Heal(healing))
			{
				nextAllowedUseTime = Time.time + successfulUseCooldown;
				return;
			}

			// If healing was rejected, return the consumed item when possible.
			inventory.TryStoreTransferredItem(medicalItem);
		}
	}
}
