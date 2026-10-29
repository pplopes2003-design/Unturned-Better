////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-side self-treatment using item IDs explicitly configured from native
	/// Unturned assets. It never assumes every medical item is interchangeable.
	/// World-loot acquisition and crafting must feed the existing NPC inventory.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCInjurySystem))]
	[RequireComponent(typeof(MobileNPCHumanoidVitals))]
	[RequireComponent(typeof(MobileNPCInventory))]
	public sealed class MobileNPCMedicalBehavior : MonoBehaviour
	{
		[Header("Native item IDs configured per prefab")]
		[SerializeField] private ushort[] bandageItemIDs = { 393, 95, 394 }; // Native Rag, Bandage, Dressing IDs.
		[SerializeField] private ushort[] splintItemIDs = { 96 }; // Native Splint ID.

		[Header("Treatment decisions")]
		[SerializeField, Min(0.2f)] private float decisionInterval = 1f;
		[SerializeField, Min(0f)] private float minimumHealthToTreatUnderFire = 20f;

		private MobileNPCInjurySystem injuries;
		private MobileNPCHumanoidVitals vitals;
		private MobileNPCInventory inventory;
		private MobileNPCBrainDriver brain;
		private float nextDecisionTime;
		private bool lastReportedSupplyNeed;
		[SerializeField, Min(0.5f)] private float treatmentDuration = 2.0f;
		private Item pendingItem;
		private bool pendingBandage;
		private float pendingHealAmount;
		private MobileNPCInjurySystem.BodyPart pendingLeg;
		private float treatmentFinishesAt;
		public bool IsTreating => pendingItem != null;

		/// <summary>Signals the scavenging/crafting layer that medical supplies are missing.</summary>
		public event Action<bool> MedicalSupplyNeedChanged;

		public bool NeedsMedicalSupplies
		{
			get
			{
				if (injuries == null || inventory == null || !injuries.NeedsMedicalAttention)
					return false;
				return (injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None
						&& !HasConfiguredItem(bandageItemIDs))
					|| (injuries.HasFracturedLeg && !HasConfiguredItem(splintItemIDs));
			}
		}

		private void Awake()
		{
			injuries = GetComponent<MobileNPCInjurySystem>();
			vitals = GetComponent<MobileNPCHumanoidVitals>();
			inventory = GetComponent<MobileNPCInventory>();
			brain = GetComponent<MobileNPCBrainDriver>();
		}

		private void OnDisable()
		{
			CancelTreatment();
		}

		private void CancelTreatment()
		{
			if (pendingItem == null)
				return;
			Item reserved = pendingItem;
			pendingItem = null;
			if (Provider.isServer && inventory != null
				&& !inventory.TryStoreTransferredItem(reserved))
				Debug.LogError("[MobileNPCMedicalBehavior] Cannot restore interrupted treatment item "
					+ reserved.id, this);
		}

		private void BeginTreatment(Item reserved, bool bandage,
			float healAmount, MobileNPCInjurySystem.BodyPart leg)
		{
			pendingItem = reserved;
			pendingBandage = bandage;
			pendingHealAmount = healAmount;
			pendingLeg = leg;
			treatmentFinishesAt = Time.time + Mathf.Max(0.5f, treatmentDuration);
			MobileNPCVisualAnimation visual = GetComponent<MobileNPCVisualAnimation>();
			if (visual != null)
				visual.PlayMedicalUseAnimation();
		}

		private void Update()
		{
			if (!Provider.isServer || injuries == null || vitals == null || inventory == null || vitals.IsDead)
				return;

			if (pendingItem != null)
			{
				bool unsafeCombat = brain != null && brain.Target != null
					&& brain.CurrentGoal != MobileNPCTactics.Goal.SeekCover
					&& brain.CurrentGoal != MobileNPCTactics.Goal.Retreat
					&& vitals.CurrentHealth > minimumHealthToTreatUnderFire;
				if (unsafeCombat)
				{
					CancelTreatment();
					return;
				}
				if (Time.time < treatmentFinishesAt)
					return;
				Item used = pendingItem;
				pendingItem = null;
				bool applied = pendingBandage
					? injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None
						&& injuries.ApplyBandageServer(pendingHealAmount)
					: injuries.ApplySplintServer(pendingLeg);
				if (!applied && !inventory.TryStoreTransferredItem(used))
					Debug.LogError("[MobileNPCMedicalBehavior] Could not return unused item " + used.id, this);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				Debug.Log("[MobileNPCMedicalBehavior] Finished treatment item=" + used.id
					+ " applied=" + applied + " HP=" + vitals.CurrentHealth.ToString("F1"), this);
#endif
				return;
			}

			bool needsSupplies = NeedsMedicalSupplies;
			if (needsSupplies != lastReportedSupplyNeed)
			{
				lastReportedSupplyNeed = needsSupplies;
				MedicalSupplyNeedChanged?.Invoke(needsSupplies);
			}

			if (Time.time < nextDecisionTime)
				return;
			nextDecisionTime = Time.time + Mathf.Max(0.2f, decisionInterval);

			MobileNPCMedicalUse genericCare = GetComponent<MobileNPCMedicalUse>();
			if (genericCare != null && genericCare.IsTreating)
				return;
			if (!injuries.NeedsMedicalAttention || !CanSafelyTreat())
				return;

			// An injured survivor with no wound dressing can salvage real carried
			// resources using safe native recipes. The inventory validates input
			// ownership and NEVER treats every garment as a free bandage.
			if (injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None
				&& !HasConfiguredItem(bandageItemIDs))
				inventory.TryCraftNativeWoundDressingServer();

			// Reserve one unit; effect is granted only after treatment ends,
			// as with the native player's Use/Aid sequence.
			if (injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None
				&& TryConsumeConfiguredItem(bandageItemIDs, out Item bandage))
			{
				BeginTreatment(bandage, true, GetNativeBandageHealAmount(bandage.id),
					MobileNPCInjurySystem.BodyPart.LeftLeg);
				return;
			}
			if (injuries.HasFracturedLeg && TryConsumeConfiguredItem(splintItemIDs, out Item splint))
			{
				MobileNPCInjurySystem.BodyPart leg = injuries.LeftLeg.IsFractured
					? MobileNPCInjurySystem.BodyPart.LeftLeg
					: MobileNPCInjurySystem.BodyPart.RightLeg;
				BeginTreatment(splint, false, 0f, leg);
			}
		}

		private bool CanSafelyTreat()
		{
			// During active combat, use cover/retreat decisions before stopping to treat.
			// Critical health is the exception: attempt treatment if a configured item exists.
			if (vitals.CurrentHealth <= minimumHealthToTreatUnderFire)
				return true;
			if (brain == null || brain.Target == null)
				return true;
			return brain.CurrentGoal == MobileNPCTactics.Goal.SeekCover
				|| brain.CurrentGoal == MobileNPCTactics.Goal.Retreat;
		}

		private bool HasConfiguredItem(ushort[] itemIDs)
		{
			if (itemIDs == null || itemIDs.Length == 0)
				return false;
			for (int slot = 0; slot < inventory.UsedSlots; ++slot)
			{
				if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
					continue;
				for (int i = 0; i < itemIDs.Length; ++i)
					if (itemIDs[i] != 0 && item.id == itemIDs[i])
						return true;
			}
			return false;
		}

		private static float GetNativeBandageHealAmount(ushort itemID)
		{
			// Match the approximate vanilla healing values (percent of 100-health NPC).
			switch (itemID)
			{
				case 393: return 5f;  // Rag
				case 95: return 15f;  // Bandage
				case 394: return 35f; // Dressing
				default: return 0f;
			}
		}

		private bool TryConsumeConfiguredItem(ushort[] itemIDs, out Item consumedItem)
		{
			consumedItem = null;
			if (!Provider.isServer || itemIDs == null || itemIDs.Length == 0)
				return false;
			for (int slot = 0; slot < inventory.UsedSlots; ++slot)
			{
				if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
					continue;
				bool match = false;
				for (int i = 0; i < itemIDs.Length; ++i)
				{
					if (itemIDs[i] != 0 && item.id == itemIDs[i])
					{
						match = true;
						break;
					}
				}
				if (match && inventory.TryConsumeOneItemByIdServer(item.id, out Item consumed))
				{
					consumedItem = consumed;
					return true;
				}
			}
			return false;
		}
	}
}