////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Lightweight server-authoritative hunger and thirst loop for mobile NPCs.
	/// Food and water-category items are consumed from the NPC's simulated inventory.
	/// Without supplies, needs eventually cause damage; this is intentionally a simple
	/// survival model, not a simulation of every vanilla consumable effect.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCHumanoidVitals))]
	[RequireComponent(typeof(MobileNPCInventory))]
	public sealed class MobileNPCSurvivalNeeds : MonoBehaviour
	{
		[SerializeField, Range(0.01f, 5f)] private float hungerDrainPerMinute = 0.8f;
		[SerializeField, Range(0.01f, 5f)] private float thirstDrainPerMinute = 1.2f;
		[SerializeField, Range(0f, 100f)] private float foodRestore = 55f;
		[SerializeField, Range(0f, 100f)] private float waterRestore = 60f;
		[SerializeField, Range(0f, 100f)] private float starvationDamage = 4f;
		[SerializeField, Min(1f)] private float damageInterval = 12f;
		[SerializeField, Min(0.5f)] private float thinkInterval = 2f;

		private MobileNPCHumanoidVitals vitals;
		private MobileNPCInventory inventory;
		[System.Serializable]
		public sealed class NeedsSnapshot
		{
			public float hunger;
			public float thirst;
		}

		/// <summary>Only the living individual's actual physiological state,
		/// never hidden items, food sources or fabricated healing.</summary>
		public NeedsSnapshot ExportSnapshotServer()
		{
			if (!Provider.isServer || vitals == null || vitals.IsDead)
				return null;
			return new NeedsSnapshot { hunger = hunger, thirst = thirst };
		}

		public void RestoreSnapshotServer(NeedsSnapshot snapshot)
		{
			if (!Provider.isServer || snapshot == null || vitals == null || vitals.IsDead
				|| float.IsNaN(snapshot.hunger) || float.IsInfinity(snapshot.hunger)
				|| float.IsNaN(snapshot.thirst) || float.IsInfinity(snapshot.thirst))
				return;
			hunger = Mathf.Clamp(snapshot.hunger, 0f, 100f);
			thirst = Mathf.Clamp(snapshot.thirst, 0f, 100f);
			// Let the existing SurvivalNeeds service decide whether real
			// carried food/water can be consumed after restore. Never grant any.
			nextThinkTime = Time.time + Random.Range(0.5f, 1.5f);
			nextDamageTime = Time.time + Mathf.Max(1f, damageInterval);
		}

		private float hunger = 100f;
		private float thirst = 100f;
		private float nextThinkTime;
		private float nextDamageTime;

		public float Hunger => hunger;
		public float Thirst => thirst;
		// Combat mode suspends the physiological timer, it does not overwrite
		// the NPC's previous hunger/thirst if the player later returns to Normal.
		public float HungerFraction => MobileNPCSettings.CombatMode ? 1f : hunger / 100f;
		public float ThirstFraction => MobileNPCSettings.CombatMode ? 1f : thirst / 100f;
		public bool NeedsFood => !MobileNPCSettings.CombatMode && hunger <= 65f;
		public bool NeedsWater => !MobileNPCSettings.CombatMode && thirst <= 65f;

		private void Awake()
		{
			vitals = GetComponent<MobileNPCHumanoidVitals>();
			inventory = GetComponent<MobileNPCInventory>();
		}

		private void Update()
		{
			if (!Provider.isServer || !Level.isLoaded || vitals == null || inventory == null || vitals.IsDead)
				return;
			if (MobileNPCSettings.CombatMode)
			{
				// No hunger/thirst damage or food consumption. Other vitals,
				// bleeding, bullets and medical behavior remain real.
				nextDamageTime = Time.time + Mathf.Max(1f, damageInterval);
				nextThinkTime = Time.time + Mathf.Max(0.5f, thinkInterval);
				return;
			}

			float elapsedMinutes = Time.deltaTime / 60f;
			hunger = Mathf.Clamp(hunger - hungerDrainPerMinute * elapsedMinutes, 0f, 100f);
			thirst = Mathf.Clamp(thirst - thirstDrainPerMinute * elapsedMinutes, 0f, 100f);

			if (Time.time < nextThinkTime)
				return;
			nextThinkTime = Time.time + Mathf.Max(0.5f, thinkInterval);

			// Starvation damage must never be skipped just because the NPC has an active goal.
			if ((hunger <= 0f || thirst <= 0f) && Time.time >= nextDamageTime)
			{
				nextDamageTime = Time.time + Mathf.Max(1f, damageInterval);
				vitals.ApplyDamage(Mathf.Max(0.1f, starvationDamage));
			}

			MobileNPCBrainDriver driver = GetComponent<MobileNPCBrainDriver>();
			// Only defer eating during active combat; exploration/looting goals should not
			// prevent an NPC from consuming supplies it already carries.
			if (driver != null && driver.Target != null
				&& driver.CurrentState == MobileNPCBrain.State.Pursuing)
				return;

			if (hunger <= 65f && inventory.TryConsumeFirstItemOfType(EItemType.FOOD, out _))
				hunger = Mathf.Min(100f, hunger + Mathf.Max(0f, foodRestore));

			if (thirst <= 65f && inventory.TryConsumeFirstItemOfType(EItemType.WATER, out _))
				thirst = Mathf.Min(100f, thirst + Mathf.Max(0f, waterRestore));
		}

		/// <summary>Used by loot scoring to prioritize food or water when the agent is running low.</summary>
		public float GetNeedPriority(EItemType type)
		{
			if (MobileNPCSettings.CombatMode) return 0f;
			if (type == EItemType.FOOD)
				return NeedsFood ? 60f + (1f - HungerFraction) * 35f : 20f;
			if (type == EItemType.WATER)
				return NeedsWater ? 65f + (1f - ThirstFraction) * 35f : 20f;
			return 0f;
		}
	}
}