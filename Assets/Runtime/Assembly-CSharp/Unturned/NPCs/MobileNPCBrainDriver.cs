////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Opt-in adapter between a mobile NPC host and its decision brain.
	/// The host is responsible for server perception, navigation, damage and replication.
	/// This component intentionally does not attach itself to map NPCs automatically.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCBrainDriver : MonoBehaviour
	{
		private MobileNPCBrain brain;
		private bool hasInitialized;
		private bool hasPublishedDecision;
		private bool lastHasDestination;
		private Vector3 lastDestination;
		private MobileNPCBrain.State lastState;

		/// <summary>
		/// Raised only when the decision changes, not every server tick. The host can
		/// use this to update its navigation goal without repeatedly resetting a path.
		/// </summary>
		public event Action<MobileNPCBrain.State, bool, Vector3> DecisionUpdated;

		public MobileNPCBrain.State CurrentState => brain != null
			? brain.CurrentState
			: MobileNPCBrain.State.Idle;

		public Transform Target => brain != null ? brain.Target : null;
		public bool HasDestination => brain != null && brain.HasDestination;
		public Vector3 Destination => brain != null ? brain.Destination : transform.position;

		/// <summary>Call from the host's server-side spawn/initialization path.</summary>
		public void InitializeServer(Vector3 spawnPosition)
		{
			if (!Provider.isServer)
			{
				return;
			}

			if (brain == null)
			{
				brain = new MobileNPCBrain();
			}

			brain.SetSpawnPosition(spawnPosition);
			brain.Reset();
			hasInitialized = true;
			hasPublishedDecision = false;
			PublishDecisionIfChanged();
		}

		/// <summary>Submit a target already validated by server-side perception.</summary>
		public void ReportVisibleTarget(Transform target)
		{
			if (!CanAcceptServerInput || target == null)
			{
				return;
			}

			brain.ReportVisibleTarget(target, Time.time);
			PublishDecisionIfChanged();
		}

		/// <summary>Submit a server-observed sound or point of interest.</summary>
		public void ReportNoise(Vector3 position, float duration = 25.0f)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.ReportNoise(position, Time.time, duration);
			PublishDecisionIfChanged();
		}

		/// <summary>Call after the host's navigation layer confirms arrival.</summary>
		public void ReportDestinationReached()
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.ReportDestinationReached();
			PublishDecisionIfChanged();
		}

		/// <summary>
		/// Tick from the host's server update after evaluating target visibility.
		/// Visibility must come from server-side perception, not client input.
		/// </summary>
		public void TickServer(bool targetIsVisible)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.Tick(Time.time, targetIsVisible);
			PublishDecisionIfChanged();
		}

		public void ResetServer()
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.Reset();
			PublishDecisionIfChanged();
		}

		private bool CanAcceptServerInput => Provider.isServer && hasInitialized && brain != null;

		private void PublishDecisionIfChanged()
		{
			if (brain == null)
			{
				return;
			}

			bool hasDestination = brain.HasDestination;
			Vector3 destination = brain.Destination;
			MobileNPCBrain.State state = brain.CurrentState;

			bool changed = !hasPublishedDecision
				|| state != lastState
				|| hasDestination != lastHasDestination
				|| (hasDestination && (destination - lastDestination).sqrMagnitude > 0.04f);

			if (!changed)
			{
				return;
			}

			hasPublishedDecision = true;
			lastState = state;
			lastHasDestination = hasDestination;
			lastDestination = destination;
			DecisionUpdated?.Invoke(state, hasDestination, destination);
		}
	}
}
