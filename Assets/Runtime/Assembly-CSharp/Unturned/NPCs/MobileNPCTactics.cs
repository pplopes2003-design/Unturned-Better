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
		private const float RetreatDuration = 28.0f;
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
		/// <summary>
		/// Priority selector: retain a committed defensive movement first;
		/// then survive critical wounds, compare verified cover against
		/// suppressive return fire, and finally request a validated withdrawal.
		/// This is intentionally a small behavior-tree selector rather than a
		/// competing controller. Host movement and cover validation remain intact.
		/// </summary>
		public bool TryChooseDefensiveGoal(Vector3 currentPosition, Vector3? validatedCoverPoint, Vector3 threatPosition, float now, bool canReturnFire = false)
		{
			bool underFire = now - lastShotAt <= 2.5f;
			bool damaged = now - lastDamageAt <= 3f;
			float critical = MobileNPCSettings.CombatMode ? 0.09f : CriticalHealthThreshold;
			float low = MobileNPCSettings.CombatMode ? 0.19f : LowHealthThreshold;
			bool needsEscape = healthFraction <= critical || (healthFraction <= low && damaged);
			bool committed = (currentGoal == Goal.Retreat || currentGoal == Goal.SeekCover)
				&& hasTacticalDestination && now < goalDeadline;

			// Critical injury outranks an ordinary cover move. A verified
			// RETREAT remains committed, but an ordinary SeekCover point is
			// not automatically a safe escape from an overwhelming threat.
			if (needsEscape && currentGoal == Goal.SeekCover)
			{
				currentGoal = Goal.Retreat;
				hasTacticalDestination = false;
				goalDeadline = now + RetreatDuration;
				return true;
			}

			// Selector child 1 (running): an existing validated journey owns
			// movement until arrival, expiry or explicit route failure.
			if (committed)
				return true;

			// Selector child 2: critically injured combatants should withdraw,
			// but cannot invent a safe position. Caller must verify the route.
			if (needsEscape)
			{
				if (currentGoal != Goal.Retreat || now >= goalDeadline)
				{
					currentGoal = Goal.Retreat;
					hasTacticalDestination = false;
					goalDeadline = now + RetreatDuration;
				}
				return true;
			}

			if (!underFire && !damaged)
			{
				if (currentGoal == Goal.Retreat || currentGoal == Goal.SeekCover)
					ClearGoal();
				return false;
			}

			// Selector child 3: utility comparison with verified destinations.
			// Never score unverified cover as usable; health and confirmed
			// ability to shoot are inputs, not magical awareness of an enemy.
			float coverUtility = validatedCoverPoint.HasValue
				? 0.60f + (1f - healthFraction) * 0.28f
					+ (damaged ? 0.12f : 0f)
				: -1f;
			float returnFireUtility = canReturnFire
				? 0.48f + healthFraction * 0.30f
					- (damaged ? 0.12f : 0f)
				: -1f;

			if (coverUtility >= returnFireUtility && validatedCoverPoint.HasValue)
			{
				currentGoal = Goal.SeekCover;
				tacticalDestination = validatedCoverPoint.Value;
				hasTacticalDestination = true;
				goalDeadline = now + CoverDuration;
				return true;
			}
			if (returnFireUtility >= 0f && healthFraction > 0.38f)
				return false;

			// Selector child 4: no safe cover or viable shot; request retreat.
			// A second stimulus must not restart an unfinished decision.
			if (currentGoal != Goal.Retreat || now >= goalDeadline)
			{
				currentGoal = Goal.Retreat;
				hasTacticalDestination = false;
				goalDeadline = now + RetreatDuration;
			}
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

		/// <summary>Renew a retreat only while the motor is making measurable
		/// progress toward its already verified destination. Never change the
		/// location or resurrect a stale/failed path.</summary>
		public bool ExtendActiveRetreat(float now)
		{
			if (currentGoal != Goal.Retreat || !hasTacticalDestination)
				return false;
			goalDeadline = Mathf.Max(goalDeadline, now + 6f);
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
