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
		[SerializeField] private ushort[] bandageItemIDs = new ushort[0];
		[SerializeField] private ushort[] splintItemIDs = new ushort[0];

		[Header("Treatment decisions")]
		[SerializeField, Min(0.2f)] private float decisionInterval = 1f;
		[SerializeField, Min(0f)] private float minimumHealthToTreatUnderFire = 20f;
		[SerializeField, Min(0f)] private float bandageHealAmount = 8f;

		private MobileNPCInjurySystem injuries;
		private MobileNPCHumanoidVitals vitals;
		private MobileNPCInventory inventory;
		private MobileNPCBrainDriver brain;
		private float nextDecisionTime;
		private bool lastReportedSupplyNeed;

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

		private void Update()
		{
			if (!Provider.isServer || injuries == null || vitals == null || inventory == null || vitals.IsDead)
				return;

			bool needsSupplies = NeedsMedicalSupplies;
			if (needsSupplies != lastReportedSupplyNeed)
			{
				lastReportedSupplyNeed = needsSupplies;
				MedicalSupplyNeedChanged?.Invoke(needsSupplies);
			}

			if (Time.time < nextDecisionTime)
				return;
			nextDecisionTime = Time.time + Mathf.Max(0.2f, decisionInterval);

			if (!injuries.NeedsMedicalAttention || !CanSafelyTreat())
				return;

			// Stop ongoing blood loss first. Do not consume a random item of the
			// generic MEDICAL category: the configured ID must represent a bandage.
			if (injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None
				&& TryConsumeConfiguredItem(bandageItemIDs))
			{
				injuries.ApplyBandageServer(bandageHealAmount);
				return;
			}

			// A splint treats a fracture but intentionally does not refill limb condition.
			if (injuries.HasFracturedLeg && TryConsumeConfiguredItem(splintItemIDs))
			{
				MobileNPCInjurySystem.BodyPart leg = injuries.LeftLeg.IsFractured
					? MobileNPCInjurySystem.BodyPart.LeftLeg
					: MobileNPCInjurySystem.BodyPart.RightLeg;
				injuries.ApplySplintServer(leg);
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

		private bool TryConsumeConfiguredItem(ushort[] itemIDs)
		{
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
				if (match && inventory.TryTakeItemServer(slot, out _))
					return true;
			}
			return false;
		}
	}
}