////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;
using SDG.Unturned;

namespace Unturned2.Line2
{
	/// <summary>
	/// Small server-side adapter for validated hits against mobile humanoid NPC colliders.
	/// Native hit detection must call this after it has resolved and validated the hit.
	/// This class does not change the vanilla client hit-report/network protocol by itself.
	/// </summary>
	public static class Line2MobileNPCDamageBridge
	{
		/// <summary>
		/// Applies one validated hit to the NPC owning the hit collider.
		/// Returns false when the collider is not part of a live mobile NPC or the hit is invalid.
		/// </summary>
        /// <summary>
        /// Recover a custom NPC hit volume on the authoritative listen-server.
        /// Vanilla InputInfo serialization may omit its colliderTransform when
        /// the ray type is NONE (MobileNPC is neither Player nor Zombie).
        /// NEVER trust only the client hitmarker: require the server's own
        /// first unobstructed physics ray to hit an active NPC anatomical
        /// collider close to the reported point. World geometry uses the
        /// native DAMAGE_SERVER geometry plus CLIP/VEHICLE blockers and
        /// the NPC ENTITY layer.
        /// </summary>
        public static bool TryResolvePhysicalServerHit(Vector3 origin,
            Vector3 reportedPoint, float maximumRange, out Transform hitTransform)
        {
            float unused;
            return TryResolvePhysicalServerHit(origin, reportedPoint,
                maximumRange, out hitTransform, out unused);
        }

        public static bool TryResolvePhysicalServerHit(Vector3 origin,
            Vector3 reportedPoint, float maximumRange, out Transform hitTransform,
            out float physicalHitDistance)
        {
            hitTransform = null;
            physicalHitDistance = 0f;
            if (!Provider.isServer || !Level.isLoaded || maximumRange <= 0f)
                return false;
            Vector3 difference = reportedPoint - origin;
            float length = difference.magnitude;
            if (length < 0.05f || length > maximumRange + 1.0f
                || float.IsNaN(length) || float.IsInfinity(length))
                return false;

            // Players/zombies and physical clip/vehicle volumes must
            // block fallback player bullets too, not just map geometry.
            int mask = RayMasks.DAMAGE_SERVER | RayMasks.BLOCK_COLLISION
                | RayMasks.ENTITY | RayMasks.PLAYER | RayMasks.ENEMY;
            // The native NPC locomotion CharacterController (AGENT layer)
            // can be closer than its anatomical trigger volumes. A single
            // Physics.Raycast then rejects every firearm hit even though the
            // six original bone colliders are correctly installed.
            // Sort all contacts and skip ONLY this same NPC's root motor;
            // never skip map obstacles, other NPCs, players or vehicles.
            RaycastHit[] contacts = Physics.RaycastAll(
                origin, difference / length,
                Mathf.Min(length + 0.75f, maximumRange + 1f),
                mask, QueryTriggerInteraction.Collide);
            if (contacts == null || contacts.Length == 0)
                return false;
            System.Array.Sort(contacts, (left, right) =>
                left.distance.CompareTo(right.distance));
            PrototypeNpcVitals firstNpc = null;
            for (int i = 0; i < contacts.Length; ++i)
            {
                RaycastHit contact = contacts[i];
                Collider candidate = contact.collider;
                if (candidate == null || contact.distance > length + 0.75f)
                    return false;
                PrototypeNpcVitals owner =
                    candidate.GetComponentInParent<PrototypeNpcVitals>();
                if (owner == null || owner.IsDead)
                    return false; // a real obstruction, never shoot through it
                if (firstNpc != null && firstNpc != owner)
                    return false; // another actor cannot be transparent
                if (firstNpc == null)
                    firstNpc = owner;
                if (candidate.GetComponent<Line2MobileNPCBodyHitbox>() != null)
                {
                    physicalHitDistance = contact.distance;
                    hitTransform = candidate.transform;
                    return true;
                }
                // Only the actor's own movement collider is transparent
                // to the bone-volume search. Other arbitrary unmarked
                // colliders are physical obstructions.
                if (!(candidate is CharacterController) ||
                    candidate.gameObject != owner.gameObject)
                    return false;
            }
            return false;
        }

		public static bool TryApplyValidatedHit(Collider hitCollider, float damage,
			Vector3 hitImpulse, Transform attacker)
		{
			if (!Provider.isServer || hitCollider == null)
				return false;

			PrototypeNpcVitals vitals = hitCollider.GetComponent<PrototypeNpcVitals>();
			if (vitals == null)
				vitals = hitCollider.GetComponentInParent<PrototypeNpcVitals>();

			if (vitals == null || vitals.IsDead)
				return false;

			return vitals.ApplyDamage(damage, hitImpulse, attacker);
		}

		/// <summary>
		/// Applies one validated hit with explicit body-region injury metadata.
		/// The caller must classify the body region from verified hit data, not guess it.
		/// </summary>
		public static bool TryApplyValidatedHitToPart(Collider hitCollider, float damage,
			Line2MobileNPCInjurySystem.BodyPart bodyPart, bool causeBleeding, bool causeFracture,
			Line2MobileNPCInjurySystem.BleedingSeverity severity, Vector3 hitImpulse, Transform attacker)
		{
			if (!Provider.isServer || hitCollider == null)
				return false;

			PrototypeNpcVitals vitals = hitCollider.GetComponent<PrototypeNpcVitals>();
			if (vitals == null)
				vitals = hitCollider.GetComponentInParent<PrototypeNpcVitals>();

			if (vitals == null || vitals.IsDead)
				return false;

			// Injury metadata and health loss are committed once, with the attacker preserved.
			return vitals.ApplyDamageToPart(damage, bodyPart, causeBleeding, causeFracture,
				severity, hitImpulse, attacker);
		}
	}
}
