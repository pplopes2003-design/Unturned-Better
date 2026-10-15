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
		private MobileNPCAdaptiveMemory adaptiveMemory;
		private MobileNPCSupplyKnowledge supplyKnowledge;
		private MobileNPCGroupMember groupMember;
		private MobileNPCAutonomyPlanner autonomyPlanner;
		private bool hasRememberedSectorClaim;
		private MobileNPCUsefulLootRumor radioLootRumor;
		private MobileNPCRadioOutbox radioOutbox;
		private ushort targetItemID;
		private byte targetItemRounds;
		private bool isRememberedSupplyTrip;
		private ushort rememberedItemID;
		private Vector3 rememberedSitePosition;
		private Vector3 rememberedGroundDestination;
		private float rememberedArrivalAt;
		private float rememberedTripExpiresAt;
		private float nextRememberedSearchAt;
		private uint targetInstanceID;
		private Vector3 targetPosition;
		private bool hasTarget;
		private uint temporarilyInaccessibleInstanceID;
		private float temporarilyInaccessibleUntil;
		private float nextScanTime;

		private void Awake()
		{
			driver = GetComponent<MobileNPCBrainDriver>();
			inventory = GetComponent<MobileNPCInventory>();
			adaptiveMemory = GetComponent<MobileNPCAdaptiveMemory>();
			supplyKnowledge = GetComponent<MobileNPCSupplyKnowledge>();
			groupMember = GetComponent<MobileNPCGroupMember>();
			autonomyPlanner = GetComponent<MobileNPCAutonomyPlanner>();
			radioLootRumor = GetComponent<MobileNPCUsefulLootRumor>();
			radioOutbox = GetComponent<MobileNPCRadioOutbox>();
		}

		private void Update()
		{
			if (!Provider.isServer || !Level.isLoaded || driver == null || inventory == null)
			{
				ClearTarget();
				return;
			}

			// Runtime template components are attached while inactive; resolve
			// late-added radio modules defensively after first activation too.
			if (radioLootRumor == null)
				radioLootRumor = GetComponent<MobileNPCUsefulLootRumor>();
			if (radioOutbox == null)
				radioOutbox = GetComponent<MobileNPCRadioOutbox>();
			UpdateRememberedSupplyTripServer();
			bool radioLootSearch = radioLootRumor != null
				&& radioLootRumor.IsActivelySearchingServer;

			if (!inventory.HasSpace)
			{
				CancelRememberedSupplyTripServer(true);
				// A full inventory invalidates pickup, NOT the ability to warn
				// allies about a physically observed useful world drop.
				ClearTarget(cancelLootGoal: true);
				if (driver.Target == null && driver.CurrentGoal == MobileNPCTactics.Goal.None
					&& Time.time >= nextScanTime)
				{
					nextScanTime = Time.time + scanInterval;
					TryReportVisibleWorldLootWhileFullServer();
				}
				return;
			}

			if (driver.Target != null || (driver.CurrentGoal != MobileNPCTactics.Goal.None
				&& !(hasTarget && driver.CurrentGoal == MobileNPCTactics.Goal.Loot)
				&& !(isRememberedSupplyTrip && driver.CurrentGoal == MobileNPCTactics.Goal.Investigate)
				&& !(radioLootSearch && driver.CurrentGoal == MobileNPCTactics.Goal.Investigate)))
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
				// The route/goal timed out while this item remained selected.
				// This is observable failure evidence, not proof of an impassable path.
				if (adaptiveMemory != null)
					adaptiveMemory.RecordOwnOutcomeServer(MobileNPCAdaptiveMemory.ExperienceKind.LootAccess, targetPosition, false);
				// An inaccessible item must not be immediately reselected
				// after the motor's physically confirmed navigation failure.
				temporarilyInaccessibleInstanceID = targetInstanceID;
				temporarilyInaccessibleUntil = Time.time + 75f;
				ClearTarget();
				nextScanTime = Time.time + scanInterval;
				return;
			}

			if (hasTarget)
			{
				// A table or shelf is a reach target, not a location where the
				// NPC's feet should stand. Check planar proximity independently
				// of item height, then let ItemManager enforce real hand-to-item
				// distance, LOS, authoritative identity and storage capacity.
				Vector3 reachOffset = targetPosition - transform.position;
				float itemHeight = Mathf.Abs(reachOffset.y);
				reachOffset.y = 0f;
				if (reachOffset.sqrMagnitude <= pickupDistance * pickupDistance
					&& itemHeight <= 2.1f)
				{
					uint attemptedInstanceID = targetInstanceID;
					bool pickedUp = inventory.TryPickupWorldItemServer(attemptedInstanceID,
						transform.position, pickupDistance + 0.5f, out string failureReason);
					// Any learned source must be an ACTUAL completed transfer, not
					// seeing loot through walls or a guaranteed respawn table.
					if (pickedUp && supplyKnowledge != null && targetItemID != 0)
						supplyKnowledge.RecordSuccessfulPickupServer(targetItemID, targetItemRounds, targetPosition);
					if (pickedUp && isRememberedSupplyTrip && targetItemID == rememberedItemID)
						CancelRememberedSupplyTripServer(false);
					if (adaptiveMemory != null)
						adaptiveMemory.RecordOwnOutcomeServer(MobileNPCAdaptiveMemory.ExperienceKind.LootAccess, targetPosition, pickedUp);
					ClearTarget();
					// Both success and failure must finish the loot goal. Failed transfers
					// are retried only after the bounded scan cooldown, not every frame.
					driver.ReportDestinationReached();
					nextScanTime = Time.time + scanInterval;
					if (!pickedUp)
					{
						temporarilyInaccessibleInstanceID = attemptedInstanceID;
						temporarilyInaccessibleUntil = Time.time + 35f;
						Debug.Log("[MobileNPCLooting] Pickup transfer failed for item instance "
							+ attemptedInstanceID + " on " + name
							+ "; reason=" + (failureReason ?? "unspecified")
							+ "; root=" + transform.position + "; item=" + targetPosition
							+ "; will rescan after cooldown.", this);
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
			float bestObservedRadioScore = float.MinValue;
			InteractableItem bestObservedForRadio = null;
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
				if (instanceID == temporarilyInaccessibleInstanceID
					&& Time.time < temporarilyInaccessibleUntil)
					continue;

				float distanceSqr = (item.transform.position - transform.position).sqrMagnitude;

				// A radio rumor only allows a locally re-seen matching native
				// world item to supersede the uncertain investigation goal.
				// Do not turn the heard position into a world-instance lookup.
				if (radioLootSearch)
				{
					if (item.jar != null && item.jar.item != null
						&& item.jar.item.id == radioLootRumor.ExpectedItemID
						&& radioLootRumor.TryConfirmLocallySeenItemServer(item))
					{
						targetItemID = item.jar.item.id;
						targetItemRounds = item.jar.item.amount;
						targetInstanceID = instanceID;
						targetPosition = item.transform.position;
						hasTarget = true;
						return;
					}
					continue;
				}

				// Actual firsthand local sightings can be announced to the
				// group, whether this NPC personally needs the item or not.
				// LOS and a physically registered world drop are mandatory.
				if (radioOutbox != null && distanceSqr <= 17f * 17f)
				{
					float importance = MobileNPCUsefulLootVision.IntrinsicImportance(item.asset);
					float broadcastScore = importance - Mathf.Sqrt(distanceSqr) * 0.5f;
					if (importance >= 60f && broadcastScore > bestObservedRadioScore
						&& MobileNPCUsefulLootVision.CanSeeWorldDropServer(
							item, transform, 17f))
					{
						bestObservedRadioScore = broadcastScore;
						bestObservedForRadio = item;
					}
				}

				float score = ScoreItem(item, distanceSqr);
				if (score <= bestScore)
					continue;

				// Region enumeration includes items behind walls and on other floors.
				// Only physically SEEN native drops can become ordinary scavenging
				// goals. Otherwise hidden loot repeatedly directs NPCs into a wall.
				// Bound visual acquisition to 17m rather than claiming omniscient
				// knowledge of every item in the 28m world-region scan.
				if (distanceSqr > 17f * 17f
					|| !MobileNPCUsefulLootVision.CanSeeWorldDropServer(item, transform, 17f,
						requireRadioUsefulCategory: false))
					continue;

				bestScore = score;
				bestItem = item;
				bestID = instanceID;
			}

			if (radioLootSearch)
				return;

			if (bestObservedForRadio != null)
				radioOutbox.TryQueueObservedUsefulLootServer(bestObservedForRadio);

			if (bestItem == null && !isRememberedSupplyTrip)
				TryPlanRememberedSupplySearchServer();

			if (bestItem != null && driver.TryStartLoot(bestItem.transform.position))
			{
				targetItemID = bestItem.jar != null && bestItem.jar.item != null
					? bestItem.jar.item.id : (ushort)0;
				targetItemRounds = bestItem.jar != null && bestItem.jar.item != null
					? bestItem.jar.item.amount : (byte)0;
				targetInstanceID = bestID;
				targetPosition = bestItem.transform.position;
				hasTarget = true;
			}
		}

		/// <summary>
		/// A full pack should not prevent a survivor from warning equipped
		/// teammates of a genuine observed nearby item. No inventory transfer
		/// or tactical loot goal can start on this path.
		/// </summary>
		private void TryReportVisibleWorldLootWhileFullServer()
		{
			if (!Provider.isServer || radioOutbox == null || ItemManager.regions == null)
				return;
			searchRegions.Clear();
			candidates.Clear();
			const float broadcastRange = 17f;
			Regions.getRegionsInRadius(transform.position, broadcastRange, searchRegions);
			ItemManager.getItemsInRadius(transform.position,
				broadcastRange * broadcastRange, searchRegions, candidates);
			InteractableItem best = null;
			float highestScore = float.NegativeInfinity;
			for (int i = 0; i < candidates.Count; i++)
			{
				InteractableItem item = candidates[i];
				if (item == null || item.asset == null || item.transform == null)
					continue;
				float importance = MobileNPCUsefulLootVision.IntrinsicImportance(item.asset);
				if (importance < 60f)
					continue;
				float distance = Vector3.Distance(transform.position, item.transform.position);
				float score = importance - distance * 0.5f;
				if (score <= highestScore
					|| !MobileNPCUsefulLootVision.CanSeeWorldDropServer(
						item, transform, broadcastRange))
					continue;
				highestScore = score;
				best = item;
			}
			if (best != null)
				radioOutbox.TryQueueObservedUsefulLootServer(best);
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
				case EItemType.THROWABLE:
				{
					ItemThrowableAsset grenade = item.asset as ItemThrowableAsset;
					// No interest in smoke, flash, unimplemented sticky/impact,
					// or another explosive if the NPC already has a few.
					score = grenade != null && grenade.isExplosive
						&& !grenade.isSticky && !grenade.explodeOnImpact
						&& inventory.CountItemType(EItemType.THROWABLE) < 2
						? 64f : 10f;
					break;
				}
				case EItemType.MELEE:
					score = inventory.CountItemType(EItemType.GUN) == 0 && inventory.CountItemType(EItemType.MELEE) == 0 ? 78f : 30f;
					break;
				case EItemType.MAGAZINE:
				{
					score = 20f;
					ItemMagazineAsset magazine = item.asset as ItemMagazineAsset;
					if (magazine != null && inventory.TryGetBestGun(out Item heldGun, out ItemGunAsset ownedGun)
						&& ownedGun.magazineCalibers != null && magazine.calibers != null
						&& magazine.CalibersContainAnyOfIds(ownedGun.magazineCalibers))
					{
						bool runningLow = heldGun.state != null && heldGun.state.Length > 10
							&& heldGun.state[10] <= 8
							&& !inventory.TryFindCompatibleMagazine(ownedGun,
								out int reserveSlot, out ItemMagazineAsset reserveAsset);
						score = runningLow ? 104f : 64f;
					}
					break;
				}
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

			// A first genuine portable walkie has group survival utility.
			// The world item still has to exist, be reachable, and be picked
			// up through ordinary ItemManager/inventory transactions. Never
			// conjure a radio or prioritize it above urgent medical supplies.
			if (itemID == MobileNPCRadioOutbox.VanillaWalkieTalkieID
				&& !inventory.ContainsItemID(itemID))
			{
				MobileNPCGroupMember group = GetComponent<MobileNPCGroupMember>();
				if (group != null && group.GroupId > 0)
					score = Mathf.Max(score, 62f);
			}
			if (itemID != 0 && inventory.ContainsItemID(itemID)) score -= 28f;
			// A personal, bounded preference from *resolved* past loot attempts. Never
			// replaces the item-type priority or the native pickup/path validation.
			if (adaptiveMemory != null && item.transform != null)
				score += adaptiveMemory.GetLootScoreAdjustment(item.transform.position);
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
			targetItemID = 0;
			targetItemRounds = 0;
		}

		/// <summary>
		/// When real survival/equipment needs are unmet, revisit an observed
		/// site for the useful item type. Location is a hypothesis, not a spawn.
		/// </summary>
		private void TryPlanRememberedSupplySearchServer()
		{
			if (supplyKnowledge == null || Time.time < nextRememberedSearchAt
				|| driver == null || driver.Target != null
				|| driver.CurrentGoal != MobileNPCTactics.Goal.None)
				return;
			nextRememberedSearchAt = Time.time + 40f;
			MobileNPCHumanoidVitals vitals = GetComponent<MobileNPCHumanoidVitals>();
			MobileNPCInjurySystem injuries = GetComponent<MobileNPCInjurySystem>();
			MobileNPCSurvivalNeeds survival = GetComponent<MobileNPCSurvivalNeeds>();
			// The personal need scorer remains authoritative. The group only
			// filters sites a teammate is already examining, so an NPC with
			// several independently learned useful sites can pick another one.
			System.Func<Vector3, bool> availableSector = groupMember != null
				&& groupMember.GroupId > 0
				? (System.Func<Vector3, bool>)groupMember.IsActivitySectorAvailableServer : null;
			if (!supplyKnowledge.TrySuggestNeededSupplySiteServer(inventory,
				vitals, injuries, survival, transform.position,
				out Vector3 rememberedPosition, out ushort itemID, availableSector))
				return;

			// Validate actual map ground; native pathfinding must still walk to it.
			if (!MobileNPCSpawnService.TryFindSafeGroundPosition(rememberedPosition,
				12f, 8f, RayMasks.BLOCK_COLLISION, 38f, out Vector3 reachableGround, out _)
				|| (reachableGround - rememberedPosition).sqrMagnitude > 100f)
				return;

			// The individual discovered this site previously or heard about it
			// through the existing local-talk lesson system. Claim its physical
			// neighborhood from the real group, not a magical global loot table.
			// Scouting may have an obsolete roam claim: relinquish it first.
			if (autonomyPlanner != null)
				autonomyPlanner.ReleaseExplorationSectorServer(false, false);
			bool claimed = groupMember != null && groupMember.GroupId > 0;
			if (claimed && !groupMember.TryClaimActivitySectorServer(reachableGround, 75f))
				return;
			if (!driver.TryStartRememberedSupplySearch(reachableGround))
			{
				if (claimed)
					groupMember.ReleaseActivitySectorServer(false, false);
				return;
			}

			hasRememberedSectorClaim = claimed;
			isRememberedSupplyTrip = true;
			rememberedItemID = itemID;
			rememberedSitePosition = rememberedPosition;
			rememberedGroundDestination = reachableGround;
			rememberedTripExpiresAt = Time.time + 65f;
			rememberedArrivalAt = -1f;
			Debug.Log("[MobileNPCLooting] " + name
				+ " checking previously observed supply item=" + itemID
				+ ", site=" + rememberedPosition
				+ " (not guaranteed to exist now).", this);
		}

		private void UpdateRememberedSupplyTripServer()
		{
			if (!isRememberedSupplyTrip)
				return;
			// Combat interrupts supply errands without training a false failure.
			if (driver == null || driver.Target != null
				|| driver.CurrentGoal == MobileNPCTactics.Goal.Retreat
				|| driver.CurrentGoal == MobileNPCTactics.Goal.SeekCover
				|| driver.CurrentGoal == MobileNPCTactics.Goal.Stalk)
			{
				CancelRememberedSupplyTripServer(true);
				return;
			}

			// A site can be 6m away through a wall or directly overhead on
			// another floor. That is NOT a physically checked/visited area.
			// Evaluate arrival at the actual reachable ground destination,
			// then require an unobstructed firsthand view of the memory area.
			Vector3 offset = rememberedGroundDestination - transform.position;
			float verticalDifference = Mathf.Abs(offset.y);
			offset.y = 0f;
			bool physicallyChecked = offset.sqrMagnitude <= 2.5f * 2.5f
				&& verticalDifference <= 1.2f
				&& !Physics.Linecast(transform.position + Vector3.up * 1.45f,
					rememberedSitePosition + Vector3.up * 0.23f,
					RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
			if (rememberedArrivalAt < 0f && physicallyChecked)
			{
				rememberedArrivalAt = Time.time;
				// Unlock normal loot acquisition on arrival. Keeping the
				// Investigate goal would reject TryStartLoot for nearby drops.
				if (driver.CurrentGoal == MobileNPCTactics.Goal.Investigate)
					driver.ReportDestinationReached();
			}

			bool visitedAndChecked = rememberedArrivalAt >= 0f
				&& Time.time >= rememberedArrivalAt + 5f;
			bool couldNotReach = Time.time >= rememberedTripExpiresAt && !hasTarget;
			if ((visitedAndChecked && !hasTarget) || couldNotReach)
			{
				if (supplyKnowledge != null)
					supplyKnowledge.RecordUnsuccessfulVisitServer(
						rememberedItemID, rememberedSitePosition);
				CancelRememberedSupplyTripServer(true, couldNotReach && rememberedArrivalAt < 0f);
			}
		}

		private void OnDisable()
		{
			// Death disables the looting component; tactical reactivation must
			// not inherit a dead actor's exclusive remembered-site claim.
			CancelRememberedSupplyTripServer(true);
		}

		private void CancelRememberedSupplyTripServer(bool finishInvestigation, bool blocked = false)
		{
			if (!isRememberedSupplyTrip)
				return;
			isRememberedSupplyTrip = false;
			if (hasRememberedSectorClaim && groupMember != null)
				groupMember.ReleaseActivitySectorServer(rememberedArrivalAt >= 0f, blocked);
			hasRememberedSectorClaim = false;
			rememberedArrivalAt = -1f;
			rememberedItemID = 0;
			if (finishInvestigation && driver != null
				&& driver.CurrentGoal == MobileNPCTactics.Goal.Investigate)
				driver.ReportDestinationReached();
		}
	}
}
