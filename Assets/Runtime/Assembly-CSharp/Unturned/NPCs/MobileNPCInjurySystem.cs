////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-authoritative injury state for mobile humanoids. Damage adapters should
	/// call ApplyHit with the body region identified by the hit collider; this component
	/// does not guess hit locations from the NPC's world position.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCInjurySystem : MonoBehaviour
	{
		public enum BodyPart
		{
			Head,
			Torso,
			LeftArm,
			RightArm,
			LeftLeg,
			RightLeg
		}

		public enum BleedingSeverity
		{
			None,
			Light,
			Heavy
		}

		[Serializable]
		public sealed class PartState
		{
			[SerializeField] private float condition = 100f;
			[SerializeField] private bool fractured;
			public float Condition => condition;
			public bool IsFractured => fractured;

			internal void Damage(float amount)
			{
				condition = Mathf.Clamp(condition - Mathf.Max(0f, amount), 0f, 100f);
			}

			internal void Fracture()
			{
				fractured = true;
			}

			internal void TreatFracture()
			{
				fractured = false;
			}
		}

		[SerializeField, Min(0.1f)] private float lightBleedDamagePerSecond = 0.35f;
		[SerializeField, Min(0.1f)] private float heavyBleedDamagePerSecond = 1.1f;
		[SerializeField, Min(0.1f)] private float bleedTickInterval = 1f;
		[SerializeField, Min(0f)] private float fracturedLegMovementMultiplier = 0.45f;
		[SerializeField, Min(0f)] private float injuredLegMovementMultiplier = 0.75f;

		[SerializeField] private PartState head = new PartState();
		[SerializeField] private PartState torso = new PartState();
		[SerializeField] private PartState leftArm = new PartState();
		[SerializeField] private PartState rightArm = new PartState();
		[SerializeField] private PartState leftLeg = new PartState();
		[SerializeField] private PartState rightLeg = new PartState();

		private MobileNPCHumanoidVitals vitals;
		private float nextBleedTick;
		private BleedingSeverity bleeding;
		private BodyPart bleedingPart;

		public event Action<BleedingSeverity, BodyPart> BleedingChanged;
		public event Action<BodyPart, float, bool> PartConditionChanged;

		public BleedingSeverity CurrentBleeding => bleeding;
		public BodyPart BleedingPart => bleedingPart;
		public PartState Head => head;
		public PartState Torso => torso;
		public PartState LeftArm => leftArm;
		public PartState RightArm => rightArm;
		public PartState LeftLeg => leftLeg;
		public PartState RightLeg => rightLeg;
		public bool HasFracturedLeg => leftLeg.IsFractured || rightLeg.IsFractured;
		public bool NeedsMedicalAttention => bleeding != BleedingSeverity.None || HasFracturedLeg
			|| head.Condition < 60f || torso.Condition < 65f
			|| leftArm.Condition < 45f || rightArm.Condition < 45f
			|| leftLeg.Condition < 55f || rightLeg.Condition < 55f;

		/// <summary>Applied by the locomotion adapter so a leg injury changes actual movement.</summary>
		public float MovementMultiplier
		{
			get
			{
				float multiplier = 1f;
				if (leftLeg.IsFractured || rightLeg.IsFractured)
					multiplier = Mathf.Min(multiplier, fracturedLegMovementMultiplier);
				else if (leftLeg.Condition < 55f || rightLeg.Condition < 55f)
					multiplier = Mathf.Min(multiplier, injuredLegMovementMultiplier);
				return multiplier;
			}
		}

		private void Awake()
		{
			vitals = GetComponent<MobileNPCHumanoidVitals>();
			if (vitals == null)
				Debug.LogError("[MobileNPCInjurySystem] Missing MobileNPCHumanoidVitals on " + name, this);
		}

		private void Update()
		{
			if (!Provider.isServer || bleeding == BleedingSeverity.None || vitals == null || vitals.IsDead)
				return;

			if (Time.time < nextBleedTick)
				return;

			nextBleedTick = Time.time + Mathf.Max(0.1f, bleedTickInterval);
			float rate = bleeding == BleedingSeverity.Heavy ? heavyBleedDamagePerSecond : lightBleedDamagePerSecond;
			vitals.ApplyDamage(rate * Mathf.Max(0.1f, bleedTickInterval));
		}

		/// <summary>
		/// Applies a validated hit to a known body part. Injury chance/severity belongs
		/// to the weapon/damage adapter, which supplies bleeding and fracture decisions.
		/// </summary>
		public bool ApplyHit(BodyPart part, float damage, bool causeBleeding, bool causeFracture,
			BleedingSeverity severity = BleedingSeverity.Light)
		{
			if (!Provider.isServer || vitals == null || vitals.IsDead || !IsValidPart(part)
				|| float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
				return false;

			if (!ApplyInjuryEffectsServer(part, damage, causeBleeding, causeFracture, severity))
				return false;
			vitals.ApplyDamage(damage);
			return true;
		}

		/// <summary>
		/// Records body-region effects without applying health damage. Use this when the
		/// damage adapter has already applied health once through MobileNPCHumanoidVitals.
		/// </summary>
		public bool ApplyInjuryEffectsServer(BodyPart part, float damage, bool causeBleeding, bool causeFracture,
			BleedingSeverity severity = BleedingSeverity.Light)
		{
			if (!Provider.isServer || vitals == null || vitals.IsDead || !IsValidPart(part)
				|| float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
				return false;

			PartState state = GetPartState(part);
			state.Damage(damage);
			if (causeFracture && (part == BodyPart.LeftLeg || part == BodyPart.RightLeg))
				state.Fracture();
			if (causeBleeding)
				SetBleedingServer(severity == BleedingSeverity.None ? BleedingSeverity.Light : severity, part);
			PartConditionChanged?.Invoke(part, state.Condition, state.IsFractured);
			return true;
		}

		public void SetBleedingServer(BleedingSeverity severity, BodyPart part)
		{
			if (!Provider.isServer || !IsValidPart(part))
				return;
			if (severity == BleedingSeverity.None)
			{
				StopBleedingServer();
				return;
			}
			// Keep the most severe active bleed; a new wound may replace the source
			// region but cannot downgrade an already-heavy bleed.
			if ((int)severity >= (int)bleeding)
			{
				bleeding = severity;
				bleedingPart = part;
				nextBleedTick = Time.time + Mathf.Max(0.1f, bleedTickInterval);
				BleedingChanged?.Invoke(bleeding, bleedingPart);
			}
		}

		public void StopBleedingServer()
		{
			if (!Provider.isServer || bleeding == BleedingSeverity.None)
				return;
			bleeding = BleedingSeverity.None;
			BleedingChanged?.Invoke(bleeding, bleedingPart);
		}

		/// <summary>Bandage action: stops bleeding and restores a modest amount of health.</summary>
		public bool ApplyBandageServer(float healAmount)
		{
			if (!Provider.isServer || vitals == null || vitals.IsDead)
				return false;
			bool treated = bleeding != BleedingSeverity.None;
			StopBleedingServer();
			if (healAmount > 0f)
				treated |= vitals.Heal(healAmount);
			return treated;
		}

		/// <summary>A splint treats one leg fracture; it does not instantly restore tissue condition.</summary>
		public bool ApplySplintServer(BodyPart leg)
		{
			if (!Provider.isServer || (leg != BodyPart.LeftLeg && leg != BodyPart.RightLeg))
				return false;
			PartState state = GetPartState(leg);
			if (!state.IsFractured)
				return false;
			state.TreatFracture();
			PartConditionChanged?.Invoke(leg, state.Condition, state.IsFractured);
			return true;
		}

		private PartState GetPartState(BodyPart part)
		{
			switch (part)
			{
				case BodyPart.Head: return head;
				case BodyPart.Torso: return torso;
				case BodyPart.LeftArm: return leftArm;
				case BodyPart.RightArm: return rightArm;
				case BodyPart.LeftLeg: return leftLeg;
				default: return rightLeg;
			}
		}

		private static bool IsValidPart(BodyPart part)
		{
			return (int)part >= (int)BodyPart.Head && (int)part <= (int)BodyPart.RightLeg;
		}
	}
}