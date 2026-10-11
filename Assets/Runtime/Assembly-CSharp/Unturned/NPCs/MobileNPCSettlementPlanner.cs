////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// First settlement-site planning step for mobile NPCs. It selects and validates a
	/// nearby ground anchor when the NPC carries construction-category items. It does
	/// not place vanilla barricades/structures; placement and persistence are separate
	/// integration steps and must not be implied by this planner.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	[RequireComponent(typeof(MobileNPCHumanoidMotor))]
	[RequireComponent(typeof(MobileNPCInventory))]
	public sealed class MobileNPCSettlementPlanner : MonoBehaviour
	{
		[SerializeField, Min(1f)] private float initialPlanningDelay = 25f;
		[SerializeField, Min(1f)] private float retryInterval = 20f;
		[SerializeField, Min(3f)] private float minimumSiteDistance = 7f;
		[SerializeField, Min(4f)] private float maximumSiteDistance = 18f;
		[SerializeField, Min(1f)] private float searchHeight = 50f;
		[SerializeField, Min(0f)] private float maximumDrop = 60f;
		[SerializeField, Range(0f, 60f)] private float maximumSlope = 25f;
		[SerializeField, Min(0.5f)] private float clearanceRadius = 0.4f;
		[SerializeField] private LayerMask groundMask = ~0;
		[SerializeField] private LayerMask obstructionMask = ~0;

		private MobileNPCBrainDriver driver;
		private MobileNPCHumanoidMotor motor;
		private MobileNPCInventory inventory;
		private bool siteChosen;
		private float nextAttemptTime;

		public bool HasChosenSite => siteChosen;
		public Vector3 ChosenSite { get; private set; }

		private void Awake()
		{
			driver = GetComponent<MobileNPCBrainDriver>();
			motor = GetComponent<MobileNPCHumanoidMotor>();
			inventory = GetComponent<MobileNPCInventory>();
		}

		private void Start()
		{
			nextAttemptTime = Time.time + Mathf.Max(1f, initialPlanningDelay);
		}

		private void Update()
		{
			if (!Provider.isServer || !Level.isLoaded || siteChosen || driver == null || motor == null || inventory == null
				|| !motor.IsInitialized || Time.time < nextAttemptTime)
			{
				return;
			}

			nextAttemptTime = Time.time + Mathf.Max(1f, retryInterval);
			if (driver.Target != null || driver.CurrentGoal != MobileNPCTactics.Goal.None || !HasConstructionSupplies())
			{
				return;
			}

			Vector3 origin = motor.HomePosition;
			int attempts = 10;
			for (int i = 0; i < attempts; ++i)
			{
				float angle = Random.Range(0f, Mathf.PI * 2f);
				float distance = Random.Range(Mathf.Max(3f, minimumSiteDistance), Mathf.Max(minimumSiteDistance + 1f, maximumSiteDistance));
				Vector3 candidate = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, searchHeight, maximumDrop,
					groundMask, maximumSlope, out Vector3 ground, out _))
				{
					continue;
				}

				Vector3 offset = ground - origin;
				offset.y = 0f;
				if (offset.sqrMagnitude < minimumSiteDistance * minimumSiteDistance)
				{
					continue;
				}

				Vector3 clearanceCenter = ground + Vector3.up * 1.0f;
				if (Physics.CheckSphere(clearanceCenter, Mathf.Clamp(clearanceRadius, 0.25f, 0.8f), obstructionMask,
					QueryTriggerInteraction.Ignore))
				{
					continue;
				}

				if (!motor.TrySetHomePositionServer(ground))
				{
					continue;
				}

				ChosenSite = ground;
				siteChosen = true;
				MobileNPCGroupMember groupMember = GetComponent<MobileNPCGroupMember>();
				groupMember?.SetGroupAnchorServer(ground);
				Debug.Log("[MobileNPCSettlementPlanner] Mobile NPC selected a provisional settlement anchor at " + ground
					+ ". No structures have been placed yet.");
				return;
			}
		}

		private bool HasConstructionSupplies()
		{
			return inventory.CountItemType(EItemType.BARRICADE) > 0
				|| inventory.CountItemType(EItemType.STORAGE) > 0
				|| inventory.CountItemType(EItemType.STRUCTURE) > 0;
		}
	}
}
