////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-authoritative first construction step for mobile settlements. Places safe
	/// fortification/storage-style barricades from the NPC's inventory. This intentionally
	/// avoids structures requiring pillars and dangerous placeables such as charges/sentries.
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
		[SerializeField, Range(1, 8)] private int maximumPieces = 4;
		[SerializeField, Min(1f)] private float pieceSpacing = 2.4f;
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
				|| placedPieces >= Mathf.Clamp(maximumPieces, 1, 8) || Time.time < nextBuildTime)
			{
				return;
			}

			nextBuildTime = Time.time + Mathf.Max(2f, buildInterval);
			if (driver.Target != null || driver.CurrentGoal != MobileNPCTactics.Goal.None
				|| Vector3.Distance(motor.transform.position, planner.ChosenSite) > motor.HomeTerritoryRadius)
			{
				return;
			}

			TryPlaceOneBarricade();
		}

		private void TryPlaceOneBarricade()
		{
			int selectedSlot = -1;
			ItemBarricadeAsset selectedAsset = null;
			int bestPriority = int.MinValue;

			// Prefer a storage unit first, then defensive perimeter pieces. This makes the
			// initial settlement more useful than consuming arbitrary buildables in slot order.
			for (int slot = 0; slot < inventory.UsedSlots; ++slot)
			{
				if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
					continue;

				ItemBarricadeAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemBarricadeAsset;
				if (asset == null || !IsAllowedBuildType(asset.build) || asset.barricade == null)
					continue;

				int priority = GetBuildPriority(asset.build);
				if (priority <= bestPriority)
					continue;

				bestPriority = priority;
				selectedSlot = slot;
				selectedAsset = asset;
			}

			if (selectedSlot < 0 || selectedAsset == null)
				return;

			// Four pieces are distributed evenly around the chosen site. A constant radius
			// avoids the uneven cross-shaped pattern from the initial prototype.
			float baseAngle = placedPieces * 90f;
			for (int attempt = 0; attempt < 4; ++attempt)
			{
				float angle = baseAngle + (attempt == 0 ? 0f : Random.Range(-18f, 18f));
				Vector3 radial = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
				Vector3 candidate = planner.ChosenSite + radial * Mathf.Max(1f, pieceSpacing);
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, searchHeight, maximumDrop,
					groundMask, maximumSlope, out Vector3 ground, out _))
				{
					continue;
				}

				Vector3 placePosition = ground + Vector3.up * Mathf.Clamp(selectedAsset.offset, -0.5f, 1.5f);
				if (Physics.CheckSphere(placePosition + Vector3.up * 1.4f,
					Mathf.Clamp(clearanceRadius, 0.4f, 1.2f), obstructionMask, QueryTriggerInteraction.Ignore))
				{
					continue;
				}

				// Use vanilla's server deployment entry point for deploy hooks and replication.
				// This is still an approximation of vanilla preview/support/claim validation.
				Barricade barricade = new Barricade(selectedAsset);
				Transform placed = BarricadeManager.dropBarricade(barricade, null, placePosition, 0f, angle, 0f, 0UL, 0UL);
				if (placed == null)
					continue;

				if (!inventory.TryTakeItemServer(selectedSlot, out _))
				{
					UnturnedLog.warn("[MobileNPCSettlementBuilder] Placed a barricade but could not remove its inventory item.");
					return;
				}

				placedPieces++;
				Debug.Log("[MobileNPCSettlementBuilder] Mobile NPC placed settlement barricade " + selectedAsset.name
					+ " (" + placedPieces + "/" + maximumPieces + ").");
				return;
			}
		}

		private static int GetBuildPriority(EBuild build)
		{
			switch (build)
			{
				case EBuild.STORAGE:
					return 100;
				case EBuild.FORTIFICATION:
				case EBuild.BARRICADE_WALL:
					return 80;
				case EBuild.BARRICADE:
					return 70;
				case EBuild.DOOR:
				case EBuild.GATE:
				case EBuild.SHUTTER:
				case EBuild.HATCH:
					return 60;
				case EBuild.BED:
					return 50;
				case EBuild.STORAGE_WALL:
					return 40;
				case EBuild.LADDER:
					return 30;
				case EBuild.GLASS:
					return 20;
				default:
					return 0;
			}
		}

		private static bool IsAllowedBuildType(EBuild build)
		{
			switch (build)
			{
				case EBuild.FORTIFICATION:
				case EBuild.BARRICADE:
				case EBuild.DOOR:
				case EBuild.GATE:
				case EBuild.BED:
				case EBuild.LADDER:
				case EBuild.STORAGE:
				case EBuild.STORAGE_WALL:
				case EBuild.SHUTTER:
				case EBuild.HATCH:
				case EBuild.BARRICADE_WALL:
				case EBuild.GLASS:
					return true;
				default:
					return false;
			}
		}
	}
}
