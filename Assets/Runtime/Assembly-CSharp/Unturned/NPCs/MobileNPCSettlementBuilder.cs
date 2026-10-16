////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt file for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using Steamworks;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-authoritative, deliberately constrained settlement pattern:
	/// one central storage barricade when available, followed by up to four
	/// defensive pieces on cardinal points. This is not free-form construction.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCSettlementPlanner))]
	[RequireComponent(typeof(MobileNPCInventory))]
	[RequireComponent(typeof(MobileNPCHumanoidMotor))]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	public sealed class MobileNPCSettlementBuilder : MonoBehaviour
	{
		[SerializeField, Min(1f)] private float initialBuildDelay = 12f;
		[SerializeField, Min(2f)] private float buildInterval = 18f;
		[SerializeField, Range(1, 5)] private int maximumPieces = 5;
		[SerializeField, Min(2f)] private float perimeterRadius = 3f;
		[SerializeField, Min(0.5f)] private float clearanceRadius = 0.8f;
		[SerializeField, Min(1f)] private float searchHeight = 40f;
		[SerializeField, Min(0f)] private float maximumDrop = 45f;
		[SerializeField, Range(0f, 60f)] private float maximumSlope = 25f;
		[SerializeField] private LayerMask groundMask = ~0;
		[SerializeField] private LayerMask obstructionMask = ~0;

		private MobileNPCSettlementPlanner planner;
		private MobileNPCInventory inventory;
		private MobileNPCHumanoidMotor motor;
		private MobileNPCBrainDriver driver;
		private float nextBuildTime;
		private float nextStorageDeliveryAt;
		private int placedPieces;
		private bool centralStoragePlaced;
		private bool generatorPlaced;
		private int perimeterPiecesPlaced;
		private InteractableStorage centralStorage;
		private InteractableGenerator managedGenerator;
		private bool hasReconciledExistingSettlement;

		public int PlacedPieces => placedPieces;

		/// <summary>
		/// Exposes only the leader's genuine saved/placed central storage.
		/// No world search is performed during radio transmissions.
		/// </summary>
		public bool TryGetCentralStorageForRadioServer(out InteractableStorage storage)
		{
			storage = null;
			MobileNPCGroupMember member = GetComponent<MobileNPCGroupMember>();
			if (!Provider.isServer || member == null || !member.CanPlanSettlement
				|| !member.HasSettlement || planner == null || !planner.HasChosenSite
				|| centralStorage == null || !centralStorage.gameObject.activeInHierarchy
				|| centralStorage.items == null
				|| centralStorage.owner != CSteamID.Nil || centralStorage.group != CSteamID.Nil
				|| (centralStorage.transform.position - member.GroupAnchor).sqrMagnitude > 3.5f * 3.5f)
				return false;
			BarricadeDrop nativeDrop =
				BarricadeDrop.FindByTransformFastMaybeNull(centralStorage.transform);
			BarricadeData nativeData = nativeDrop != null ? nativeDrop.GetServersideData() : null;
			if (nativeDrop == null || nativeData == null
				|| nativeData.owner != 0UL || nativeData.group != 0UL
				|| !(nativeDrop.asset is ItemBarricadeAsset)
				|| ((ItemBarricadeAsset)nativeDrop.asset).build != EBuild.STORAGE)
				return false;
			storage = centralStorage;
			return true;
		}

		/// <summary>
		/// Returns only a real native, zero-owner world generator associated with
		/// this living leader's settlement. It is cached at placement/restore;
		/// never select a nearby player's private generator for NPC refueling.
		/// </summary>
		public bool TryGetManagedGeneratorForPowerServer(out InteractableGenerator generator)
		{
			generator = null;
			MobileNPCGroupMember member = GetComponent<MobileNPCGroupMember>();
			if (!Provider.isServer || member == null || !member.CanPlanSettlement
				|| !member.HasSettlement || planner == null || !planner.HasChosenSite
				|| !hasReconciledExistingSettlement || managedGenerator == null
				|| !managedGenerator.gameObject.activeInHierarchy
				|| managedGenerator.IsChildOfVehicle
				|| (managedGenerator.transform.position - member.GroupAnchor).sqrMagnitude > 6f * 6f)
				return false;
			BarricadeDrop drop = BarricadeDrop.FindByTransformFastMaybeNull(managedGenerator.transform);
			BarricadeData data = drop != null ? drop.GetServersideData() : null;
			if (drop == null || data == null || data.owner != 0UL || data.group != 0UL
				|| !(drop.asset is ItemGeneratorAsset))
				return false;
			generator = managedGenerator;
			return true;
		}

		private void Awake()
		{
			planner = GetComponent<MobileNPCSettlementPlanner>();
			inventory = GetComponent<MobileNPCInventory>();
			motor = GetComponent<MobileNPCHumanoidMotor>();
			driver = GetComponent<MobileNPCBrainDriver>();
		}

		private void Start()
		{
			nextBuildTime = Time.time + Mathf.Max(1f, initialBuildDelay);
		}

		private void Update()
		{
			if (!Provider.isServer || !Level.isLoaded)
				return;

			// Move carried supplies into the vanilla container whenever its server-side
			// InteractableStorage has finished initialization. Keep construction items
			// in the NPC inventory so the settlement can continue expanding.
			if (Time.time >= nextStorageDeliveryAt)
			{
				nextStorageDeliveryAt = Time.time + 2f;
				TryTransferSuppliesToCentralStorage();
			}

			// Leader succession must discover an ALREADY BUILT real storage
			// even when the shared pattern is complete (placedPieces >= max).
			// Previously reconciliation lived after that early return, so the
			// successor could never discover the old leader's stockpile and
			// all A-Life guard jobs/radio station consumers lost their anchor.
			// This is a one-time native scene reconciliation, never per frame.
			if (!hasReconciledExistingSettlement && planner != null
				&& planner.HasChosenSite)
			{
				MobileNPCGroupMember currentLeader = GetComponent<MobileNPCGroupMember>();
				if (currentLeader == null || currentLeader.CanPlanSettlement)
					ReconcileExistingSettlement();
			}

			if (planner == null || !planner.HasChosenSite
				|| inventory == null || motor == null || driver == null || !motor.IsInitialized
				|| placedPieces >= Mathf.Clamp(maximumPieces, 1, 5) || Time.time < nextBuildTime)
				return;

			// The settlement is a group resource: only its elected living leader may
			// place pieces. Otherwise every member with the shared anchor could build
			// the same pattern concurrently and consume supplies redundantly.
			MobileNPCGroupMember groupMember = GetComponent<MobileNPCGroupMember>();
			if (groupMember != null && !groupMember.CanPlanSettlement)
				return;
			if (groupMember != null)
			{
				// A successor inherits construction progress instead of placing the same pattern again.
				placedPieces = Mathf.Max(placedPieces, groupMember.SettlementPiecesPlaced);
				perimeterPiecesPlaced = Mathf.Max(perimeterPiecesPlaced, groupMember.SettlementPerimeterPiecesPlaced);
				centralStoragePlaced |= groupMember.SettlementCentralStoragePlaced;
			}

			// Reconciliation was already attempted above even when every
			// settlement piece is complete, including successor leaders.

			if (placedPieces >= Mathf.Clamp(maximumPieces, 1, 5) || Time.time < nextBuildTime)
				return;

			nextBuildTime = Time.time + Mathf.Max(2f, buildInterval);
			if (driver.Target != null || driver.CurrentGoal != MobileNPCTactics.Goal.None)
				return;
			// Placing world barricades remotely from home was equivalent to
			// magically constructing from tens of metres away. Ask the regular
			// navigation brain to walk to the real, chosen site first.
			if (Vector3.Distance(transform.position, planner.ChosenSite) > 5f)
			{
				if (Vector3.Distance(transform.position, planner.ChosenSite)
					<= motor.HomeTerritoryRadius
					&& motor.CanReachNearbyCoverServer(planner.ChosenSite))
					driver.TryStartRememberedSupplySearch(planner.ChosenSite);
				return;
			}

			TryPlaceNextPatternPiece();
		}

		private void TryPlaceNextPatternPiece()
		{
			// A shared stockpile is the first settlement objective. If none has been
			// placed or carried, ask the recipe system for a storage item before spending
			// available materials on perimeter defenses.
			if (!centralStoragePlaced && !HasStorageBuildable())
			{
				Items sharedItems = centralStorage != null ? centralStorage.items : null;
				if (inventory.TryCraftBuildableForSettlementServer(sharedItems, true))
				{
					Debug.Log("[MobileNPCSettlementBuilder] Crafted the settlement's missing central storage.");
					return;
				}
			}

			// Pattern: central storage, then four cardinal defensive positions.
			// If no storage recipe/material is available, the group may still build defenses.
			bool placeStorage = !centralStoragePlaced && HasStorageBuildable();
			int selectedSlot = -1;
			ItemBarricadeAsset selectedAsset = null;
			ItemStructureAsset selectedStructureAsset = null;

			// Reserve one of the four perimeter positions for a REAL carried generator,
			// if present. This extension never creates a generator, fuel or electricity.
			// A powerless/unfueled generator still occupies a physical station slot.
			bool placeGenerator = !placeStorage && !generatorPlaced
				&& TrySelectGeneratorBuildable(out selectedSlot, out selectedAsset);
			bool placeStructure = !placeStorage && !placeGenerator
				&& TrySelectStructureBuildable(out selectedSlot, out selectedStructureAsset);
			if (!placeGenerator && !placeStructure && !TrySelectBuildable(placeStorage, out selectedSlot, out selectedAsset))
			{
				// If no suitable buildable is carried, try one safe vanilla recipe before
				// the next construction interval. Ingredients are never invented.
				Items sharedItems = centralStorage != null ? centralStorage.items : null;
				bool needsStorage = !centralStoragePlaced && !HasStorageBuildable();
				bool crafted = needsStorage && inventory.TryCraftBuildableForSettlementServer(sharedItems, true);
				if (!crafted)
					crafted = inventory.TryCraftBuildableForSettlementServer(sharedItems, false);
				if (crafted)
					Debug.Log("[MobileNPCSettlementBuilder] Crafted a buildable matching current settlement needs; placement will be retried next interval.");
				return;
			}

			Vector3 candidate;
			float angle;
			if (placeStorage)
			{
				candidate = planner.ChosenSite;
				angle = 0f;
			}
			else
			{
				if (!TrySelectAvailablePerimeterSlot(out candidate, out angle))
					return;
			}

			// A blocked pattern point is retried next interval instead of inventing
			// a completely new base layout.
			if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, searchHeight, maximumDrop,
				groundMask, maximumSlope, out Vector3 ground, out _))
				return;

			float itemOffset = placeStructure ? 0f : selectedAsset.offset;
			Vector3 placePosition = ground + Vector3.up * Mathf.Clamp(itemOffset, -0.5f, 1.5f);
			if (Physics.CheckSphere(placePosition + Vector3.up * 1.0f,
				Mathf.Clamp(clearanceRadius, 0.4f, 1.2f), obstructionMask, QueryTriggerInteraction.Ignore))
				return;

			// Finalize placement type before taking the item. If a vanilla
			// structure's housing anchor does not exist, use a carried simple
			// barricade instead; do not place a structure and then charge for
			// a different slot.
			if (placeStructure)
			{
				string obstructionHint = null;
				if (UseableHousingUtils.ValidatePendingPlacement(selectedStructureAsset,
					ref placePosition, angle, ref obstructionHint)
					!= EHousingPlacementResult.Success)
				{
					if (!TrySelectBuildable(false, out selectedSlot, out selectedAsset))
						return;
					placeStructure = false;
					placePosition = ground + Vector3.up
						* Mathf.Clamp(selectedAsset.offset, -0.5f, 1.5f);
					if (Physics.CheckSphere(placePosition + Vector3.up,
						Mathf.Clamp(clearanceRadius, 0.4f, 1.2f),
						obstructionMask, QueryTriggerInteraction.Ignore))
						return;
				}
			}
			ushort expectedId = placeStructure ? selectedStructureAsset.id : selectedAsset.id;
			if (!inventory.TryTakeItemServer(selectedSlot, out Item committedItem))
				return;
			if (committedItem == null || committedItem.id != expectedId)
			{
				if (committedItem != null && !inventory.TryStoreTransferredItem(committedItem))
					UnturnedLog.warn("[MobileNPCSettlementBuilder] Could not roll back an unexpected construction item.");
				return;
			}
			// Native placement is performed only AFTER real inventory debit.
			// If world placement fails, return the original item unchanged.
			Transform placedBarricade = null;
			bool placedSuccessfully;
			if (placeStructure)
			{
				placedSuccessfully = StructureManager.dropStructure(
					new Structure(selectedStructureAsset, selectedStructureAsset.health),
					placePosition, 0f, angle, 0f, 0UL, 0UL);
			}
			else
			{
				Barricade barricade;
				if (placeGenerator)
				{
					if (committedItem.state == null || committedItem.state.Length < 3)
					{
						if (!inventory.TryStoreTransferredItem(committedItem))
							UnturnedLog.warn("[MobileNPCSettlementBuilder] Could not restore an invalid generator item.");
						return;
					}
					barricade = new Barricade(selectedAsset, selectedAsset.health,
						(byte[])committedItem.state.Clone());
				}
				else
					barricade = new Barricade(selectedAsset);
				placedBarricade = BarricadeManager.dropBarricade(
					barricade, null, placePosition, 0f, angle, 0f, 0UL, 0UL);
				placedSuccessfully = placedBarricade != null;
			}
			if (!placedSuccessfully)
			{
				if (!inventory.TryStoreTransferredItem(committedItem))
					UnturnedLog.warn("[MobileNPCSettlementBuilder] Native placement failed and construction item could not be restored.");
				return;
			}
			// Construction items can be stacked. Charge exactly one buildable
			// per world object rather than deleting the whole stack.
			if (committedItem.amount > 1)
			{
				Item remainder = new Item(committedItem.id,
					(byte)(committedItem.amount - 1), committedItem.quality,
					committedItem.state != null ? (byte[])committedItem.state.Clone() : new byte[0]);
				if (!inventory.TryStoreTransferredItem(remainder))
					UnturnedLog.warn("[MobileNPCSettlementBuilder] Could not restore remainder of stacked buildables.");
			}

			placedPieces++;
			MobileNPCGroupMember progressMember = GetComponent<MobileNPCGroupMember>();
			progressMember?.RecordSettlementPiecePlacedServer(placeStorage);
			if (placeStorage)
			{
				centralStoragePlaced = true;
				centralStorage = placedBarricade != null
					? placedBarricade.GetComponentInChildren<InteractableStorage>(true)
					: null;
				TryTransferSuppliesToCentralStorage();
			}
			else
			{
				perimeterPiecesPlaced++;
				if (placeGenerator)
				{
					generatorPlaced = true;
					managedGenerator = placedBarricade != null
						? placedBarricade.GetComponentInChildren<InteractableGenerator>(true) : null;
				}
			}

			string placedName = placeStructure ? selectedStructureAsset.name : selectedAsset.name;
			Debug.Log("[MobileNPCSettlementBuilder] Placed fixed-pattern settlement piece "
				+ placedName + " (" + placedPieces + "/" + Mathf.Clamp(maximumPieces, 1, 5) + ").");
		}


		/// <summary>
		/// Reconnects to vanilla-saved storage after loading a map and advances past
		/// perimeter points that are already occupied. Vanilla world objects remain the
		/// source of truth; this component never serializes or recreates those structures.
		/// </summary>
		private void ReconcileExistingSettlement()
		{
			hasReconciledExistingSettlement = true;
			if (centralStorage == null)
			{
				InteractableStorage[] storages = Object.FindObjectsOfType<InteractableStorage>();
				float bestDistanceSquared = 3.5f * 3.5f;
				for (int i = 0; i < storages.Length; ++i)
				{
					InteractableStorage candidate = storages[i];
					// NPC-built storage is placed with nil owner/group IDs. Never redirect
					// NPC loot into a nearby player's private container.
					if (candidate == null || candidate.items == null
						|| candidate.owner != CSteamID.Nil || candidate.group != CSteamID.Nil)
						continue;
					BarricadeDrop nativeStorage =
						BarricadeDrop.FindByTransformFastMaybeNull(candidate.transform);
					BarricadeData nativeData = nativeStorage != null
						? nativeStorage.GetServersideData() : null;
					if (nativeData == null || nativeData.owner != 0UL
						|| nativeData.group != 0UL
						|| !(nativeStorage.asset is ItemBarricadeAsset)
						|| ((ItemBarricadeAsset)nativeStorage.asset).build != EBuild.STORAGE)
						continue;

					float distanceSquared = (candidate.transform.position - planner.ChosenSite).sqrMagnitude;
					if (distanceSquared >= bestDistanceSquared)
						continue;

					bestDistanceSquared = distanceSquared;
					centralStorage = candidate;
				}
			}

			if (centralStorage != null)
				centralStoragePlaced = true;

			// One-time reconciliation, not a per-radio-transmission scene scan.
			// Detect even an OFF or empty-fuel native generator to avoid duplicates
			// after reloading and changing the living settlement leader.
			InteractableGenerator[] generators = Object.FindObjectsOfType<InteractableGenerator>();
			for (int i = 0; i < generators.Length; i++)
			{
				if (generators[i] != null && generators[i].gameObject.activeInHierarchy
					&& !generators[i].IsChildOfVehicle
					&& (generators[i].transform.position - planner.ChosenSite).sqrMagnitude <= 6f * 6f)
				{
					BarricadeDrop drop = BarricadeDrop.FindByTransformFastMaybeNull(generators[i].transform);
					BarricadeData data = drop != null ? drop.GetServersideData() : null;
					if (data == null || data.owner != 0UL || data.group != 0UL
						|| !(drop.asset is ItemGeneratorAsset))
						continue;
					generatorPlaced = true;
					managedGenerator = generators[i];
					break;
				}
			}
		}

		/// <summary>
		/// Keeps the fixed four-point pattern moving forward when a saved structure or
		/// terrain obstruction already occupies a slot. Invalid ground is skipped rather
		/// than causing the leader to retry the same impossible point forever.
		/// </summary>
		private bool TrySelectAvailablePerimeterSlot(out Vector3 candidate, out float angle)
		{
			candidate = Vector3.zero;
			angle = 0f;
			while (perimeterPiecesPlaced < 4)
			{
				angle = perimeterPiecesPlaced * 90f;
				Vector3 radial = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
				Vector3 slot = planner.ChosenSite + radial * Mathf.Max(2f, perimeterRadius);
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(slot, searchHeight, maximumDrop,
					groundMask, maximumSlope, out Vector3 ground, out _))
				{
					perimeterPiecesPlaced++;
					continue;
				}

				Vector3 placePosition = ground + Vector3.up;
				if (Physics.CheckSphere(placePosition, Mathf.Clamp(clearanceRadius, 0.4f, 1.2f),
					obstructionMask, QueryTriggerInteraction.Ignore))
				{
					perimeterPiecesPlaced++;
					continue;
				}

				candidate = ground;
				return true;
			}

			return false;
		}

		/// <summary>
		/// Transfers non-construction supplies into the NPC's central vanilla storage.
		/// Items are removed from the simulated inventory only after Items accepts them.
		/// </summary>
		private void TryTransferSuppliesToCentralStorage()
		{
			MobileNPCGroupMember currentGroup = GetComponent<MobileNPCGroupMember>();
			// Trading vendors sell the goods in their OWN inventory through
			// MobileNPCTradeService. Emptying them into storage every two
			// seconds would make the shop permanently appear out of stock.
			// Group couriers replenish that real inventory face to face.
			if (MobileNPCArchetypePolicy.ForNPC(gameObject)
				== MobileNPCArchetypePolicy.Preset.Trader)
				return;
			if (!Provider.isServer || centralStorage == null || centralStorage.items == null
				|| inventory == null || inventory.UsedSlots == 0
				|| currentGroup == null || !currentGroup.CanPlanSettlement
				|| !TryGetCentralStorageForRadioServer(out _)
				|| (transform.position - centralStorage.transform.position).sqrMagnitude > 4f * 4f)
				return;

			bool changed = false;
			for (int slot = inventory.UsedSlots - 1; slot >= 0; --slot)
			{
				if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
					continue;

				ItemAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemAsset;
				if (asset == null || asset.type == EItemType.BARRICADE
					|| asset.type == EItemType.STORAGE || asset.type == EItemType.STRUCTURE
					|| asset.type == EItemType.GENERATOR)
					continue;

				// Debit from the NPC first: if another routine changes the
				// inventory before this transaction, no duplicate is deposited.
				if (!inventory.TryTakeItemServer(slot, out Item removed))
					continue;
				if (!centralStorage.items.tryAddItem(removed, false))
				{
					if (!inventory.TryStoreTransferredItem(removed))
						UnturnedLog.warn("[MobileNPCSettlementBuilder] Storage rejected an item and rollback failed.");
					continue;
				}
				changed = true;
			}

			if (changed)
				centralStorage.items.onStateUpdated?.Invoke();
		}

		private bool HasStorageBuildable()
		{
			for (int slot = 0; slot < inventory.UsedSlots; ++slot)
			{
				if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
					continue;
				ItemBarricadeAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemBarricadeAsset;
				if (asset != null && asset.barricade != null && asset.build == EBuild.STORAGE)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Generator must be a physical native barricade asset already owned by this
		/// NPC, with the proper GENERATOR build type. Never fabricate an asset ID.
		/// </summary>
		private bool TrySelectGeneratorBuildable(out int selectedSlot, out ItemBarricadeAsset selectedAsset)
		{
			selectedSlot = -1;
			selectedAsset = null;
			for (int i = 0; i < inventory.UsedSlots; i++)
			{
				if (!inventory.TryGetItemSnapshot(i, out Item item) || item == null)
					continue;
				ItemGeneratorAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemGeneratorAsset;
				if (asset == null || asset.barricade == null || asset.build != EBuild.GENERATOR)
					continue;
				selectedSlot = i;
				selectedAsset = asset;
				return true;
			}
			return false;
		}

		private bool TrySelectStructureBuildable(out int selectedSlot, out ItemStructureAsset selectedAsset)
		{
			selectedSlot = -1;
			selectedAsset = null;
			int bestPriority = 0;

			for (int slot = 0; slot < inventory.UsedSlots; ++slot)
			{
				if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
					continue;

				ItemStructureAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemStructureAsset;
				if (asset == null || asset.structure == null)
					continue;

				int priority;
				switch (asset.construct)
				{
					case EConstruct.WALL:
					case EConstruct.RAMPART:
						priority = 90;
						break;
					case EConstruct.PILLAR:
						priority = 80;
						break;
					case EConstruct.FLOOR:
						priority = 70;
						break;
					default:
						// Excludes roofs, ramps, doors, ladders, and other complex pieces
						// until the settlement planner supports their placement requirements.
						priority = 0;
						break;
				}

				if (priority <= bestPriority)
					continue;

				bestPriority = priority;
				selectedSlot = slot;
				selectedAsset = asset;
			}

			return selectedSlot >= 0 && selectedAsset != null;
		}

		private bool TrySelectBuildable(bool storageOnly, out int selectedSlot, out ItemBarricadeAsset selectedAsset)
		{
			selectedSlot = -1;
			selectedAsset = null;
			int bestPriority = int.MinValue;
			for (int slot = 0; slot < inventory.UsedSlots; ++slot)
			{
				if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
					continue;
				ItemBarricadeAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemBarricadeAsset;
				if (asset == null || asset.barricade == null)
					continue;

				int priority = GetBuildPriority(asset.build, storageOnly);
				if (priority <= 0 || priority <= bestPriority)
					continue;
				bestPriority = priority;
				selectedSlot = slot;
				selectedAsset = asset;
			}
			return selectedSlot >= 0 && selectedAsset != null;
		}

		private static int GetBuildPriority(EBuild build, bool storageOnly)
		{
			if (storageOnly)
				return build == EBuild.STORAGE ? 100 : 0;

			// Deliberately narrow allow-list: no doors, beds, ladders, glass,
			// sentries, explosives, or arbitrary structure/foundation placement.
			switch (build)
			{
				case EBuild.FORTIFICATION:
					return 80;
				case EBuild.BARRICADE_WALL:
					return 70;
				case EBuild.BARRICADE:
					return 60;
				default:
					return 0;
			}
		}
	}
}
