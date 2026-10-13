////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Opt-in server-side decision adapter for a future mobile humanoid entity.
	/// The host remains responsible for perception, navigation, damage and replication.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCBrainDriver : MonoBehaviour
	{
		private MobileNPCBrain brain;
		private MobileNPCTactics tactics;
		private float nextDefensiveReplanAt;
		private Vector3 lastDefensiveThreatPosition;
		private bool hasInitialized;
		private bool hasPublishedDecision;
		private bool lastHasDestination;
		private Vector3 lastDestination;
		private MobileNPCBrain.State lastState;
		private MobileNPCTactics.Goal lastGoal;

		public event System.Action<MobileNPCBrain.State, MobileNPCTactics.Goal, bool, Vector3> DecisionUpdated;

		public MobileNPCBrain.State CurrentState => brain != null ? brain.CurrentState : MobileNPCBrain.State.Idle;
		public MobileNPCTactics.Goal CurrentGoal => tactics != null ? tactics.CurrentGoal : MobileNPCTactics.Goal.None;
		public Transform Target => brain != null ? brain.Target : null;
		public bool HasDestination => tactics != null && tactics.HasTacticalDestination || brain != null && brain.HasDestination;
		public Vector3 Destination => tactics != null && tactics.HasTacticalDestination
			? tactics.TacticalDestination
			: brain != null && brain.HasDestination ? brain.Destination : transform.position;

		public void InitializeServer(Vector3 spawnPosition)
		{
			if (!Provider.isServer)
			{
				return;
			}

			if (brain == null)
			{
				brain = new MobileNPCBrain();
				tactics = new MobileNPCTactics();
			}

			brain.SetSpawnPosition(spawnPosition);
			brain.Reset();
			tactics.ClearGoal();
			hasInitialized = true;
			MobileNPCRegistry.Register(this);
			hasPublishedDecision = false;
			PublishDecisionIfChanged();
		}

		public void ReportVisibleTarget(Transform target)
		{
			if (!CanAcceptServerInput || target == null)
			{
				return;
			}

			brain.ReportVisibleTarget(target, Time.time);
			PublishDecisionIfChanged();
		}

		public void ReportNoise(Vector3 position, float duration = 25.0f)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.ReportNoise(position, Time.time, duration);
			PublishDecisionIfChanged();
		}

		/// <summary>Update server-authoritative health without implying a new damage event.</summary>
		public void ReportHealth(float currentHealth, float maximumHealth)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			tactics.ReportHealth(currentHealth, maximumHealth);
			PublishDecisionIfChanged();
		}

		/// <summary>Call only after server damage handling has updated health.</summary>
		public void ReportDamage(float currentHealth, float maximumHealth)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			tactics.ReportDamage(currentHealth, maximumHealth, Time.time);
			PublishDecisionIfChanged();
		}

		/// <summary>Call when server perception detects incoming gunfire without a known source.</summary>
		public void ReportIncomingFire()
		{
			ReportIncomingFire(transform.position, null);
		}

		/// <summary>
		/// Reacts to a gunshot stimulus: suppresses aim, investigates the shooter's last
		/// known position, and attempts to move behind nearby geometry instead of standing exposed.
		/// </summary>
		public void ReportIncomingFire(Vector3 threatPosition, Transform threatTransform)
		{
			if (!CanAcceptServerInput)
				return;

			tactics.ReportIncomingFire(Time.time);
			// Keep a valid cover/retreat destination long enough to travel toward it.
			// Repeated nearby gunshots previously reset the target every frame/shot.
			bool alreadyDefending = tactics.CurrentGoal == MobileNPCTactics.Goal.SeekCover
				|| tactics.CurrentGoal == MobileNPCTactics.Goal.Retreat;
			Vector3 threatDelta = threatPosition - lastDefensiveThreatPosition;
			threatDelta.y = 0f;
			bool keepDefense = alreadyDefending && tactics.HasTacticalDestination
				&& Time.time < nextDefensiveReplanAt && threatDelta.sqrMagnitude < 100f;
			MobileNPCHumanoidAim aim = GetComponent<MobileNPCHumanoidAim>();
			if (aim != null)
				aim.ReportSuppression();

			// Preserve an already-visible combat target; otherwise search the gunshot source.
			if (brain.CurrentState != MobileNPCBrain.State.Pursuing || brain.Target == null)
				brain.ReportNoise(threatPosition, Time.time, 25f);

			if (keepDefense)
			{
				PublishDecisionIfChanged();
				return;
			}

			lastDefensiveThreatPosition = threatPosition;
			nextDefensiveReplanAt = Time.time + 2.5f;
			Vector3? coverPoint = TryFindNearbyCoverPoint(threatPosition);
			if (tactics.TryChooseDefensiveGoal(transform.position, coverPoint, threatPosition, Time.time)
				&& tactics.CurrentGoal == MobileNPCTactics.Goal.Retreat && !tactics.HasTacticalDestination)
			{
				Vector3 away = transform.position - threatPosition;
				away.y = 0f;
				if (away.sqrMagnitude > 0.01f)
				{
					Vector3 candidate = transform.position + away.normalized * 9f;
					if (MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, 12f, 8f, ~0, 38f,
						out Vector3 safeRetreat, out _))
					{
						tactics.ReportValidatedRetreatPoint(safeRetreat, Time.time);
					}
					else
					{
						// If the map offers no safe retreat point, avoid blindly charging the shooter.
						brain.ReportNoise(transform.position, Time.time, 4f);
					}
				}
			}
			PublishDecisionIfChanged();
		}

		private Vector3? TryFindNearbyCoverPoint(Vector3 threatPosition)
		{
			Vector3 away = transform.position - threatPosition;
			away.y = 0f;
			if (away.sqrMagnitude < 0.01f)
				away = -transform.forward;
			away.Normalize();

			// Sample a compact fan behind and to either side of the NPC. A candidate is
			// accepted only when safe ground exists and world geometry blocks the threat ray.
			float[] angles = { -70f, -40f, -20f, 0f, 20f, 40f, 70f };
			float[] distances = { 4f, 7f, 10f };
			for (int d = 0; d < distances.Length; ++d)
			{
				for (int a = 0; a < angles.Length; ++a)
				{
					Vector3 direction = Quaternion.AngleAxis(angles[a], Vector3.up) * away;
					Vector3 rawCandidate = transform.position + direction * distances[d];
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(rawCandidate, 12f, 8f, ~0, 38f,
						out Vector3 candidate, out _))
						continue;

					Vector3 rayOrigin = threatPosition + Vector3.up * 1.4f;
					Vector3 rayTarget = candidate + Vector3.up * 1.0f;
					Vector3 rayOffset = rayTarget - rayOrigin;
					if (rayOffset.sqrMagnitude < 0.1f)
						continue;

					// The shooter-to-cover ray may first hit the shooter's own
					// collider. Inspect the full segment and accept only actual world
					// geometry, not either participant's body.
					RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayOffset.normalized,
						rayOffset.magnitude, ~0, QueryTriggerInteraction.Ignore);
					bool protectedByWorld = false;
					for (int h = 0; h < hits.Length; ++h)
					{
						Collider obstruction = hits[h].collider;
						if (obstruction == null || obstruction.transform == transform
							|| obstruction.transform.IsChildOf(transform))
							continue;
						if (obstruction.GetComponentInParent<Player>() != null
							|| obstruction.GetComponentInParent<MobileNPCHumanoidVitals>() != null
							|| obstruction.GetComponentInParent<Zombie>() != null)
							continue;
						if (hits[h].distance < rayOffset.magnitude - 0.75f
							&& Vector3.Angle(hits[h].normal, Vector3.up) > 50f)
						{
							protectedByWorld = true;
							break;
						}
					}
					if (!protectedByWorld || (candidate - transform.position).sqrMagnitude <= 1f)
						continue;

					// A covered point is useless if a wall blocks the route to it.
					// Check a chest-height corridor; the motor still performs actual
					// navigation and local obstacle avoidance during movement.
					Vector3 approach = candidate - transform.position;
					approach.y = 0f;
					if (approach.sqrMagnitude < 0.5f)
						continue;
					Vector3 routeOrigin = transform.position + Vector3.up * 1f;
					RaycastHit[] approachHits = Physics.SphereCastAll(routeOrigin, 0.25f,
						approach.normalized, approach.magnitude, ~0, QueryTriggerInteraction.Ignore);
					bool blockedApproach = false;
					for (int h = 0; h < approachHits.Length; ++h)
					{
						Collider obstruction = approachHits[h].collider;
						if (obstruction == null || obstruction.transform == transform
							|| obstruction.transform.IsChildOf(transform))
							continue;
						if (obstruction.GetComponentInParent<Player>() != null
							|| obstruction.GetComponentInParent<MobileNPCHumanoidVitals>() != null
							|| obstruction.GetComponentInParent<Zombie>() != null)
							continue;
						if (approachHits[h].distance < approach.magnitude - 0.5f)
						{
							blockedApproach = true;
							break;
						}
					}
					if (!blockedApproach)
						return candidate;
				}
			}
			return null;
		}

		/// <summary>
		/// Chooses a defensive goal. Cover must already be checked for reachability and
		/// protection by the host. If retreat is selected, submit a validated safe point.
		/// </summary>
		public bool TryChooseDefensiveGoal(Vector3? validatedCoverPoint, Vector3 threatPosition)
		{
			if (!CanAcceptServerInput)
			{
				return false;
			}

			bool selected = tactics.TryChooseDefensiveGoal(transform.position, validatedCoverPoint, threatPosition, Time.time);
			PublishDecisionIfChanged();
			return selected;
		}

		/// <summary>
		/// Starts a flank only after the caller validates safe ground, line of fire,
		/// and a meaningful displacement from the current position.
		/// </summary>
		public bool TryStartFlank(Vector3 validatedPosition)
		{
			if (!CanAcceptServerInput
				|| float.IsNaN(validatedPosition.x) || float.IsInfinity(validatedPosition.x)
				|| float.IsNaN(validatedPosition.y) || float.IsInfinity(validatedPosition.y)
				|| float.IsNaN(validatedPosition.z) || float.IsInfinity(validatedPosition.z)
				|| (validatedPosition - transform.position).sqrMagnitude < 9f)
				return false;

			bool selected = tactics.TryStartFlank(validatedPosition, Time.time);
			PublishDecisionIfChanged();
			return selected;
		}

		public void ReportValidatedRetreatPoint(Vector3 safePoint)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			tactics.ReportValidatedRetreatPoint(safePoint, Time.time);
			PublishDecisionIfChanged();
		}

		/// <summary>
		/// Starts moving toward a loot item only when the host has validated that item
		/// and confirmed inventory capacity. Actual pickup is a separate server action.
		/// </summary>
		public bool TryStartLoot(Vector3 itemPosition)
		{
			if (!CanAcceptServerInput || !tactics.TryStartLoot(itemPosition, Time.time))
			{
				return false;
			}

			PublishDecisionIfChanged();
			return true;
		}

		public void ReportDestinationReached()
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			if (tactics.CurrentGoal == MobileNPCTactics.Goal.Flank)
			{
				// Keep the NPC at its alternate firing position for the bounded flank
				// window instead of immediately resuming direct pursuit and crowding the player.
				return;
			}
			if (tactics.CurrentGoal != MobileNPCTactics.Goal.None)
			{
				tactics.ReportGoalReached();
			}
			else
			{
				brain.ReportDestinationReached();
			}

			PublishDecisionIfChanged();
		}

		public void TickServer(bool targetIsVisible)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.Tick(Time.time, targetIsVisible);
			if (tactics.IsGoalExpired(Time.time))
			{
				tactics.ClearGoal();
			}
			PublishDecisionIfChanged();
		}

		/// <summary>Updates a companion follow destination without overriding an active combat target.</summary>
		public void SetFollowDestinationServer(Vector3 destination)
		{
			if (!CanAcceptServerInput || brain == null
				|| float.IsNaN(destination.x) || float.IsInfinity(destination.x)
				|| float.IsNaN(destination.y) || float.IsInfinity(destination.y)
				|| float.IsNaN(destination.z) || float.IsInfinity(destination.z)
				|| brain.CurrentState == MobileNPCBrain.State.Pursuing && brain.Target != null)
				return;

			brain.SetFollowDestination(destination);
			PublishDecisionIfChanged();
		}

		/// <summary>Updates the return-home anchor without resetting current combat state.</summary>
		public void SetHomePositionServer(Vector3 homePosition)
		{
			if (!CanAcceptServerInput
				|| float.IsNaN(homePosition.x) || float.IsInfinity(homePosition.x)
				|| float.IsNaN(homePosition.y) || float.IsInfinity(homePosition.y)
				|| float.IsNaN(homePosition.z) || float.IsInfinity(homePosition.z))
			{
				return;
			}

			brain.SetSpawnPosition(homePosition);
		}

		public void ResetServer()
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.Reset();
			tactics.ClearGoal();
			PublishDecisionIfChanged();
		}

		private void OnEnable()
		{
			if (hasInitialized)
			{
				MobileNPCRegistry.Register(this);
			}
		}

		private void OnDisable()
		{
			MobileNPCRegistry.Unregister(this);
		}

		private bool CanAcceptServerInput => Provider.isServer && hasInitialized && brain != null && tactics != null;

		private void PublishDecisionIfChanged()
		{
			if (brain == null || tactics == null)
			{
				return;
			}

			bool tacticalDestination = tactics.HasTacticalDestination;
			bool hasDestination = tacticalDestination || brain.HasDestination;
			Vector3 destination = tacticalDestination ? tactics.TacticalDestination : brain.Destination;
			MobileNPCBrain.State state = brain.CurrentState;
			MobileNPCTactics.Goal goal = tactics.CurrentGoal;

			bool changed = !hasPublishedDecision
				|| state != lastState
				|| goal != lastGoal
				|| hasDestination != lastHasDestination
				|| (hasDestination && (destination - lastDestination).sqrMagnitude > 0.04f);

			if (!changed)
			{
				return;
			}

			hasPublishedDecision = true;
			lastState = state;
			lastGoal = goal;
			lastHasDestination = hasDestination;
			lastDestination = destination;
			DecisionUpdated?.Invoke(state, goal, hasDestination, destination);
		}
	}
}
