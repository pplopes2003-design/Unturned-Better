////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Tactical movement support for armed mobile NPCs: hold a useful firing distance,
	/// seek cover during reloads, and attempt a lateral flank when the target stays occluded.
	/// All decisions and destination changes are server-authoritative.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	[RequireComponent(typeof(MobileNPCHumanoidPerception))]
	[RequireComponent(typeof(MobileNPCHumanoidRangedCombat))]
	public sealed class MobileNPCTacticalCombatBehavior : MonoBehaviour
	{
		[SerializeField, Min(2f)] private float preferredMinimumRange = 7f;
		[SerializeField, Min(3f)] private float preferredMaximumRange = 24f;
		[SerializeField, Min(0.2f)] private float blockedLineOfFireDelay = 1.6f;
		[SerializeField, Min(1f)] private float flankRadius = 8f;
		[SerializeField, Min(0.5f)] private float flankRetryCooldown = 5f;
		[SerializeField, Min(0.1f)] private float tacticalUpdateInterval = 0.35f;
		[SerializeField, Min(0.1f)] private float eyeHeight = 1.45f;
		[SerializeField, Min(0.1f)] private float targetHeight = 1.0f;
		[SerializeField] private LayerMask lineOfFireMask = ~0;

		private MobileNPCBrainDriver brain;
		private MobileNPCHumanoidPerception perception;
		private MobileNPCHumanoidRangedCombat rangedCombat;
		private MobileNPCHumanoidVitals vitals;
		private MobileNPCInventory inventory;
		private MobileNPCHumanoidMotor motor;
		private float nextUpdateTime;
		private float blockedSince = -1f;
		private float nextFlankTime;
		private Transform subscribedTarget;
		private readonly Vector3[] candidateOffsets = new Vector3[8];

		private void Awake()
		{
			brain = GetComponent<MobileNPCBrainDriver>();
			perception = GetComponent<MobileNPCHumanoidPerception>();
			rangedCombat = GetComponent<MobileNPCHumanoidRangedCombat>();
			vitals = GetComponent<MobileNPCHumanoidVitals>();
			inventory = GetComponent<MobileNPCInventory>();
			motor = GetComponent<MobileNPCHumanoidMotor>();

			candidateOffsets[0] = new Vector3(1f, 0f, 0f);
			candidateOffsets[1] = new Vector3(-1f, 0f, 0f);
			candidateOffsets[2] = new Vector3(0.7071f, 0f, 0.7071f);
			candidateOffsets[3] = new Vector3(-0.7071f, 0f, 0.7071f);
			candidateOffsets[4] = new Vector3(0.7071f, 0f, -0.7071f);
			candidateOffsets[5] = new Vector3(-0.7071f, 0f, -0.7071f);
			candidateOffsets[6] = new Vector3(0f, 0f, 1f);
			candidateOffsets[7] = new Vector3(0f, 0f, -1f);
		}

		private void OnEnable()
		{
			if (rangedCombat == null)
				rangedCombat = GetComponent<MobileNPCHumanoidRangedCombat>();
			if (rangedCombat != null)
			{
				rangedCombat.ReloadStarted += HandleReloadStarted;
				rangedCombat.ReloadFinished += HandleReloadFinished;
			}
		}

		private void OnDisable()
		{
			if (rangedCombat != null)
			{
				rangedCombat.ReloadStarted -= HandleReloadStarted;
				rangedCombat.ReloadFinished -= HandleReloadFinished;
			}
			subscribedTarget = null;
		}

		private void Update()
		{
			if (!Provider.isServer || brain == null || perception == null || rangedCombat == null
				|| Time.time < nextUpdateTime || brain.CurrentState != MobileNPCBrain.State.Pursuing)
				return;

			nextUpdateTime = Time.time + Mathf.Max(0.1f, tacticalUpdateInterval);
			Transform target = brain.Target;
			if (target == null)
			{
				ResetOcclusion();
				return;
			}

			if (subscribedTarget != target)
			{
				subscribedTarget = target;
				ResetOcclusion();
			}

			// Never plan a flank from the live transform of an occluded target.
			// The intelligence/memory layer owns investigation after contact is lost.
			Player observedPlayer = target.GetComponent<Player>();
			if (observedPlayer == null)
				observedPlayer = target.GetComponentInParent<Player>();
			MobileNPCHumanoidDisposition observedNPC = target.GetComponent<MobileNPCHumanoidDisposition>();
			if (observedNPC == null)
				observedNPC = target.GetComponentInParent<MobileNPCHumanoidDisposition>();
			Zombie observedZombie = target.GetComponent<Zombie>();
			if (observedZombie == null)
				observedZombie = target.GetComponentInParent<Zombie>();
			bool perceptionConfirmed = observedPlayer != null && perception.VisiblePlayer == observedPlayer
				|| observedNPC != null && perception.VisibleNPC == observedNPC
				|| observedZombie != null && perception.VisibleZombie == observedZombie;
			if (!perceptionConfirmed)
			{
				ResetOcclusion();
				return;
			}

			// Reloading takes priority over flank and spacing. ReportIncomingFire uses the
			// existing server-side cover validation and does not apply any extra damage.
			if (rangedCombat.IsReloading)
				return;

			Vector3 targetPoint = target.position + Vector3.up * targetHeight;
			Vector3 origin = transform.position + Vector3.up * eyeHeight;
			Vector3 toTarget = targetPoint - origin;
			float distance = toTarget.magnitude;
			bool visibleNow = IsClearLineOfFire(origin, targetPoint, target);
			bool targetConfirmed = IsCurrentTargetConfirmed(target);

			if (!targetConfirmed)
			{
				ResetOcclusion();
				return;
			}

			if (visibleNow)
			{
				blockedSince = -1f;

				// If too close, move laterally to a firing position instead of pursuing
				// directly into the player's collision capsule. Do not approach if already
				// inside effective range; the ranged weapon can fire from this position.
				if (distance < preferredMinimumRange && Time.time >= nextFlankTime)
					TryChooseFlank(target, true);
				return;
			}

			if (blockedSince < 0f)
				blockedSince = Time.time;

			if (Time.time - blockedSince >= blockedLineOfFireDelay && Time.time >= nextFlankTime)
				TryChooseFlank(target, false);
		}

		private bool IsCurrentTargetConfirmed(Transform target)
		{
			Player player = target.GetComponent<Player>();
			if (player == null) player = target.GetComponentInParent<Player>();
			if (player != null)
				return player.life != null && !player.life.isDead;

			MobileNPCHumanoidDisposition npc = target.GetComponent<MobileNPCHumanoidDisposition>();
			if (npc == null) npc = target.GetComponentInParent<MobileNPCHumanoidDisposition>();
			if (npc != null)
			{
				MobileNPCHumanoidVitals targetVitals = npc.GetComponent<MobileNPCHumanoidVitals>();
				return targetVitals != null && !targetVitals.IsDead
					&& MobileNPCHumanoidDisposition.AreHostile(GetComponent<MobileNPCHumanoidDisposition>(), npc);
			}

			Zombie zombie = target.GetComponent<Zombie>();
			if (zombie == null) zombie = target.GetComponentInParent<Zombie>();
			return zombie != null && !zombie.isDead;
		}

		private bool IsClearLineOfFire(Vector3 origin, Vector3 targetPoint, Transform target)
		{
			Vector3 offset = targetPoint - origin;
			if (offset.sqrMagnitude < 0.01f)
				return true;

			// Ignore our own hitbox and resolve the first external obstacle.
			// A single Physics.Raycast can otherwise misclassify our own collider
			// as blocked line of fire and force endless flanking.
			RaycastHit[] hits = Physics.RaycastAll(origin, offset.normalized, offset.magnitude,
				lineOfFireMask, QueryTriggerInteraction.Ignore);
			float nearestDistance = float.PositiveInfinity;
			Transform nearest = null;
			for (int i = 0; i < hits.Length; ++i)
			{
				Transform hitTransform = hits[i].transform;
				if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
					continue;
				if (hits[i].distance < nearestDistance)
				{
					nearestDistance = hits[i].distance;
					nearest = hitTransform;
				}
			}
			if (nearest == null)
				return true;
			return nearest == target || target.IsChildOf(nearest) || nearest.IsChildOf(target);
		}

		private void TryChooseFlank(Transform target, bool targetVisible)
		{
			if (brain == null || target == null || brain.CurrentGoal == MobileNPCTactics.Goal.Retreat
				|| brain.CurrentGoal == MobileNPCTactics.Goal.SeekCover)
				return;

			Vector3 targetPosition = target.position;
			Vector3 awayFromTarget = transform.position - targetPosition;
			awayFromTarget.y = 0f;
			if (awayFromTarget.sqrMagnitude < 0.01f)
				awayFromTarget = -transform.forward;
			awayFromTarget.Normalize();

			Vector3 lateral = Vector3.Cross(Vector3.up, awayFromTarget).normalized;
			// Prefer a lateral reposition first; then test diagonal positions around the target.
			Vector3[] directions =
			{
				lateral, -lateral,
				(awayFromTarget + lateral).normalized,
				(awayFromTarget - lateral).normalized,
				(-awayFromTarget + lateral).normalized,
				(-awayFromTarget - lateral).normalized
			};

			float bestScore = float.NegativeInfinity;
			Vector3 bestPosition = Vector3.zero;
			bool found = false;
			for (int i = 0; i < directions.Length; ++i)
			{
				Vector3 raw = targetPosition + directions[i] * flankRadius;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(raw, 5f, 4f, ~0, 38f,
					out Vector3 candidate, out Collider groundCollider, out _)
					|| (groundCollider != null
						&& (groundCollider.GetComponentInParent<InteractableStorage>() != null
							|| groundCollider.GetComponentInParent<InteractableBed>() != null
							|| groundCollider.GetComponentInParent<InteractableMannequin>() != null)))
					continue;

				if ((candidate - transform.position).sqrMagnitude < 9f)
					continue;

				Vector3 candidateOrigin = candidate + Vector3.up * eyeHeight;
				Vector3 candidateTarget = targetPosition + Vector3.up * targetHeight;
				if (!IsClearLineOfFire(candidateOrigin, candidateTarget, target))
					continue;

				float candidateDistance = Vector3.Distance(candidate, targetPosition);
				if (candidateDistance < preferredMinimumRange || candidateDistance > preferredMaximumRange)
					continue;

				// Favor side/rear displacement and avoid a candidate that is almost the same
				// direction from the target as the NPC's current position.
				Vector3 candidateRadial = candidate - targetPosition;
				candidateRadial.y = 0f;
				candidateRadial.Normalize();
				float angularChange = 1f - Mathf.Clamp(Vector3.Dot(awayFromTarget, candidateRadial), -1f, 1f);
				float score = angularChange * 5f - Mathf.Abs(candidateDistance - 14f) * 0.15f;
				if (score > bestScore)
				{
					bestScore = score;
					bestPosition = candidate;
					found = true;
				}
			}

			nextFlankTime = Time.time + Mathf.Max(0.5f, flankRetryCooldown);
			if (found)
			{
				brain.TryStartFlank(bestPosition);
				ResetOcclusion();
				return;
			}

			// Exceptional high ground is ONLY considered when all ordinary
			// lateral flanks are unavailable. The threat must remain
			// directly confirmed by current perception in Update, and the
			// actual firing line must improve. No surprise roof searches,
			// omniscient target positions, or needless exposed jumping.
			if (!targetVisible
				&& brain.CurrentGoal == MobileNPCTactics.Goal.None
				&& rangedCombat.SelectedGunAsset != null
				&& rangedCombat.RoundsRemaining > 0 && !rangedCombat.IsReloading
				&& vitals != null && vitals.HealthFraction >= 0.70f
				&& motor != null
				&& motor.TryFindObservedElevatedFiringPositionServer(
					targetPosition, out Vector3 observedRaised)
				&& IsClearLineOfFire(observedRaised + Vector3.up * eyeHeight,
					targetPosition + Vector3.up * targetHeight, target)
				&& brain.TryStartObservedElevatedFlankServer(observedRaised))
			{
				ResetOcclusion();
				return;
			}
			if (!targetVisible)
			{
				// No physically validated better firing location exists;
				// preserve ordinary target memory and path recovery.
				blockedSince = Time.time;
			}
		}

		private void HandleReloadStarted()
		{
			if (!Provider.isServer || brain == null || perception == null || brain.Target == null)
				return;

			// Reload protection may use a source position only when the target is
			// *currently* visible to this NPC. The old implementation queried the
			// live hidden Transform and effectively granted X-ray cover planning.
			Transform target = brain.Target;
			Player player = target.GetComponentInParent<Player>();
			MobileNPCHumanoidDisposition npc = target.GetComponentInParent<MobileNPCHumanoidDisposition>();
			Zombie zombie = target.GetComponentInParent<Zombie>();
			bool confirmed = player != null && perception.VisiblePlayer == player
				|| npc != null && perception.VisibleNPC == npc
				|| zombie != null && perception.VisibleZombie == zombie;
			if (confirmed)
				brain.ReportIncomingFire(target.position, target);
		}

		private void HandleReloadFinished()
		{
			// The existing tactical goal expires or clears when its destination is reached.
			// Keep a short cooldown so the NPC does not oscillate between cover and firing.
			nextFlankTime = Mathf.Max(nextFlankTime, Time.time + 1.5f);
		}

		private void ResetOcclusion()
		{
			blockedSince = -1f;
		}
	}
}
