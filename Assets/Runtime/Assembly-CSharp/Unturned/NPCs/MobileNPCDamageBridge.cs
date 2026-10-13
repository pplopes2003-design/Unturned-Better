////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Small server-side adapter for validated hits against mobile humanoid NPC colliders.
	/// Native hit detection must call this after it has resolved and validated the hit.
	/// This class does not change the vanilla client hit-report/network protocol by itself.
	/// </summary>
	public static class MobileNPCDamageBridge
	{
		/// <summary>
		/// Applies one validated hit to the NPC owning the hit collider.
		/// Returns false when the collider is not part of a live mobile NPC or the hit is invalid.
		/// </summary>
		public static bool TryApplyValidatedHit(Collider hitCollider, float damage,
			Vector3 hitImpulse, Transform attacker)
		{
			if (!Provider.isServer || hitCollider == null)
				return false;

			MobileNPCHumanoidVitals vitals = hitCollider.GetComponent<MobileNPCHumanoidVitals>();
			if (vitals == null)
				vitals = hitCollider.GetComponentInParent<MobileNPCHumanoidVitals>();

			if (vitals == null || vitals.IsDead)
				return false;

			return vitals.ApplyDamage(damage, hitImpulse, attacker);
		}

		/// <summary>
		/// Applies one validated hit with explicit body-region injury metadata.
		/// The caller must classify the body region from verified hit data, not guess it.
		/// </summary>
		public static bool TryApplyValidatedHitToPart(Collider hitCollider, float damage,
			MobileNPCInjurySystem.BodyPart bodyPart, bool causeBleeding, bool causeFracture,
			MobileNPCInjurySystem.BleedingSeverity severity, Vector3 hitImpulse, Transform attacker)
		{
			if (!Provider.isServer || hitCollider == null)
				return false;

			MobileNPCHumanoidVitals vitals = hitCollider.GetComponent<MobileNPCHumanoidVitals>();
			if (vitals == null)
				vitals = hitCollider.GetComponentInParent<MobileNPCHumanoidVitals>();

			if (vitals == null || vitals.IsDead)
				return false;

			// Injury metadata and health loss are committed once, with the attacker preserved.
			return vitals.ApplyDamageToPart(damage, bodyPart, causeBleeding, causeFracture,
				severity, hitImpulse, attacker);
		}
	}
}
