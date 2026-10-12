////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt file for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
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
		private int perimeterPiecesPlaced;

		public int PlacedPieces => placedPieces;

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
			if (!Provider.isServer || !Level.isLoaded || planner == null || !planner.HasChosenSite
				|| inventory == null || motor == null || driver == null || !motor.IsInitialized
				|| placedPieces >= Mathf.Clamp(maximumPieces, 1, 5) || Time.time < nextBuildTime)
				return;

			nextBuildTime = Time.time + Mathf.Max(2f, buildInterval);
			if (driver.Target != null || driver.CurrentGoal != MobileNPCTactics.Goal.None
				|| Vector3.Distance(motor.transform.position, planner.ChosenSite) > motor.HomeTerritoryRadius)
				return;

			TryPlaceNextPatternPiece();
		}

		private void TryPlaceNextPatternPiece()
		{
			// Pattern: central storage, then four cardinal defensive positions.
			// If no storage item exists, use only the perimeter pattern.
			bool placeStorage = !centralStoragePlaced && HasStorageBuildable();
			int selectedSlot = -1;
			ItemBarricadeAsset selectedAsset = null;
			ItemStructureAsset selectedStructureAsset = null;

			// Prefer vanilla wall/pillar/floor structures for the perimeter when available.
			// Storage remains a barricade because vanilla storage containers are barricades.
			bool placeStructure = !placeStorage
				&& TrySelectStructureBuildable(out selectedSlot, out selectedStructureAsset);
			if (!placeStructure && !TrySelectBuildable(placeStorage, out selectedSlot, out selectedAsset))
				return;

			Vector3 candidate;
			float angle;
			if (placeStorage)
			{
				candidate = planner.ChosenSite;
				angle = 0f;
			}
			else
			{
				if (perimeterPiecesPlaced >= 4)
					return;
				angle = perimeterPiecesPlaced * 90f;
				Vector3 radial = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
				candidate = planner.ChosenSite + radial * Mathf.Max(2f, perimeterRadius);
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

			if (placeStructure)
			{
				// Use Unturned's authoritative structure manager so the piece is registered
				// in the world and replicated through the vanilla structure pipeline.
				if (!StructureManager.dropStructure(
					new Structure(selectedStructureAsset, selectedStructureAsset.health),
					placePosition, 0f, angle, 0f, 0UL, 0UL))
					return;
			}
			else
			{
				Barricade barricade = new Barricade(selectedAsset);
				Transform placed = BarricadeManager.dropBarricade(
					barricade, null, placePosition, 0f, angle, 0f, 0UL, 0UL);
				if (placed == null)
					return;
			}

			if (!inventory.TryTakeItemServer(selectedSlot, out _))
			{
				UnturnedLog.warn("[MobileNPCSettlementBuilder] Placed a buildable but could not remove its inventory item.");
				return;
			}

			placedPieces++;
			if (placeStorage)
				centralStoragePlaced = true;
			else
				perimeterPiecesPlaced++;

			string placedName = placeStructure ? selectedStructureAsset.name : selectedAsset.name;
			Debug.Log("[MobileNPCSettlementBuilder] Placed fixed-pattern settlement piece "
				+ placedName + " (" + placedPieces + "/" + Mathf.Clamp(maximumPieces, 1, 5) + ").");
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
