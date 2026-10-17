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
		// Do not reveal every unloaded/hidden crate inside a 220m global scan.
		// A landed crate is first-hand evidence only within local visual reach.
		private const float MaximumSearchDistance = 28f;
		private const float LootDistance = 3.5f;
		private static float nextScanTime;
		private static readonly List<MobileNPCBrainDriver> Drivers = new List<MobileNPCBrainDriver>(32);
		private static readonly RaycastHit[] VisualObstructions = new RaycastHit[16];

		public static void ResetServer()
		{
			nextScanTime = 0f;
			Drivers.Clear();
		}

		public static void TickServer()
		{
			if (!Provider.isServer || !Level.isLoaded || MobileNPCSettings.CombatOnly
				|| !MobileNPCSettings.AirdropContestEnabled || Time.time < nextScanTime)
				return;

			nextScanTime = Time.time + ScanInterval;
			MobileNPCRegistry.CopyActiveDrivers(Drivers);
			if (Drivers.Count == 0)
				return;

			InteractableStorage[] storages = Object.FindObjectsOfType<InteractableStorage>();
			for (int i = 0; i < Drivers.Count; ++i)
			{
				MobileNPCBrainDriver driver = Drivers[i];
				if (driver == null || driver.Target != null)
					continue;
				MobileNPCAirdropRumor radioRumor = driver.GetComponent<MobileNPCAirdropRumor>();
				bool checkingRadioRumor = radioRumor != null
					&& radioRumor.IsInvestigatingAirdropServer;
				if (driver.CurrentGoal != MobileNPCTactics.Goal.None
					&& driver.CurrentGoal != MobileNPCTactics.Goal.Loot
					&& !(checkingRadioRumor
						&& driver.CurrentGoal == MobileNPCTactics.Goal.Investigate))
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
					if (distanceSquared < bestDistanceSquared
						&& CanSeeCrateServer(driver.transform.position,
							storage.transform))
					{
						bestDistanceSquared = distanceSquared;
						nearest = storage;
					}
				}

				if (nearest == null)
					continue;

				// Report only a genuinely nearby landed carepackage, not a
				// remotely scanned crate. The radio outbox independently checks
				// owned equipment, faction/group and its transmission cooldown.
				if (bestDistanceSquared <= 28f * 28f)
				{
					MobileNPCRadioOutbox radio = driver.GetComponent<MobileNPCRadioOutbox>();
					if (radio != null)
						radio.TryQueueObservedAirdrop(nearest.transform.position);
				}

				// Only after physically verifying a real, visible, nonempty
				// carepackage may a listener promote uncertain radio hearsay to
				// an ordinary loot objective with native item validation.
				if (checkingRadioRumor)
					radioRumor.TryInvestigateConfirmedCrateServer(nearest.transform.position);

				if (driver.CurrentGoal == MobileNPCTactics.Goal.Investigate)
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

		/// <summary>
		/// Reject observed reports and crate looting through buildings, fences
		/// and walls. The NPC must genuinely have line of sight, not merely have
		/// a crate in a region-wide authoritative scan. This probe is only run
		/// for candidates within the 28m visible-local envelope every 2s.
		/// </summary>
		private static bool CanSeeCrateServer(Vector3 observer, Transform crate)
		{
			if (crate == null)
				return false;
			Vector3 from = observer + Vector3.up * 1.35f;
			Vector3 to = crate.position + Vector3.up * 0.8f;
			Vector3 direction = to - from;
			float distance = direction.magnitude;
			if (distance <= 0.05f || distance > MaximumSearchDistance + 2f)
				return false;
			int count = Physics.RaycastNonAlloc(from, direction / distance,
				VisualObstructions, distance, RayMasks.BLOCK_COLLISION,
				QueryTriggerInteraction.Ignore);
			// A saturated buffer could hide an actual occluder.
			if (count >= VisualObstructions.Length)
				return false;
			for (int hitIndex = 0; hitIndex < count; hitIndex++)
			{
				Collider obstacle = VisualObstructions[hitIndex].collider;
				if (obstacle == null || obstacle.transform == crate
					|| obstacle.transform.IsChildOf(crate))
					continue;
				return false;
			}
			return true;
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