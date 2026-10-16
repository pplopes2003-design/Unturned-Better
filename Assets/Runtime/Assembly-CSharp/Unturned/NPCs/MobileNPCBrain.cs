////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-side decision state for a mobile humanoid NPC.
	/// This class deliberately does not move a Transform or replicate network state.
	/// The owning entity supplies perception and consumes the resulting destination.
	/// </summary>
	public sealed class MobileNPCBrain
	{
		public enum State
		{
			Idle,
			Investigating,
			Pursuing,
			Following,
			Returning
		}

		private const float TargetMemoryDuration = 8.0f;
		private const float InvestigationDuration = 25.0f;

		private State state;
		private Transform target;
		private Vector3 spawnPosition;
		private Vector3 destination;
		private Vector3 investigationCenter;
		private int investigationSearchCount;
		private float targetMemoryDeadline;
		private float investigationDeadline;
		private bool hasSpawnPosition;
		private bool hasDestination;

		public State CurrentState => state;
		public Transform Target => target;
		public Vector3 Destination => destination;
		public bool HasDestination => hasDestination;

		public void SetSpawnPosition(Vector3 position)
		{
			spawnPosition = position;
			hasSpawnPosition = true;
		}

		/// <summary>Call only for a target validated by the server's perception layer.</summary>
		public void ReportVisibleTarget(Transform visibleTarget, float now)
		{
			if (visibleTarget == null)
			{
				return;
			}

			target = visibleTarget;
			targetMemoryDeadline = now + TargetMemoryDuration;
			destination = visibleTarget.position;
			investigationCenter = destination;
			investigationSearchCount = 0;
			hasDestination = true;
			state = State.Pursuing;
		}

		/// <summary>Sets a short-lived companion destination when no combat target is active.</summary>
		public void SetFollowDestination(Vector3 position)
		{
			if (state == State.Pursuing && target != null)
				return;

			target = null;
			destination = position;
			hasDestination = true;
			state = State.Following;
		}

		/// <summary>Reports a sound or point of interest observed by the server.</summary>
		public void ReportNoise(Vector3 position, float now, float duration = InvestigationDuration)
		{
			if (state == State.Pursuing && target != null)
			{
				return;
			}

			target = null;
			destination = position;
			investigationCenter = position;
			investigationSearchCount = 0;
			hasDestination = true;
			investigationDeadline = now + Mathf.Max(0.1f, duration);
			state = State.Investigating;
		}

		/// <summary>
		/// Refreshes the decision. targetIsVisible must come from server-side perception;
		/// pathfinding and reachability checks remain the responsibility of the entity.
		/// </summary>
		public void Tick(float now, bool targetIsVisible)
		{
			if (state == State.Pursuing)
			{
				if (target != null && (targetIsVisible || now < targetMemoryDeadline))
				{
					if (targetIsVisible)
					{
						targetMemoryDeadline = now + TargetMemoryDuration;
						destination = target.position;
					}

					// While the target is occluded, retain its last observed position instead
					// of following its live Transform through walls.
					hasDestination = true;
					return;
				}

				investigationCenter = destination;
				target = null;
				state = State.Investigating;
				investigationSearchCount = 0;
				investigationDeadline = now + InvestigationDuration;
				destination = investigationCenter;
				hasDestination = true;
				return;
			}

			// The companion contract refreshes this destination at a low frequency.
			if (state == State.Following)
			{
				if (!hasDestination)
					state = State.Idle;
				else
					return;
			}

			if (state == State.Investigating && now >= investigationDeadline)
			{
				if (hasSpawnPosition)
				{
					destination = spawnPosition;
					hasDestination = true;
					state = State.Returning;
				}
				else
				{
					hasDestination = false;
					state = State.Idle;
				}

				return;
			}

			if (state == State.Returning && hasSpawnPosition)
			{
				destination = spawnPosition;
				hasDestination = true;
			}
		}

		/// <summary>
		/// Accept a search hypothesis from the sensory memory advisor, never a live
		/// player Transform. Preserve the original investigation expiration time.
		/// </summary>
		public bool TrySetInvestigationHint(Vector3 point, float now)
		{
			if (state != State.Investigating || now >= investigationDeadline
				|| float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)
				|| float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
				return false;

			destination = point;
			hasDestination = true;
			return true;
		}

		/// <summary>Called by the locomotion layer after it confirms arrival.</summary>
		public void ReportDestinationReached()
		{
			if (state == State.Returning)
			{
				state = State.Idle;
				hasDestination = false;
			}
			else if (state == State.Following)
			{
				hasDestination = false;
				state = State.Idle;
			}
			else if (state == State.Investigating)
			{
				// Search a small deterministic spiral around the last known position rather
				// than immediately falling back to random roaming after reaching the clue.
				if (investigationSearchCount < 3)
				{
					float angle = (investigationSearchCount * 137.5f) * Mathf.Deg2Rad;
					float radius = 3.5f + investigationSearchCount * 1.15f;
					Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
					destination = investigationCenter + offset;
					investigationSearchCount++;
					hasDestination = true;
				}
				else
				{
					// The three actually reached search points exhausted the
					// physical investigation. Do not remain in Investigating
					// with no destination until a timer expires, appearing to
					// freeze or spin in the same corridor.
					investigationDeadline = 0f;
					investigationSearchCount = 0;
					if (hasSpawnPosition)
					{
						destination = spawnPosition;
						hasDestination = true;
						state = State.Returning;
					}
					else
					{
						hasDestination = false;
						state = State.Idle;
					}
				}
			}
		}

		public void Reset()
		{
			state = State.Idle;
			target = null;
			hasDestination = false;
			targetMemoryDeadline = 0.0f;
			investigationDeadline = 0.0f;
			investigationCenter = Vector3.zero;
			investigationSearchCount = 0;
			destination = hasSpawnPosition ? spawnPosition : Vector3.zero;
		}
	}
}
