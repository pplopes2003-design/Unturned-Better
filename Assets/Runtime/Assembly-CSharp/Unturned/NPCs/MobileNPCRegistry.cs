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
		public static void NotifyGunfire(Vector3 shooterPosition, float radius, Player shooter)
		{
			if (!Provider.isServer || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f)
				return;

			activeDrivers.RemoveWhere(driver => driver == null || !driver.isActiveAndEnabled);
			float radiusSquared = radius * radius;
			foreach (MobileNPCBrainDriver driver in activeDrivers)
			{
				if (driver == null || !driver.gameObject.activeInHierarchy)
					continue;

				MobileNPCHumanoidVitals vitals = driver.GetComponent<MobileNPCHumanoidVitals>();
				if (vitals != null && vitals.IsDead)
					continue;

				Vector3 offset = driver.transform.position - shooterPosition;
				if (offset.sqrMagnitude > radiusSquared)
					continue;

				driver.ReportIncomingFire(shooterPosition, shooter != null ? shooter.transform : null);
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
