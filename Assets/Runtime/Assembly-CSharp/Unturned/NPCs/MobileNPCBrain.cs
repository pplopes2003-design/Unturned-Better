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
			Returning
		}

		private const float TargetMemoryDuration = 8.0f;
		private const float InvestigationDuration = 25.0f;

		private State state;
		private Transform target;
		private Vector3 spawnPosition;
		private Vector3 destination;
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
			hasDestination = true;
			state = State.Pursuing;
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
					}

					destination = target.position;
					hasDestination = true;
					return;
				}

				target = null;
				state = State.Investigating;
				investigationDeadline = now + InvestigationDuration;
				// Search the last known target location before returning home.
				investigationDeadline = Mathf.Max(investigationDeadline, now + 0.1f);
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

		/// <summary>Called by the locomotion layer after it confirms arrival.</summary>
		public void ReportDestinationReached()
		{
			if (state == State.Returning)
			{
				state = State.Idle;
				hasDestination = false;
			}
			else if (state == State.Investigating)
			{
				// The perception/locomotion layer may provide another search point.
				hasDestination = false;
			}
		}

		public void Reset()
		{
			state = State.Idle;
			target = null;
			hasDestination = false;
			targetMemoryDeadline = 0.0f;
			investigationDeadline = 0.0f;
			destination = hasSpawnPosition ? spawnPosition : Vector3.zero;
		}
	}
}
