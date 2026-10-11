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
		private bool hasInitialized;
		private bool hasPublishedDecision;
		private bool lastHasDestination;
		private Vector3 lastDestination;
		private MobileNPCBrain.State lastState;
		private MobileNPCTactics.Goal lastGoal;

		public event Action<MobileNPCBrain.State, MobileNPCTactics.Goal, bool, Vector3> DecisionUpdated;

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

		/// <summary>Call when server perception detects nearby incoming gunfire.</summary>
		public void ReportIncomingFire()
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			tactics.ReportIncomingFire(Time.time);
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
