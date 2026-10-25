////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Tactical goal selection for a server-owned mobile NPC.
	/// The host must validate cover, visibility, inventory capacity, item ownership,
	/// and path reachability before submitting candidates or executing an action.
	/// </summary>
	public sealed class MobileNPCTactics
	{
		public enum Goal
		{
			None,
			Investigate,
			Pursue,
			SeekCover,
			Retreat,
			Loot,
			Flank,
			Stalk,
			// Append only: do not renumber existing named goals.
			Reinforce,
			Regroup,
			// Added after existing goals to preserve snapshot/diagnostic IDs.
			RaidStaging
		}

		private const float LowHealthThreshold = 0.30f;
		private const float CriticalHealthThreshold = 0.15f;
		private const float CoverDuration = 12.0f;
		private const float RetreatDuration = 14.0f;
		private const float LootDuration = 12.0f;

		private Goal currentGoal;
		private Vector3 tacticalDestination;
		private float goalDeadline;
		private float lastShotAt = -1000.0f;
		private float lastDamageAt = -1000.0f;
		private float healthFraction = 1.0f;
		private bool hasTacticalDestination;

		public Goal CurrentGoal => currentGoal;
		public bool HasTacticalDestination => hasTacticalDestination;
		public Vector3 TacticalDestination => tacticalDestination;

		/// <summary>Call when the server confirms the NPC has taken damage.</summary>
		public void ReportHealth(float currentHealth, float maximumHealth)
		{
			healthFraction = maximumHealth > 0.0f
				? Mathf.Clamp01(currentHealth / maximumHealth)
				: 0.0f;
		}

		/// <summary>Call when the server confirms the NPC has taken damage.</summary>
		public void ReportDamage(float currentHealth, float maximumHealth, float now)
		{
			ReportHealth(currentHealth, maximumHealth);
			lastDamageAt = now;
		}

		/// <summary>Call when the server detects incoming gunfire near the NPC.</summary>
		public void ReportIncomingFire(float now)
		{
			lastShotAt = now;
		}

		/// <summary>
		/// Chooses between cover and retreat using a cover point already validated by
		/// server-side navigation and line-of-sight checks. Returns false if no tactical
		/// response is currently required.
		/// </summary>
		public bool TryChooseDefensiveGoal(Vector3 currentPosition, Vector3? validatedCoverPoint, Vector3 threatPosition, float now, bool canReturnFire = false)
		{
			bool underRecentFire = now - lastShotAt <= 2.5f;
			bool recentlyDamaged = now - lastDamageAt <= 3.0f;
			// Combat mode increases courage, never eliminates mortal risk.
			// Even the most aggressive fighter still withdraws near death.
			bool criticallyWounded = healthFraction <= (MobileNPCSettings.CombatMode ? 0.09f : CriticalHealthThreshold);
			bool badlyWounded = healthFraction <= (MobileNPCSettings.CombatMode ? 0.19f : LowHealthThreshold);

			if (!underRecentFire && !recentlyDamaged && !badlyWounded)
			{
				// Suppression ends faster than a purposeful withdrawal.
				// Do not abandon a reachable shelter as soon as shooting stops.
				if ((currentGoal == Goal.SeekCover || currentGoal == Goal.Retreat)
					&& now < goalDeadline)
					return true;
				if (currentGoal == Goal.SeekCover || currentGoal == Goal.Retreat)
					ClearGoal();
				return false;
			}

			if (criticallyWounded || (badlyWounded && recentlyDamaged))
			{
				// Keep a previously verified escape destination despite repeated hits.
				// Replacing it each decision tick causes abort/re-advance oscillation.
				if (currentGoal == Goal.Retreat && hasTacticalDestination
					&& now < goalDeadline)
					return true;
				currentGoal = Goal.Retreat;
				goalDeadline = now + RetreatDuration;
				hasTacticalDestination = false;
				return true;
			}

			// A physically validated defensive destination is a COMMITMENT.
			// Reacquiring the target or hearing another shot must not change
			// a retreat into a charge, or swap cover on every decision tick.
			// An urgent critical injury above is the sole preemption here.
			if ((currentGoal == Goal.SeekCover || currentGoal == Goal.Retreat)
				&& hasTacticalDestination && now < goalDeadline)
				return true;

			if (validatedCoverPoint.HasValue)
			{
				currentGoal = Goal.SeekCover;
				tacticalDestination = validatedCoverPoint.Value;
				hasTacticalDestination = true;
				goalDeadline = now + CoverDuration;
				return true;
			}

			// F.E.A.R.-style defensive selection must not turn EVERY shot into
			// a blind flight: a healthy, armed survivor with a truly confirmed
			// target can stand and return fire when no reachable cover exists.
			// No-target, unarmed, or seriously wounded survivors still withdraw.
			if (canReturnFire && healthFraction > (MobileNPCSettings.CombatMode ? 0.38f : 0.55f))
			{
				// Armed return fire is preferable to inventing an unsafe
				// retreat, but it cannot cancel a still-valid defense plan.
				if ((currentGoal == Goal.SeekCover || currentGoal == Goal.Retreat)
					&& now < goalDeadline)
					return true;
				return false;
			}
			// If the NPC cannot safely retaliate and no cover was found,
			// validate a retreat position rather than standing exposed.
			currentGoal = Goal.Retreat;
			hasTacticalDestination = false;
			goalDeadline = now + RetreatDuration;
			return true;
		}

		/// <summary>
		/// A nearby comrade falling may justify a short voluntary fallback.
		/// This is the existing RETREAT goal, not a new combat brain; its
		/// position must be physically validated by the caller first.
		/// </summary>
		public bool TryStartValidatedSquadWithdrawal(Vector3 safePoint, float now)
		{
			if (!IsFinite(safePoint) || currentGoal == Goal.SeekCover
				|| currentGoal == Goal.Retreat)
				return false;
			currentGoal = Goal.Retreat;
			tacticalDestination = safePoint;
			hasTacticalDestination = true;
			goalDeadline = now + 9f;
			return true;
		}

		/// <summary>Accept a retreat point only after the host validates it as safe.</summary>
		public bool ReportValidatedRetreatPoint(Vector3 safePoint, float now)
		{
			if (currentGoal != Goal.Retreat)
			{
				return false;
			}

			tacticalDestination = safePoint;
			hasTacticalDestination = true;
			goalDeadline = now + RetreatDuration;
			return true;
		}

		/// <summary>
		/// Starts a loot goal only for a server-validated, reachable item and only when
		/// no urgent defensive goal is active. The host still performs the actual pickup.
		/// </summary>
		public bool TryStartLoot(Vector3 itemPosition, float now)
		{
			if (currentGoal == Goal.Retreat || currentGoal == Goal.SeekCover || currentGoal == Goal.Stalk)
			{
				return false;
			}

			currentGoal = Goal.Loot;
			tacticalDestination = itemPosition;
			hasTacticalDestination = true;
			goalDeadline = now + LootDuration;
			return true;
		}

		/// <summary>
		/// Quietly approach a validated concealed vantage point near the last
		/// sensory clue. This does not authorize firing or reveal the actual target.
		/// </summary>
		public bool TryStartStalk(Vector3 coveredPoint, float now)
		{
			if (currentGoal != Goal.None || !IsFinite(coveredPoint))
				return false;
			currentGoal = Goal.Stalk;
			tacticalDestination = coveredPoint;
			hasTacticalDestination = true;
			goalDeadline = now + 7f;
			return true;
		}

		private static bool IsFinite(Vector3 point)
		{
			return !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z)
				&& !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z);
		}

		/// <summary>Starts a time-bounded flank to a host-validated alternate firing position.</summary>
		public bool TryStartFlank(Vector3 validatedPosition, float now)
		{
			if (!IsFinite(validatedPosition)
				|| currentGoal == Goal.Retreat || currentGoal == Goal.SeekCover
				|| currentGoal == Goal.Stalk)
				return false;

			// A movement plan must have time to execute. Do not replace an
			// active flank destination whenever another controller proposes a
			// different angle on its next tick. Arrival, a confirmed route
			// failure, expiration, or urgent defense can still end the goal.
			if (currentGoal == Goal.Flank && hasTacticalDestination && now < goalDeadline)
				return false;

			currentGoal = Goal.Flank;
			tacticalDestination = validatedPosition;
			hasTacticalDestination = true;
			goalDeadline = now + 8.0f;
			return true;
		}

		/// <summary>Remembered supply trip, with an ordinary bounded movement goal.
		/// The motor must still validate navigation and real loot after arrival.</summary>
		public bool TryStartSupplySearch(Vector3 place, float now)
		{
			if (currentGoal != Goal.None || !IsFinite(place))
				return false;
			currentGoal = Goal.Investigate;
			tacticalDestination = place;
			hasTacticalDestination = true;
			goalDeadline = now + 65f;
			return true;
		}

		/// <summary>
		/// A physically reachable approximate ally rendezvous, not a hostile
		/// target. The caller must verify health, gear, confidence and safe ground.
		/// This never transfers the original shooter's identity or Transform.
		/// </summary>
		public bool TryStartRadioReinforcement(Vector3 safeRendezvous, float now)
		{
			if (currentGoal != Goal.None || !IsFinite(safeRendezvous))
				return false;
			currentGoal = Goal.Reinforce;
			tacticalDestination = safeRendezvous;
			hasTacticalDestination = true;
			goalDeadline = now + 52f;
			return true;
		}

		/// <summary>A brief, physically validated rally point near an
		/// actually audible living squad leader, not a faction teleporter.
		/// Tactical threats preempt this noncombat goal immediately.</summary>
		public bool TryStartGroupRegroup(Vector3 safePoint, float now)
		{
			if (currentGoal != Goal.None || !IsFinite(safePoint))
				return false;
			currentGoal = Goal.Regroup;
			tacticalDestination = safePoint;
			hasTacticalDestination = true;
			goalDeadline = now + 14f;
			return true;
		}

		/// <summary>Approach a truly observed rival group base from a
		/// reachable staging point. This is NOT a direct enemy target or
		/// permission to damage any structure. Contact is reacquired normally.</summary>
		public bool TryStartRaidStaging(Vector3 safePoint, float now)
		{
			if (currentGoal != Goal.None || !IsFinite(safePoint))
				return false;
			currentGoal = Goal.RaidStaging;
			tacticalDestination = safePoint;
			hasTacticalDestination = true;
			goalDeadline = now + 48f;
			return true;
		}

		public bool IsGoalExpired(float now)
		{
			return currentGoal != Goal.None && now >= goalDeadline;
		}

		public void ReportGoalReached()
		{
			ClearGoal();
		}

		public void ClearGoal()
		{
			currentGoal = Goal.None;
			hasTacticalDestination = false;
			goalDeadline = 0.0f;
		}
	}
}
