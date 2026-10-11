////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Experimental single-player server-side loot loop. It scans nearby authoritative
	/// drops only while the NPC has no combat target or existing tactical goal, walks to
	/// one candidate, and asks ItemManager to transfer it on arrival.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	[RequireComponent(typeof(MobileNPCInventory))]
	public sealed class MobileNPCLooting : MonoBehaviour
	{
		[SerializeField, Min(2f)] private float scanRadius = 10f;
		[SerializeField, Min(0.5f)] private float pickupDistance = 1.4f;
		[SerializeField, Min(0.5f)] private float scanInterval = 2.5f;
		private readonly List<RegionCoordinate> searchRegions = new List<RegionCoordinate>(16);
		private readonly List<InteractableItem> candidates = new List<InteractableItem>(32);
		private MobileNPCBrainDriver driver;
		private MobileNPCInventory inventory;
		private uint targetInstanceID;
		private Vector3 targetPosition;
		private bool hasTarget;
		private float nextScanTime;

		private void Awake()
		{
			driver = GetComponent<MobileNPCBrainDriver>();
			inventory = GetComponent<MobileNPCInventory>();
		}

		private void Update()
		{
			if (!Provider.isServer || !Level.isLoaded || driver == null || inventory == null || !inventory.HasSpace)
			{
				ClearTarget();
				return;
			}

			if (driver.Target != null || (driver.CurrentGoal != MobileNPCTactics.Goal.None && !(hasTarget && driver.CurrentGoal == MobileNPCTactics.Goal.Loot)))
			{
				ClearTarget();
				return;
			}

			if (hasTarget)
			{
				if ((targetPosition - transform.position).sqrMagnitude <= pickupDistance * pickupDistance)
				{
					bool pickedUp = inventory.TryPickupWorldItemServer(targetInstanceID, transform.position, pickupDistance + 0.5f);
					ClearTarget();
					driver.ReportDestinationReached();
				}
				else if (Time.time >= nextScanTime)
				{
					// A dropped item may have been taken by the player or another NPC.
					if (!IsTargetStillPresent())
					{
						ClearTarget();
						driver.ReportDestinationReached();
					}
					nextScanTime = Time.time + scanInterval;
				}
				return;
			}

			if (Time.time < nextScanTime)
			{
				return;
			}
			nextScanTime = Time.time + scanInterval;
			if (ItemManager.regions == null)
			{
				return;
			}

			searchRegions.Clear();
			candidates.Clear();
			Regions.getRegionsInRadius(transform.position, scanRadius, searchRegions);
			ItemManager.getItemsInRadius(transform.position, scanRadius * scanRadius, searchRegions, candidates);

			float bestDistanceSqr = float.MaxValue;
			InteractableItem bestItem = null;
			uint bestID = 0;
			for (int i = 0; i < candidates.Count; ++i)
			{
				InteractableItem item = candidates[i];
				if (item == null || item.transform == null)
				{
					continue;
				}

				float distanceSqr = (item.transform.position - transform.position).sqrMagnitude;
				if (distanceSqr >= bestDistanceSqr || !ItemManager.TryGetWorldItemInstanceID(item, out uint instanceID))
				{
					continue;
				}

				bestDistanceSqr = distanceSqr;
				bestItem = item;
				bestID = instanceID;
			}

			if (bestItem != null && driver.TryStartLoot(bestItem.transform.position))
			{
				targetInstanceID = bestID;
				targetPosition = bestItem.transform.position;
				hasTarget = true;
			}
		}

		private bool IsTargetStillPresent()
		{
			if (ItemManager.regions == null)
			{
				return false;
			}

			searchRegions.Clear();
			candidates.Clear();
			Regions.getRegionsInRadius(transform.position, scanRadius + 2f, searchRegions);
			ItemManager.getItemsInRadius(transform.position, (scanRadius + 2f) * (scanRadius + 2f), searchRegions, candidates);
			for (int i = 0; i < candidates.Count; ++i)
			{
				if (candidates[i] != null && ItemManager.TryGetWorldItemInstanceID(candidates[i], out uint id) && id == targetInstanceID)
				{
					return true;
				}
			}
			return false;
		}

		private void ClearTarget()
		{
			hasTarget = false;
			targetInstanceID = 0;
		}
	}
}
