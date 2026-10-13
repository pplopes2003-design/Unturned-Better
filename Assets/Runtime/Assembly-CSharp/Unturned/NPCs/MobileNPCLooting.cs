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
		[SerializeField, Min(2f)] private float scanRadius = 28f;
		[SerializeField, Min(0.5f)] private float pickupDistance = 1.4f;
		[SerializeField, Min(0.5f)] private float scanInterval = 2.5f;
		[SerializeField, Range(0f, 1f)] private float buildingLootPreference = 0.25f;
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
			if (!Provider.isServer || !Level.isLoaded || driver == null || inventory == null)
			{
				ClearTarget();
				return;
			}

			if (!inventory.HasSpace)
			{
				// A full inventory invalidates the pickup objective; don't leave the motor
				// following a stale loot destination after the target has been discarded.
				ClearTarget(cancelLootGoal: true);
				return;
			}

			if (driver.Target != null || (driver.CurrentGoal != MobileNPCTactics.Goal.None && !(hasTarget && driver.CurrentGoal == MobileNPCTactics.Goal.Loot)))
			{
				// Combat or another tactical order takes precedence over looting.
				ClearTarget(cancelLootGoal: driver.CurrentGoal == MobileNPCTactics.Goal.Loot);
				return;
			}

			if (hasTarget && driver.CurrentGoal != MobileNPCTactics.Goal.Loot)
			{
				// The tactical goal can expire before the item disappears (for example,
				// when a table or curb blocks the final approach). Do not keep returning
				// forever with a stale target and no active movement destination.
				ClearTarget();
				nextScanTime = Time.time + scanInterval;
				return;
			}

			if (hasTarget)
			{
				if ((targetPosition - transform.position).sqrMagnitude <= pickupDistance * pickupDistance)
				{
					uint attemptedInstanceID = targetInstanceID;
					bool pickedUp = inventory.TryPickupWorldItemServer(attemptedInstanceID, transform.position, pickupDistance + 0.5f);
					ClearTarget();
					// Both success and failure must finish the loot goal. Failed transfers
					// are retried only after the bounded scan cooldown, not every frame.
					driver.ReportDestinationReached();
					nextScanTime = Time.time + scanInterval;
					if (!pickedUp)
					{
						Debug.Log("[MobileNPCLooting] Pickup transfer failed for item instance "
							+ targetInstanceID + " on " + name + "; will rescan after cooldown.", this);
					}
				}
				else if (Time.time >= nextScanTime)
				{
					// A dropped item may have been taken by the player or another NPC.
					if (!IsTargetStillPresent())
					{
						ClearTarget();
						driver.ReportDestinationReached();
						nextScanTime = Time.time + scanInterval;
					}
					else if (driver.CurrentGoal != MobileNPCTactics.Goal.Loot)
					{
						ClearTarget();
						nextScanTime = Time.time + scanInterval;
					}
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

			float bestScore = float.MinValue;
			InteractableItem bestItem = null;
			uint bestID = 0;
			for (int i = 0; i < candidates.Count; ++i)
			{
				InteractableItem item = candidates[i];
				if (item == null || item.transform == null)
				{
					continue;
				}

				if (!ItemManager.TryGetWorldItemInstanceID(item, out uint instanceID))
				{
					continue;
				}

				float distanceSqr = (item.transform.position - transform.position).sqrMagnitude;
				float score = ScoreItem(item, distanceSqr);
				if (score <= bestScore)
				{
					continue;
				}

				bestScore = score;
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

		/// <summary>Ranks drops by situational usefulness first and distance second.</summary>
		private float ScoreItem(InteractableItem item, float distanceSqr)
		{
			if (item == null || item.asset == null || inventory == null) return float.MinValue;
			EItemType type = item.asset.type;
			ushort itemID = item.jar != null && item.jar.item != null ? item.jar.item.id : (ushort)0;
			float score;

			switch (type)
			{
				case EItemType.MEDICAL:
					MobileNPCHumanoidVitals vitals = GetComponent<MobileNPCHumanoidVitals>();
					score = vitals != null && vitals.HealthFraction < 0.75f ? 100f + (1f - vitals.HealthFraction) * 35f : 24f;
					break;
				case EItemType.GUN:
					score = inventory.CountItemType(EItemType.GUN) == 0 ? 82f : 43f;
					break;
				case EItemType.MELEE:
					score = inventory.CountItemType(EItemType.GUN) == 0 && inventory.CountItemType(EItemType.MELEE) == 0 ? 78f : 30f;
					break;
				case EItemType.MAGAZINE:
					score = inventory.CountItemType(EItemType.GUN) > 0 ? 57f : 20f;
					break;
				case EItemType.FOOD:
				case EItemType.WATER:
				{
					MobileNPCSurvivalNeeds needs = GetComponent<MobileNPCSurvivalNeeds>();
					score = needs != null ? needs.GetNeedPriority(type) : 52f;
					break;
				}
				case EItemType.BACKPACK:
					score = 46f;
					break;
				case EItemType.VEST:
				case EItemType.HAT:
				case EItemType.SHIRT:
				case EItemType.PANTS:
				case EItemType.MASK:
				case EItemType.GLASSES:
					score = 34f;
					break;
				case EItemType.BARRICADE:
				case EItemType.STORAGE:
				case EItemType.STRUCTURE:
				{
					MobileNPCSettlementPlanner settlementPlanner = GetComponent<MobileNPCSettlementPlanner>();
					bool needsSettlementSupplies = settlementPlanner != null && !settlementPlanner.HasChosenSite
						&& inventory.CountItemType(EItemType.BARRICADE) + inventory.CountItemType(EItemType.STORAGE)
						+ inventory.CountItemType(EItemType.STRUCTURE) < 3;
					score = needsSettlementSupplies ? 58f + buildingLootPreference * 20f : 12f + buildingLootPreference * 45f;
					break;
				}
				case EItemType.SUPPLY:
					// Supply-category items are not necessarily placeable buildables. Keep
					// them at ordinary loot priority instead of counting them toward a base.
					score = 18f;
					break;
				default:
					score = 8f;
					break;
			}

			if (itemID != 0 && inventory.ContainsItemID(itemID)) score -= 28f;
			// The nearest item gets a small advantage, but cannot outrank a critical need.
			score -= Mathf.Sqrt(distanceSqr) * 1.8f;
			// Avoid filling the final slot with low-value clutter.
			if (inventory.FreeSlots <= 2 && score < 55f) score -= 18f;
			return score;
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

		private void ClearTarget(bool cancelLootGoal = false)
		{
			if (cancelLootGoal && driver != null && driver.CurrentGoal == MobileNPCTactics.Goal.Loot)
			{
				driver.ReportDestinationReached();
			}
			hasTarget = false;
			targetInstanceID = 0;
		}
	}
}
