////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-only registry for explicitly configured mobile NPC brain drivers.
	/// This does not spawn, replicate, or modify legacy/static NPCs. It gives future
	/// faction, population, and cleanup systems a safe way to enumerate mobile agents.
	/// </summary>
	public static class MobileNPCRegistry
	{
		private static readonly HashSet<MobileNPCBrainDriver> activeDrivers = new HashSet<MobileNPCBrainDriver>();

		public static int Count => activeDrivers.Count;

		/// <summary>Counts living mobile NPCs without treating corpses awaiting cleanup as population.</summary>
		public static int CountLivingDrivers()
		{
			if (!Provider.isServer)
				return 0;

			activeDrivers.RemoveWhere(driver => driver == null || !driver.isActiveAndEnabled);
			int count = 0;
			foreach (MobileNPCBrainDriver driver in activeDrivers)
			{
				if (driver == null)
					continue;
				MobileNPCHumanoidVitals vitals = driver.GetComponent<MobileNPCHumanoidVitals>();
				if (vitals == null || !vitals.IsDead)
					count++;
			}
			return count;
		}

		internal static void Register(MobileNPCBrainDriver driver)
		{
			if (Provider.isServer && driver != null && driver.isActiveAndEnabled)
			{
				activeDrivers.Add(driver);
			}
		}

		internal static void Unregister(MobileNPCBrainDriver driver)
		{
			if (driver != null)
			{
				activeDrivers.Remove(driver);
			}
		}

		/// <summary>Broadcasts a server-confirmed gunshot as a stimulus to mobile NPCs in hearing range.</summary>
		public static void NotifyGunfire(Vector3 shooterPosition, float radius, Player shooter,
			Transform sourceNPC = null)
		{
			if (!Provider.isServer || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f)
				return;

			activeDrivers.RemoveWhere(driver => driver == null || !driver.isActiveAndEnabled);
			float radiusSquared = radius * radius;
			foreach (MobileNPCBrainDriver driver in activeDrivers)
			{
				if (driver == null || !driver.gameObject.activeInHierarchy
					|| sourceNPC != null && driver.transform == sourceNPC)
					continue;

				MobileNPCHumanoidVitals vitals = driver.GetComponent<MobileNPCHumanoidVitals>();
				if (vitals != null && vitals.IsDead)
					continue;

				Vector3 offset = driver.transform.position - shooterPosition;
				if (offset.sqrMagnitude > radiusSquared)
					continue;

				// An audible shot is not proof that this NPC is being shot at.
				// Do not grant the shooter's exact coordinates or Transform to
				// every listener; genuine impacts retain ReportIncomingFire.
				float distance = Mathf.Sqrt(offset.sqrMagnitude);
				float confidence = Mathf.Clamp01(1f - distance / Mathf.Max(1f, radius));
				if (confidence < 0.12f)
					continue;
				float uncertainty = Mathf.Lerp(8f, 1.5f, confidence);
				Vector2 error = Random.insideUnitCircle * uncertainty;
				Vector3 approximate = shooterPosition
					+ new Vector3(error.x, 0f, error.y);
				driver.ReportClassifiedNoiseServer(approximate,
					MobileNPCBrainDriver.InnateSoundClass.Gunshot,
					confidence, 22f);
			}
		}

		/// <summary>
		/// Deliver only plausibly audible footstep evidence. Listeners receive a
		/// noisy location, not a player Transform or continuously tracked position.
		/// Wall obstruction reduces hearing range, and distant listeners hear nothing.
		/// </summary>
		public static void NotifyPlayerFootstep(Vector3 footstepPosition, float radius, float confidence)
		{
			if (!Provider.isServer || radius <= 0f || float.IsNaN(radius)
				|| float.IsInfinity(radius))
				return;

			activeDrivers.RemoveWhere(driver => driver == null || !driver.isActiveAndEnabled);
			foreach (MobileNPCBrainDriver driver in activeDrivers)
			{
				if (driver == null || !driver.gameObject.activeInHierarchy)
					continue;
				MobileNPCHumanoidVitals vitals = driver.GetComponent<MobileNPCHumanoidVitals>();
				if (vitals != null && vitals.IsDead)
					continue;

				Vector3 ear = driver.transform.position + Vector3.up * 1.5f;
				Vector3 sound = footstepPosition + Vector3.up * 0.8f;
				Vector3 offset = sound - ear;
				float distance = offset.magnitude;
				if (distance > radius || distance < 0.1f)
					continue;

				// Structures attenuate rather than magically deleting every sound.
				bool occluded = false;
				RaycastHit[] obstructions = Physics.RaycastAll(ear, offset / distance,
					distance, RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
				for (int h = 0; h < obstructions.Length; ++h)
				{
					Collider collider = obstructions[h].collider;
					if (collider == null || collider.transform == driver.transform
						|| collider.transform.IsChildOf(driver.transform)
						|| collider.GetComponentInParent<Player>() != null
						|| collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
						continue;
					occluded = true;
					break;
				}
				float audibleRadius = radius * (occluded ? 0.4f : 1f);
				if (distance > audibleRadius)
					continue;

				float clarity = Mathf.Clamp01(confidence * (1f - distance / (audibleRadius + 0.01f)));
				if (clarity < 0.12f)
					continue;

				// Lower-confidence, distant sounds have proportionally worse bearing.
				float errorRadius = Mathf.Lerp(4.5f, 0.6f, clarity);
				Vector2 offset2D = Random.insideUnitCircle * errorRadius;
				Vector3 clue = footstepPosition + new Vector3(offset2D.x, 0f, offset2D.y);
				driver.ReportPlayerFootstepClue(clue, clarity);
			}
		}

		/// <summary>
		/// Copies currently active server drivers into a caller-owned list. Stale Unity
		/// objects are pruned, and the registry's internal collection is never exposed.
		/// </summary>
		public static void CopyActiveDrivers(List<MobileNPCBrainDriver> destination)
		{
			if (destination == null)
			{
				return;
			}

			destination.Clear();
			if (!Provider.isServer)
			{
				return;
			}

			activeDrivers.RemoveWhere(driver => driver == null || !driver.isActiveAndEnabled);
			destination.AddRange(activeDrivers);
		}
	}
}
