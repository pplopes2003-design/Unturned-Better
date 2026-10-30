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
		[SerializeField, Min(0.5f)] private float treatmentDuration = 2.0f;
		private Item pendingItem;
		private float pendingHeal;
		private float treatmentFinishesAt;
		public bool IsTreating => pendingItem != null;

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

		private void OnDisable()
		{
			CancelPendingTreatment();
		}

		private void CancelPendingTreatment()
		{
			if (pendingItem == null)
				return;
			if (Provider.isServer && inventory != null
				&& !inventory.TryStoreTransferredItem(pendingItem))
				Debug.LogError("[MobileNPCMedicalUse] Could not restore reserved item after interrupted treatment.", this);
			pendingItem = null;
			pendingHeal = 0f;
		}

		private void Update()
		{
			if (!Provider.isServer || !Level.isLoaded || vitals == null || inventory == null || driver == null)
				return;

			if (pendingItem != null)
			{
				// Vanilla UseableConsumeable grants the effect only after the
				// Use animation. Likewise, do not heal on the first treatment frame.
				// An interrupted treatment returns the reserved owned item.
				// Combat Total NPCs frequently retain a target even while
				// badly wounded. Mere target memory must not cancel a life-saving
				// consumable already reserved. At critical HP finish the action;
				// normal wounds still require cover or a break in engagement.
				bool criticalCare = vitals.HealthFraction <= 0.25f;
				bool urgentCombat = !criticalCare && driver.Target != null
					&& driver.CurrentGoal != MobileNPCTactics.Goal.SeekCover
					&& driver.CurrentGoal != MobileNPCTactics.Goal.Retreat;
				if (vitals.IsDead || urgentCombat
					|| !criticalCare && driver.CurrentGoal == MobileNPCTactics.Goal.Flank
					|| !criticalCare && driver.CurrentGoal == MobileNPCTactics.Goal.Stalk
					|| !criticalCare && driver.CurrentGoal == MobileNPCTactics.Goal.Loot)
				{
					CancelPendingTreatment();
					return;
				}
				if (Time.time < treatmentFinishesAt)
					return;
				Item used = pendingItem;
				float healing = pendingHeal;
				pendingItem = null;
				pendingHeal = 0f;
				if (vitals.Heal(healing))
				{
					nextAllowedUseTime = Time.time + successfulUseCooldown;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					Debug.Log("[MobileNPCMedicalUse] Completed timed care: item=" + used.id
						+ " hp=" + vitals.CurrentHealth.ToString("F1"), this);
#endif
				}
				else if (!inventory.TryStoreTransferredItem(used))
					Debug.LogError("[MobileNPCMedicalUse] Could not restore rejected treatment item.", this);
				return;
			}
			MobileNPCMedicalBehavior woundCare = GetComponent<MobileNPCMedicalBehavior>();
			if (woundCare != null && woundCare.IsTreating)
				return;
			if (vitals.IsDead || vitals.HealthFraction <= 0f || vitals.HealthFraction >= healThreshold
				|| Time.time < nextAttemptTime || Time.time < nextAllowedUseTime)
				return;

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
				&& !protectedTreatment && vitals.HealthFraction > 0.25f)
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
			pendingItem = consumed;
			pendingHeal = healAmount;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.Log("[MobileNPCMedicalUse] Treatment started npc=" + name
				+ " hp=" + vitals.CurrentHealth.ToString("F1")
				+ " item=" + consumed.id, this);
#endif
			treatmentFinishesAt = Time.time + Mathf.Max(0.5f, treatmentDuration);
			// Presentation may play a real native Use clip when available;
			// treatment and HP remain authoritative on the server.
			MobileNPCVisualAnimation visuals = GetComponent<MobileNPCVisualAnimation>();
			if (visuals != null)
				visuals.PlayMedicalUseAnimation();
		}
	}
}
