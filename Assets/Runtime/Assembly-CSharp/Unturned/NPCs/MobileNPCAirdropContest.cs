////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Lets mobile NPCs contest landed vanilla airdrop crates. It only targets storage
	/// objects marked by the game's CarepackageDestroy component, never ordinary chests.
	/// Loot transfer is server-local and does not change vanilla airdrop spawning.
	/// </summary>
	internal static class MobileNPCAirdropContest
	{
		private const float ScanInterval = 2f;
		private const float MaximumSearchDistance = 220f;
		private const float LootDistance = 3.5f;
		private static float nextScanTime;
		private static readonly List<MobileNPCBrainDriver> Drivers = new List<MobileNPCBrainDriver>(32);

		public static void ResetServer()
		{
			nextScanTime = 0f;
			Drivers.Clear();
		}

		public static void TickServer()
		{
			if (!Provider.isServer || !Level.isLoaded || Time.time < nextScanTime)
				return;

			nextScanTime = Time.time + ScanInterval;
			MobileNPCRegistry.CopyActiveDrivers(Drivers);
			if (Drivers.Count == 0)
				return;

			InteractableStorage[] storages = Object.FindObjectsOfType<InteractableStorage>();
			for (int i = 0; i < Drivers.Count; ++i)
			{
				MobileNPCBrainDriver driver = Drivers[i];
				if (driver == null || driver.Target != null
					|| driver.CurrentGoal != MobileNPCTactics.Goal.None && driver.CurrentGoal != MobileNPCTactics.Goal.Loot)
					continue;

				MobileNPCInventory inventory = driver.GetComponent<MobileNPCInventory>();
				MobileNPCHumanoidVitals vitals = driver.GetComponent<MobileNPCHumanoidVitals>();
				if (inventory == null || !inventory.HasSpace || vitals != null && vitals.IsDead)
					continue;

				InteractableStorage nearest = null;
				float bestDistanceSquared = MaximumSearchDistance * MaximumSearchDistance;
				for (int storageIndex = 0; storageIndex < storages.Length; ++storageIndex)
				{
					InteractableStorage storage = storages[storageIndex];
					if (storage == null || !storage.gameObject.activeInHierarchy
						|| storage.GetComponent<CarepackageDestroy>() == null
						|| storage.items == null || storage.items.getItemCount() == 0)
						continue;

					Vector3 offset = storage.transform.position - driver.transform.position;
					float distanceSquared = offset.sqrMagnitude;
					if (distanceSquared < bestDistanceSquared)
					{
						bestDistanceSquared = distanceSquared;
						nearest = storage;
					}
				}

				if (nearest == null)
					continue;

				if (bestDistanceSquared <= LootDistance * LootDistance)
				{
					if (TransferAirdropLootServer(nearest, inventory))
					{
						driver.ReportDestinationReached();
						Debug.Log("[MobileNPCAirdropContest] " + driver.name + " looted a landed airdrop crate.");
					}
				}
				else if (driver.CurrentGoal == MobileNPCTactics.Goal.None)
				{
					driver.TryStartLoot(nearest.transform.position);
				}
			}
		}

		private static bool TransferAirdropLootServer(InteractableStorage storage, MobileNPCInventory inventory)
		{
			if (!Provider.isServer || storage == null || storage.items == null || inventory == null)
				return false;

			bool transferredAny = false;
			for (int index = storage.items.getItemCount() - 1; index >= 0 && inventory.HasSpace; --index)
			{
				ItemJar jar = storage.items.getItem((byte)index);
				if (jar == null || jar.item == null || jar.item.id == 0 || jar.item.amount == 0)
					continue;

				if (!inventory.TryStoreTransferredItem(jar.item))
					continue;

				storage.items.removeItem((byte)index);
				transferredAny = true;
			}

			if (transferredAny)
				storage.items.onStateUpdated?.Invoke();
			return transferredAny;
		}
	}
}