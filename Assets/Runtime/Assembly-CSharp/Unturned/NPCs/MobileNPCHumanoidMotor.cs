////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using SDG.Framework.Water;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// First movement/scene-entity integration for a mobile humanoid NPC.
	/// Attach to a server-spawned humanoid prefab with a CharacterController and
	/// MobileNPCBrainDriver. This motor intentionally moves only on the server;
	/// network replication and client interpolation are a separate integration step.
	/// It prefers Unturned's native A* graph, with bounded local routing as fallback.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(CharacterController))]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	public sealed class MobileNPCHumanoidMotor : MonoBehaviour
	{
		[SerializeField] private Transform visualRoot;
		[SerializeField, Min(0.1f)] private float walkSpeed = 2.2f;
		[SerializeField, Min(0.1f)] private float runSpeed = 4.0f;
		[SerializeField, Min(0.1f)] private float turnSpeed = 540.0f;
		[SerializeField, Min(0.1f)] private float sneakSpeed = 1.15f;
		[SerializeField, Range(0.5f, 1f)] private float crouchedHeightFraction = 0.67f;
		[SerializeField, Min(0.5f)] private float jumpCooldown = 2.5f;
		[Header("Verified planned low-obstacle jump (no ladder/vault link)")]
		[SerializeField, Range(0.4f, 1.0f)] private float plannedJumpMaximumObstacleHeight = 0.70f;
		[SerializeField, Range(0.8f, 2.0f)] private float plannedJumpTravelDistance = 1.35f;
		[SerializeField, Range(2.5f, 5.5f)] private float plannedJumpHorizontalSpeed = 4.0f;
		[SerializeField, Range(6f, 12f)] private float plannedJumpVerticalImpulse = 9.5f;
		[SerializeField, Min(0.05f)] private float stoppingDistance = 0.65f;
		[SerializeField, Min(0.1f)] private float gravity = 20.0f;
		[SerializeField] private bool runWhenPursuing = true;

		[Header("Swimming")]
		[SerializeField, Min(0.1f)] private float swimSpeed = 1.8f;
		[SerializeField, Min(0.1f)] private float swimSurfaceOffset = 1.0f;
		[SerializeField, Min(0.1f)] private float swimVerticalCorrection = 2.0f;
		[SerializeField, Min(0.1f)] private float swimMaximumVerticalSpeed = 1.5f;

		[Header("Home territory")]
		[SerializeField, Min(5f)] private float homeTerritoryRadius = 220f;

		[Header("Autonomous roaming (local prototype)")]
		[SerializeField] private bool roamingEnabled = true;
		[SerializeField, Min(2f)] private float roamingRadius = 220f;
		[SerializeField, Min(1f)] private float roamingPauseMin = 2f;
		[SerializeField, Min(1f)] private float roamingPauseMax = 6f;
		[SerializeField, Min(1f)] private float roamingSearchHeight = 50f;
		[SerializeField, Min(0f)] private float roamingMaximumDrop = 70f;
		[SerializeField, Range(0f, 60f)] private float roamingMaximumSlope = 38f;
		[SerializeField] private LayerMask roamingGroundMask = ~0;

		[Header("Local obstacle avoidance and bounded route fallback")]
		[SerializeField, Min(0.5f)] private float obstacleProbeDistance = 1.6f;
		[SerializeField, Min(0.05f)] private float obstacleProbeRadius = 0.28f;
		[SerializeField, Range(15f, 80f)] private float obstacleAvoidanceAngle = 48f;
		[SerializeField] private LayerMask obstacleMask = ~0;

		private static readonly float[] FallbackSteeringAngles = { -90f, 90f, -135f, 135f, 180f };
		private static readonly RaycastHit[] ObstacleHitBuffer = new RaycastHit[16];
		private static readonly Collider[] RouteOccupancyBuffer = new Collider[32];
		private const int RouteGridSide = 9;
		private const int RouteGridCenter = RouteGridSide / 2;
		private const float RouteGridCellSize = 1.25f;
		private const int RouteGridNodeLimit = RouteGridSide * RouteGridSide;
		// Vertical search is invoked only for nearby destinations on different
		// elevations. Two independently sampled walkable surfaces per XZ cell;
		// ordinary ground and the floor above cannot become one fake node.
		private const int VerticalNodeLimit = RouteGridNodeLimit * 2;
		private static float nextSharedVerticalRouteSlot;
		private static float nextSharedKnownAccessRouteSlot;
		private static readonly Vector2Int[] RouteGridDirections =
		{
			new Vector2Int(1, 0), new Vector2Int(-1, 0),
			new Vector2Int(0, 1), new Vector2Int(0, -1),
			new Vector2Int(1, 1), new Vector2Int(1, -1),
			new Vector2Int(-1, 1), new Vector2Int(-1, -1)
		};

		private CharacterController characterController;
		private MobileNPCBrainDriver brainDriver;
		private MobileNPCInjurySystem injurySystem;
		private MobileNPCDoorNavigation doorNavigation;
		private MobileNPCLadderTraversal ladderTraversal;
		private MobileNPCVaultTraversal vaultTraversal;
		public bool IsVaultTraversing => vaultTraversal != null && vaultTraversal.IsTraversing;
		public bool IsClambering => vaultTraversal != null && vaultTraversal.IsClambering;
		// A directed physical approach to a native Ladder collider, never
		// a synthetic vertical edge in the ordinary walking grid.
		private bool hasPlannedLadderApproach;
		private Vector3 plannedLadderEntrance;
		private Vector3 plannedLadderGoal;
		private float plannedLadderExpiry;
		private float nextLadderApproachPlanTime;
		private readonly List<Vector3> ladderApproachScratch = new List<Vector3>(128);
		private MobileNPCTravelMemory travelMemory;
		private MobileNPCNativeAStarRouteProvider nativeAStarRouteProvider;
		private Vector3 spawnPosition;
		private Vector3 velocity;
		private bool hasInitialized;
		private bool targetIsVisible;
		private readonly MobileNPCLocomotionPolicy locomotionPolicy = new MobileNPCLocomotionPolicy();
		private float nextJumpTime;
		private bool isPlannedJumping;
		private Vector3 plannedJumpLanding;
		private readonly RaycastHit[] plannedJumpArcHitBuffer = new RaycastHit[20];
		private Vector3 plannedJumpDirection;
		private Vector3 plannedJumpDestinationSnapshot;
		private float plannedJumpStartedAt;
		private float suppressJourneyUntil;
		private float nextPlannedJumpProbeTime;
		private static float nextSharedPlannedJumpProbeSlot;
		public bool IsJumping => isPlannedJumping || (velocity.y > 0.5f
			&& characterController != null && !characterController.isGrounded);
		public bool IsClimbingLadder => ladderTraversal != null && ladderTraversal.IsClimbing;
		public bool IsTraversingLadder => ladderTraversal != null && ladderTraversal.IsTraversing;
		private float nextFootstepNoiseAt;
		private float standingHeight;
		private Vector3 standingCenter;
		private bool physicallyCrouched;
		public bool IsCrouched => physicallyCrouched;
		public MobileNPCLocomotionPolicy.Gait CurrentGait => locomotionPolicy.CurrentGait;
		public bool IsSneaking => CurrentGait == MobileNPCLocomotionPolicy.Gait.Sneak;
		public bool IsSprinting => CurrentGait == MobileNPCLocomotionPolicy.Gait.Sprint;
		private bool hasRoamingDestination;
		private Vector3 roamingDestination;
		private float nextRoamingDecisionTime;
		private Vector3 lastStuckCheckPosition;
		private Vector3 lastTrackedStuckGoal;
		private Vector3 lastMeaningfulRouteProgressPosition;
		private bool hasTrackedStuckGoal;
		private float nextStuckCheckTime;
		private float nextGroundSafetyCheckTime;
		private float nextGroundFailureLogTime;
		private float nextFallingDiagnosticTime;
		private float blockedMovementDuration;
		private bool useRecoverySteering;
		private float recoverySteeringUntil;
		private float recoverySteeringAngle;
		private bool hasDetourWaypoint;
		private Vector3 detourWaypoint;
		private Vector3 detourGoalSnapshot;
		private float nextDetourAttemptTime;
		private readonly List<Vector3> routeWaypoints = new List<Vector3>(180);
		private readonly List<Vector3> actualTravelFootsteps
			= new List<Vector3>(MobileNPCTravelMemory.MaxFootsteps);
		private readonly List<Vector3> rememberedRouteScratch
			= new List<Vector3>(MobileNPCTravelMemory.MaxFootsteps);
		private readonly List<Vector3> learnedAccessChain
			= new List<Vector3>(MobileNPCTravelMemory.MaxAccessChainFootsteps);
		private readonly List<int> learnedAccessRouteIds
			= new List<int>(MobileNPCTravelMemory.MaxAccessChainRoutes);
		private readonly List<int> learnedAccessStageStarts
			= new List<int>(MobileNPCTravelMemory.MaxAccessChainRoutes);
		private readonly List<int> activeAccessRouteIds
			= new List<int>(MobileNPCTravelMemory.MaxAccessChainRoutes);
		private readonly List<int> activeAccessWaypointStarts
			= new List<int>(MobileNPCTravelMemory.MaxAccessChainRoutes);
		private int activeAccessFinalWaypointIndex = -1;
		private readonly float[] knownAccessEntryRetryUntil = new float[8];
		private int pendingKnownAccessEntryRouteId = -1;
		private readonly List<Vector3> learnedAccessApproach = new List<Vector3>(32);
		private float nextKnownAccessPlanTime;
		private Vector3 lastKnownAccessGoal;
		private bool hasPendingKnownAccessPath;
		private Vector3 actualTravelStart;
		private Vector3 actualTravelGoal;
		private bool trackingActualJourney;
		private bool actualJourneyExceededLimit;
		private int activeRememberedRouteIndex = -1;
		private int activeContinuationRouteIndex = -1;
		private int learnedStairStartWaypointIndex = -1;
		private int learnedContinuationStartWaypointIndex = -1;
		private float nextRememberedRouteAttemptTime;
		private readonly List<RegionCoordinate> explorationRegions = new List<RegionCoordinate>(16);
		private readonly List<LevelObject> explorationObjects = new List<LevelObject>(64);
		// Reuse the bounded search workspace so several blocked NPCs do not allocate
		// five arrays and a reconstruction list on every route-planning attempt.
		private readonly Vector3[] routeSearchPositions = new Vector3[RouteGridNodeLimit];
		private readonly bool[] routeSearchWalkable = new bool[RouteGridNodeLimit];
		private readonly float[] routeSearchCosts = new float[RouteGridNodeLimit];
		private readonly int[] routeSearchPrevious = new int[RouteGridNodeLimit];
		private readonly bool[] routeSearchClosed = new bool[RouteGridNodeLimit];
		// 0=unknown, 1=exposed, 2=sheltered. Only tactical routes probe LOS.
		// Per-instance arrays prevent cross-NPC exposure knowledge leaks.
		private readonly byte[] routeSearchExposure = new byte[RouteGridNodeLimit];
		private readonly List<Vector3> reversedRouteScratch = new List<Vector3>(RouteGridSide);
		private readonly List<Vector3> coverRouteScratch = new List<Vector3>(RouteGridSide);
		private readonly Vector3[] verticalPositions = new Vector3[VerticalNodeLimit];
		private readonly bool[] verticalWalkable = new bool[VerticalNodeLimit];
		private readonly float[] verticalCost = new float[VerticalNodeLimit];
		private readonly int[] verticalPrevious = new int[VerticalNodeLimit];
		private readonly bool[] verticalClosed = new bool[VerticalNodeLimit];
		private readonly RaycastHit[] verticalFloorHitBuffer = new RaycastHit[24];
		private readonly List<Vector3> verticalReverseScratch = new List<Vector3>(VerticalNodeLimit);
		private int routeWaypointIndex;
		private float nextRoutePlanTime;
		private float nextLocalRoutePlanTime;
		private float nextVerticalRoutePlanTime;
		private int consecutiveVerticalPlanFailures;
		private Vector3 failedVerticalGoal;
		private float nextPlannedRouteValidationTime;
		private float nextRouteCornerShortcutTime;
		private Vector3 lastRoamingDirection;
		private bool hasLastRoamingDirection;
		private int avoidanceSide = 1;
		private float avoidanceSideUntil;
		private int consecutiveStuckRecoveries;
		// Per-survivor, short-lived local navigation evidence. These are NOT
		// global map portals or inferred paths. They discourage repeated
		// searches into a wall and expire when conditions may have changed.
		private const int MaxRecentNavDeadEnds = 6;
		private readonly Vector3[] recentNavDeadEndPositions = new Vector3[MaxRecentNavDeadEnds];
		private readonly Vector3[] recentNavDeadEndGoals = new Vector3[MaxRecentNavDeadEnds];
		private readonly float[] recentNavDeadEndExpiry = new float[MaxRecentNavDeadEnds];
		private int nextNavDeadEndSlot;
		private static float nextSharedUnknownRouteSlot;

		public bool IsInitialized => hasInitialized;
		public bool IsMoving { get; private set; }
		/// <summary>Server-local home anchor captured when this NPC initializes.</summary>
		public Vector3 HomePosition => spawnPosition;
		public float HomeTerritoryRadius => Mathf.Max(5f, homeTerritoryRadius);
		public bool IsWithinHomeTerritory
		{
			get
			{
				Vector3 offset = transform.position - spawnPosition;
				offset.y = 0f;
				return offset.sqrMagnitude <= HomeTerritoryRadius * HomeTerritoryRadius;
			}
		}

		/// <summary>
		/// Updates this NPC's home anchor on the server after validating safe ground.
		/// This is a runtime anchor only; persistence and settlement ownership are separate systems.
		/// </summary>
		public bool TrySetHomePositionServer(Vector3 candidate)
		{
			if (!Provider.isServer || !hasInitialized)
			{
				return false;
			}

			if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
				Mathf.Max(1f, roamingSearchHeight), Mathf.Max(0f, roamingMaximumDrop),
				roamingGroundMask, roamingMaximumSlope, out Vector3 resolved, out _))
			{
				return false;
			}

			spawnPosition = resolved;
			brainDriver.SetHomePositionServer(resolved);
			hasRoamingDestination = false;
			nextRoamingDecisionTime = Time.time + Random.Range(1f, 2f);
			return true;
		}

		/// <summary>Call after the server has placed this humanoid at its spawn point.</summary>
		public void InitializeServer()
		{
			if (!Provider.isServer || characterController == null || brainDriver == null)
			{
				return;
			}

			spawnPosition = transform.position;
			velocity = Vector3.zero;
			standingHeight = characterController.height;
			standingCenter = characterController.center;
			physicallyCrouched = false;
			locomotionPolicy.Reset();
			nextJumpTime = 0f;
			isPlannedJumping = false;
			if (ladderTraversal != null)
				ladderTraversal.ResetTraversal();
			if (vaultTraversal != null)
				vaultTraversal.ResetServer();
			plannedJumpLanding = Vector3.zero;
			plannedJumpDirection = Vector3.zero;
			plannedJumpDestinationSnapshot = Vector3.zero;
			suppressJourneyUntil = 0f;
			nextPlannedJumpProbeTime = 0f;
			nextLocalRoutePlanTime = 0f;
			nextVerticalRoutePlanTime = 0f;
			consecutiveVerticalPlanFailures = 0;
			failedVerticalGoal = Vector3.zero;
			trackingActualJourney = false;
			actualTravelFootsteps.Clear();
			activeRememberedRouteIndex = -1;
			activeContinuationRouteIndex = -1;
			learnedStairStartWaypointIndex = -1;
			learnedContinuationStartWaypointIndex = -1;
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
			nextRememberedRouteAttemptTime = 0f;
			nextKnownAccessPlanTime = 0f;
			hasPendingKnownAccessPath = false;
			nextPlannedRouteValidationTime = 0f;
			nextRouteCornerShortcutTime = 0f;
			brainDriver.InitializeServer(spawnPosition);
			hasRoamingDestination = false;
			nextRoamingDecisionTime = Time.time + Random.Range(2f, 5f);
			lastStuckCheckPosition = transform.position;
			lastMeaningfulRouteProgressPosition = transform.position;
			hasTrackedStuckGoal = false;
			nextStuckCheckTime = Time.time + 0.75f;
			blockedMovementDuration = 0f;
			useRecoverySteering = false;
			consecutiveStuckRecoveries = 0;
			System.Array.Clear(recentNavDeadEndExpiry, 0, recentNavDeadEndExpiry.Length);
			nextNavDeadEndSlot = 0;
			avoidanceSide = Random.value < 0.5f ? -1 : 1;
			hasInitialized = true;
		}

		private void Awake()
		{
			characterController = GetComponent<CharacterController>();
			brainDriver = GetComponent<MobileNPCBrainDriver>();
			injurySystem = GetComponent<MobileNPCInjurySystem>();
			doorNavigation = GetComponent<MobileNPCDoorNavigation>();
			ladderTraversal = GetComponent<MobileNPCLadderTraversal>();
			if (ladderTraversal == null)
				ladderTraversal = gameObject.AddComponent<MobileNPCLadderTraversal>();
			vaultTraversal = GetComponent<MobileNPCVaultTraversal>();
			if (vaultTraversal == null)
				vaultTraversal = gameObject.AddComponent<MobileNPCVaultTraversal>();
			travelMemory = GetComponent<MobileNPCTravelMemory>();
			nativeAStarRouteProvider = GetComponent<MobileNPCNativeAStarRouteProvider>();
			if (nativeAStarRouteProvider == null)
				nativeAStarRouteProvider = gameObject.AddComponent<MobileNPCNativeAStarRouteProvider>();
			if (visualRoot == null)
			{
				visualRoot = transform;
			}
		}

		private void Start()
		{
			if (Provider.isServer && !hasInitialized)
			{
				InitializeServer();
			}
		}

		private void Update()
		{
			if (!Provider.isServer || !hasInitialized || !characterController.enabled)
			{
				IsMoving = false;
				return;
			}

			float deltaTime = Time.deltaTime;
			if (deltaTime <= 0.0f)
			{
				return;
			}

			// Unturned water volumes are queryable even on the server. Detect water before
			// ground recovery so an intentional swim is never mistaken for a terrain fall.
			bool isUnderwater = WaterUtility.isPointUnderwater(
				transform.position + Vector3.up * Mathf.Min(0.9f, characterController.height * 0.5f),
				out WaterVolume waterVolume);
			float waterSurfaceY = isUnderwater
				? WaterUtility.getWaterSurfaceElevation(waterVolume, transform.position)
				: float.NegativeInfinity;

			// Low-frequency guard against transient terrain-collision failures. Do not
			// teleport a swimmer toward ground while it is inside a water volume.
			if (!isUnderwater && Time.time >= nextGroundSafetyCheckTime)
			{
				nextGroundSafetyCheckTime = Time.time + 0.5f;
				RecoverFromUnexpectedFall();
			}

			brainDriver.TickServer(targetIsVisible);
			Vector3 planarDirection = Vector3.zero;
			bool hasBrainDestination = brainDriver.HasDestination;
			if (hasBrainDestination)
			{
				// Combat, investigation and tactical goals always take precedence over roaming or returning home.
				hasRoamingDestination = false;
			}
			else if (!IsWithinHomeTerritory)
			{
				// Recover from knockback or other external displacement before choosing another roaming waypoint.
				// This remains ordinary collision-aware movement; it does not teleport the NPC home.
				roamingDestination = spawnPosition;
				hasRoamingDestination = true;
			}
			else if (roamingEnabled && Time.time >= nextRoamingDecisionTime && !hasRoamingDestination)
			{
				TryChooseRoamingDestination();
			}

			bool waitingForDoorToOpen = false;
			bool waitingForLearnedAccess = false;
			Vector3 jumpRequestedDirection = Vector3.zero;
			bool hasDestination = hasBrainDestination || hasRoamingDestination;
			Vector3 destination = hasBrainDestination ? brainDriver.Destination : roamingDestination;
			if (hasDestination && !isUnderwater && !isPlannedJumping
				&& !IsVaultTraversing && !physicallyCrouched
				&& (injurySystem == null || injurySystem.MovementMultiplier > 0.5f)
				&& ladderTraversal != null && !ladderTraversal.IsTraversing
				&& Mathf.Abs(destination.y - transform.position.y) >= 1.4f
				&& PlanarDistanceSquared(destination, transform.position) < 30f
				&& ladderTraversal.TryStartServer(destination, roamingGroundMask,
					roamingMaximumSlope))
			{
				// This is a real native ladder action, not a remembered walk or
				// a tentative ordinary A* path. Preserve the brain's final goal.
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				hasDetourWaypoint = false;
				hasPendingKnownAccessPath = false;
				hasPlannedLadderApproach = false;
				activeRememberedRouteIndex = -1;
				activeContinuationRouteIndex = -1;
				activeAccessRouteIds.Clear();
				activeAccessWaypointStarts.Clear();
				trackingActualJourney = false;
				actualTravelFootsteps.Clear();
			}
			if (!hasDestination)
			{
				hasPlannedLadderApproach = false;
				if (ladderTraversal != null && ladderTraversal.IsTraversing)
					ladderTraversal.AbortServer();
				if (vaultTraversal != null && vaultTraversal.IsTraversing)
					vaultTraversal.AbortServer();
				trackingActualJourney = false;
				actualTravelFootsteps.Clear();
				activeRememberedRouteIndex = -1;
				activeContinuationRouteIndex = -1;
				learnedStairStartWaypointIndex = -1;
				learnedContinuationStartWaypointIndex = -1;
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
				hasPendingKnownAccessPath = false;
				// Completed/expired goals must not leave the previous route waiting to
				// steer the next unrelated investigation or roaming destination.
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				hasDetourWaypoint = false;
				useRecoverySteering = false;
			}
			if (hasDestination && !IsTraversingLadder && !IsVaultTraversing)
			{
				BeginOrContinueActualJourney(destination);
				// Keep a planned route while its goal remains stable. The previous condition
				// cleared routeWaypoints whenever hasDetourWaypoint was false, which is also
				// the normal state for a multi-corner native A* route.
				if (PlanarDistanceSquared(destination, detourGoalSnapshot) > 2.25f
					|| Mathf.Abs(destination.y - detourGoalSnapshot.y) > 0.8f)
				{
					hasDetourWaypoint = false;
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
					activeRememberedRouteIndex = -1;
					activeContinuationRouteIndex = -1;
					learnedStairStartWaypointIndex = -1;
					learnedContinuationStartWaypointIndex = -1;
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
					hasPendingKnownAccessPath = false;
					if ((destination - plannedLadderGoal).sqrMagnitude > 2.25f)
						hasPlannedLadderApproach = false;
				}
				else if (routeWaypoints.Count == 0 && !hasDetourWaypoint)
				{
					routeWaypointIndex = 0;
				}

				// A known route must have been physically traversed by THIS
				// survivor. Recheck every short segment against current geometry
				// before spending new CPU on global/native A* planning.
				if (!isUnderwater && routeWaypoints.Count == 0 && !hasDetourWaypoint
					&& Time.time >= nextRememberedRouteAttemptTime)
				{
					nextRememberedRouteAttemptTime = Time.time + 4f;
					TryReuseActualLearnedRoute(destination);
				}
				// A learned *floor-access* journey can serve a different room:
				// approach its observed stair entrance using actual navigation,
				// then traverse the individually verified remembered connector.
				// Pending async A* to the entrance must not be cancelled by an
				// unrelated request to the final upstairs destination.
				if (!isUnderwater && routeWaypoints.Count == 0 && !hasDetourWaypoint)
					TryBuildKnownFloorAccessRoute(destination, out waitingForLearnedAccess);
				// A real Ladder is a directed off-mesh movement action. Before
				// global pathfinding to another floor, consider an approach
				// through actual walkable ground to its observed entry side.
				bool waitingForLadderApproach = false;
				if (!isUnderwater && !waitingForLearnedAccess
					&& routeWaypoints.Count == 0 && !hasDetourWaypoint)
					TryBuildPhysicalLadderApproach(destination, out waitingForLadderApproach);
				// Ask for a map-wide path before the NPC gets close enough to collide with
				// something. This is essential for open-world goals separated by buildings,
				// fences or terrain; local obstacle probing alone cannot discover such routes.
				Vector3 globalGoalOffset = destination - transform.position;
				globalGoalOffset.y = 0f;
				if (!isUnderwater && !waitingForLearnedAccess
					&& !waitingForLadderApproach
					&& routeWaypoints.Count == 0 && !hasDetourWaypoint
					&& globalGoalOffset.sqrMagnitude > stoppingDistance * stoppingDistance
					&& Time.time >= nextRoutePlanTime)
				{
					nextRoutePlanTime = Time.time + 0.75f;
					if (nativeAStarRouteProvider != null && nativeAStarRouteProvider.IsAvailable
						&& TryBuildNativeAStarRoute(destination))
					{
						detourGoalSnapshot = destination;
						hasDetourWaypoint = false;
					}
				}
				// A separate, bounded two-surface-per-cell local search can
				// discover REAL stairs between nearby floors. Never infer a
				// vertical shortcut merely from identical X/Z coordinates.
				if (!isUnderwater && !waitingForLearnedAccess
					&& !waitingForLadderApproach
					&& routeWaypoints.Count == 0 && !hasDetourWaypoint
					&& Mathf.Abs(destination.y - transform.position.y) > 1.0f
					&& PlanarDistanceSquared(destination, transform.position) <= 30f
					&& Time.time >= nextVerticalRoutePlanTime
					&& Time.time >= nextSharedVerticalRouteSlot)
				{
					nextVerticalRoutePlanTime = Time.time + 2.5f;
					// Across all NPCs, admit at most one expensive vertical
					// search roughly every 0.16s. Ordinary local routing is
					// unaffected by this shared CPU budget.
					nextSharedVerticalRouteSlot = Time.time + 0.16f;
					if (TryBuildNearbyVerticalRoute(destination))
					{
						detourGoalSnapshot = destination;
						hasDetourWaypoint = false;
						consecutiveVerticalPlanFailures = 0;
					}
					else
					{
						// Repeated physically confirmed failures are meaningful for
						// optional tactical cover only. Do not cancel pursuit simply
						// because one small stairwell search cannot find an entrance.
						if ((destination - failedVerticalGoal).sqrMagnitude > 2.25f)
							consecutiveVerticalPlanFailures = 0;
						failedVerticalGoal = destination;
						++consecutiveVerticalPlanFailures;
						if (consecutiveVerticalPlanFailures >= 3
							&& brainDriver.ReportTacticalDestinationUnreachableServer(destination))
							consecutiveVerticalPlanFailures = 0;
					}
				}
				// Detour-style corridor refinement: skip an unnecessary near
				// corner only when a short, same-floor collision- and ground-
				// checked segment to the following waypoint is traversable.
				// This never skips the final brain goal or a door/steep stair.
				if (!isUnderwater && routeWaypointIndex + 1 < routeWaypoints.Count
					&& activeAccessRouteIds.Count == 0
					&& activeRememberedRouteIndex < 0
					&& Time.time >= nextRouteCornerShortcutTime)
				{
					nextRouteCornerShortcutTime = Time.time + 0.35f;
					if (CanSafelyShortcutRouteCorner(routeWaypoints[routeWaypointIndex + 1]))
						++routeWaypointIndex;
				}

				// Waypoint arrival is planar: the physics controller handles minor
				// elevation differences independently (stairs, curbs, uneven ground).
				if (routeWaypointIndex < routeWaypoints.Count
					&& PlanarDistanceSquared(transform.position, routeWaypoints[routeWaypointIndex]) <= 1.0f
					&& Mathf.Abs(routeWaypoints[routeWaypointIndex].y - transform.position.y)
					<= Mathf.Max(0.45f, characterController.stepOffset + 0.2f))
				{
					++routeWaypointIndex;
					if (routeWaypointIndex >= routeWaypoints.Count)
					{
						routeWaypoints.Clear();
						routeWaypointIndex = 0;
						// Finished the cached waypoints. Final brain destination
						// completion is separately judged from actual XYZ position.
						activeRememberedRouteIndex = -1;
						activeContinuationRouteIndex = -1;
						learnedStairStartWaypointIndex = -1;
						learnedContinuationStartWaypointIndex = -1;
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
						hasDetourWaypoint = false;
						nextDetourAttemptTime = Time.time + 0.35f;
					}
				}
				// A single-hop detour is not the goal. Once reached, stop steering
				// toward the old corner and resume the original destination.
				if (hasDetourWaypoint && PlanarDistanceSquared(transform.position, detourWaypoint) <= 0.8f * 0.8f
					&& Mathf.Abs(detourWaypoint.y - transform.position.y)
					<= Mathf.Max(0.45f, characterController.stepOffset + 0.2f))
				{
					hasDetourWaypoint = false;
					nextDetourAttemptTime = Time.time + 0.3f;
				}
				// Do not march blindly through a wall while native A* is
				// still calculating an approach to the known doorway/stair.
				// Brain goal and combat remain active while locomotion pauses.
				Vector3 movementDestination = routeWaypointIndex < routeWaypoints.Count
					? routeWaypoints[routeWaypointIndex]
					: (hasDetourWaypoint ? detourWaypoint
						: ((waitingForLearnedAccess || waitingForLadderApproach)
							? transform.position : destination));

				// Routes are snapshots of the scene. If a door closes or an entity blocks
				// the next segment after planning, discard the stale route and try again
				// through the existing cooldown-limited detour path.
				if (routeWaypointIndex < routeWaypoints.Count
					&& Time.time >= nextPlannedRouteValidationTime)
				{
					// Retest a route only periodically; moving actors and doors
					// should not erase multi-corner A* paths every rendered frame.
					nextPlannedRouteValidationTime = Time.time + 0.3f;
					Vector3 routeOffset = movementDestination - transform.position;
					routeOffset.y = 0f;
					if (routeOffset.sqrMagnitude > 0.1f)
					{
						Vector3 routeOrigin = transform.position
							+ Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
						float routeRadius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
							Mathf.Max(0.05f, characterController.radius * 0.9f));
						float segmentDistance = Mathf.Min(routeOffset.magnitude,
							Mathf.Max(0.5f, obstacleProbeDistance));
						if (IsObstacleBlockingAlong(routeOrigin, routeRadius, routeOffset.normalized,
							segmentDistance)
							&& !IsOnlyTemporaryActorBlockingAlong(routeOrigin, routeRadius,
								routeOffset.normalized, segmentDistance))
						{
							// The closest collision may be a genuine unlocked barricade
							// door. Ask its native server system to open it before throwing
							// away an otherwise valid path. Never unlock, warp or bypass.
							if (doorNavigation != null
								&& doorNavigation.TryHandleBlockingDoorServer(
									routeOrigin, routeOffset.normalized,
									routeRadius, segmentDistance, obstacleMask))
							{
								waitingForDoorToOpen = true;
							}
							else
							{
								// Real geometry invalidated this path; a stale remembered
								// route must lose confidence before any new planning.
								ReportLearnedRouteInvalidatedServer();
								routeWaypoints.Clear();
								routeWaypointIndex = 0;
								hasDetourWaypoint = false;
								nextDetourAttemptTime = Time.time;
								movementDestination = destination;
							}
						}
					}
				}

				Vector3 goalOffset = destination - transform.position;
				goalOffset.y = 0f;
				if (!waitingForLearnedAccess && !waitingForLadderApproach
					&& !hasDetourWaypoint
					&& routeWaypoints.Count == 0
					&& goalOffset.sqrMagnitude > stoppingDistance * stoppingDistance
					&& Time.time >= nextDetourAttemptTime
					&& IsObstacleBlocking(goalOffset.normalized))
				{
					Vector3 doorProbeOrigin = transform.position
						+ Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
					float doorProbeRadius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
						Mathf.Max(0.05f, characterController.radius * 0.9f));
					if (doorNavigation != null
						&& doorNavigation.TryHandleBlockingDoorServer(doorProbeOrigin,
							goalOffset.normalized, doorProbeRadius,
							Mathf.Min(2.5f, goalOffset.magnitude), obstacleMask))
					{
						waitingForDoorToOpen = true;
					}
					else
					{
						TryCreateDetourWaypoint(destination);
						if (routeWaypointIndex < routeWaypoints.Count)
							movementDestination = routeWaypoints[routeWaypointIndex];
						else if (hasDetourWaypoint)
							movementDestination = detourWaypoint;
					}
				}
				Vector3 offset = movementDestination - transform.position;
				offset.y = 0.0f;
				jumpRequestedDirection = offset;
				if (offset.sqrMagnitude > stoppingDistance * stoppingDistance)
				{
					planarDirection = offset.normalized;
				}
				else if (hasBrainDestination)
				{
					// A detour node or A* intermediary is not the destination of
					// the brain's seek-cover, loot, investigate or pursue goal.
					// Do not acknowledge arrival until the *actual requested goal*
					// is physically within stopping distance, with no artificial
					// bridging across stairs or different floors.
					if (PlanarDistanceSquared(transform.position, destination)
						<= stoppingDistance * stoppingDistance
						&& Mathf.Abs(destination.y - transform.position.y)
						<= Mathf.Max(0.45f, characterController.stepOffset + 0.2f))
						brainDriver.ReportDestinationReached();
				}
				else if (PlanarDistanceSquared(transform.position, destination)
				<= stoppingDistance * stoppingDistance
				&& Mathf.Abs(destination.y - transform.position.y)
				<= Mathf.Max(0.45f, characterController.stepOffset + 0.2f))
				{
					hasRoamingDestination = false;
					nextRoamingDecisionTime = Time.time + Random.Range(
						Mathf.Max(1f, roamingPauseMin), Mathf.Max(Mathf.Max(1f, roamingPauseMin), roamingPauseMax));
				}
			}

			if (hasDestination && planarDirection.sqrMagnitude <= 0.0001f && !hasBrainDestination)
			{
				// If terrain-aware steering rejects every direction, abandon this roaming
				// waypoint and sample a different ground point rather than freezing forever.
				hasRoamingDestination = false;
				hasDetourWaypoint = false;
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				nextRoamingDecisionTime = Time.time + Random.Range(0.4f, 1.2f);
			}

			// A suspicious footstep can suspend walking briefly while the survivor
			// listens. A confirmed threat or retreat pre-empts this immediately.
			if (brainDriver.IsListening || brainDriver.IsAmbushPreparing
				|| waitingForDoorToOpen || waitingForLearnedAccess
				|| waitingForLadderApproach)
				planarDirection = Vector3.zero;

			bool wantedToMove = planarDirection.sqrMagnitude > 0.0001f;
			if (wantedToMove)
			{
				Vector3 requestedDirection = planarDirection;
				if (useRecoverySteering && Time.time < recoverySteeringUntil)
				{
					Vector3 recoveryDirection = Quaternion.AngleAxis(recoverySteeringAngle, Vector3.up) * requestedDirection;
					planarDirection = IsObstacleBlocking(recoveryDirection)
						? ApplyTerrainAwareSteering(requestedDirection, isUnderwater)
						: ApplyTerrainAwareSteering(recoveryDirection, isUnderwater);
				}
				else
				{
					planarDirection = ApplyTerrainAwareSteering(requestedDirection, isUnderwater);
				}
			}

			Vector3 facingDirection = planarDirection;
			if (brainDriver.IsListening)
			{
				// Face the uncertain sound bearing, not the player's hidden live
				// position. Rotation is deliberately slower than combat aiming.
				facingDirection = brainDriver.ListeningClue - transform.position;
				facingDirection.y = 0f;
			}
			if (facingDirection.sqrMagnitude > 0.0001f)
			{
				Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
				float steeringSpeed = brainDriver.IsListening ? turnSpeed * 0.35f : turnSpeed;
				transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, steeringSpeed * deltaTime);
			}

			float injuryMovementMultiplier = injurySystem != null ? injurySystem.MovementMultiplier : 1f;
			MobileNPCLocomotionPolicy.Situation context = new MobileNPCLocomotionPolicy.Situation
			{
				HasDestination = wantedToMove,
				TargetVisible = targetIsVisible,
				Swimming = isUnderwater,
				Injured = injuryMovementMultiplier <= 0.5f,
				Retreating = brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat,
				UnderFire = brainDriver.CurrentGoal == MobileNPCTactics.Goal.SeekCover,
				InCover = brainDriver.CurrentGoal == MobileNPCTactics.Goal.SeekCover
					&& !hasBrainDestination,
				Investigating = brainDriver.CurrentState == MobileNPCBrain.State.Investigating,
				Stalking = brainDriver.CurrentGoal == MobileNPCTactics.Goal.Stalk,
				Pursuing = brainDriver.CurrentState == MobileNPCBrain.State.Pursuing,
				NearTarget = brainDriver.Target != null
					&& (brainDriver.Target.position - transform.position).sqrMagnitude < 100f,
				DestinationDistance = hasDestination ? Vector3.Distance(destination, transform.position) : 0f
			};
			MobileNPCLocomotionPolicy.Gait gait = locomotionPolicy.Decide(context, Time.time);
			UpdateCrouchStance(!isPlannedJumping && !IsTraversingLadder
				&& !IsVaultTraversing
				&& (gait == MobileNPCLocomotionPolicy.Gait.Sneak
					|| brainDriver.IsListening || brainDriver.IsAmbushPreparing)
				&& !isUnderwater);
			float speed = gait == MobileNPCLocomotionPolicy.Gait.Sprint && runWhenPursuing
				? runSpeed : gait == MobileNPCLocomotionPolicy.Gait.Sneak ? sneakSpeed : walkSpeed;
			speed *= injuryMovementMultiplier;
			Vector3 motion = planarDirection * speed;

			// Match the player's grounded handling: CharacterController.isGrounded alone can be
			// one frame behind, so confirm contact with the same short BLOCK_COLLISION sphere cast.
			bool groundedBeforeMove = !isUnderwater && IsGroundedUsingPlayerProbe();
			if (isUnderwater)
			{
				if (IsTraversingLadder)
					ladderTraversal.AbortServer();
				if (IsVaultTraversing)
					vaultTraversal.AbortServer();
				// Entering water aborts the ground-only ballistic action.
				// Swimming remains owned by the existing native water physics.
				if (isPlannedJumping)
				{
					isPlannedJumping = false;
					suppressJourneyUntil = Time.time + 0.3f;
				}
				// Simple buoyant locomotion: hold the root just below the water surface,
				// rather than applying ground gravity while the capsule is submerged.
				// Horizontal movement still follows the current AI destination and collision.
				float desiredRootY = waterSurfaceY - Mathf.Max(0.5f, swimSurfaceOffset);
				float verticalError = desiredRootY - transform.position.y;
				velocity.y = Mathf.Clamp(verticalError * Mathf.Max(0.1f, swimVerticalCorrection),
					-Mathf.Max(0.1f, swimMaximumVerticalSpeed), Mathf.Max(0.1f, swimMaximumVerticalSpeed));
				motion.x = planarDirection.x * Mathf.Max(0.1f, swimSpeed);
				motion.z = planarDirection.z * Mathf.Max(0.1f, swimSpeed);
				motion.y = velocity.y;
			}
			else
			{
				if (groundedBeforeMove && velocity.y < 0.0f)
				{
					// Avoid pushing the capsule through map surfaces when grounded.
					velocity.y = 0.0f;
				}

				// PlayerMovement applies Unity gravity with a 3x multiplier while airborne.
				velocity.y += Physics.gravity.y * (gravity / 9.81f) * deltaTime * 3.0f;
				velocity.y = Mathf.Max(-100.0f, velocity.y);
				motion.y = velocity.y;

				// Off-mesh low obstacle jump is a deliberately verified ACTION,
				// not a random vertical impulse every time steering sees a wall.
				// The route goal remains active and the CharacterController
				// executes the actual gravity-driven trajectory.
				if (isPlannedJumping && (!hasDestination
					|| (destination - plannedJumpDestinationSnapshot).sqrMagnitude > 4f
					|| Time.time - plannedJumpStartedAt > 1.1f))
				{
					isPlannedJumping = false;
					suppressJourneyUntil = Time.time + 0.3f;
				}
				if (isPlannedJumping && groundedBeforeMove
					&& Time.time - plannedJumpStartedAt >= 0.16f)
				{
					bool actuallyLanded = Vector3.Distance(transform.position,
						plannedJumpLanding) < 0.65f;
					isPlannedJumping = false;
					suppressJourneyUntil = Time.time + 0.3f;
					if (!actuallyLanded)
						RememberLocalNavigationDeadEnd(transform.position, destination);
				}
				// Higher parapets require a distinct three-phase clamber;
				// never disguise this as the low-obstacle ballistic jump.
				if (!isPlannedJumping && !IsTraversingLadder
					&& !IsVaultTraversing && vaultTraversal != null
					&& hasDestination && groundedBeforeMove
					&& !physicallyCrouched && !waitingForDoorToOpen
					&& !waitingForLearnedAccess && !waitingForLadderApproach
					&& injuryMovementMultiplier >= 0.95f
					&& gait != MobileNPCLocomotionPolicy.Gait.Sneak
					&& !brainDriver.IsListening && !brainDriver.IsAmbushPreparing
					&& !(targetIsVisible && (brainDriver.CurrentGoal
							== MobileNPCTactics.Goal.SeekCover
						|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat))
					&& vaultTraversal.TryStartServer(destination,
						jumpRequestedDirection, roamingGroundMask,
						roamingMaximumSlope, obstacleMask))
				{
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
					hasDetourWaypoint = false;
					hasPendingKnownAccessPath = false;
					hasPlannedLadderApproach = false;
					activeRememberedRouteIndex = -1;
					activeContinuationRouteIndex = -1;
					activeAccessRouteIds.Clear();
					activeAccessWaypointStarts.Clear();
					trackingActualJourney = false;
					actualTravelFootsteps.Clear();
					wantedToMove = true;
				}
				if (!isPlannedJumping && !IsTraversingLadder
					&& !IsVaultTraversing && hasDestination && groundedBeforeMove
					&& !waitingForDoorToOpen && !waitingForLearnedAccess
					&& !waitingForLadderApproach
					&& Time.time >= nextJumpTime
					// Injured legs alter gait speed and invalidate the
					// precomputed horizontal ballistic distance.
					&& injuryMovementMultiplier >= 0.95f
					&& gait != MobileNPCLocomotionPolicy.Gait.Sneak
					&& !brainDriver.IsListening && !brainDriver.IsAmbushPreparing
					// Defensive exposure goals should choose real cover/retreat
					// paths rather than an airborne exposed leap by default.
					&& !(targetIsVisible && (brainDriver.CurrentGoal
							== MobileNPCTactics.Goal.SeekCover
						|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat))
					&& TryBeginPlannedLowJump(jumpRequestedDirection, destination))
				{
					wantedToMove = true;
				}
				if (isPlannedJumping)
				{
					// Deliberately bypass wall-follow and local replanning
					// during the short already-verified airborne connection.
					planarDirection = plannedJumpDirection;
					motion.x = plannedJumpDirection.x * plannedJumpHorizontalSpeed;
					motion.z = plannedJumpDirection.z * plannedJumpHorizontalSpeed;
					motion.y = velocity.y;
					wantedToMove = true;
				}
			}

			bool ladderMovingThisFrame = hasDestination && !isUnderwater
				&& IsTraversingLadder;
			if (ladderMovingThisFrame)
			{
				// Ladder is a verified native interaction surface. The motor
				// alone performs movement; never teleport or disable collision.
				motion = ladderTraversal.GetMotionServer(destination, deltaTime);
				if (ladderTraversal.IsClimbing)
				{
					// Actual rung movement, not a ground walk: suppress gravity.
					velocity.y = 0f;
				}
				else
				{
					// The short physical APPROACH still obeys gravity and the
					// ordinary ground collision of CharacterController.Move.
					motion.y = velocity.y;
				}
				wantedToMove = motion.sqrMagnitude > 0.0001f;
			}
			bool vaultMovingThisFrame = hasDestination && !isUnderwater
				&& IsVaultTraversing;
			if (vaultMovingThisFrame)
			{
				bool applyGravity;
				motion = vaultTraversal.GetMotionServer(destination, out applyGravity);
				if (!applyGravity)
				{
					velocity.y = 0f;
				}
				else
				{
					motion.y = velocity.y;
				}
				wantedToMove = motion.sqrMagnitude > 0.0001f;
			}
			Vector3 positionBeforeMovement = transform.position;
			CollisionFlags collision = characterController.Move(motion * deltaTime);
			if (vaultMovingThisFrame)
			{
				vaultTraversal.AfterMoveServer(positionBeforeMovement);
				if (!IsVaultTraversing)
					suppressJourneyUntil = Time.time + 0.35f;
			}
			if (ladderMovingThisFrame)
				ladderTraversal.AfterMoveServer(positionBeforeMovement, collision);
			if ((collision & CollisionFlags.Below) != 0 && velocity.y < 0.0f)
			{
				velocity.y = -2.0f;
			}
			// A side contact can coexist with genuine slide-along-wall progress.
			// Use actual planar displacement rather than treating every side
			// collision as zero movement for animation/footstep decisions.
			Vector3 planarDisplacement = transform.position - positionBeforeMovement;
			planarDisplacement.y = 0f;
			// Unexpected airborne side contact means the real controller could
			// not execute the predicted off-mesh move. Stop horizontal steering,
			// keep native gravity and learn a temporary failed approach; never
			// push through the wall or declare the landing completed.
			if (isPlannedJumping && (collision & CollisionFlags.Sides) != 0
				&& Time.time - plannedJumpStartedAt > 0.07f
				&& planarDisplacement.sqrMagnitude < 0.0004f)
			{
				isPlannedJumping = false;
				suppressJourneyUntil = Time.time + 0.3f;
				RememberLocalNavigationDeadEnd(transform.position, destination);
			}
			IsMoving = wantedToMove && planarDisplacement.sqrMagnitude > 0.000001f;
			// Noise is based on actual movement, not merely the desired AI gait.
			// Crouching suppresses the zombie-alert call; running is louder than walking.
			// Native zombie perception remains responsible for deciding investigation.
			if (IsMoving && !isUnderwater && !isPlannedJumping
				&& !ladderMovingThisFrame && !vaultMovingThisFrame
				&& (collision & CollisionFlags.Below) != 0
				&& Time.time >= nextFootstepNoiseAt)
			{
				float audibleRadius = IsCrouched ? 0f : IsSprinting ? 9f : 3.5f;
				nextFootstepNoiseAt = Time.time + (IsSprinting ? 0.36f : 0.55f);
				if (audibleRadius > 0f)
					ZombieManager.alertZombiesToNoise(transform.position, audibleRadius);
			}
			RecordActualMovementAndJourneyOutcome(hasDestination, destination, isUnderwater);
			UpdateStuckRecovery(!ladderMovingThisFrame
				&& !vaultMovingThisFrame && wantedToMove, deltaTime);
		}

		/// <summary>
		/// Switch the actual capsule to a crouched stance (not just animation).
		/// A clearance test prevents standing up through a low ceiling.
		/// </summary>
		private void UpdateCrouchStance(bool wantsCrouch)
		{
			if (characterController == null || standingHeight <= 0f || wantsCrouch == physicallyCrouched)
				return;
			if (!wantsCrouch)
			{
				float radius = characterController.radius * 0.9f;
				// Only query the newly occupied head space, not the full capsule:
				// probing the full capsule includes floor contact and can prevent
				// standing up forever on ordinary terrain.
				float currentTop = transform.position.y + characterController.center.y
					+ characterController.height * 0.5f;
				float desiredTop = transform.position.y + standingCenter.y + standingHeight * 0.5f;
				Vector3 lowerHead = new Vector3(transform.position.x,
					Mathf.Max(currentTop + radius * 0.2f, desiredTop - radius), transform.position.z);
				Vector3 upperHead = new Vector3(transform.position.x,
					Mathf.Max(lowerHead.y, desiredTop - radius * 0.2f), transform.position.z);
				Collider[] hits = Physics.OverlapCapsule(lowerHead, upperHead, radius,
					RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
				for (int i = 0; i < hits.Length; ++i)
				{
					if (!IsOwnCollider(hits[i]))
						return;
				}
			}

			float newHeight = wantsCrouch
				? Mathf.Max(characterController.radius * 2.2f, standingHeight * crouchedHeightFraction)
				: standingHeight;
			// Preserve the same foot position while reducing controller height.
			characterController.height = newHeight;
			characterController.center = standingCenter - Vector3.up * (standingHeight - newHeight) * 0.5f;
			physicallyCrouched = wantsCrouch;
		}

		/// <summary>
		/// Checks whether the route is blocked at shin height but free at torso
		/// height. The native controller still resolves collision during the jump;
		/// high walls and steep terrain never trigger a leap.
		/// </summary>
		/// <summary>
		/// A physically planned off-mesh hop over one short world obstruction,
		/// NOT an arbitrary jump-to-rooftop or ladder connection. The simulator
		/// uses the same vertical acceleration as Update/CharacterController.Move,
		/// verifies a real landing surface and sweeps the standing capsule
		/// along the predicted parabola. No Transform displacement is applied.
		/// </summary>
		private bool TryBeginPlannedLowJump(Vector3 desired, Vector3 currentGoal)
		{
			if (!Provider.isServer || isPlannedJumping || characterController == null
				|| !characterController.enabled || Time.time < nextJumpTime
				|| Time.time < nextPlannedJumpProbeTime
				|| Time.time < nextSharedPlannedJumpProbeSlot
				|| physicallyCrouched)
				return false;
			// Only one full ballistic validation begins every 0.10s across
			// the population, and each individual waits 0.75s after a probe.
			nextSharedPlannedJumpProbeSlot = Time.time + 0.10f;
			nextPlannedJumpProbeTime = Time.time + 0.75f;
			desired.y = 0f;
			if (desired.sqrMagnitude < 0.6f)
				return false;
			Vector3 direction = desired.normalized;
			float lowProbeDistance = Mathf.Min(0.90f,
				Mathf.Max(0.65f, characterController.radius + 0.48f));
			Vector3 lowProbe = transform.position + Vector3.up * 0.26f;
			int lowHits = Physics.SphereCastNonAlloc(lowProbe, 0.11f,
				direction, ObstacleHitBuffer, lowProbeDistance, obstacleMask,
				QueryTriggerInteraction.Ignore);
			if (lowHits == 0 || lowHits >= ObstacleHitBuffer.Length)
				return false;
			bool foundStaticLowObstacle = false;
			for (int i = 0; i < lowHits; ++i)
			{
				Collider collider = ObstacleHitBuffer[i].collider;
				if (collider == null || IsOwnCollider(collider))
					continue;
				// An actor, vehicle or registered door is never a jump link:
				// it moves, can be locked, or has different gameplay rules.
				if (collider.GetComponentInParent<Player>() != null
					|| collider.GetComponentInParent<Zombie>() != null
					|| collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null
					|| collider.GetComponentInParent<InteractableDoor>() != null
					|| collider.GetComponentInParent<InteractableVehicle>() != null)
					return false;
				foundStaticLowObstacle = true;
			}
			if (!foundStaticLowObstacle
				|| IsObstacleBlockingAlong(transform.position
					+ Vector3.up * (plannedJumpMaximumObstacleHeight + 0.45f),
					0.15f, direction, lowProbeDistance))
				return false;

			float travel = plannedJumpTravelDistance;
			Vector3 candidate = transform.position + direction * travel;
			if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
				1.0f, 1.0f, roamingGroundMask,
				Mathf.Min(characterController.slopeLimit, roamingMaximumSlope),
				out Vector3 landing, out _)
				|| Vector3.Distance(new Vector3(candidate.x, 0f, candidate.z),
					new Vector3(landing.x, 0f, landing.z)) > 0.25f
				|| Mathf.Abs(landing.y - transform.position.y) > 0.25f
				|| !Level.checkSafeIncludingClipVolumes(landing))
				return false;
			float radius = Mathf.Clamp(characterController.radius * 0.78f, 0.16f, 0.30f);
			if (IsRouteNodeOccupied(landing, radius))
				return false;
			// We only jump toward a goal that lies beyond the obstacle. Do not
			// jump past a nearby target or across an unrelated room boundary.
			Vector3 desiredFlat = currentGoal - transform.position;
			desiredFlat.y = 0f;
			if (Vector3.Dot(desiredFlat, direction) < travel + 0.10f)
				return false;

			if (Physics.gravity.y >= -1f)
				return false;
			float acceleration = Mathf.Abs(Physics.gravity.y)
				* (gravity / 9.81f) * 3f;
			if (acceleration < 0.01f)
				return false;
			float duration = travel / plannedJumpHorizontalSpeed;
			float impulse = (landing.y - transform.position.y
				+ 0.5f * acceleration * duration * duration) / duration;
			if (impulse < 7.0f || impulse > plannedJumpVerticalImpulse + 1.2f)
				return false;
			float apex = impulse * impulse / (2f * acceleration);
			if (apex < plannedJumpMaximumObstacleHeight + 0.10f || apex > 1.15f)
				return false;
			// Perform limited swept, full-body samples along the ballistic
			// trajectory. Reject thin rails and overhead obstructions between
			// samples, not only collisions at the endpoints.
			Vector3 previous = transform.position;
			for (int step = 1; step < 12; ++step)
			{
				float t = duration * step / 12f;
				Vector3 next = transform.position + direction
					* (plannedJumpHorizontalSpeed * t)
					+ Vector3.up * (impulse * t - 0.5f * acceleration * t * t);
				if (!Level.checkSafeIncludingClipVolumes(next)
					|| !IsPlannedJumpArcClear(previous, next, radius))
					return false;
				previous = next;
			}
			// The predicted final step terminates at the already checked,
			// grounded landing; never insert airborne waypoints into A*.
			plannedJumpLanding = landing;
			plannedJumpDirection = direction;
			plannedJumpDestinationSnapshot = currentGoal;
			plannedJumpStartedAt = Time.time;
			isPlannedJumping = true;
			velocity.y = impulse;
			nextJumpTime = Time.time + Mathf.Max(0.5f, jumpCooldown);
			// A successful airborne traversal is not a proven walkable floor
			// edge for the individual's separate learned walking routes.
			trackingActualJourney = false;
			actualTravelFootsteps.Clear();
			suppressJourneyUntil = Time.time + duration + 0.3f;
			return true;
		}

		private bool IsPlannedJumpArcClear(Vector3 from, Vector3 to, float radius)
		{
			float height = Mathf.Max(characterController.height, radius * 2.2f);
			Vector3 basePoint = from + Vector3.up * (radius + 0.08f);
			Vector3 topPoint = from + Vector3.up * (height - radius + 0.04f);
			Vector3 movement = to - from;
			float distance = movement.magnitude;
			if (distance < 0.001f)
				return true;
			int hitCount = Physics.CapsuleCastNonAlloc(basePoint, topPoint,
				radius, movement / distance, plannedJumpArcHitBuffer,
				distance, obstacleMask, QueryTriggerInteraction.Ignore);
			if (hitCount >= plannedJumpArcHitBuffer.Length)
				return false;
			for (int i = 0; i < hitCount; ++i)
				if (plannedJumpArcHitBuffer[i].collider != null
					&& !IsOwnCollider(plannedJumpArcHitBuffer[i].collider))
					return false;
			return true;
		}


		/// <summary>
		/// Mirrors PlayerMovement.checkGround: short sphere cast against the game's
		/// BLOCK_COLLISION mask, supplemented by CharacterController's contact state.
		/// This keeps NPC grounding aligned with the terrain/collision layers used by players.
		/// </summary>
		private bool IsGroundedUsingPlayerProbe()
		{
			if (characterController == null || !characterController.enabled)
			{
				return false;
			}

			if (characterController.isGrounded)
			{
				return true;
			}

			float radius = Mathf.Min(PlayerStance.RADIUS - 0.001f, characterController.radius * 0.9f);
			if (radius <= 0.01f)
			{
				return false;
			}

			const float CHECK_LENGTH = PlayerMovement.SKIN_WIDTH + 0.025f;
			Vector3 origin = transform.position + Vector3.up * radius;
			RaycastHit[] hits = Physics.SphereCastAll(origin, radius, Vector3.down, CHECK_LENGTH,
				RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < hits.Length; ++i)
			{
				Collider hitCollider = hits[i].collider;
				if (hitCollider != null && !IsOwnCollider(hitCollider)
					&& Vector3.Angle(hits[i].normal, Vector3.up) <= 55f)
				{
					return true;
				}
			}

			// Some terrain colliders are reported by a ray but missed by the short
			// sphere cast when the capsule is within its skin-width tolerance. The
			// Player.log confirms that BLOCK_COLLISION can see the terrain, so use a
			// short center ray as a second grounded signal before applying gravity.
			RaycastHit[] groundHits = Physics.RaycastAll(transform.position + Vector3.up * 0.15f,
				Vector3.down, 0.30f, RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < groundHits.Length; ++i)
			{
				Collider hitCollider = groundHits[i].collider;
				if (hitCollider != null && !IsOwnCollider(hitCollider)
					&& Vector3.Angle(groundHits[i].normal, Vector3.up) <= 55f)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>Prevents an unexpected terrain-collision failure from losing an NPC below the map.</summary>
		private void RecoverFromUnexpectedFall()
		{
			if (!Provider.isServer || characterController == null || !characterController.enabled
				|| IsTraversingLadder || IsVaultTraversing
				|| float.IsNaN(transform.position.y) || float.IsInfinity(transform.position.y))
			{
				return;
			}

			// First probe close to the NPC. This detects a short collision failure before
			// gravity can accelerate into an unrecoverable fall. RaycastAll lets us ignore
			// the NPC's own CharacterController and visual hierarchy explicitly.
			float currentY = transform.position.y;
			if (currentY < spawnPosition.y - 1.5f && Time.time >= nextFallingDiagnosticTime)
			{
				nextFallingDiagnosticTime = Time.time + 2.0f;
				DiagnoseFallingNPC(currentY);
			}
			Vector3 localOrigin = transform.position + Vector3.up * 1.0f;
			bool foundGround = TryFindRecoveryGround(localOrigin, 10f, currentY, out RaycastHit hit);

			// If the NPC has already fallen below the local probe, search from above its
			// validated spawn elevation. This fallback is intentionally not used unless
			// the local probe fails, to avoid preferring a distant roof over nearby ground.
			if (!foundGround && currentY < spawnPosition.y - 0.75f)
			{
				Vector3 fallbackOrigin = new Vector3(transform.position.x, spawnPosition.y + 200f, transform.position.z);
				float fallbackDistance = Mathf.Max(400f, fallbackOrigin.y - currentY + 50f);
				foundGround = TryFindRecoveryGround(fallbackOrigin, fallbackDistance, spawnPosition.y, out hit);
			}

			if (!foundGround)
			{
				if (Time.time >= nextGroundFailureLogTime)
				{
					nextGroundFailureLogTime = Time.time + 5f;
					Debug.LogWarning("[MobileNPCHumanoidMotor] Ground recovery probe found no walkable collider at x="
						+ transform.position.x.ToString("F2") + ", z=" + transform.position.z.ToString("F2")
						+ ", y=" + currentY.ToString("F2") + ", spawnY=" + spawnPosition.y.ToString("F2")
						+ ". Inspect terrain collider/layer coverage.", this);
				}
				return;
			}

			// A walkable surface must be materially above the root; otherwise this is
			// ordinary grounded movement and must not be snapped.
			if (Vector3.Angle(hit.normal, Vector3.up) > 55f || currentY >= hit.point.y - 0.75f)
				return;

			Vector3 recoveredPosition = hit.point + Vector3.up * 0.05f;
			if (!Level.checkSafeIncludingClipVolumes(recoveredPosition))
			{
				if (Time.time >= nextGroundFailureLogTime)
				{
					nextGroundFailureLogTime = Time.time + 5f;
					Debug.LogWarning("[MobileNPCHumanoidMotor] Found ground at " + hit.point
						+ " but recovery was rejected by level bounds/clip volumes.", this);
				}
				return;
			}

			characterController.enabled = false;
			transform.position = recoveredPosition;
			velocity.y = 0f;
			isPlannedJumping = false;
			suppressJourneyUntil = Time.time + 0.35f;
			// Emergency fall recovery is a safety reposition, not a physical
			// walking segment. Never turn it into a learned shortcut or stair.
			trackingActualJourney = false;
			actualTravelFootsteps.Clear();
			activeRememberedRouteIndex = -1;
			activeContinuationRouteIndex = -1;
			learnedStairStartWaypointIndex = -1;
			learnedContinuationStartWaypointIndex = -1;
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
			hasRoamingDestination = false;
			hasDetourWaypoint = false;
			routeWaypoints.Clear();
			routeWaypointIndex = 0;
			Physics.SyncTransforms();
			characterController.enabled = true;
			Debug.LogWarning("[MobileNPCHumanoidMotor] Recovered NPC after unexpected terrain fall; grounded at "
				+ recoveredPosition + ". Check map collision layers if this repeats.", this);
		}

		/// <summary>Reports collision state only while falling, to distinguish a bad ground mask from a disabled/non-colliding controller.</summary>
		private void DiagnoseFallingNPC(float currentY)
		{
			Vector3 probeOrigin = new Vector3(transform.position.x, Mathf.Max(currentY + 2f, spawnPosition.y + 2f), transform.position.z);
			float probeDistance = Mathf.Max(10f, probeOrigin.y - currentY + 5f);
			bool hitAnyGround = false;
			string anyGround = "none";
			RaycastHit[] allHits = Physics.RaycastAll(probeOrigin, Vector3.down, probeDistance, ~0, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < allHits.Length; ++i)
			{
				if (allHits[i].collider == null || IsOwnCollider(allHits[i].collider) || Vector3.Angle(allHits[i].normal, Vector3.up) > 55f)
					continue;
				hitAnyGround = true;
				anyGround = allHits[i].collider.name + "(layer=" + allHits[i].collider.gameObject.layer + ", y=" + allHits[i].point.y.ToString("F2") + ")";
				break;
			}
			bool hitBlockCollision = Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit blockHit, probeDistance, RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
			Debug.LogWarning("[MobileNPCHumanoidMotor] FALL DIAGNOSTIC: pos=" + transform.position
				+ ", spawn=" + spawnPosition + ", active=" + gameObject.activeInHierarchy
				+ ", layer=" + gameObject.layer + ", controllerEnabled=" + characterController.enabled
				+ ", detectCollisions=" + characterController.detectCollisions + ", grounded=" + characterController.isGrounded
				+ ", radius=" + characterController.radius + ", height=" + characterController.height
				+ ", center=" + characterController.center + ", skinWidth=" + characterController.skinWidth
				+ ", stepOffset=" + characterController.stepOffset + ", slopeLimit=" + characterController.slopeLimit
				+ ", hitWalkableWithAllLayers=" + hitAnyGround + ", allLayersGround=" + anyGround
				+ ", hitBlockCollision=" + hitBlockCollision
				+ (hitBlockCollision ? ", blockGround=" + blockHit.collider.name + "(layer=" + blockHit.collider.gameObject.layer + ", y=" + blockHit.point.y.ToString("F2") + ")" : "")
				+ ", ownRootColliders=" + GetComponents<Collider>().Length + ".", this);
		}

		private bool TryFindRecoveryGround(Vector3 origin, float distance, float preferredY, out RaycastHit selectedHit)
		{
			selectedHit = default(RaycastHit);
			RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
			float bestVerticalDifference = float.PositiveInfinity;
			bool found = false;
			for (int i = 0; i < hits.Length; ++i)
			{
				RaycastHit candidate = hits[i];
				if (candidate.collider == null || IsOwnCollider(candidate.collider)
					|| Vector3.Angle(candidate.normal, Vector3.up) > 55f)
				{
					continue;
				}

				float verticalDifference = Mathf.Abs(candidate.point.y - preferredY);
				if (verticalDifference < bestVerticalDifference)
				{
					bestVerticalDifference = verticalDifference;
					selectedHit = candidate;
					found = true;
				}
			}
			return found;
		}
		private void RememberLocalNavigationDeadEnd(Vector3 blockedPosition, Vector3 goal)
		{
			if (!Provider.isServer || !Level.checkSafeIncludingClipVolumes(blockedPosition))
				return;
			recentNavDeadEndPositions[nextNavDeadEndSlot] = blockedPosition;
			recentNavDeadEndGoals[nextNavDeadEndSlot] = goal;
			recentNavDeadEndExpiry[nextNavDeadEndSlot] = Time.time + 18f;
			nextNavDeadEndSlot = (nextNavDeadEndSlot + 1) % MaxRecentNavDeadEnds;
		}

		private float LocalDeadEndPenalty(Vector3 candidate, Vector3 goal)
		{
			float penalty = 0f;
			for (int i = 0; i < MaxRecentNavDeadEnds; ++i)
			{
				if (Time.time > recentNavDeadEndExpiry[i]
					|| PlanarDistanceSquared(goal, recentNavDeadEndGoals[i]) > 9f)
					continue;
				float squareDistance = PlanarDistanceSquared(candidate,
					recentNavDeadEndPositions[i]);
				if (squareDistance < 9f)
					penalty += 12f * (1f - squareDistance / 9f);
			}
			return penalty;
		}

		private void UpdateStuckRecovery(bool wantedToMove, float deltaTime)
		{
			if (Time.time < nextStuckCheckTime)
			{
				return;
			}

			float elapsed = Mathf.Max(0.1f, Time.time - (nextStuckCheckTime - 0.75f));
			nextStuckCheckTime = Time.time + 0.75f;
			Vector3 samplePosition = transform.position;
			Vector3 previousPosition = lastStuckCheckPosition;
			float movedDistance = Vector3.Distance(samplePosition, previousPosition);
			lastStuckCheckPosition = samplePosition;

			bool madeMeaningfulProgress = movedDistance >= 0.12f;
			if (brainDriver != null && brainDriver.HasDestination)
			{
				Vector3 currentGoal = brainDriver.Destination;
				// A newly assigned target must not inherit the previous destination's
				// stuck counter, otherwise switching from loot to combat can trip it.
				if (!hasTrackedStuckGoal || PlanarDistanceSquared(currentGoal, lastTrackedStuckGoal) > 9f)
				{
					hasTrackedStuckGoal = true;
					lastTrackedStuckGoal = currentGoal;
					lastMeaningfulRouteProgressPosition = samplePosition;
					blockedMovementDuration = 0f;
					useRecoverySteering = false;
					return;
				}
				lastTrackedStuckGoal = currentGoal;
				float previousGoalDistance = Mathf.Sqrt(PlanarDistanceSquared(previousPosition, currentGoal));
				float currentGoalDistance = Mathf.Sqrt(PlanarDistanceSquared(samplePosition, currentGoal));
				float anchorDistance = Mathf.Sqrt(PlanarDistanceSquared(
					lastMeaningfulRouteProgressPosition, currentGoal));
				// Sidestepping a wall may temporarily fail to reduce goal distance.
				// Accept a substantial net detour only if it has not carried the
				// NPC significantly farther away from the destination.
				bool advancingTowardGoal = previousGoalDistance - currentGoalDistance >= 0.12f;
				bool advancingAroundObstacle =
					PlanarDistanceSquared(samplePosition, lastMeaningfulRouteProgressPosition) >= 1.44f
					&& currentGoalDistance <= anchorDistance + 0.6f;
				// Taking a valid corner route often increases straight-line
				// distance to the *final* destination. Measure progress against
				// the current route/detour waypoint as well, so the motor doesn't
				// tear down working A* paths halfway around a building.
				bool advancingAlongPlannedRoute = false;
				if (routeWaypointIndex < routeWaypoints.Count)
				{
					Vector3 waypoint = routeWaypoints[routeWaypointIndex];
					advancingAlongPlannedRoute =
						Mathf.Sqrt(PlanarDistanceSquared(previousPosition, waypoint))
						- Mathf.Sqrt(PlanarDistanceSquared(samplePosition, waypoint)) >= 0.10f;
				}
				else if (hasDetourWaypoint)
				{
					advancingAlongPlannedRoute =
						Mathf.Sqrt(PlanarDistanceSquared(previousPosition, detourWaypoint))
						- Mathf.Sqrt(PlanarDistanceSquared(samplePosition, detourWaypoint)) >= 0.10f;
				}
				madeMeaningfulProgress = advancingTowardGoal || advancingAroundObstacle
					|| advancingAlongPlannedRoute;
				if (madeMeaningfulProgress)
					lastMeaningfulRouteProgressPosition = samplePosition;
			}
			else
			{
				hasTrackedStuckGoal = false;
				lastMeaningfulRouteProgressPosition = samplePosition;
			}

			if (!wantedToMove || madeMeaningfulProgress)
			{
				blockedMovementDuration = 0f;
				useRecoverySteering = false;
				if (movedDistance >= 0.7f)
					consecutiveStuckRecoveries = 0;
				return;
			}

			blockedMovementDuration += elapsed;
			if (blockedMovementDuration < 1.5f)
			{
				return;
			}

			// Preserve combat/investigation destinations. Only roaming waypoints are discarded;
			// tactical movement gets a temporary alternate steering angle instead.
			if (hasRoamingDestination && !brainDriver.HasDestination)
			{
				hasRoamingDestination = false;
				hasDetourWaypoint = false;
				nextRoamingDecisionTime = Time.time + Random.Range(2f, 4f);
				blockedMovementDuration = 0f;
				useRecoverySteering = false;
				return;
			}

			if (brainDriver.HasDestination)
			{
				// Actual lack of movement is evidence that this local approach
				// failed, not that the global tactical goal has been completed.
				RememberLocalNavigationDeadEnd(samplePosition, brainDriver.Destination);
				// A failed detour must not keep pulling the NPC toward the same unusable point.
				// Clear it so the next bounded attempt can choose a different side.
				ReportLearnedRouteInvalidatedServer();
				hasDetourWaypoint = false;
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				nextDetourAttemptTime = Time.time + 0.2f;
				// Do not randomly flip left/right each time the agent touches a wall.
				// Keep one side until a genuine route attempt has had time to progress.
				consecutiveStuckRecoveries++;
				if (consecutiveStuckRecoveries >= 4
					&& brainDriver.ReportTacticalDestinationUnreachableServer(brainDriver.Destination))
				{
					// Avoid hammering an impossible seek-cover/flank point. The
					// driver records the failed physical route as personal evidence,
					// then chooses another option on a later decision tick.
					blockedMovementDuration = 0f;
					useRecoverySteering = false;
					consecutiveStuckRecoveries = 0;
					hasDetourWaypoint = false;
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
					nextLocalRoutePlanTime = Time.time + 0.75f;
					return;
				}
				// Give a chosen side enough time to reach the end of a house wall.
				// Flip only after that side's commitment actually expires, not
				// on every 1.5-second stuck recovery event.
				if (Time.time >= avoidanceSideUntil)
				{
					avoidanceSide = -avoidanceSide;
					avoidanceSideUntil = Time.time + 4f;
				}
				recoverySteeringAngle = avoidanceSide * 90f;
				useRecoverySteering = true;
				recoverySteeringUntil = Time.time + 1.5f;
				nextRoutePlanTime = Time.time;
				Debug.LogWarning("[MobileNPCHumanoidMotor] Blocked at " + transform.position
					+ "; attempting alternate steering side=" + avoidanceSide
					+ ", stuckRecoveries=" + consecutiveStuckRecoveries
					+ ", brainGoal=" + brainDriver.CurrentGoal + ".", this);
				blockedMovementDuration = 0.25f;
			}
		}

		private bool IsObstacleBlocking(Vector3 direction)
		{
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f)
			{
				return true;
			}

			Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
			float probeRadius = Mathf.Clamp(obstacleProbeRadius, 0.05f, Mathf.Max(0.05f, characterController.radius * 0.9f));
			return IsObstacleBlockingAlong(origin, probeRadius, direction.normalized,
				Mathf.Max(0.5f, obstacleProbeDistance));
		}


		/// <summary>
		/// Collision probe that ignores this humanoid's own colliders. NonAlloc is the
		/// normal path; if the fixed buffer fills, fall back to a complete query so a
		/// nearby obstruction cannot be missed because of result truncation.
		/// </summary>
		private bool IsObstacleBlockingAlong(Vector3 origin, float radius, Vector3 direction, float distance)
		{
			int hitCount = Physics.SphereCastNonAlloc(origin, radius, direction, ObstacleHitBuffer,
				distance, obstacleMask, QueryTriggerInteraction.Ignore);
			if (hitCount >= ObstacleHitBuffer.Length)
			{
				RaycastHit[] allHits = Physics.SphereCastAll(origin, radius, direction, distance,
					obstacleMask, QueryTriggerInteraction.Ignore);
				for (int i = 0; i < allHits.Length; ++i)
				{
					if (!IsOwnCollider(allHits[i].collider))
						return true;
				}
				return false;
			}

			for (int i = 0; i < hitCount; ++i)
			{
				if (!IsOwnCollider(ObstacleHitBuffer[i].collider))
					return true;
			}
			return false;
		}

		/// <summary>
		/// An NPC passing through a doorway is not a permanent map obstacle.
		/// Only preserve an otherwise valid A* route if EVERY blocking hit
		/// belongs to another living actor. Any solid wall, vehicle or truncated
		/// hit buffer is conservatively treated as a genuine obstruction.
		/// </summary>
		private bool IsOnlyTemporaryActorBlockingAlong(Vector3 origin, float radius,
			Vector3 direction, float distance)
		{
			int hitCount = Physics.SphereCastNonAlloc(origin, radius, direction,
				ObstacleHitBuffer, distance, obstacleMask, QueryTriggerInteraction.Ignore);
			if (hitCount == 0 || hitCount >= ObstacleHitBuffer.Length)
				return false;
			bool foundOtherActor = false;
			for (int i = 0; i < hitCount; ++i)
			{
				Collider collider = ObstacleHitBuffer[i].collider;
				if (collider == null || IsOwnCollider(collider))
					continue;
				bool isOtherActor =
					collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null
					|| collider.GetComponentInParent<Player>() != null
					|| collider.GetComponentInParent<Zombie>() != null;
				if (!isOtherActor)
					return false;
				foundOtherActor = true;
			}
			return foundOtherActor;
		}

		private bool IsOwnCollider(Collider hitCollider)
		{
			return hitCollider != null
				&& (hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform));
		}

		private Vector3 ApplyTerrainAwareSteering(Vector3 desiredDirection, bool isUnderwater)
		{
			desiredDirection.y = 0f;
			if (desiredDirection.sqrMagnitude < 0.0001f)
				return Vector3.zero;
			desiredDirection.Normalize();

			Vector3 obstacleSafeDirection = ApplyLocalObstacleAvoidance(desiredDirection);
			if (obstacleSafeDirection.sqrMagnitude < 0.0001f)
				return Vector3.zero;
			if (isUnderwater || HasWalkableGroundAhead(obstacleSafeDirection))
				return obstacleSafeDirection;

			// A clear ray is not enough on uneven ground: reject directions whose next
			// short step has no nearby walkable surface, then try the sides. Swimming is
			// exempt because the swim controller intentionally has no ground beneath it.
			Vector3 bestDirection = Vector3.zero;
			float bestAlignment = float.NegativeInfinity;
			for (int i = 0; i < FallbackSteeringAngles.Length; ++i)
			{
				Vector3 candidate = Quaternion.AngleAxis(FallbackSteeringAngles[i], Vector3.up) * desiredDirection;
				if (IsObstacleBlocking(candidate) || !HasWalkableGroundAhead(candidate))
					continue;
				float alignment = Vector3.Dot(candidate, desiredDirection);
				if (alignment > bestAlignment)
				{
					bestAlignment = alignment;
					bestDirection = candidate;
				}
			}
			return bestDirection;
		}

		private static float PlanarDistanceSquared(Vector3 a, Vector3 b)
		{
			float dx = a.x - b.x;
			float dz = a.z - b.z;
			return dx * dx + dz * dz;
		}

		private bool HasWalkableGroundAhead(Vector3 direction)
		{
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f)
				return false;
			direction.Normalize();
			// Check the next actual walking stride, not a point several metres
			// ahead: the old long probe rejected otherwise traversable curbs.
			Vector3 candidate = transform.position + direction * Mathf.Clamp(obstacleProbeDistance * 0.65f, 0.65f, 1.25f);
			if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, 5f, 3.5f,
				roamingGroundMask, Mathf.Clamp(roamingMaximumSlope, 35f, 48f), out Vector3 ground, out _))
				return false;
			float rise = ground.y - transform.position.y;
			// Ordinary CharacterController stepOffset can climb small curbs,
			// but tall ledges should still require a planned route.
			return rise <= Mathf.Max(0.35f, characterController.stepOffset + characterController.skinWidth + 0.1f)
				&& rise >= -1.1f;
		}

		private Vector3 ApplyLocalObstacleAvoidance(Vector3 desiredDirection)
		{
			desiredDirection.y = 0f;
			if (desiredDirection.sqrMagnitude < 0.0001f)
			{
				return Vector3.zero;
			}

			desiredDirection.Normalize();
			Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
			float probeDistance = Mathf.Max(0.5f, obstacleProbeDistance);
			float probeRadius = Mathf.Clamp(obstacleProbeRadius, 0.05f, Mathf.Max(0.05f, characterController.radius * 0.9f));
			if (!IsObstacleBlockingAlong(origin, probeRadius, desiredDirection, probeDistance))
			{
				return desiredDirection;
			}

			// Probe both sides and prefer the clear direction that remains most aligned with the goal.
			// This is deliberately short-range steering: it cannot route around large buildings.
			Vector3 left = Quaternion.AngleAxis(-obstacleAvoidanceAngle, Vector3.up) * desiredDirection;
			Vector3 right = Quaternion.AngleAxis(obstacleAvoidanceAngle, Vector3.up) * desiredDirection;
			bool leftBlocked = IsObstacleBlockingAlong(origin, probeRadius, left, probeDistance);
			bool rightBlocked = IsObstacleBlockingAlong(origin, probeRadius, right, probeDistance);

			if (!leftBlocked && !rightBlocked)
			{
				// Commit to one side during a wall-follow interval: per-frame random
				// choice previously caused left/right oscillation at house corners.
				if (Time.time >= avoidanceSideUntil)
				{
					avoidanceSide = Vector3.Dot(transform.right, desiredDirection) >= 0f ? 1 : -1;
					avoidanceSideUntil = Time.time + 3f;
				}
				return avoidanceSide < 0 ? left : right;
			}
			if (!leftBlocked)
			{
				avoidanceSide = -1;
				avoidanceSideUntil = Time.time + 3f;
				return left;
			}
			if (!rightBlocked)
			{
				avoidanceSide = 1;
				avoidanceSideUntil = Time.time + 3f;
				return right;
			}

			// Wider probes help escape corners while still respecting the local collision mask.
			Vector3 bestDirection = Vector3.zero;
			float bestAlignment = float.NegativeInfinity;
			for (int i = 0; i < FallbackSteeringAngles.Length; ++i)
			{
				Vector3 candidate = Quaternion.AngleAxis(FallbackSteeringAngles[i], Vector3.up) * desiredDirection;
				if (IsObstacleBlockingAlong(origin, probeRadius, candidate, probeDistance))
				{
					continue;
				}

				float alignment = Vector3.Dot(candidate, desiredDirection);
				if (alignment > bestAlignment)
				{
					bestAlignment = alignment;
					bestDirection = candidate;
				}
			}

			return bestDirection;
		}


		/// <summary>
		/// Creates one short-lived waypoint around a nearby obstruction. The original
		/// brain/roaming destination remains authoritative and resumes after the detour.
		/// This is bounded local routing, not map-wide pathfinding.
		/// </summary>
		private void TryCreateDetourWaypoint(Vector3 destination)
		{
			nextDetourAttemptTime = Time.time + 1.0f;
			Vector3 toGoal = destination - transform.position;
			toGoal.y = 0f;
			if (toGoal.sqrMagnitude < 0.01f)
				return;

			// Prefer Unturned's native A* graph when the licensed runtime library exists.
			// The provider is reflection-only because the public SDK omits ASPFP sources.
			if (TryBuildNativeAStarRoute(destination))
			{
				detourGoalSnapshot = destination;
				hasDetourWaypoint = false;
				return;
			}

			// Fallback: bounded local grid search. The grid is centered on the NPC,
			// limited to 81 nodes, and resolves only ground close to the current floor.
			// A distant goal is approached in successive local segments rather than
			// expanding the search without limit.
			if (TryBuildLocalRoute(destination))
			{
				detourGoalSnapshot = destination;
				hasDetourWaypoint = false;
				return;
			}

			// Conservative fallback: retain the prior single-hop detour when the grid
			// cannot find a route in the bounded search area.
			Vector3 desired = toGoal.normalized;
			Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f, Mathf.Max(0.05f, characterController.radius * 0.9f));
			float probeDistance = Mathf.Max(0.5f, obstacleProbeDistance);
			float step = Mathf.Clamp(probeDistance + characterController.radius * 2f, 1.5f, 4.5f);
			float bestScore = float.NegativeInfinity;
			Vector3 bestPoint = Vector3.zero;

			for (int i = 0; i < FallbackSteeringAngles.Length; ++i)
			{
				Vector3 direction = Quaternion.AngleAxis(FallbackSteeringAngles[i], Vector3.up) * desired;
				if (IsObstacleBlockingAlong(origin, radius, direction, step))
					continue;

				Vector3 candidate = transform.position + direction * step;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
					Mathf.Max(1f, roamingSearchHeight), Mathf.Max(0f, roamingMaximumDrop),
					roamingGroundMask, roamingMaximumSlope, out Vector3 resolved, out _))
					continue;

				Vector3 resolvedOffset = resolved - transform.position;
				resolvedOffset.y = 0f;
				if (resolvedOffset.sqrMagnitude < 1f)
					continue;
				Vector3 resolvedDirection = resolvedOffset.normalized;
				if (IsObstacleBlockingAlong(origin, radius, resolvedDirection,
					Mathf.Min(resolvedOffset.magnitude, step)))
					continue;

				float progress = Vector3.Dot(resolvedDirection, desired);
				Vector3 candidateGoalOffset = destination - resolved;
				candidateGoalOffset.y = 0f;
				float goalDistanceImprovement = toGoal.magnitude - candidateGoalOffset.magnitude;
				float score = progress * 3f + goalDistanceImprovement * 0.5f - resolvedOffset.magnitude * 0.05f;
				if (score > bestScore)
				{
					bestScore = score;
					bestPoint = resolved;
				}
			}

			if (bestScore > float.NegativeInfinity)
			{
				detourWaypoint = bestPoint;
				detourGoalSnapshot = destination;
				hasDetourWaypoint = true;
			}
		}

		/// <summary>
		/// Requests a route from Unturned's active A* Pathfinding Project graph when the
		/// game runtime exposes it. The provider is asynchronous; false means either
		/// no route is ready yet or the native backend is unavailable.
		/// </summary>
		/// <summary>
		/// Tracks the physical journey requested by this NPC, separately from
		/// speculative route plans. A changing combat goal discards the old
		/// partial trace rather than "learning" a path never completed.
		/// </summary>
		private void BeginOrContinueActualJourney(Vector3 requestedGoal)
		{
			// Never begin a "walked" route from an airborne position, nor
			// re-learn the arc as a ground connector on the next physics tick.
			if (travelMemory == null || isPlannedJumping
				|| IsTraversingLadder || IsVaultTraversing
				|| Time.time < suppressJourneyUntil)
			{
				trackingActualJourney = false;
				actualTravelFootsteps.Clear();
				return;
			}
			if (!trackingActualJourney ||
				Vector3.Distance(actualTravelGoal, requestedGoal) > 1.2f)
			{
				trackingActualJourney = true;
				actualJourneyExceededLimit = false;
				actualTravelStart = transform.position;
				actualTravelGoal = requestedGoal;
				actualTravelFootsteps.Clear();
				actualTravelFootsteps.Add(transform.position);
			}
		}

		/// <summary>
		/// Called AFTER CharacterController.Move, never on planned waypoints.
		/// Outliers/teleports, swimming and unfinished journeys are not taught.
		/// </summary>
		private void RecordActualMovementAndJourneyOutcome(bool hasDestination,
			Vector3 requestedGoal, bool isUnderwater)
		{
			if (!hasDestination || !trackingActualJourney || isUnderwater
				|| isPlannedJumping || IsTraversingLadder
				|| IsVaultTraversing || Time.time < suppressJourneyUntil
				|| travelMemory == null || actualJourneyExceededLimit)
				return;
			Vector3 actual = transform.position;
			Vector3 previous = actualTravelFootsteps[actualTravelFootsteps.Count - 1];
			float displacement = Vector3.Distance(previous, actual);
			if (displacement > 2.6f)
			{
				// A teleport, physics glitch or very large unsampled displacement
				// is not a verified continuous walkable connector.
				actualJourneyExceededLimit = true;
				return;
			}
			bool reachedActualGoal =
				PlanarDistanceSquared(actual, requestedGoal)
					<= stoppingDistance * stoppingDistance
				&& Mathf.Abs(actual.y - requestedGoal.y)
					<= Mathf.Max(0.4f, characterController.stepOffset + 0.15f);
			if (displacement >= 1.05f || (reachedActualGoal && displacement >= 0.1f))
			{
				if (actualTravelFootsteps.Count >= MobileNPCTravelMemory.MaxFootsteps)
				{
					actualJourneyExceededLimit = true;
					return;
				}
				actualTravelFootsteps.Add(actual);
			}
			if (reachedActualGoal)
			{
				if (actualTravelFootsteps.Count >= 3
					&& travelMemory.RememberCompletedJourneyServer(
						actualTravelStart, requestedGoal, actualTravelFootsteps))
				{
					activeRememberedRouteIndex = -1;
					activeContinuationRouteIndex = -1;
					learnedStairStartWaypointIndex = -1;
					learnedContinuationStartWaypointIndex = -1;
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
				}
				trackingActualJourney = false;
				actualTravelFootsteps.Clear();
			}
		}

		/// <summary>
		/// Try an individual's first-hand remembered journey BEFORE rebuilding
		/// the same route. All remembered segments are physical candidates,
		/// never authoritative free movement. Revalidate current ground and
		/// collision, rejecting changed doors/walls and wrong-storey endpoints.
		/// </summary>
		private bool TryReuseActualLearnedRoute(Vector3 destination)
		{
			if (!Provider.isServer || travelMemory == null || characterController == null
				|| routeWaypoints.Count > 0
				|| (targetIsVisible && brainDriver != null
					&& (brainDriver.CurrentGoal == MobileNPCTactics.Goal.SeekCover
						|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat))
				|| !travelMemory.TryFindCandidateServer(transform.position,
					destination, rememberedRouteScratch, out int learnedIndex))
				return false;

			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
				Mathf.Max(0.05f, characterController.radius * 0.9f));
			Vector3 previous = transform.position;
			bool valid = true;
			for (int i = 0; i < rememberedRouteScratch.Count; ++i)
			{
				Vector3 next = rememberedRouteScratch[i];
				float distance = Vector3.Distance(previous, next);
				if (distance > 2.6f || !Level.checkSafeIncludingClipVolumes(next)
					|| (distance > 0.25f
						&& !IsActuallyWalkableStairConnection(previous, next, radius)))
				{
					valid = false;
					break;
				}
				previous = next;
			}
			// Past arrival may be one stopping radius from the new destination.
			// Never append an unvalidated final point behind another wall.
			float finalGap = Vector3.Distance(previous, destination);
			if (finalGap > 1.25f || (finalGap > 0.4f
				&& !IsActuallyWalkableStairConnection(previous, destination, radius)))
				valid = false;

			if (!valid)
			{
				travelMemory.ReportReuseFailureServer(learnedIndex);
				return false;
			}

			routeWaypoints.Clear();
			for (int i = 1; i < rememberedRouteScratch.Count; ++i)
				routeWaypoints.Add(rememberedRouteScratch[i]);
			if (finalGap > 0.6f)
				routeWaypoints.Add(destination);
			routeWaypointIndex = 0;
			detourGoalSnapshot = destination;
			hasDetourWaypoint = false;
			activeRememberedRouteIndex = learnedIndex;
			activeContinuationRouteIndex = -1;
			learnedStairStartWaypointIndex = -1;
			learnedContinuationStartWaypointIndex = -1;
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
			nextPlannedRouteValidationTime = 0f;
			return routeWaypoints.Count > 0;
		}

		private void ReportLearnedRouteInvalidatedServer()
		{
			if (travelMemory != null && activeAccessRouteIds.Count > 0)
			{
				int entry = activeAccessRouteIds[0];
				if (entry >= 0 && entry < knownAccessEntryRetryUntil.Length)
					knownAccessEntryRetryUntil[entry] = Time.time + 5f;
				// A newly computed approach or the last unobserved link to a
				// changed brain goal must not penalize a completed old journey.
				if (routeWaypointIndex >= activeAccessWaypointStarts[0]
					&& (activeAccessFinalWaypointIndex < 0
						|| routeWaypointIndex < activeAccessFinalWaypointIndex))
				{
					int stage = 0;
					for (int i = 1; i < activeAccessWaypointStarts.Count; ++i)
						if (routeWaypointIndex >= activeAccessWaypointStarts[i])
							stage = i;
					// At a stage's first waypoint the attempted movement may be
					// the small, newly verified gap BETWEEN old journeys,
					// rather than an observed step within that journey.
					if (routeWaypointIndex > activeAccessWaypointStarts[stage])
						travelMemory.ReportReuseFailureServer(activeAccessRouteIds[stage]);
				}
			}
			else if (travelMemory != null)
			{
				bool directRoute = learnedStairStartWaypointIndex < 0;
				bool onStair = !directRoute
					&& routeWaypointIndex >= learnedStairStartWaypointIndex
					&& (learnedContinuationStartWaypointIndex < 0
						|| routeWaypointIndex < learnedContinuationStartWaypointIndex);
				bool onContinuation = learnedContinuationStartWaypointIndex >= 0
					&& routeWaypointIndex >= learnedContinuationStartWaypointIndex;
				if (activeRememberedRouteIndex >= 0 && (directRoute || onStair))
					travelMemory.ReportReuseFailureServer(activeRememberedRouteIndex);
				if (activeContinuationRouteIndex >= 0 && onContinuation
					&& activeContinuationRouteIndex != activeRememberedRouteIndex)
					travelMemory.ReportReuseFailureServer(activeContinuationRouteIndex);
			}
			activeRememberedRouteIndex = -1;
			activeContinuationRouteIndex = -1;
			learnedStairStartWaypointIndex = -1;
			learnedContinuationStartWaypointIndex = -1;
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
		}

		/// <summary>
		/// Portal-style, two-stage routing assembled from actual experience:
		/// (1) compute a REAL physically walkable route to a previously visited
		/// stair entrance; (2) revalidate and append this NPC's actually traveled
		/// floor-connecting footsteps. The brain's final target remains unchanged.
		/// No arbitrary map portal, instantaneous elevation or faction knowledge.
		/// </summary>
		/// <summary>
		/// Plan a real path to one individually observed access, then join up
		/// to four first-hand, directed stair/corridor journeys. Every joining
		/// segment is physically verified; the original brain goal is retained.
		/// Per-entry cooldowns let a second known staircase be attempted when
		/// an approach or old corridor is no longer navigable.
		/// </summary>
		private bool TryBuildKnownFloorAccessRoute(Vector3 destination,
			out bool waitingForApproach)
		{
			waitingForApproach = false;
			if (!Provider.isServer || travelMemory == null || characterController == null
				|| !characterController.enabled || routeWaypoints.Count > 0
				// For same-floor rooms, preserve cheap local steering inside 5m.
				// Longer known corridors can now be composed like stair paths.
				|| (Mathf.Abs(destination.y - transform.position.y) < 1.2f
					&& PlanarDistanceSquared(destination, transform.position) <= 25f)
				|| (targetIsVisible && brainDriver != null
					&& (brainDriver.CurrentGoal == MobileNPCTactics.Goal.SeekCover
						|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat)))
				return false;

			if ((lastKnownAccessGoal - destination).sqrMagnitude > 2.25f)
			{
				hasPendingKnownAccessPath = false;
				pendingKnownAccessEntryRouteId = -1;
				lastKnownAccessGoal = destination;
			}
			if (Time.time < nextKnownAccessPlanTime)
			{
				waitingForApproach = hasPendingKnownAccessPath;
				return false;
			}
			// Bound physics-heavy validation across all active NPCs, not only
			// per NPC. Jitter next admission to avoid the same actors always
			// monopolizing the shared slot when 20-40 NPCs replan together.
			if (Time.time < nextSharedKnownAccessRouteSlot)
			{
				waitingForApproach = hasPendingKnownAccessPath;
				nextKnownAccessPlanTime = Time.time + Random.Range(0.12f, 0.28f);
				return false;
			}
			nextSharedKnownAccessRouteSlot = Time.time + 0.12f;
			nextKnownAccessPlanTime = Time.time + 0.65f + Random.Range(0f, 0.25f);

			int excludedEntries = 0;
			if (hasPendingKnownAccessPath && pendingKnownAccessEntryRouteId >= 0)
			{
				// Keep the SAME entrance requested by asynchronous native A*
				// until its pending/ready result is consumed or the goal changes.
				excludedEntries = 255 ^ (1 << pendingKnownAccessEntryRouteId);
			}
			else
			{
				for (int i = 0; i < knownAccessEntryRetryUntil.Length; ++i)
					if (Time.time < knownAccessEntryRetryUntil[i])
						excludedEntries |= 1 << i;
			}
			if (!travelMemory.TryFindKnownAccessChainServer(transform.position,
				destination, learnedAccessChain, learnedAccessRouteIds,
				learnedAccessStageStarts, out Vector3 entrance, excludedEntries))
			{
				hasPendingKnownAccessPath = false;
				pendingKnownAccessEntryRouteId = -1;
				return false;
			}
			int entryRouteIndex = learnedAccessRouteIds[0];
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
				Mathf.Max(0.05f, characterController.radius * 0.9f));
			learnedAccessApproach.Clear();
			float entranceDistance = Vector3.Distance(transform.position, entrance);
			if (entranceDistance <= 0.8f)
			{
				// Already standing at an observed entry. A* can report no route
				// when start and goal occupy the same cell. Verify the very
				// short physical connector instead of forgetting this access.
				if (entranceDistance > 0.2f
					&& !IsActuallyWalkableStairConnection(
						transform.position, entrance, radius))
				{
					knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
					return false;
				}
				learnedAccessApproach.Add(entrance);
			}
			else if (entranceDistance <= 5.0f)
			{
				if (!TryBuildLocalRoute(entrance, learnedAccessApproach, true))
				{
					knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
					hasPendingKnownAccessPath = false;
					pendingKnownAccessEntryRouteId = -1;
					return false;
				}
			}
			else
			{
				if (nativeAStarRouteProvider == null
					|| !nativeAStarRouteProvider.IsAvailable)
				{
					knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
					hasPendingKnownAccessPath = false;
					pendingKnownAccessEntryRouteId = -1;
					return false;
				}
				if (!nativeAStarRouteProvider.TryGetRoute(
					transform.position, entrance, learnedAccessApproach))
				{
					hasPendingKnownAccessPath = nativeAStarRouteProvider.IsRequestPending;
					pendingKnownAccessEntryRouteId = hasPendingKnownAccessPath
						? entryRouteIndex : -1;
					waitingForApproach = hasPendingKnownAccessPath;
					if (!hasPendingKnownAccessPath)
						knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
					return false;
				}
			}
			hasPendingKnownAccessPath = false;
			pendingKnownAccessEntryRouteId = -1;
			if (learnedAccessApproach.Count < 1
				|| learnedAccessApproach.Count > 32
				|| learnedAccessChain.Count < 3
				|| learnedAccessChain.Count > MobileNPCTravelMemory.MaxAccessChainFootsteps
				|| learnedAccessRouteIds.Count < 1
				|| learnedAccessRouteIds.Count > MobileNPCTravelMemory.MaxAccessChainRoutes
				|| learnedAccessRouteIds.Count != learnedAccessStageStarts.Count
				|| learnedAccessApproach.Count + learnedAccessChain.Count > 176)
			{
				knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
				return false;
			}

			Vector3 previous = transform.position;
			for (int i = 0; i < learnedAccessApproach.Count; ++i)
			{
				Vector3 next = learnedAccessApproach[i];
				float length = Vector3.Distance(previous, next);
				if (length > 10f || !Level.checkSafeIncludingClipVolumes(next)
					|| (length > 0.2f
						&& !IsActuallyWalkableStairConnection(previous, next, radius)))
				{
					knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
					return false;
				}
				previous = next;
			}
			if (Vector3.Distance(previous, entrance) > 1.2f
				|| Mathf.Abs(previous.y - entrance.y) > 0.6f)
			{
				knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
				return false;
			}

			int stage = 0;
			for (int i = 0; i < learnedAccessChain.Count; ++i)
			{
				while (stage + 1 < learnedAccessStageStarts.Count
					&& i >= learnedAccessStageStarts[stage + 1])
					++stage;
				Vector3 next = learnedAccessChain[i];
				float length = Vector3.Distance(previous, next);
				if (length > 2.6f || !Level.checkSafeIncludingClipVolumes(next)
					|| (length > 0.2f
						&& !IsActuallyWalkableStairConnection(previous, next, radius)))
				{
					// An unobserved gap between records is a planning failure,
					// NOT proof that either of the completed routes is false.
					if (i > learnedAccessStageStarts[stage])
						travelMemory.ReportReuseFailureServer(learnedAccessRouteIds[stage]);
					knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
					return false;
				}
				previous = next;
			}
			float finalDistance = Vector3.Distance(previous, destination);
			if (finalDistance > 1.9f
				|| (finalDistance > 0.35f
					&& !IsActuallyWalkableStairConnection(previous, destination, radius)))
			{
				knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
				return false;
			}

			routeWaypoints.Clear();
			for (int i = 0; i < learnedAccessApproach.Count; ++i)
				if (Vector3.Distance(transform.position, learnedAccessApproach[i]) > 0.5f)
					routeWaypoints.Add(learnedAccessApproach[i]);
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
			for (int s = 0; s < learnedAccessRouteIds.Count; ++s)
			{
				int first = learnedAccessStageStarts[s];
				int end = s + 1 < learnedAccessStageStarts.Count
					? learnedAccessStageStarts[s + 1] : learnedAccessChain.Count;
				int stageWaypointStart = routeWaypoints.Count;
				for (int i = first; i < end; ++i)
					if (routeWaypoints.Count == 0
						|| Vector3.Distance(routeWaypoints[routeWaypoints.Count - 1],
							learnedAccessChain[i]) > 0.25f)
						routeWaypoints.Add(learnedAccessChain[i]);
				if (routeWaypoints.Count > stageWaypointStart)
				{
					activeAccessRouteIds.Add(learnedAccessRouteIds[s]);
					activeAccessWaypointStarts.Add(stageWaypointStart);
				}
			}
			activeAccessFinalWaypointIndex = routeWaypoints.Count;
			if (finalDistance > 0.55f)
				routeWaypoints.Add(destination);
			if (routeWaypoints.Count < 1)
			{
				activeAccessRouteIds.Clear();
				activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
				return false;
			}
			routeWaypointIndex = 0;
			detourGoalSnapshot = destination;
			hasDetourWaypoint = false;
			activeRememberedRouteIndex = -1;
			activeContinuationRouteIndex = -1;
			learnedStairStartWaypointIndex = -1;
			learnedContinuationStartWaypointIndex = -1;
			nextPlannedRouteValidationTime = 0f;
			return true;
		}

		/// <summary>
		/// Assemble an actual A* walking path to the ENTRY of a native Ladder.
		/// The off-mesh change of floor remains exclusively owned by
		/// MobileNPCLadderTraversal after direct tag/contact revalidation.
		/// No pathfinding graph edge is added across the building wall.
		/// </summary>
		private bool TryBuildPhysicalLadderApproach(Vector3 goal,
			out bool waitingForNativeLadderPath)
		{
			waitingForNativeLadderPath = false;
			if (!Provider.isServer || ladderTraversal == null
				|| IsTraversingLadder || IsVaultTraversing || isPlannedJumping
				|| characterController == null || !characterController.enabled
				|| routeWaypoints.Count != 0 || hasDetourWaypoint
				|| physicallyCrouched
				|| (injurySystem != null && injurySystem.MovementMultiplier <= 0.5f)
				|| Mathf.Abs(goal.y - transform.position.y) < 1.4f)
				return false;
			if (hasPlannedLadderApproach
				&& ((goal - plannedLadderGoal).sqrMagnitude > 2.25f
					|| Time.time > plannedLadderExpiry))
				hasPlannedLadderApproach = false;
			if (Time.time < nextLadderApproachPlanTime)
			{
				waitingForNativeLadderPath = hasPlannedLadderApproach;
				return false;
			}

			if (!hasPlannedLadderApproach)
			{
				if (!ladderTraversal.TryFindApproachServer(goal,
					roamingGroundMask, roamingMaximumSlope, out plannedLadderEntrance))
					return false;
				hasPlannedLadderApproach = true;
				plannedLadderGoal = goal;
				plannedLadderExpiry = Time.time + 10f;
			}

			float distance = Vector3.Distance(transform.position, plannedLadderEntrance);
			if (distance < 1.9f)
			{
				// Near enough for the *real* short-range ladder detector to
				// make the decision on the next update. Hold other planners
				// briefly, without pretending the floor transition happened.
				waitingForNativeLadderPath = true;
				nextLadderApproachPlanTime = Time.time + 0.4f;
				return false;
			}
			ladderApproachScratch.Clear();
			if (distance <= RouteGridCenter * RouteGridCellSize)
			{
				if (!TryBuildLocalRoute(plannedLadderEntrance,
					ladderApproachScratch, true))
				{
					hasPlannedLadderApproach = false;
					nextLadderApproachPlanTime = Time.time + 4f;
					return false;
				}
			}
			else
			{
				if (nativeAStarRouteProvider == null
					|| !nativeAStarRouteProvider.IsAvailable)
				{
					hasPlannedLadderApproach = false;
					nextLadderApproachPlanTime = Time.time + 4f;
					return false;
				}
				if (!nativeAStarRouteProvider.TryGetRoute(transform.position,
					plannedLadderEntrance, ladderApproachScratch))
				{
					waitingForNativeLadderPath = nativeAStarRouteProvider.IsRequestPending;
					nextLadderApproachPlanTime = Time.time + 0.45f;
					if (!waitingForNativeLadderPath)
					{
						hasPlannedLadderApproach = false;
						nextLadderApproachPlanTime = Time.time + 4f;
					}
					return false;
				}
			}
			if (ladderApproachScratch.Count == 0 || ladderApproachScratch.Count > 128)
			{
				hasPlannedLadderApproach = false;
				nextLadderApproachPlanTime = Time.time + 4f;
				return false;
			}
			Vector3 pathEnd = ladderApproachScratch[ladderApproachScratch.Count - 1];
			float connector = Vector3.Distance(pathEnd, plannedLadderEntrance);
			if (connector > 1.25f
				|| Mathf.Abs(pathEnd.y - plannedLadderEntrance.y) > 0.35f
				|| (connector > 0.2f
					&& !IsActuallyWalkableStairConnection(pathEnd,
						plannedLadderEntrance,
						Mathf.Max(0.08f, characterController.radius * 0.75f))))
			{
				hasPlannedLadderApproach = false;
				nextLadderApproachPlanTime = Time.time + 4f;
				return false;
			}
			routeWaypoints.Clear();
			for (int i = 0; i < ladderApproachScratch.Count; ++i)
				if (Vector3.Distance(transform.position, ladderApproachScratch[i]) > 0.45f)
					routeWaypoints.Add(ladderApproachScratch[i]);
			if (routeWaypoints.Count == 0
				|| Vector3.Distance(routeWaypoints[routeWaypoints.Count - 1],
					plannedLadderEntrance) > 0.20f)
				routeWaypoints.Add(plannedLadderEntrance);
			routeWaypointIndex = 0;
			detourGoalSnapshot = goal;
			nextPlannedRouteValidationTime = 0f;
			nextLadderApproachPlanTime = Time.time + 0.9f;
			return true;
		}

		private bool TryBuildNativeAStarRoute(Vector3 destination)
		{
			if (nativeAStarRouteProvider == null
				|| !nativeAStarRouteProvider.TryGetRoute(transform.position, destination, routeWaypoints))
			{
				return false;
			}

			routeWaypointIndex = 0;
			if (routeWaypoints.Count > 0
				&& (routeWaypoints[0] - transform.position).sqrMagnitude <= 0.36f)
			{
				routeWaypoints.RemoveAt(0);
			}

			if (routeWaypoints.Count == 0)
				return false;

			return true;
		}

		/// <summary>
		/// Bounded, side-effect-free route feasibility probe for actual urban
		/// cover around ONE nearby corner. Never overwrites the currently
		/// followed path or its replanning timer. The brain itself limits how
		/// many candidates can request this expensive 81-node check.
		/// </summary>
		public bool CanReachNearbyCoverServer(Vector3 coverPoint)
		{
			if (!Provider.isServer || !hasInitialized || characterController == null
				|| !characterController.enabled
				|| PlanarDistanceSquared(transform.position, coverPoint) > 42f
				|| Mathf.Abs(coverPoint.y - transform.position.y) > 1.0f)
				return false;
			return TryBuildLocalRoute(coverPoint, coverRouteScratch, true);
		}

		/// <summary>
		/// Local multi-surface route for a REAL flight of stairs or sloped ramp.
		/// There are at most two independently grounded surfaces per horizontal
		/// cell. Identical X/Z on two floors does NOT create a vertical edge:
		/// only adjacent cells linked by a sampled, stepwise walkable connector do.
		/// The ordinary horizontal planner remains cheaper for outdoor walking.
		/// </summary>
		private bool TryBuildNearbyVerticalRoute(Vector3 destination)
		{
			// A narrow real staircase may have no treads at 1.25m samples.
			// One smaller second pass is allowed for nearby vertical targets;
			// the same global per-NPC admission and each A* budget still apply.
			if (TryBuildNearbyVerticalRouteAtResolution(destination, RouteGridCellSize))
				return true;
			return PlanarDistanceSquared(destination, transform.position) <= 6.25f
				&& TryBuildNearbyVerticalRouteAtResolution(
					destination, RouteGridCellSize * 0.5f);
		}

		private bool TryBuildNearbyVerticalRouteAtResolution(
			Vector3 destination, float cellSize)
		{
			if (!Provider.isServer || characterController == null || !characterController.enabled
				|| !Level.checkSafeIncludingClipVolumes(destination)
				|| Mathf.Abs(destination.y - transform.position.y) > 4.5f
				|| PlanarDistanceSquared(destination, transform.position) > 30f)
				return false;

			Vector3 basePosition = transform.position;
			float lowY = Mathf.Min(basePosition.y, destination.y) - 0.65f;
			float highY = Mathf.Max(basePosition.y, destination.y) + 1.4f;
			float rayLength = highY - lowY;
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
				Mathf.Max(0.05f, characterController.radius * 0.9f));
			for (int i = 0; i < VerticalNodeLimit; ++i)
			{
				verticalWalkable[i] = false;
				verticalClosed[i] = false;
				verticalCost[i] = float.PositiveInfinity;
				verticalPrevious[i] = -1;
			}
			for (int z = 0; z < RouteGridSide; ++z)
			{
				for (int x = 0; x < RouteGridSide; ++x)
				{
					int cell = z * RouteGridSide + x;
					Vector3 world = basePosition + new Vector3(
						(x - RouteGridCenter) * cellSize, 0f,
						(z - RouteGridCenter) * cellSize);
					world.y = highY;
					int count = Physics.RaycastNonAlloc(world, Vector3.down,
						verticalFloorHitBuffer, rayLength, roamingGroundMask,
						QueryTriggerInteraction.Ignore);
					// Buffer truncation may conceal a ceiling or an intermediate
					// staircase step, so never infer safe ground from partial data.
					if (count <= 0 || count >= verticalFloorHitBuffer.Length)
						continue;
					float lowest = float.PositiveInfinity;
					float highest = float.NegativeInfinity;
					for (int j = 0; j < count; ++j)
					{
						RaycastHit groundHit = verticalFloorHitBuffer[j];
						Collider collider = groundHit.collider;
						if (collider == null || IsOwnCollider(collider)
							|| collider.GetComponentInParent<Player>() != null
							|| collider.GetComponentInParent<Zombie>() != null
							|| collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null
							|| Vector3.Angle(groundHit.normal, Vector3.up)
								> Mathf.Min(characterController.slopeLimit, roamingMaximumSlope))
							continue;
						Vector3 ground = groundHit.point + Vector3.up * 0.05f;
						if (ground.y < lowY || ground.y > highY
							|| !Level.checkSafeIncludingClipVolumes(ground))
							continue;
						if (ground.y < lowest) lowest = ground.y;
						if (ground.y > highest) highest = ground.y;
					}
					if (float.IsPositiveInfinity(lowest))
						continue;
					int first = cell * 2;
					Vector3 lower = new Vector3(world.x, lowest, world.z);
					if (!IsRouteNodeOccupied(lower, radius)
						|| IsOnlyUnlockedDoorOccupyingRouteNodeServer(lower, radius)
						|| IsOnlyTemporaryActorOccupyingRouteNode(lower, radius))
					{
						verticalPositions[first] = lower;
						verticalWalkable[first] = true;
					}
					if (highest - lowest > 0.38f)
					{
						Vector3 upper = new Vector3(world.x, highest, world.z);
						if (!IsRouteNodeOccupied(upper, radius)
							|| IsOnlyUnlockedDoorOccupyingRouteNodeServer(upper, radius)
							|| IsOnlyTemporaryActorOccupyingRouteNode(upper, radius))
						{
							verticalPositions[first + 1] = upper;
							verticalWalkable[first + 1] = true;
						}
					}
				}
			}

			int startCell = (RouteGridCenter * RouteGridSide + RouteGridCenter) * 2;
			int start = -1;
			float nearestStart = 0.75f;
			for (int layer = 0; layer < 2; ++layer)
			{
				int index = startCell + layer;
				float error = verticalWalkable[index]
					? Mathf.Abs(verticalPositions[index].y - basePosition.y)
					: float.PositiveInfinity;
				if (error < nearestStart)
				{
					nearestStart = error;
					start = index;
				}
			}
			if (start < 0)
				return false;
			verticalPositions[start] = basePosition;
			verticalCost[start] = 0f;

			// A complete route requires a connected floor at the requested
			// destination, not the closest X/Z location on the wrong storey.
			int destinationX = Mathf.RoundToInt((destination.x - basePosition.x) / cellSize)
				+ RouteGridCenter;
			int destinationZ = Mathf.RoundToInt((destination.z - basePosition.z) / cellSize)
				+ RouteGridCenter;
			if (destinationX < 0 || destinationX >= RouteGridSide
				|| destinationZ < 0 || destinationZ >= RouteGridSide)
				return false;
			int destinationCell = destinationZ * RouteGridSide + destinationX;
			int goal = -1;
			float bestGoalError = float.PositiveInfinity;
			for (int layer = 0; layer < 2; ++layer)
			{
				int index = destinationCell * 2 + layer;
				if (!verticalWalkable[index])
					continue;
				float horizontalError = PlanarDistanceSquared(verticalPositions[index], destination);
				float heightError = Mathf.Abs(verticalPositions[index].y - destination.y);
				if (horizontalError > 1.20f || heightError > 0.65f)
					continue;
				float score = horizontalError + heightError * heightError;
				if (score < bestGoalError)
				{
					bestGoalError = score;
					goal = index;
				}
			}
			if (goal < 0 || goal == start)
				return false;

			// Hard cap prevents simultaneous 40-NPC searches from exploding into
			// thousands of raycasts per route. Exhaustion fails closed.
			int connectorProbeBudget = 180;
			for (int iteration = 0; iteration < VerticalNodeLimit; ++iteration)
			{
				int current = -1;
				float best = float.PositiveInfinity;
				for (int i = 0; i < VerticalNodeLimit; ++i)
				{
					if (!verticalWalkable[i] || verticalClosed[i]
						|| float.IsPositiveInfinity(verticalCost[i]))
						continue;
					float score = verticalCost[i]
						+ Vector3.Distance(verticalPositions[i], verticalPositions[goal]);
					if (score < best)
					{
						best = score;
						current = i;
					}
				}
				if (current < 0 || connectorProbeBudget <= 0)
					break;
				verticalClosed[current] = true;
				if (current == goal)
					break;
				int cell = current / 2;
				int cx = cell % RouteGridSide;
				int cz = cell / RouteGridSide;
				for (int d = 0; d < RouteGridDirections.Length; ++d)
				{
					int dx = RouteGridDirections[d].x;
					int dz = RouteGridDirections[d].y;
					int nx = cx + dx;
					int nz = cz + dz;
					if (nx < 0 || nx >= RouteGridSide || nz < 0 || nz >= RouteGridSide)
						continue;
					if (dx != 0 && dz != 0)
					{
						float expected = verticalPositions[current].y;
						if (!VerticalCellHasCompatibleFloor(cz * RouteGridSide + nx, expected)
							|| !VerticalCellHasCompatibleFloor(nz * RouteGridSide + cx, expected))
							continue;
					}
					int adjacentCell = (nz * RouteGridSide + nx) * 2;
					for (int layer = 0; layer < 2; ++layer)
					{
						int next = adjacentCell + layer;
						if (!verticalWalkable[next] || verticalClosed[next])
							continue;
						Vector3 edge = verticalPositions[next] - verticalPositions[current];
						float flat = Mathf.Sqrt(edge.x * edge.x + edge.z * edge.z);
						if (flat < 0.2f || Mathf.Abs(edge.y) > flat
							* Mathf.Tan(Mathf.Min(characterController.slopeLimit, roamingMaximumSlope)
							* Mathf.Deg2Rad))
							continue;
						--connectorProbeBudget;
						if (!IsActuallyWalkableStairConnection(
							verticalPositions[current], verticalPositions[next], radius))
							continue;
						float tentative = verticalCost[current] + edge.magnitude;
						if (tentative < verticalCost[next])
						{
							verticalCost[next] = tentative;
							verticalPrevious[next] = current;
						}
					}
				}
			}
			if (!verticalClosed[goal] || verticalPrevious[goal] < 0)
				return false;

			verticalReverseScratch.Clear();
			int cursor = goal;
			while (cursor != start && cursor >= 0
				&& verticalReverseScratch.Count < VerticalNodeLimit)
			{
				verticalReverseScratch.Add(verticalPositions[cursor]);
				cursor = verticalPrevious[cursor];
			}
			if (cursor != start || verticalReverseScratch.Count == 0)
				return false;
			routeWaypoints.Clear();
			for (int i = verticalReverseScratch.Count - 1; i >= 0; --i)
				routeWaypoints.Add(verticalReverseScratch[i]);
			routeWaypointIndex = 0;
			nextPlannedRouteValidationTime = 0f;
			return true;
		}

		private bool VerticalCellHasCompatibleFloor(int cell, float expectedY)
		{
			for (int layer = 0; layer < 2; ++layer)
			{
				int index = cell * 2 + layer;
				if (verticalWalkable[index]
					&& Mathf.Abs(verticalPositions[index].y - expectedY) <= 0.75f)
					return true;
			}
			return false;
		}

		private bool IsActuallyWalkableStairConnection(Vector3 from,
			Vector3 to, float radius)
		{
			Vector3 edge = to - from;
			float planarLength = Mathf.Sqrt(edge.x * edge.x + edge.z * edge.z);
			if (planarLength < 0.2f)
				return false;
			float length = edge.magnitude;
			Vector3 chestOrigin = from + Vector3.up * Mathf.Max(0.45f,
				characterController.height * 0.55f);
			if (IsObstacleBlockingAlong(chestOrigin, radius, edge / length, length)
				&& !IsOnlyTemporaryActorBlockingAlong(chestOrigin, radius,
					edge / length, length)
				&& (doorNavigation == null
					|| !doorNavigation.IsTraversableUnlockedDoorEdgeServer(
						chestOrigin, edge / length, radius, length, obstacleMask)))
				return false;
			// A chest-level clear ray is not permission to step across a stair
			// well. Sample the actual floor every <=0.35m, ensuring each
			// consecutive rise/drop can be handled by the CharacterController.
			int samples = Mathf.CeilToInt(planarLength / 0.35f);
			float lastY = from.y;
			for (int i = 1; i <= samples; ++i)
			{
				float t = (float)i / samples;
				Vector3 desired = Vector3.Lerp(from, to, t);
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(desired,
					0.46f, 0.55f, roamingGroundMask,
					Mathf.Min(characterController.slopeLimit, roamingMaximumSlope),
					out Vector3 actualFloor, out _)
					|| Mathf.Abs(actualFloor.y - desired.y) > 0.30f
					|| actualFloor.y - lastY > characterController.stepOffset
						+ characterController.skinWidth + 0.10f
					|| lastY - actualFloor.y > 0.52f)
					return false;
				// The floor may be physically connected yet a low ceiling or
				// railing could intersect the full NPC capsule between grid
				// nodes. Intermediate clearance is part of the connector.
				if (i < samples && IsRouteNodeOccupied(actualFloor, radius)
					&& !IsOnlyUnlockedDoorOccupyingRouteNodeServer(actualFloor, radius)
					&& !IsOnlyTemporaryActorOccupyingRouteNode(actualFloor, radius))
					return false;
				lastY = actualFloor.y;
			}
			return Mathf.Abs(lastY - to.y) <= 0.30f;
		}

		/// <summary>
		/// A clear chest sweep between grounded grid nodes is not evidence of
		/// a walkable floor between them. A midpoint probe rejects small gaps,
		/// broken bridges and false connections across overlapping interiors.
		/// Called only after the cheap occupancy and height tests passed.
		/// </summary>
		private bool HasContinuousLocalFloor(Vector3 from, Vector3 to)
		{
			Vector3 midpoint = (from + to) * 0.5f;
			if (!MobileNPCSpawnService.TryFindSafeGroundPosition(midpoint,
				0.7f, 0.85f, roamingGroundMask,
				Mathf.Min(characterController.slopeLimit, roamingMaximumSlope),
				out Vector3 floor, out _)
				|| Mathf.Abs(floor.y - midpoint.y) > 0.35f)
				return false;
			float riseLimit = characterController.stepOffset
				+ characterController.skinWidth + 0.10f;
			return floor.y - from.y <= riseLimit
				&& to.y - floor.y <= riseLimit
				&& from.y - floor.y <= 0.55f
				&& floor.y - to.y <= 0.55f
				&& Level.checkSafeIncludingClipVolumes(floor);
		}

		private bool TryBuildLocalRoute(Vector3 destination,
			List<Vector3> alternateOutput = null, bool requireExactLocalGoal = false)
		{
			// A two-dimensional "nearest reachable" partial route is not
			// evidence that a destination on a DIFFERENT floor can be reached.
			// Use the verified vertical connector planner instead for local
			// upstairs/downstairs goals; ordinary distant horizontal routing
			// remains useful while approaching a separate stairwell entrance.
			if (!requireExactLocalGoal
				&& Mathf.Abs(destination.y - transform.position.y) > 1.0f
				&& PlanarDistanceSquared(destination, transform.position) <= 30f)
				return false;
			if (!requireExactLocalGoal)
			{
				if (Time.time < nextLocalRoutePlanTime
					|| Time.time < nextSharedUnknownRouteSlot)
					return false;
				// Broad local discovery is the expensive fallback when a native
				// graph is unavailable. Spread its ground probes across NPCs.
				nextSharedUnknownRouteSlot = Time.time + 0.10f;
				nextLocalRoutePlanTime = Time.time + 0.75f
					+ Random.Range(0f, 0.25f);
			}

			Vector3 goalOffset = destination - transform.position;
			goalOffset.y = 0f;
			if (goalOffset.sqrMagnitude < 0.01f)
				return false;

			Vector3 localGoal = transform.position + Vector3.ClampMagnitude(goalOffset, RouteGridCenter * RouteGridCellSize);
			Vector3[] positions = routeSearchPositions;
			bool[] walkable = routeSearchWalkable;
			float[] costs = routeSearchCosts;
			int[] previous = routeSearchPrevious;
			bool[] closed = routeSearchClosed;
			System.Array.Clear(walkable, 0, walkable.Length);
			System.Array.Clear(closed, 0, closed.Length);
			System.Array.Clear(routeSearchExposure, 0, routeSearchExposure.Length);
			for (int i = 0; i < RouteGridNodeLimit; ++i)
			{
				costs[i] = float.PositiveInfinity;
				previous[i] = -1;
			}

			Vector3 basePosition = transform.position;
			float floorY = basePosition.y;
			float maxFloorDelta = Mathf.Clamp(characterController.height * 0.45f, 0.8f, 1.25f);
			float routeRadius = Mathf.Clamp(obstacleProbeRadius, 0.05f, Mathf.Max(0.05f, characterController.radius * 0.9f));
			for (int z = 0; z < RouteGridSide; ++z)
			{
				for (int x = 0; x < RouteGridSide; ++x)
				{
					int index = z * RouteGridSide + x;
					Vector3 candidate = basePosition + new Vector3(
						(x - RouteGridCenter) * RouteGridCellSize, 0f,
						(z - RouteGridCenter) * RouteGridCellSize);
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
						1.6f, 2.2f, roamingGroundMask, roamingMaximumSlope,
						out Vector3 resolved, out _))
						continue;
					if (Mathf.Abs(resolved.y - floorY) > maxFloorDelta)
						continue;
					if (index != RouteGridCenter * RouteGridSide + RouteGridCenter
						&& IsRouteNodeOccupied(resolved, routeRadius)
						&& !IsOnlyUnlockedDoorOccupyingRouteNodeServer(resolved, routeRadius)
						&& !IsOnlyTemporaryActorOccupyingRouteNode(resolved, routeRadius))
						continue;

					// Node occupancy is already checked above; real traversability
					// is validated for every A* edge below. The previous probe
					// started at this candidate but cast FURTHER AWAY from the NPC,
					// incorrectly rejecting perfectly usable cells beside a wall,
					// and disconnecting the grid at building corners.
					positions[index] = resolved;
					walkable[index] = true;
				}
			}

			int start = RouteGridCenter * RouteGridSide + RouteGridCenter;
			if (!walkable[start])
			{
				positions[start] = basePosition;
				walkable[start] = true;
			}
			Vector3 goalFromBase = localGoal - basePosition;
			// Keep the desired grid cell as a preference only. It may be blocked or
			// disconnected, so choose the nearest reachable node after the graph search.
			int goalX = Mathf.Clamp(RouteGridCenter + Mathf.RoundToInt(goalFromBase.x / RouteGridCellSize), 0, RouteGridSide - 1);
			int goalZ = Mathf.Clamp(RouteGridCenter + Mathf.RoundToInt(goalFromBase.z / RouteGridCellSize), 0, RouteGridSide - 1);
			int preferredGoal = goalZ * RouteGridSide + goalX;
			// When this preferred cell is the approach where the survivor just
			// got stuck, do not stop A* there or select it again. Explore
			// physically connected lateral frontiers before retrying that wall.
			bool preferredRecentlyBlocked = !requireExactLocalGoal
				&& walkable[preferredGoal]
				&& LocalDeadEndPenalty(positions[preferredGoal], destination) >= 2.5f;

			// Killzone-like tactical A*: during an actual *visible* hostile
			// engagement, seeking cover/retreat may prefer a physically sheltered
			// corridor rather than the exposed shortest path. Never use the
			// live position of an unseen player as an omniscient path cost.
			bool tacticalRoute = targetIsVisible && brainDriver != null
				&& brainDriver.Target != null
				&& (brainDriver.CurrentGoal == MobileNPCTactics.Goal.SeekCover
					|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat)
				&& (brainDriver.Target.position - transform.position).sqrMagnitude < 1600f;
			Vector3 observedThreatPoint = tacticalRoute
				? brainDriver.Target.position + Vector3.up * 1.25f : Vector3.zero;
			// At most 24 supplementary LOS probes per local route, not 81x
			// every frame for a crowd of NPCs. Unknown nodes are treated as
			// conservatively exposed; checked sheltered nodes become cheaper.
			int exposureProbeBudget = 24;
			// Shared search admission and a local fixed budget prevent a crowd
			// from doing unbounded ground checks in a single planning pass.
			int continuousFloorProbeBudget = 220;

			costs[start] = 0f;
			for (int iteration = 0; iteration < RouteGridNodeLimit; ++iteration)
			{
				int current = -1;
				float bestEstimatedTotalCost = float.PositiveInfinity;
				for (int i = 0; i < RouteGridNodeLimit; ++i)
				{
					if (!closed[i] && walkable[i] && !float.IsPositiveInfinity(costs[i]))
					{
						// A* prioritizes nodes by known travel cost plus a straight-line
						// estimate to the preferred local goal. Edge costs are physical
						// distances, so this heuristic is admissible for this grid.
						Vector3 remaining = positions[i] - localGoal;
						remaining.y = 0f;
						float estimatedTotalCost = costs[i] + remaining.magnitude;
						if (estimatedTotalCost < bestEstimatedTotalCost)
						{
							bestEstimatedTotalCost = estimatedTotalCost;
							current = i;
						}
					}
				}
				if (current < 0)
					break;
				closed[current] = true;
				// Stop early when the preferred destination cell is reached. If it is
				// blocked or disconnected, keep exploring the reachable component so
				// the fallback can select its nearest reachable node.
				if (current == preferredGoal && current != start
					&& !preferredRecentlyBlocked)
					break;

				int cx = current % RouteGridSide;
				int cz = current / RouteGridSide;
				for (int d = 0; d < RouteGridDirections.Length; ++d)
				{
					int nx = cx + RouteGridDirections[d].x;
					int nz = cz + RouteGridDirections[d].y;
					if (nx < 0 || nx >= RouteGridSide || nz < 0 || nz >= RouteGridSide)
						continue;
					int next = nz * RouteGridSide + nx;
					if (!walkable[next] || closed[next])
						continue;

					// A diagonal edge must not squeeze through a blocked grid corner.
					// Both adjacent cardinal cells must be walkable before the diagonal
					// can be considered, even if its own sphere cast happens to be clear.
					if (RouteGridDirections[d].x != 0 && RouteGridDirections[d].y != 0)
					{
						int horizontalSide = cz * RouteGridSide + nx;
						int verticalSide = nz * RouteGridSide + cx;
						if (!walkable[horizontalSide] || !walkable[verticalSide])
							continue;
					}

					Vector3 edge = positions[next] - positions[current];
					float verticalChange = edge.y;
					// Neighbouring cells may be on different floors even when each
					// individual ground probe is valid. Don't connect levels the
					// CharacterController cannot actually step between.
					if (verticalChange > characterController.stepOffset
						+ characterController.skinWidth + 0.1f
						|| verticalChange < -1.1f)
						continue;
					edge.y = 0f;
					float edgeLength = edge.magnitude;
					if (edgeLength < 0.1f)
						continue;
					Vector3 edgeProbeStart = positions[current] + Vector3.up
						* Mathf.Max(0.45f, characterController.height * 0.55f);
					if (IsObstacleBlockingAlong(edgeProbeStart, routeRadius,
						edge.normalized, edgeLength)
						&& !IsOnlyTemporaryActorBlockingAlong(edgeProbeStart,
							routeRadius, edge.normalized, edgeLength)
						&& (doorNavigation == null
							|| !doorNavigation.IsTraversableUnlockedDoorEdgeServer(
								edgeProbeStart, edge.normalized, routeRadius,
								edgeLength, obstacleMask)))
						continue;
					if (continuousFloorProbeBudget <= 0)
						continue;
					--continuousFloorProbeBudget;
					if (!HasContinuousLocalFloor(positions[current], positions[next]))
						continue;
					float exposureCost = 0f;
					if (tacticalRoute)
					{
						if (routeSearchExposure[next] == 0 && exposureProbeBudget > 0)
						{
							--exposureProbeBudget;
							routeSearchExposure[next] = IsLocalRouteNodeSheltered(
								positions[next], observedThreatPoint) ? (byte)2 : (byte)1;
						}
						exposureCost = routeSearchExposure[next] == 2 ? 0f : 1.45f;
					}
					float tentative = costs[current] + edgeLength + exposureCost;
					if (tentative < costs[next])
					{
						costs[next] = tentative;
						previous[next] = current;
					}
				}
			}

			int goal = -1;
			float bestFrontierScore = float.PositiveInfinity;
			for (int i = 0; i < RouteGridNodeLimit; ++i)
			{
				if (!closed[i] || !walkable[i] || i == start
					|| previous[i] < 0 || float.IsInfinity(costs[i]))
					continue;
				if (i == preferredGoal && !preferredRecentlyBlocked)
				{
					// A physically reachable preferred cell is still useful
					// even if the final target is several local grids away.
					goal = i;
					break;
				}
				if (requireExactLocalGoal)
					continue;

				// The closest cell to a target on the opposite side of a wall
				// is often a dead end. Penalize *experienced* dead ends while
				// preferring reachable boundary/frontier cells that can lead
				// around the wall on subsequent local planning windows.
				float forwardDistance = Mathf.Sqrt(
					PlanarDistanceSquared(positions[i], localGoal));
				float travelDistance = Mathf.Sqrt(
					PlanarDistanceSquared(positions[i], basePosition));
				if (travelDistance < 1.4f)
					continue;
				int x = i % RouteGridSide;
				int z = i / RouteGridSide;
				bool boundary = x <= 1 || x >= RouteGridSide - 2
					|| z <= 1 || z >= RouteGridSide - 2;
				float frontierScore = forwardDistance
					+ Mathf.Max(0f, costs[i]) * 0.12f
					+ LocalDeadEndPenalty(positions[i], destination)
					- (boundary ? 0.65f : 0f);
				if (frontierScore < bestFrontierScore)
				{
					bestFrontierScore = frontierScore;
					goal = i;
				}
			}
			if (goal < 0 || previous[goal] < 0)
				return false;
			if (requireExactLocalGoal)
			{
				// For cover feasibility an arbitrary "closest reachable" node is
				// NOT proof that the requested cover is reachable.
				if (goal != preferredGoal
					|| PlanarDistanceSquared(positions[goal], destination) > 0.85f
					|| Mathf.Abs(positions[goal].y - destination.y) > 0.4f
					|| IsRouteNodeOccupied(destination, routeRadius))
					return false;
				Vector3 last = destination - positions[goal];
				last.y = 0f;
				float lastDistance = last.magnitude;
				if (lastDistance > 0.12f && IsObstacleBlockingAlong(
					positions[goal] + Vector3.up * 0.95f,
					routeRadius, last / lastDistance, lastDistance))
					return false;
			}

			List<Vector3> reversed = reversedRouteScratch;
			reversed.Clear();
			int cursor = goal;
			while (cursor != start && cursor >= 0 && reversed.Count < RouteGridNodeLimit)
			{
				reversed.Add(positions[cursor]);
				cursor = previous[cursor];
			}
			if (cursor != start || reversed.Count == 0)
				return false;

			List<Vector3> destinationRoute = alternateOutput ?? routeWaypoints;
			destinationRoute.Clear();
			for (int i = reversed.Count - 1; i >= 0; --i)
				destinationRoute.Add(reversed[i]);
			if (alternateOutput == null)
			{
				routeWaypointIndex = 0;
				nextPlannedRouteValidationTime = 0f;
			}
			return destinationRoute.Count > 0;
		}

		/// <summary>
		/// Local Detour-style visibility optimization. A short corridor may be
		/// straightened only after validating collision and real walkable floor
		/// at intermediate samples, not merely a chest-level raycast.
		/// </summary>
		private bool CanSafelyShortcutRouteCorner(Vector3 nextWaypoint)
		{
			Vector3 delta = nextWaypoint - transform.position;
			if (Mathf.Abs(delta.y) > Mathf.Max(0.3f, characterController.stepOffset)
				|| !Level.checkSafeIncludingClipVolumes(nextWaypoint))
				return false;
			delta.y = 0f;
			float length = delta.magnitude;
			if (length < 0.4f || length > 4.0f)
				return false;
			Vector3 direction = delta / length;
			Vector3 origin = transform.position
				+ Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
				Mathf.Max(0.05f, characterController.radius * 0.9f));
			if (IsObstacleBlockingAlong(origin, radius, direction, length))
				return false;

			Vector3 previousFloor = transform.position;
			int steps = Mathf.CeilToInt(length);
			for (int i = 1; i <= steps; ++i)
			{
				Vector3 sample = transform.position + direction * (length * i / steps);
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(sample, 1.4f, 1.6f,
					roamingGroundMask, roamingMaximumSlope, out Vector3 floor, out _)
					|| Mathf.Abs(floor.y - previousFloor.y) >
					Mathf.Max(0.35f, characterController.stepOffset + 0.1f))
					return false;
				previousFloor = floor;
			}
			return true;
		}

		/// <summary>
		/// Actual cover ray from a navigable node to a currently observed threat.
		/// Only world geometry counts; Player/NPC/Zombie colliders cannot supply
		/// free cover. Saturated buffers fail closed to "exposed", not imagined
		/// protection.
		/// </summary>
		private bool IsLocalRouteNodeSheltered(Vector3 node, Vector3 threatEye)
		{
			Vector3 origin = node + Vector3.up * 1.08f;
			Vector3 delta = threatEye - origin;
			float distance = delta.magnitude;
			if (distance <= 0.5f)
				return false;
			int hits = Physics.RaycastNonAlloc(origin, delta / distance,
				ObstacleHitBuffer, distance - 0.2f, RayMasks.BLOCK_COLLISION,
				QueryTriggerInteraction.Ignore);
			if (hits >= ObstacleHitBuffer.Length)
				return false;
			for (int i = 0; i < hits; ++i)
			{
				Collider collider = ObstacleHitBuffer[i].collider;
				if (collider == null || IsOwnCollider(collider)
					|| collider.GetComponentInParent<Player>() != null
					|| collider.GetComponentInParent<Zombie>() != null
					|| collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
					continue;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Crowd occupancy is temporary, not a permanent missing stair.
		/// A route may be considered through a person if EVERY collider
		/// blocking that full-sized humanoid node is another living actor;
		/// actual CharacterController collision still prevents body overlap.
		/// </summary>
		private bool IsOnlyTemporaryActorOccupyingRouteNode(
			Vector3 groundPosition, float radius)
		{
			float height = Mathf.Max(radius * 2f, characterController.height);
			Vector3 center = groundPosition + Vector3.up * (height * 0.5f + 0.10f);
			float segment = Mathf.Max(0f, height * 0.5f - radius);
			int count = Physics.OverlapCapsuleNonAlloc(
				center - Vector3.up * segment,
				center + Vector3.up * segment,
				radius, RouteOccupancyBuffer, obstacleMask,
				QueryTriggerInteraction.Ignore);
			if (count <= 0 || count >= RouteOccupancyBuffer.Length)
				return false;
			bool foundActor = false;
			for (int i = 0; i < count; ++i)
			{
				Collider hit = RouteOccupancyBuffer[i];
				if (hit == null || IsOwnCollider(hit))
					continue;
				if (hit.GetComponentInParent<Player>() == null
					&& hit.GetComponentInParent<Zombie>() == null
					&& hit.GetComponentInParent<MobileNPCHumanoidVitals>() == null)
					return false;
				foundActor = true;
			}
			return foundActor;
		}

		private bool IsOnlyUnlockedDoorOccupyingRouteNodeServer(
			Vector3 groundPosition, float radius)
		{
			if (doorNavigation == null)
				return false;
			float height = Mathf.Max(radius * 2f, characterController.height);
			Vector3 center = groundPosition + Vector3.up * (height * 0.5f + 0.10f);
			float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
			return doorNavigation.IsOnlyUnlockedDoorOccupyingNodeServer(
				center - Vector3.up * halfSegment,
				center + Vector3.up * halfSegment, radius, obstacleMask);
		}

		private bool IsRouteNodeOccupied(Vector3 groundPosition, float radius)
		{
			float height = Mathf.Max(radius * 2f, characterController.height);
			// Keep the occupancy volume just above actual walked-on floor;
			// otherwise a terrain collider touching its lower sphere makes
			// every adjacent node appear occupied and disconnects the grid.
			Vector3 center = groundPosition + Vector3.up * (height * 0.5f + 0.10f);
			float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
			Vector3 bottom = center - Vector3.up * halfSegment;
			Vector3 top = center + Vector3.up * halfSegment;

			// CheckCapsule cannot exclude this NPC's own CharacterController. Using it
			// directly marks nearby grid cells as occupied by the NPC itself and can
			// disconnect the local route graph before path search even begins.
			int hitCount = Physics.OverlapCapsuleNonAlloc(bottom, top, radius,
				RouteOccupancyBuffer, obstacleMask, QueryTriggerInteraction.Ignore);
			if (hitCount >= RouteOccupancyBuffer.Length)
			{
				// Only allocate on buffer saturation; the full query avoids accepting a
				// genuinely blocked cell because the non-alloc result was truncated.
				Collider[] allHits = Physics.OverlapCapsule(bottom, top, radius,
					obstacleMask, QueryTriggerInteraction.Ignore);
				for (int i = 0; i < allHits.Length; ++i)
				{
					if (!IsOwnCollider(allHits[i]))
						return true;
				}
				return false;
			}

			for (int i = 0; i < hitCount; ++i)
			{
				if (!IsOwnCollider(RouteOccupancyBuffer[i]))
					return true;
			}
			return false;
		}

		private void TryChooseRoamingDestination()
		{
			if (!Provider.isServer || !roamingEnabled)
			{
				return;
			}

			float effectiveRoamingRadius = Mathf.Min(Mathf.Max(2f, roamingRadius), HomeTerritoryRadius);

			// Prefer actual map structures, but keep searching over open terrain when a
			// beach, field or remote island has no nearby large objects.
			if (Random.value < 0.55f && TryChooseStructureExplorationPoint(effectiveRoamingRadius, out Vector3 structurePoint))
			{
				roamingDestination = structurePoint;
				Vector3 structureDirection = structurePoint - transform.position;
				structureDirection.y = 0f;
				if (structureDirection.sqrMagnitude > 0.01f)
				{
					lastRoamingDirection = structureDirection.normalized;
					hasLastRoamingDirection = true;
				}
				hasRoamingDestination = true;
				return;
			}

			// Continue in a roughly straight line for a while, validating each prospective
			// ground point. If the NPC approaches the edge of its exploration radius, turn
			// back inward instead of walking to the old spawn point and repeatedly restarting.
			Vector3 fromHome = transform.position - spawnPosition;
			fromHome.y = 0f;
			Vector3 baseDirection;
			if (fromHome.magnitude >= effectiveRoamingRadius * 0.72f)
			{
				baseDirection = -fromHome.normalized;
			}
			else if (hasLastRoamingDirection && lastRoamingDirection.sqrMagnitude > 0.01f)
			{
				baseDirection = lastRoamingDirection.normalized;
			}
			else
			{
				baseDirection = transform.forward;
				baseDirection.y = 0f;
				if (baseDirection.sqrMagnitude < 0.01f)
					baseDirection = Random.insideUnitSphere;
				baseDirection.y = 0f;
				baseDirection.Normalize();
			}

			for (int attempt = 0; attempt < 12; ++attempt)
			{
				float turn = Random.Range(-48f, 48f) + (attempt > 5 ? Random.Range(-35f, 35f) : 0f);
				Vector3 direction = Quaternion.AngleAxis(turn, Vector3.up) * baseDirection;
				float distance = Random.Range(24f, 72f);
				Vector3 candidate = transform.position + direction * distance;
				Vector3 fromHomeCandidate = candidate - spawnPosition;
				fromHomeCandidate.y = 0f;
				if (fromHomeCandidate.sqrMagnitude > effectiveRoamingRadius * effectiveRoamingRadius)
					continue;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, Mathf.Max(1f, roamingSearchHeight),
					Mathf.Min(8f, Mathf.Max(0f, roamingMaximumDrop)), roamingGroundMask,
					Mathf.Clamp(roamingMaximumSlope, 25f, 42f), out Vector3 resolved, out _))
					continue;
				if (Mathf.Abs(resolved.y - transform.position.y) > 8f)
					continue;
				Vector3 resolvedDirection = resolved - transform.position;
				resolvedDirection.y = 0f;
				if (resolvedDirection.sqrMagnitude < 16f)
					continue;
				lastRoamingDirection = resolvedDirection.normalized;
				hasLastRoamingDirection = true;
				roamingDestination = resolved;
				hasRoamingDestination = true;
				return;
			}

			// Back off when this terrain patch has no valid candidate; do not search every frame.
			nextRoamingDecisionTime = Time.time + Random.Range(3f, 7f);
		}

		private bool TryChooseStructureExplorationPoint(float radius, out Vector3 destination)
		{
			destination = Vector3.zero;
			if (LevelObjects.objects == null)
				return false;

			explorationRegions.Clear();
			explorationObjects.Clear();
			Regions.getRegionsInRadius(transform.position, Mathf.Max(2f, radius), explorationRegions);
			for (int regionIndex = 0; regionIndex < explorationRegions.Count; ++regionIndex)
			{
				RegionCoordinate coordinate = explorationRegions[regionIndex];
				if (coordinate.x >= LevelObjects.objects.GetLength(0)
					|| coordinate.y >= LevelObjects.objects.GetLength(1))
					continue;

				List<LevelObject> regionObjects = LevelObjects.objects[coordinate.x, coordinate.y];
				if (regionObjects == null)
					continue;

				for (int objectIndex = 0; objectIndex < regionObjects.Count; ++objectIndex)
				{
					LevelObject levelObject = regionObjects[objectIndex];
					if (levelObject == null || levelObject.transform == null || levelObject.asset == null
						|| levelObject.asset.type != EObjectType.LARGE)
						continue;

					Vector3 offset = levelObject.transform.position - transform.position;
					offset.y = 0f;
					Vector3 fromHome = levelObject.transform.position - spawnPosition;
					fromHome.y = 0f;
					if (offset.sqrMagnitude < 36f || offset.sqrMagnitude > radius * radius
						|| fromHome.sqrMagnitude > HomeTerritoryRadius * HomeTerritoryRadius)
						continue;

					explorationObjects.Add(levelObject);
				}
			}

			if (explorationObjects.Count == 0)
				return false;

			// Try several different nearby large objects; don't get stuck repeatedly
			// selecting an inaccessible shell or a prop on an unsafe slope.
			int attempts = Mathf.Min(6, explorationObjects.Count);
			for (int attempt = 0; attempt < attempts; ++attempt)
			{
				int index = Random.Range(0, explorationObjects.Count);
				LevelObject levelObject = explorationObjects[index];
				explorationObjects.RemoveAt(index);
				if (levelObject == null || levelObject.transform == null)
					continue;

				Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(3f, 7f);
				Vector3 candidate = levelObject.transform.position + new Vector3(offset.x, 0f, offset.y);
				if (MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, Mathf.Max(1f, roamingSearchHeight),
					Mathf.Max(0f, roamingMaximumDrop), roamingGroundMask, roamingMaximumSlope,
					out Vector3 resolved, out _))
				{
					destination = resolved;
					return true;
				}
			}

			return false;
		}

		/// <summary>Set only from authoritative server-side perception.</summary>
		public void SetTargetVisibleFromServer(bool isVisible)
		{
			if (Provider.isServer && hasInitialized)
			{
				targetIsVisible = isVisible;
			}
		}

		private void OnDestroy()
		{
			if (brainDriver != null)
			{
				brainDriver.ResetServer();
			}
		}
	}
}
