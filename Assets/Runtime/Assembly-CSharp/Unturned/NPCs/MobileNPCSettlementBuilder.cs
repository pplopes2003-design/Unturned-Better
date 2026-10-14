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
		private int placedPieces;
		private bool centralStoragePlaced;
		private bool generatorPlaced;
		private int perimeterPiecesPlaced;
		private InteractableStorage centralStorage;
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
				|| (centralStorage.transform.position - member.GroupAnchor).sqrMagnitude > 3.5f * 3.5f)
				return false;
			storage = centralStorage;
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
			TryTransferSuppliesToCentralStorage();

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

			// Vanilla structures survive world reloads independently of this NPC snapshot.
			// Re-discover the central storage and skip already occupied perimeter slots so
			// a restored settlement does not try to place duplicate pieces at the same points.
			if (!hasReconciledExistingSettlement)
				ReconcileExistingSettlement();

			if (placedPieces >= Mathf.Clamp(maximumPieces, 1, 5) || Time.time < nextBuildTime)
				return;

			nextBuildTime = Time.time + Mathf.Max(2f, buildInterval);
			if (driver.Target != null || driver.CurrentGoal != MobileNPCTactics.Goal.None
				|| Vector3.Distance(motor.transform.position, planner.ChosenSite) > motor.HomeTerritoryRadius)
				return;

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

			Transform placedBarricade = null;
			if (placeStructure)
			{
				// Apply vanilla housing rules before placement. Arbitrary coordinates are not
				// enough for walls/pillars because they must connect to valid housing anchors.
				string obstructionHint = null;
				bool validHousingPlacement = UseableHousingUtils.ValidatePendingPlacement(
					selectedStructureAsset, ref placePosition, angle, ref obstructionHint) == EHousingPlacementResult.Success;

				// A rejected structure must not permanently block a valid barricade pattern.
				// Try a supported barricade at the same slot instead of retrying the same
				// incompatible structure on every build interval.
				bool structurePlaced = validHousingPlacement && StructureManager.dropStructure(
					new Structure(selectedStructureAsset, selectedStructureAsset.health),
					placePosition, 0f, angle, 0f, 0UL, 0UL);
				if (!structurePlaced)
				{
					if (!TrySelectBuildable(false, out selectedSlot, out selectedAsset))
						return;

					placeStructure = false;
					placePosition = ground + Vector3.up * Mathf.Clamp(selectedAsset.offset, -0.5f, 1.5f);
					if (Physics.CheckSphere(placePosition + Vector3.up * 1.0f,
						Mathf.Clamp(clearanceRadius, 0.4f, 1.2f), obstructionMask, QueryTriggerInteraction.Ignore))
						return;

					placedBarricade = BarricadeManager.dropBarricade(
						new Barricade(selectedAsset), null, placePosition, 0f, angle, 0f, 0UL, 0UL);
					if (placedBarricade == null)
						return;
				}
			}
			else
			{
				Barricade barricade;
				if (placeGenerator)
				{
					// Native generator state encodes powered flag and fuel.
					// Preserve the REAL inventory item's fuel/state on placement;
					// new Barricade(asset) would reset both to zero.
					if (!inventory.TryGetItemSnapshot(selectedSlot, out Item sourceGenerator)
						|| sourceGenerator == null || sourceGenerator.state == null
						|| sourceGenerator.state.Length < 3)
						return;
					barricade = new Barricade(selectedAsset, selectedAsset.health,
						(byte[])sourceGenerator.state.Clone());
				}
				else
					barricade = new Barricade(selectedAsset);
				placedBarricade = BarricadeManager.dropBarricade(
					barricade, null, placePosition, 0f, angle, 0f, 0UL, 0UL);
				if (placedBarricade == null)
					return;
			}

			if (!inventory.TryTakeItemServer(selectedSlot, out _))
			{
				UnturnedLog.warn("[MobileNPCSettlementBuilder] Placed a buildable but could not remove its inventory item.");
				return;
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
					generatorPlaced = true;
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
					&& (generators[i].transform.position - planner.ChosenSite).sqrMagnitude <= 6f * 6f)
				{
					generatorPlaced = true;
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
			if (!Provider.isServer || centralStorage == null || centralStorage.items == null
				|| inventory == null || inventory.UsedSlots == 0)
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

				if (!centralStorage.items.tryAddItem(item, false))
					continue;

				if (!inventory.TryTakeItemServer(slot, out _))
				{
					UnturnedLog.warn("[MobileNPCSettlementBuilder] Storage accepted an item but inventory transfer failed.");
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
