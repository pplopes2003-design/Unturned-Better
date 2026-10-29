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
		// Native rag/bandage/dressing. Splints and arbitrary MEDICAL items
		// cannot restore generic HP; wound-care owns bleeding/fractures.
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

			// The dedicated wound-care component owns bleeding AND fractures.
			// The generic HP healer must not consume a real splint, rag or
			// dressing before the injury handler can use it correctly. These
			// distinct services share only the authentic native inventory.
			MobileNPCInjurySystem injuries = GetComponent<MobileNPCInjurySystem>();
			if (injuries != null && (injuries.CurrentBleeding
				!= MobileNPCInjurySystem.BleedingSeverity.None || injuries.HasFracturedLeg))
				return;

			// Do not interrupt an active fight or a higher-priority tactical action.
			bool protectedTreatment = driver.CurrentGoal == MobileNPCTactics.Goal.SeekCover
				|| driver.CurrentGoal == MobileNPCTactics.Goal.Retreat;
			if ((driver.Target != null || driver.CurrentGoal != MobileNPCTactics.Goal.None)
				&& !protectedTreatment)
			{
				nextAttemptTime = Time.time + retryInterval;
				return;
			}

			nextAttemptTime = Time.time + retryInterval;
			// Select a real healing consumable rather than taking the first
			// MEDICAL item (which may be a splint or another non-healing aid).
			// Use a small dressing before a stronger one to reduce waste.
			ushort chosen = 0;
			float healAmount = 0f;
			for (int slot = 0; slot < inventory.UsedSlots; ++slot)
			{
				if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
					continue;
				float amount = item.id == 393 ? 5f
					: item.id == 95 ? 15f : item.id == 394 ? 35f : 0f;
				if (amount <= 0f)
					continue;
				if (chosen == 0 || amount < healAmount)
				{
					chosen = item.id;
					healAmount = amount;
				}
			}
			if (chosen == 0 || !inventory.TryConsumeOneItemByIdServer(chosen,
				out Item consumed))
				return;
			if (vitals.Heal(healAmount))
			{
				nextAllowedUseTime = Time.time + successfulUseCooldown;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				Debug.Log("[MobileNPCMedicalUse] " + name + " used item "
					+ chosen + " for " + healAmount.ToString("F0")
					+ " nominal HP; remaining=" + vitals.CurrentHealth.ToString("F1"), this);
#endif
				return;
			}
			// Invalid health transition: restore the item rather than lose it.
			if (!inventory.TryStoreTransferredItem(consumed))
				Debug.LogError("[MobileNPCMedicalUse] Failed to return unspent healing item "
					+ chosen + " after rejected healing.", this);
		}
	}
}
