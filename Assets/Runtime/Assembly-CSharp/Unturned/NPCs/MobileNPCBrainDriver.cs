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

		/// <summary>
		/// Raised when the brain changes its requested destination. Subscribe on the
		/// server to feed the destination into the host's navigation implementation.
		/// </summary>
		public event Action<Vector3> DestinationRequested;

		public MobileNPCBrain.State CurrentState => brain != null
			? brain.CurrentState
			: MobileNPCBrain.State.Idle;

		public Transform Target => brain != null ? brain.Target : null;

		public bool HasDestination => brain != null && brain.HasDestination;

		public Vector3 Destination => brain != null ? brain.Destination : transform.position;

		/// <summary>
		/// Initializes the decision brain for this actor. Call from the host's
		/// server-side spawn/initialization path, not from a client.
		/// </summary>
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
			PublishDestination();
		}

		/// <summary>Submit a target already validated by server-side perception.</summary>
		public void ReportVisibleTarget(Transform target)
		{
			if (!CanAcceptServerInput || target == null)
			{
				return;
			}

			brain.ReportVisibleTarget(target, Time.time);
			PublishDestination();
		}

		/// <summary>Submit a server-observed sound or point of interest.</summary>
		public void ReportNoise(Vector3 position, float duration = 25.0f)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.ReportNoise(position, Time.time, duration);
			PublishDestination();
		}

		/// <summary>Call after the host's navigation layer confirms arrival.</summary>
		public void ReportDestinationReached()
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.ReportDestinationReached();
			PublishDestination();
		}

		/// <summary>
		/// Tick from the host's server update after it has evaluated target visibility.
		/// Visibility must be established by server-side perception, not client input.
		/// </summary>
		public void TickServer(bool targetIsVisible)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.Tick(Time.time, targetIsVisible);
			PublishDestination();
		}

		public void ResetServer()
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			brain.Reset();
			PublishDestination();
		}

		private bool CanAcceptServerInput => Provider.isServer && hasInitialized && brain != null;

		private void PublishDestination()
		{
			if (brain != null && brain.HasDestination)
			{
				DestinationRequested?.Invoke(brain.Destination);
			}
		}
	}
}
