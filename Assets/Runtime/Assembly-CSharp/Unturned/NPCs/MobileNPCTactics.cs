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
			Loot
		}

		private const float LowHealthThreshold = 0.30f;
		private const float CriticalHealthThreshold = 0.15f;
		private const float CoverDuration = 4.0f;
		private const float RetreatDuration = 6.0f;
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
		public bool TryChooseDefensiveGoal(Vector3 currentPosition, Vector3? validatedCoverPoint, Vector3 threatPosition, float now)
		{
			bool underRecentFire = now - lastShotAt <= 2.5f;
			bool recentlyDamaged = now - lastDamageAt <= 3.0f;
			bool criticallyWounded = healthFraction <= CriticalHealthThreshold;
			bool badlyWounded = healthFraction <= LowHealthThreshold;

			if (!underRecentFire && !recentlyDamaged && !badlyWounded)
			{
				if (currentGoal == Goal.SeekCover || currentGoal == Goal.Retreat)
				{
					ClearGoal();
				}
				return false;
			}

			if (criticallyWounded || (badlyWounded && recentlyDamaged))
			{
				// Retreat destination should be selected by the host as a safe point
				// away from the threat; this class never invents a path or teleports.
				currentGoal = Goal.Retreat;
				goalDeadline = now + RetreatDuration;
				hasTacticalDestination = false;
				return true;
			}

			if (validatedCoverPoint.HasValue)
			{
				currentGoal = Goal.SeekCover;
				tacticalDestination = validatedCoverPoint.Value;
				hasTacticalDestination = true;
				goalDeadline = now + CoverDuration;
				return true;
			}

			// If no covered point was found, the host should validate a retreat point.
			// Do not keep the NPC planted in its current exposed position just because
			// a nearby hard-cover candidate was unavailable.
			currentGoal = Goal.Retreat;
			hasTacticalDestination = false;
			goalDeadline = now + RetreatDuration;
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
			if (currentGoal == Goal.Retreat || currentGoal == Goal.SeekCover)
			{
				return false;
			}

			currentGoal = Goal.Loot;
			tacticalDestination = itemPosition;
			hasTacticalDestination = true;
			goalDeadline = now + LootDuration;
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
