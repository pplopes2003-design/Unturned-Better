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
		private float hunger = 100f;
		private float thirst = 100f;
		private float nextThinkTime;
		private float nextDamageTime;

		public float Hunger => hunger;
		public float Thirst => thirst;
		public float HungerFraction => hunger / 100f;
		public float ThirstFraction => thirst / 100f;
		public bool NeedsFood => hunger <= 65f;
		public bool NeedsWater => thirst <= 65f;

		private void Awake()
		{
			vitals = GetComponent<MobileNPCHumanoidVitals>();
			inventory = GetComponent<MobileNPCInventory>();
		}

		private void Update()
		{
			if (!Provider.isServer || !Level.isLoaded || vitals == null || inventory == null || vitals.IsDead)
				return;

			float elapsedMinutes = Time.deltaTime / 60f;
			hunger = Mathf.Clamp(hunger - hungerDrainPerMinute * elapsedMinutes, 0f, 100f);
			thirst = Mathf.Clamp(thirst - thirstDrainPerMinute * elapsedMinutes, 0f, 100f);

			if (Time.time < nextThinkTime)
				return;
			nextThinkTime = Time.time + Mathf.Max(0.5f, thinkInterval);

			MobileNPCBrainDriver driver = GetComponent<MobileNPCBrainDriver>();
			// Combat and urgent tactical movement take precedence over stopping to eat.
			if (driver != null && (driver.Target != null || driver.CurrentGoal != MobileNPCTactics.Goal.None))
				return;

			if (hunger <= 65f && inventory.TryConsumeFirstItemOfType(EItemType.FOOD, out _))
				hunger = Mathf.Min(100f, hunger + Mathf.Max(0f, foodRestore));

			if (thirst <= 65f && inventory.TryConsumeFirstItemOfType(EItemType.WATER, out _))
				thirst = Mathf.Min(100f, thirst + Mathf.Max(0f, waterRestore));

			if ((hunger <= 0f || thirst <= 0f) && Time.time >= nextDamageTime)
			{
				nextDamageTime = Time.time + Mathf.Max(1f, damageInterval);
				vitals.ApplyDamage(Mathf.Max(0.1f, starvationDamage));
			}
		}

		/// <summary>Used by loot scoring to prioritize food or water when the agent is running low.</summary>
		public float GetNeedPriority(EItemType type)
		{
			if (type == EItemType.FOOD)
				return NeedsFood ? 60f + (1f - HungerFraction) * 35f : 20f;
			if (type == EItemType.WATER)
				return NeedsWater ? 65f + (1f - ThirstFraction) * 35f : 20f;
			return 0f;
		}
	}
}