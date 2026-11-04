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

		[SerializeField, Min(0.05f)] private float stoppingDistance = 0.65f;
		// The native PlayerMovement uses Physics.gravity.y * 3 while airborne.
		// This is a MULTIPLIER, not acceleration in m/s². The old serialized
		// "gravity = 20" produced ~60m/s² and made native JUMP=7 fail.
		// New field name deliberately avoids inheriting obsolete prefab data.
		[SerializeField, Range(0.5f, 2f)] private float gravityMultiplier = 1.0f;
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
		[SerializeField, Range(0f, 85f)] private float roamingMaximumSlope = 75f;
		[SerializeField] private LayerMask roamingGroundMask = ~0;

		[Header("Local obstacle avoidance and bounded route fallback")]
		[SerializeField, Min(0.5f)] private float obstacleProbeDistance = 1.6f;
		[SerializeField, Min(0.05f)] private float obstacleProbeRadius = 0.28f;
		[SerializeField, Range(15f, 80f)] private float obstacleAvoidanceAngle = 48f;
		[SerializeField] private LayerMask obstacleMask = ~0;

		private static readonly float[] FallbackSteeringAngles = { -90f, 90f, -135f, 135f, 180f };
		// A tiny fixed set of real ground exits used ONLY by NPCs currently
		// standing on a verified object top; never a generic off-mesh link.
		private static readonly float[] FurnitureExitAngles = { 0f, -45f, 45f };
		private static readonly float[] FurnitureExitDistances = { 1.6f, 2.2f };
		private static float nextSharedFurnitureExitSlot;
		// Optional ONE-candidate physics preflight for a confirmed gunfight,
		// never an unsolicited exploration goal or new A* connection.
		private static float nextSharedElevatedTacticProbeTime;
		private float nextElevatedTacticProbeTime;
		private Vector3 lastPhysicallyValidatedElevatedTacticGoal;
		private float validatedElevatedTacticUntil;
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
		// Optional fine-grid inspection only for nearby, same-floor unknown
		// passages. An independent shared slot caps the extra physics work.
		private static float nextSharedNarrowPassageSlot;
		// Source-less geometry openings: no prefab "Door", marker, teleport
		// or navmesh provider is assumed to exist on vanilla building facades.
		private static float nextSharedPhysicalThresholdSlot;
		private float nextPhysicalThresholdScanTime;
		private float nextPhysicalThresholdDiagnosticTime;
		private float nextThresholdRouteStateLog;
		// A physical doorway is narrow: the generic 1m waypoint-arrival
		// tolerance could skip its approach and push into a solid jamb.
		private bool hasPhysicalThresholdRoute;
		private MobileNPCBrainDriver.MovementOwner physicalThresholdOwner;
		private int physicalThresholdApproachWaypointIndex;
		private Vector3 physicalThresholdBeyondWaypoint;
		private Vector3 physicalThresholdEntryWaypoint;
		// A verified floor-level low opening can be crawled through with
		// the SAME real CharacterController at its actual crouch height.
		// Neither window sills nor solid walls are passable by this helper.
		private static float nextSharedLowOpeningProbeAt;
		private float nextLowOpeningProbeAt;
		private float lowOpeningVerifiedUntil;
		private float nextLowOpeningDiagnosticAt;
		private Vector3 lowOpeningHeading;
		// Room-scavenging hints must inspect the NPC's OWN region first.
		// Nearby 8 regions are a fallback, not an opportunity to exhaust
		// a capped global scan before reaching the current town block.
		private static readonly Vector2Int[] UrbanRegionOffsets =
		{
			new Vector2Int(0, 0),
			new Vector2Int(1, 0), new Vector2Int(-1, 0),
			new Vector2Int(0, 1), new Vector2Int(0, -1),
			new Vector2Int(1, 1), new Vector2Int(-1, 1),
			new Vector2Int(1, -1), new Vector2Int(-1, -1)
		};
		private static readonly Vector2Int[] RouteGridDirections =
		{
			new Vector2Int(1, 0), new Vector2Int(-1, 0),
			new Vector2Int(0, 1), new Vector2Int(0, -1),
			new Vector2Int(1, 1), new Vector2Int(1, -1),
			new Vector2Int(-1, 1), new Vector2Int(-1, -1)
		};

		private CharacterController characterController;
		private MobileNPCBrainDriver brainDriver;
		private MobileNPCHumanoidRangedCombat rangedCombat;
		private MobileNPCHumanoidCombat meleeCombat;
		private bool wasHoldingDeliberateFire;
		private MobileNPCAutonomyPlanner autonomyPlanner;
		private MobileNPCZombieRiskAssessment zombieRisk;
		private MobileNPCAutonomyPlanner.Job previousRoutineJob;
		private bool hasPreviousRoutineJob;
		private MobileNPCHumanoidPerception visualPerception;
		private MobileNPCInjurySystem injurySystem;
		private MobileNPCDoorNavigation doorNavigation;
		private MobileNPCLadderTraversal ladderTraversal;
		private MobileNPCVaultTraversal vaultTraversal;
		// Vault means an Unturned-style jump over a parapet, never a
		// separate levitating clamber or dedicated climb animation.
		public bool IsVaultTraversing => isPlannedJumping && plannedJumpUsesVaultPlan;
		// A directed physical approach to a native Ladder collider, never
		// a synthetic vertical edge in the ordinary walking grid.
		private bool hasPlannedLadderApproach;
		private Vector3 plannedLadderEntrance;
		private Vector3 plannedLadderGoal;
		private float plannedLadderExpiry;
		private float nextLadderApproachPlanTime;
		private readonly List<Vector3> ladderApproachScratch = new List<Vector3>(128);
		private MobileNPCTravelMemory travelMemory;
		private MobileNPCIndependentRouteProvider independentRouteProvider;
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
		private Vector3 plannedJumpStartPosition;
		private float plannedJumpActualHorizontalSpeed;
		private float plannedJumpHorizontalDelay;
		private float plannedJumpForwardDistance;
		private float plannedJumpMaximumDuration = 1.1f;
		private bool plannedJumpUsesVaultPlan;
		private bool plannedJumpLandsOnObjectTop;
		private Collider plannedJumpSupportCollider;
		// This is a SINGLE physical surface learned only after grounded
		// landing, never a discovered prefab/network-wide climb link.
		private Collider verifiedFurnitureSupportCollider;
		private float verifiedFurnitureSupportY;
		private float verifiedFurnitureSupportValidUntil;
		private float nextVerifiedFurnitureSupportCheckTime;
		private readonly RaycastHit[] furnitureSupportProbeHits = new RaycastHit[12];
		private float nextRaisedObjectLandingDiagnosticTime;
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
		// Suspended A-Life waypoint is a short-lived INTENTION only. It
		// never stores an ongoing physics route, visible enemy or loot item.
		private bool hasSuspendedRoutineWaypoint;
		private Vector3 suspendedRoutineWaypoint;
		private MobileNPCAutonomyPlanner.Job suspendedRoutineJob;
		private float suspendedRoutineExpiresAt;
		private int noviceDirectorHintsUsed;
		// Failed bearings never consume an individual's two lifetime hints.
		// Back off between map-spawn sampling attempts, even when none exists.
		private float nextNoviceBearingAttemptAt;
		private Vector3 lastStuckCheckPosition;
		private Vector3 lastTrackedStuckGoal;
		private Vector3 lastMeaningfulRouteProgressPosition;
		private bool hasTrackedStuckGoal;
		private bool lastTrackedStuckWasBrainGoal;
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
		private readonly List<Vector3> narrowPassageScratch = new List<Vector3>(RouteGridNodeLimit);
		private readonly Vector3[] verticalPositions = new Vector3[VerticalNodeLimit];
		private readonly bool[] verticalWalkable = new bool[VerticalNodeLimit];
		private readonly float[] verticalCost = new float[VerticalNodeLimit];
		private readonly int[] verticalPrevious = new int[VerticalNodeLimit];
		private readonly bool[] verticalClosed = new bool[VerticalNodeLimit];
		private readonly RaycastHit[] verticalFloorHitBuffer = new RaycastHit[24];
		private readonly List<Vector3> verticalReverseScratch = new List<Vector3>(VerticalNodeLimit);
		private int routeWaypointIndex;
		// A local A* frontier can be physically reached without reaching
		// the real task. Keep that separate from final-goal arrival and
		// briefly discourage choosing the same wall-facing frontier again.
		private bool hasPartialLocalFrontier;
		private Vector3 partialLocalFrontier;
		private Vector3 partialLocalTarget;
		// A personal, short-lived preference after the survivor PHYSICALLY
		// reaches a frontier blocked by static geometry. Not a known door.
		private Vector3 observedEntranceSurveyOrigin;
		private Vector3 observedEntranceSurveyGoal;
		private float observedEntranceSurveyUntil;
		private float observedEntranceSurveyEpisodeUntil;
		private float nextObservedEntranceSurveyStartTime;
		private bool observedEntranceSurveyWasBrainGoal;
		private int observedEntranceSurveySide;
		private float nextRoutePlanTime;
		// If the native graph sends us straight through a table, cabinet
		// or newly placed barricade, briefly let collision-aware local A*
		// try a different real route instead of requesting the same path.
		private float nativeNearObstructionRetryUntil;
		private float nextNativeObstructionDiagnosticAt;
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
		// Short-lived wall-follow commitment grounded in an actual static
		// obstacle contact normal. Local A*, native A*, doors and learned
		// access routes retain precedence over this last-mile fallback.
		private bool hasWallContourCommit;
		private Vector3 wallContourGoal;
		private Vector3 wallContourStart;
		private float wallContourUntil;
		private float nextBlockedDiagnosticAt;
		private int consecutiveStuckRecoveries;
		// Ground route vs safe jump arbitration. A low wall by itself is NOT
		// permission to jump: walking around and native doors take priority.
		private float firstObservedObstacleTime;
		private Vector3 lastObservedObstacleGoal;
		private float nextObstacleComparisonTime;
		private float noJumpUntil;
		private static float nextSharedObstacleComparisonTime;
		private readonly List<Vector3> obstacleWalkComparisonScratch
			= new List<Vector3>(RouteGridNodeLimit);
		// Per-survivor, short-lived local navigation evidence. These are NOT
		// global map portals or inferred paths. They discourage repeated
		// searches into a wall and expire when conditions may have changed.
		private const int MaxRecentNavDeadEnds = 6;
		private readonly Vector3[] recentNavDeadEndPositions = new Vector3[MaxRecentNavDeadEnds];
		private readonly Vector3[] recentNavDeadEndGoals = new Vector3[MaxRecentNavDeadEnds];
		private readonly float[] recentNavDeadEndExpiry = new float[MaxRecentNavDeadEnds];
		private int nextNavDeadEndSlot;
		private static float nextSharedUnknownRouteSlot;
		// Coarse, same-surface long-horizon navigation only when native ASPFP
		// is absent. Physics is shared/budgeted in the planner; local exact
		// doorways, stairs and combat movement still use existing systems.
		private readonly MobileNPCGlobalRoutePlanner globalRoutePlanner
			= new MobileNPCGlobalRoutePlanner();
		private readonly List<Vector3> globalRouteScratch = new List<Vector3>(160);
		private MobileNPCGlobalRoutePlanner.ResolveFooting globalFootingResolver;
		private MobileNPCGlobalRoutePlanner.ValidateEdge globalEdgeValidator;
		private float nextGlobalRouteDiagnosticAt;
		// Conservative, opt-in hybrid local recovery. Never replace native/known door paths.
		[SerializeField] private bool enableHybridRecovery = true;
		// Diagnostic build: produce the promised separate navigation trace by default.
		// Can be disabled in Inspector on crowded/performance runs.
		[SerializeField] private bool enableNavigationReports = true;
		private float nextHybridRecoveryAt;
		private static float nextSharedHybridRecoveryAt;
		private readonly List<Vector3> hybridRouteScratch = new List<Vector3>(128);
		private HybridNPCNavigationTelemetry navigationTelemetry;
		private HybridNPCPathFollower hybridPathMonitor = new HybridNPCPathFollower();
		private bool monitoringHybridPath;

		private void RecordHybridRoute(Vector3 from, Vector3 goal, bool success, string cause, float elapsedMs)
		{
			if (navigationTelemetry == null || !navigationTelemetry.LoggingEnabled) return;
			float distance = 0f;
			for (int i = 1; i < hybridRouteScratch.Count; i++)
				distance += Vector3.Distance(hybridRouteScratch[i - 1], hybridRouteScratch[i]);
			navigationTelemetry.RecordRoute(GetInstanceID().ToString(), "level",
				from, goal, -1, -1, -1, hybridRouteScratch.Count,
				distance, elapsedMs, success, cause);
		}

		private bool TryHybridRecovery(Vector3 goal)
		{
			if (!enableHybridRecovery || Time.time < nextHybridRecoveryAt
				|| Time.time < nextSharedHybridRecoveryAt
				|| routeWaypoints.Count != 0 || hasDetourWaypoint)
				return false;
			nextHybridRecoveryAt = Time.time + 4f;
			nextSharedHybridRecoveryAt = Time.time + 0.35f;
			if (Vector3.Distance(transform.position, goal) > 24f)
				return false;
			// Prefer physical world collision layers over DefaultRaycastLayers;
			// the motor already uses obstacleMask for blocking geometry.
			var config = HybridNPCGridPlanner.Settings.Default;
			// Dynamic humanoid agents belong to a separate movement problem:
			// excluding their layer prevents the stationary A* grid from
			// treating a temporary spawn crowd as permanent building walls.
			config.collisionMask = obstacleMask & ~(1 << gameObject.layer);
			config.ignoredRoot = transform;
			config.maxSearchRadius = 24f;
			config.maxExpandedNodes = 850;
			config.agentRadius = characterController.radius * 0.90f;
			config.agentHeight = characterController.height;
			config.maxStepUp = characterController.stepOffset;
			config.maxSlopeDegrees = characterController.slopeLimit;
			hybridRouteScratch.Clear();
			var watch = System.Diagnostics.Stopwatch.StartNew();
			bool found = HybridNPCObservedPortalRegistry.TryRoute(transform.position,
				goal, config, hybridRouteScratch);
			if (!found)
			{
				hybridRouteScratch.Clear();
				found = HybridNPCGridPlanner.TryFindPath(transform.position,
					goal, config, hybridRouteScratch);
			}
			watch.Stop();
			RecordHybridRoute(transform.position, goal, found,
				found ? "hybrid_local_success" : "hybrid_" + HybridNPCGridPlanner.LastFailureReason,
				(float)watch.Elapsed.TotalMilliseconds);
			if (!found || hybridRouteScratch.Count < 2)
				return false;
			routeWaypoints.AddRange(hybridRouteScratch);
			routeWaypointIndex = 0;
			hasPartialLocalFrontier = false;
			hasPhysicalThresholdRoute = false;
			detourGoalSnapshot = goal;
			nextPlannedRouteValidationTime = 0f;
			hybridPathMonitor.SetPath(hybridRouteScratch);
			monitoringHybridPath = true;
			return true;
		}

		/// <summary>Limited, per-individual beginning-of-life guidance is
		/// progress, not a rechargeable resource after save/reload.</summary>
		public int ExportNoviceHintsUsedServer()
		{
			return Provider.isServer ? noviceDirectorHintsUsed : 0;
		}

		public void RestoreNoviceHintsUsedServer(int used)
		{
			if (Provider.isServer && hasInitialized)
				noviceDirectorHintsUsed = Mathf.Clamp(used, 0, 2);
		}

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
			globalRoutePlanner.Reset();
			nextHybridRecoveryAt = 0f;
			monitoringHybridPath = false;
			hybridPathMonitor.Clear();
			if (enableNavigationReports)
			{
				if (navigationTelemetry == null)
					navigationTelemetry = GetComponent<HybridNPCNavigationTelemetry>();
				if (navigationTelemetry == null)
					navigationTelemetry = gameObject.AddComponent<HybridNPCNavigationTelemetry>();
				navigationTelemetry.SetEnabled(true);
			}
			globalRouteScratch.Clear();
			nextGlobalRouteDiagnosticAt = 0f;
			nextJumpTime = 0f;
			isPlannedJumping = false;
			if (ladderTraversal != null)
				ladderTraversal.ResetTraversal();
			plannedJumpUsesVaultPlan = false;
			plannedJumpLandsOnObjectTop = false;
			plannedJumpSupportCollider = null;
			ClearVerifiedFurnitureSupport();
			nextRaisedObjectLandingDiagnosticTime = 0f;
			nextElevatedTacticProbeTime = 0f;
			validatedElevatedTacticUntil = 0f;
			plannedJumpLanding = Vector3.zero;
			plannedJumpDirection = Vector3.zero;
			plannedJumpDestinationSnapshot = Vector3.zero;
			suppressJourneyUntil = 0f;
			nextPlannedJumpProbeTime = 0f;
			nextLocalRoutePlanTime = 0f;
			nativeNearObstructionRetryUntil = 0f;
			nextNativeObstructionDiagnosticAt = 0f;
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
			hasPartialLocalFrontier = false;
			ResetObservedEntranceSurvey();
			brainDriver.InitializeServer(spawnPosition);
			hasRoamingDestination = false;
			hasSuspendedRoutineWaypoint = false;
			suspendedRoutineExpiresAt = 0f;
			noviceDirectorHintsUsed = 0;
			nextNoviceBearingAttemptAt = Time.time + Random.Range(2f, 6f);
			nextRoamingDecisionTime = Time.time + Random.Range(2f, 5f);
			lastStuckCheckPosition = transform.position;
			lastMeaningfulRouteProgressPosition = transform.position;
			hasTrackedStuckGoal = false;
			lastTrackedStuckWasBrainGoal = false;
			nextStuckCheckTime = Time.time + 0.75f;
			blockedMovementDuration = 0f;
			useRecoverySteering = false;
			hasWallContourCommit = false;
			wallContourUntil = 0f;
			nextBlockedDiagnosticAt = 0f;
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
			rangedCombat = GetComponent<MobileNPCHumanoidRangedCombat>();
			meleeCombat = GetComponent<MobileNPCHumanoidCombat>();
			autonomyPlanner = GetComponent<MobileNPCAutonomyPlanner>();
			zombieRisk = GetComponent<MobileNPCZombieRiskAssessment>();
			injurySystem = GetComponent<MobileNPCInjurySystem>();
			doorNavigation = GetComponent<MobileNPCDoorNavigation>();
			ladderTraversal = GetComponent<MobileNPCLadderTraversal>();
			if (ladderTraversal == null)
				ladderTraversal = gameObject.AddComponent<MobileNPCLadderTraversal>();
			vaultTraversal = GetComponent<MobileNPCVaultTraversal>();
			if (vaultTraversal == null)
				vaultTraversal = gameObject.AddComponent<MobileNPCVaultTraversal>();
			travelMemory = GetComponent<MobileNPCTravelMemory>();
			// Cache the two delegates once. Creating method-group delegates
			// each Update on 20-40 NPCs would cause needless GC spikes.
			globalFootingResolver = TryResolveGlobalFootingServer;
			globalEdgeValidator = IsGlobalEdgePhysicallyTraversableServer;
			independentRouteProvider = GetComponent<MobileNPCIndependentRouteProvider>();
			if (independentRouteProvider == null)
				independentRouteProvider = gameObject.AddComponent<MobileNPCIndependentRouteProvider>();
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
			if (monitoringHybridPath)
			{
				// The motor owns the actual route index. A verified physical
				// corner shortcut may advance it ahead of the diagnostic follower.
				// Never let a stale monitor cancel the motor's newer waypoint.
				if (routeWaypoints.Count == 0 || routeWaypointIndex >= routeWaypoints.Count)
				{
					monitoringHybridPath = false;
					hybridPathMonitor.Clear();
				}
				else if ((hybridPathMonitor.DesiredWaypoint
					- routeWaypoints[routeWaypointIndex]).sqrMagnitude > 0.25f)
				{
					hybridPathMonitor.SetPath(routeWaypoints.GetRange(
						routeWaypointIndex, routeWaypoints.Count - routeWaypointIndex));
				}
			}
			if (monitoringHybridPath)
			{
				var progress = hybridPathMonitor.Tick(transform.position, deltaTime);
				if (navigationTelemetry != null && navigationTelemetry.LoggingEnabled)
					navigationTelemetry.RecordMovement(GetInstanceID().ToString(),
						"level", transform.position, detourGoalSnapshot,
						hybridPathMonitor.DesiredWaypoint, velocity.magnitude,
						progress.ToString());
				if (progress == HybridNPCPathFollower.RouteState.Stalled)
				{
					if (navigationTelemetry != null)
						navigationTelemetry.RecordProblem(GetInstanceID().ToString(),
							"level", transform.position, detourGoalSnapshot,
							"hybrid_waypoint_stalled", "reset_and_replan", false);
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
					monitoringHybridPath = false;
					hybridPathMonitor.Clear();
					nextHybridRecoveryAt = Time.time + 5f;
				}
				else if (progress == HybridNPCPathFollower.RouteState.Arrived
					|| routeWaypoints.Count == 0)
				{
					monitoringHybridPath = false;
					hybridPathMonitor.Clear();
				}
			}
			if (verifiedFurnitureSupportCollider != null
				&& Time.time >= nextVerifiedFurnitureSupportCheckTime)
				RefreshVerifiedFurnitureSupportServer();
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
			bool routineOwnsMovement = brainDriver.RoutineMayControlMovement;
			// A retreat awaiting a valid escape route owns the locomotion slot.
			// Never start or resume a patrol, loot search or return-home path
			// while its tactical arbiter is deciding where to withdraw.
			if (!routineOwnsMovement && !hasBrainDestination)
			{
				if (hasRoamingDestination && autonomyPlanner != null)
					autonomyPlanner.ReleaseExplorationSectorServer(false, false);
				hasRoamingDestination = false;
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				hasDetourWaypoint = false;
			}

			// A routine switch may invalidate only a low-priority wander,
			// never a combat/loot/reinforcement path. This is the online
			// handoff between strategic job arbitration and physical motor.
			if (routineOwnsMovement && !hasBrainDestination && autonomyPlanner != null)
			{
				MobileNPCAutonomyPlanner.Job routine = autonomyPlanner.EvaluateRoutineServer();
				if (hasPreviousRoutineJob && routine != previousRoutineJob)
				{
					// Old strategy must not resume an obsolete patrol/loot waypoint.
					hasSuspendedRoutineWaypoint = false;
				}
				if (hasPreviousRoutineJob && routine != previousRoutineJob
					&& hasRoamingDestination)
				{
					autonomyPlanner.ReleaseExplorationSectorServer(false, false);
					hasRoamingDestination = false;
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
					hasDetourWaypoint = false;
					nextRoamingDecisionTime = Time.time + 0.5f;
				}
				previousRoutineJob = routine;
				hasPreviousRoutineJob = true;
			}

			if (hasBrainDestination)
			{
				// Combat, investigation, confirmed loot, radio and follow orders
				// preempt online scouting without leaving a false visited sector.
				if (hasRoamingDestination && autonomyPlanner != null)
				{
					MobileNPCAutonomyPlanner.Job routine = autonomyPlanner.CurrentJob;
					if ((routine == MobileNPCAutonomyPlanner.Job.Patrol
						|| routine == MobileNPCAutonomyPlanner.Job.Scavenge)
						&& Level.checkSafeIncludingClipVolumes(roamingDestination))
					{
						suspendedRoutineWaypoint = roamingDestination;
						suspendedRoutineJob = routine;
						suspendedRoutineExpiresAt = Time.time + 38f;
						hasSuspendedRoutineWaypoint = true;
					}
					autonomyPlanner.ReleaseExplorationSectorServer(false, false);
				}
				// Combat, investigation and tactical goals always take precedence over roaming or returning home.
				hasRoamingDestination = false;
			}
			else if (!routineOwnsMovement)
			{
				// Active investigation, combat aim, reload or defensive
				// planning owns this frame even without a movement endpoint.
			}
			else if (!IsWithinHomeTerritory)
			{
				// Recover from knockback or other external displacement before choosing another roaming waypoint.
				// This remains ordinary collision-aware movement; it does not teleport the NPC home.
				roamingDestination = spawnPosition;
				hasRoamingDestination = true;
			}
			else if (roamingEnabled && autonomyPlanner != null
				&& autonomyPlanner.EvaluateRoutineServer() == MobileNPCAutonomyPlanner.Job.Recover)
			{
				// A safe, short recovery pause is a *routine*, not a tactical
				// immobilization: any brain destination above preempts it instantly.
				if (hasRoamingDestination)
				{
					hasRoamingDestination = false;
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
					hasDetourWaypoint = false;
				}
				nextRoamingDecisionTime = Time.time + 2f;
			}
			else if (roamingEnabled && Time.time >= nextRoamingDecisionTime && !hasRoamingDestination)
			{
				// Once a genuine combat/sound interruption has ended, try
				// resuming the prior strategic destination ONCE. Every piece
				// of world ground and group coordination is checked again.
				if (!TryResumeSuspendedRoutineWaypointServer())
				{
					// When scouting, favor a nearby, physically observed
					// boundary of personal knowledge before random wandering.
					// Tactical/combat destinations have already preempted this.
					bool sensedFrontier = false;
					if (autonomyPlanner != null
						&& (autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Patrol
							|| autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge))
					{
						MobileNPCSpatialMemory spatial = GetComponent<MobileNPCSpatialMemory>();
						Vector3 frontier;
						if (spatial != null
							&& spatial.TryGetExplorationFrontier(transform.position, out frontier)
							&& Level.checkSafeIncludingClipVolumes(frontier))
						{
							roamingDestination = frontier;
							hasRoamingDestination = true;
							nextRoamingDecisionTime = Time.time + 5f;
							sensedFrontier = true;
						}
					}
					if (!sensedFrontier)
						TryChooseRoamingDestination();
				}
			}

			// Refuse an optional scouting assignment which would lead
			// INTO a recently observed zombie group. A postponed site is
			// not a visited room or permanent impassable geometry.
			if (!hasBrainDestination && hasRoamingDestination && zombieRisk != null)
			{
				Vector3 towardSite = roamingDestination - transform.position;
				towardSite.y = 0f;
				float length = towardSite.magnitude;
				if (length > 0.5f)
				{
					Vector3 imminent = transform.position
						+ towardSite / length * Mathf.Min(5f, length);
					float currentRisk = zombieRisk.GetKnownRiskAtServer(transform.position);
					float approachRisk = zombieRisk.GetKnownRiskAtServer(imminent);
					if (approachRisk >= 0.8f && approachRisk > currentRisk + 0.15f)
					{
						if (autonomyPlanner != null)
							autonomyPlanner.ReleaseExplorationSectorServer(false, false);
						hasRoamingDestination = false;
						routeWaypoints.Clear();
						routeWaypointIndex = 0;
						hasDetourWaypoint = false;
						nextRoamingDecisionTime = Time.time + Random.Range(2.0f, 3.0f);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
						Debug.Log("[MobileNPCZombieRisk] postponed unsafe scouting approach npc="
							+ name + " risk=" + approachRisk.ToString("F2"), this);
#endif
					}
				}
			}

			bool waitingForDoorToOpen = false;
			bool waitingForLearnedAccess = false;
			// Shared across route planning AND ground movement / jump arbitration.
			// Previously declared inside the destination block, out of scope for
			// the later animation, vault and jumping checks in this Update.
			bool waitingForLadderApproach = false;
			// A ranged survivor is NOT a melee zombie that must charge the
			// center of its target. Deliberately stop and aim when a personally
			// confirmed living threat is at sensible firearm range, while
			// retaining the pursuit memory and all active defensive goals.
			bool holdDeliberateFire = false;
			if (!isUnderwater && !isPlannedJumping && !IsClimbingLadder
				&& !IsTraversingLadder && targetIsVisible && hasBrainDestination
				&& brainDriver.CurrentState == MobileNPCBrain.State.Pursuing
				&& brainDriver.CurrentGoal == MobileNPCTactics.Goal.None
				&& brainDriver.Target != null && rangedCombat != null)
			{
				Vector3 opponentOffset = brainDriver.Target.position - transform.position;
				opponentOffset.y = 0f;
				holdDeliberateFire = rangedCombat.CanHoldDeliberateFiringPositionServer(
					opponentOffset.magnitude);
			}
			if (holdDeliberateFire && !wasHoldingDeliberateFire)
			{
				// Discard a previously computed *movement* path, never the
				// perceived combat target or its private evidence.
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				hasDetourWaypoint = false;
			}
			wasHoldingDeliberateFire = holdDeliberateFire;
			// Tactical combat owns locomotion even while holding a firing
			// position. A suspended scavenging waypoint must not wake up and
			// pull the actor sideways while its gun is trained on a threat.
			bool hasDestination = hasBrainDestination
				? !holdDeliberateFire : routineOwnsMovement && hasRoamingDestination;
			bool waitingForGlobalPlan = false;
			Vector3 destination = hasBrainDestination ? brainDriver.Destination : roamingDestination;
			if (hasDestination && !isUnderwater && !isPlannedJumping
				&& !physicallyCrouched
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
				hasPartialLocalFrontier = false;
				hasPhysicalThresholdRoute = false;
				globalRoutePlanner.Reset();
				ResetObservedEntranceSurvey();
				// Completed/expired goals must not leave the previous route waiting to
				// steer the next unrelated investigation or roaming destination.
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				hasDetourWaypoint = false;
				useRecoverySteering = false;
				hasWallContourCommit = false;
			}
			if (hasDestination && !IsTraversingLadder)
			{
				BeginOrContinueActualJourney(destination);
				// Keep a planned route while its goal remains stable. The previous condition
				// cleared routeWaypoints whenever hasDetourWaypoint was false, which is also
				// the normal state for a multi-corner native A* route.
				// Pursuit targets move while we approach an observed opening.
				// Preserve a PHYSICALLY verified threshold crossing against
				// small target motion on the far side; otherwise the goal
				// snapshot invalidates the passage every time prey walks 1.5m.
				// Large movements or a different floor still invalidate it.
				bool changedDestination = PlanarDistanceSquared(destination,
					detourGoalSnapshot) > 2.25f
					|| Mathf.Abs(destination.y - detourGoalSnapshot.y) > 0.8f
					// Even an identical world point may now represent an urgent
					// retreat rather than the old exploration or pursuit task.
					|| hasPhysicalThresholdRoute
						&& physicalThresholdOwner != brainDriver.CurrentMovementOwner;
				bool preserveObservedThreshold = hasPhysicalThresholdRoute
					// A nearby NEW tactical decision must never inherit the old
					// patrol/investigation crossing merely due to proximity.
					&& physicalThresholdOwner == brainDriver.CurrentMovementOwner
					&& routeWaypointIndex < routeWaypoints.Count
					&& routeWaypointIndex <= physicalThresholdApproachWaypointIndex + 2
					&& Mathf.Abs(destination.y - detourGoalSnapshot.y) <= 0.8f
					&& PlanarDistanceSquared(destination, detourGoalSnapshot) <= 36f;
				if (changedDestination && !preserveObservedThreshold)
				{
					hasPartialLocalFrontier = false;
					hasPhysicalThresholdRoute = false;
					// The fallback planner compares its own actual goal and origin;
					// resetting it here would happen every frame while no
					// route exists because detourGoalSnapshot only tracks
					// committed paths, starving the incremental search.
					ResetObservedEntranceSurvey();
					// A failed native corridor for the OLD goal must not
					// delay an unrelated incoming combat or rescue task.
					nativeNearObstructionRetryUntil = 0f;
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
				// One async native A* provider per survivor: an already pending
				// route to a genuine Ladder must not be overwritten by an
				// independent route request to a remembered staircase.
				if (!isUnderwater && !hasPlannedLadderApproach
					&& routeWaypoints.Count == 0 && !hasDetourWaypoint)
					TryBuildKnownFloorAccessRoute(destination, out waitingForLearnedAccess);
				// A real Ladder is a directed off-mesh movement action. Before
				// global pathfinding to another floor, consider an approach
				// through actual walkable ground to its observed entry side.
				if (!isUnderwater && !waitingForLearnedAccess
					&& routeWaypoints.Count == 0 && !hasDetourWaypoint)
					TryBuildPhysicalLadderApproach(destination, out waitingForLadderApproach);
				// Zombie's native seeker keeps its path destination active through a
				// building opening. Our waypoint motor previously discovered physical
				// thresholds chiefly during stuck recovery, after local/global routes
				// had already steered it into the facade. Give a NEARBY, actually
				// obstructed doorway a bounded chance before coarse path planning.
				// This still requires the same capsule, floor and wall-side proofs.
				Vector3 doorwayGoal = destination - transform.position;
				doorwayGoal.y = 0f;
				if (!isUnderwater && !isPlannedJumping && characterController.isGrounded
					&& !waitingForLearnedAccess && !waitingForLadderApproach
					&& routeWaypoints.Count == 0 && !hasDetourWaypoint
					&& doorwayGoal.sqrMagnitude >= 4f
					&& doorwayGoal.sqrMagnitude <= 144f
					// The final goal may be upstairs; doorway approach stays ground-level.
					&& Mathf.Abs(destination.y - transform.position.y) <= 4.5f
					&& Time.time >= nextPhysicalThresholdScanTime
					&& Time.time >= nextSharedPhysicalThresholdSlot)
				{
					TryPlanObservedBuildingThresholdServer(destination);
				}
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
					if (independentRouteProvider != null && independentRouteProvider.IsAvailable
						&& TryBuildIndependentRoute(destination))
					{
						detourGoalSnapshot = destination;
						hasDetourWaypoint = false;
					}
				}
				// If the optional ASPFP library was not shipped with this SDK,
			// progressively sample a 61m-wide physical grid. Unlike a naive
			// steering detour, every global edge has both standing-capsule and
			// continuous-ground proof. The world goal remains authoritative,
			// and the regular local/doorway planner handles the final approach.
			if (!isUnderwater && !isPlannedJumping
				&& !waitingForLearnedAccess && !waitingForLadderApproach
				&& routeWaypoints.Count == 0 && !hasDetourWaypoint
				&& globalGoalOffset.sqrMagnitude > 100f
				&& Mathf.Abs(destination.y - transform.position.y) <= 6f)
			{
				globalRouteScratch.Clear();
				// Before the coarse 3m global search, try one physically
				// verified, already traversed doorway as a SHORT staging hop.
				// This does not claim to reach the final world destination.
				// Once the far side is reached, the global planner resumes.
				if (globalGoalOffset.sqrMagnitude > 900f
					&& independentRouteProvider != null
					&& independentRouteProvider.IsAvailable
					&& independentRouteProvider.TryGetConfirmedGatewayStage(
						transform.position, destination, globalRouteScratch)
					&& globalRouteScratch.Count >= 2
					&& IsGlobalEdgePhysicallyTraversableServer(
						transform.position, globalRouteScratch[0]))
				{
					routeWaypoints.AddRange(globalRouteScratch);
					// The physical planner includes its origin point. Avoid
					// asking the motor to revisit the position it already has.
					if (routeWaypoints.Count > 0
						&& (routeWaypoints[0] - transform.position).sqrMagnitude <= 0.36f)
						routeWaypoints.RemoveAt(0);
					routeWaypointIndex = 0;
					hasPartialLocalFrontier = false;
					hasPhysicalThresholdRoute = false;
					detourGoalSnapshot = destination;
					nextPlannedRouteValidationTime = 0f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					if (Time.time >= nextGlobalRouteDiagnosticAt)
					{
						nextGlobalRouteDiagnosticAt = Time.time + 5f;
						Debug.Log("[MobileNPCGlobalRoute] verified gateway stage npc="
							+ name + " stageEnd="
							+ routeWaypoints[routeWaypoints.Count - 1]
							+ " finalGoal=" + destination, this);
					}
#endif
				}
				globalRouteScratch.Clear();
				if (routeWaypoints.Count == 0 && globalRoutePlanner.Step(transform.position, destination,
					Time.time, Time.frameCount, GetInstanceID(),
					globalFootingResolver, globalEdgeValidator,
					globalRouteScratch) && globalRouteScratch.Count > 0
					&& IsGlobalEdgePhysicallyTraversableServer(
						transform.position, globalRouteScratch[0]))
				{
					routeWaypoints.AddRange(globalRouteScratch);
					routeWaypointIndex = 0;
					hasPartialLocalFrontier = false;
					hasPhysicalThresholdRoute = false;
					detourGoalSnapshot = destination;
					nextPlannedRouteValidationTime = 0f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					if (Time.time >= nextGlobalRouteDiagnosticAt)
					{
						nextGlobalRouteDiagnosticAt = Time.time + 5f;
						Debug.Log("[MobileNPCGlobalRoute] npc=" + name
							+ " nodes=" + routeWaypoints.Count
							+ " stagedEnd=" + routeWaypoints[routeWaypoints.Count - 1]
							+ " finalGoal=" + destination, this);
					}
#endif
				}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				else if (routeWaypoints.Count == 0 && !globalRoutePlanner.IsSearching
					&& globalRoutePlanner.LastOutcome == "no-connected-route"
					&& Time.time >= nextGlobalRouteDiagnosticAt)
				{
					nextGlobalRouteDiagnosticAt = Time.time + 15f;
					Debug.LogWarning("[MobileNPCGlobalRoute] No connected corridor npc=" + name
						+ " from=" + transform.position + " finalGoal=" + destination
						+ " expanded=" + globalRoutePlanner.LastExpanded
						+ "; retaining local doorway and obstacle fallbacks.", this);
				}
#endif
			}
			// Hybrid recovery is attempted only after existing navigation has
			// declined a route, and only for a nearby physically verified goal.
			if (!isUnderwater && !isPlannedJumping && !waitingForLearnedAccess
				&& !waitingForLadderApproach && routeWaypoints.Count == 0
				&& !hasDetourWaypoint && !hasPhysicalThresholdRoute)
				TryHybridRecovery(destination);
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
				// A partial A* frontier or one-hop wall detour can remain active
				// indefinitely, suppressing the doorway detector that only ran
				// when routeWaypoints was empty. After a REAL physical stall,
				// allow the independent facade detector to replace that bad
				// route with a verified two-sided passage. Its ground and capsule
				// checks remain mandatory; never overwrite an active threshold.
				if (!hasPhysicalThresholdRoute && consecutiveStuckRecoveries >= 1
					&& !isUnderwater && !IsTraversingLadder
					&& characterController.isGrounded
					// The final goal may be upstairs; doorway approach stays ground-level.
					&& Mathf.Abs(destination.y - transform.position.y) <= 4.5f
					&& PlanarDistanceSquared(transform.position, destination) <= 400f
					&& TryPlanObservedBuildingThresholdServer(destination))
				{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					Debug.Log("[MobileNPCThresholdTraverse] recovered stalled partial route via real opening npc="
						+ name, this);
#endif
				}

				// A destination/route may have been replaced by an unrelated
				// tactical system. Never apply narrow-portal tolerances to
				// its waypoints merely because their count happens to match.
				if (hasPhysicalThresholdRoute
					&& (routeWaypoints.Count < 2
						|| physicalThresholdApproachWaypointIndex
							>= routeWaypoints.Count - 1
						|| PlanarDistanceSquared(
							routeWaypoints[routeWaypoints.Count - 1],
							physicalThresholdBeyondWaypoint) > 0.09f))
					hasPhysicalThresholdRoute = false;

				// Detour-style corridor refinement: skip an unnecessary near
				// corner only when a short, same-floor collision- and ground-
				// checked segment to the following waypoint is traversable.
				// This never skips the final brain goal or a door/steep stair.
				if (!isUnderwater && routeWaypointIndex + 1 < routeWaypoints.Count
					&& !hasPhysicalThresholdRoute
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
					&& PlanarDistanceSquared(transform.position, routeWaypoints[routeWaypointIndex])
					<= (hasPhysicalThresholdRoute
						&& routeWaypointIndex >= physicalThresholdApproachWaypointIndex
						? 0.16f
						: routeWaypointIndex < routeWaypoints.Count - 1
							? 0.49f : 1.0f)
					&& Mathf.Abs(routeWaypoints[routeWaypointIndex].y - transform.position.y)
					<= (hasPhysicalThresholdRoute
						|| routeWaypointIndex < routeWaypoints.Count - 1
							? 0.45f
							: Mathf.Max(0.45f, characterController.stepOffset + 0.2f)))
				{
					// Reaching the FAR threshold waypoint, not merely discovering
					// an opening, proves that the NPC actually crossed it.
					if (hasPhysicalThresholdRoute
						&& routeWaypointIndex == routeWaypoints.Count - 1
						&& Vector3.Distance(transform.position, physicalThresholdBeyondWaypoint) < 0.55f)
					{
						MobileNPCSpatialMemory personal = GetComponent<MobileNPCSpatialMemory>();
						if (personal != null)
							personal.RememberObservedPassage(physicalThresholdEntryWaypoint,
								physicalThresholdBeyondWaypoint);
						// Promote a candidate to a shared graph edge only after
						// the actual character controller reaches its far side.
						HybridNPCObservedPortalRegistry.ConfirmCrossing(
							physicalThresholdEntryWaypoint, physicalThresholdBeyondWaypoint);
						if (navigationTelemetry != null && navigationTelemetry.LoggingEnabled)
							navigationTelemetry.RecordPortal(GetInstanceID().ToString(),
								"level", physicalThresholdEntryWaypoint, physicalThresholdBeyondWaypoint,
								-1, -1, -1, true, "physically_crossed_facade");
					}
					// This is the only valid proof of progressing through the
					// verified opening; route discovery alone is not success.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					if (hasPhysicalThresholdRoute && Time.time >= nextThresholdRouteStateLog)
					{
						nextThresholdRouteStateLog = Time.time + 1f;
						Debug.Log("[MobileNPCThresholdTraverse] npc=" + name
							+ " reachedIndex=" + routeWaypointIndex
							+ " approachIndex=" + physicalThresholdApproachWaypointIndex
							+ " actor=" + transform.position
							+ " beyond=" + physicalThresholdBeyondWaypoint, this);
					}
#endif
					++routeWaypointIndex;
					if (routeWaypointIndex >= routeWaypoints.Count)
					{
						// Reaching a LOCAL exploratory frontier is not evidence
						// that the actual target was reached. If its direct path
						// remains physically obstructed, avoid the same near-wall
						// frontier for a few seconds on the next bounded A* pass.
						if (hasPartialLocalFrontier
							&& PlanarDistanceSquared(partialLocalTarget, destination) <= 2.25f
							&& PlanarDistanceSquared(partialLocalFrontier, transform.position) <= 1.4f
							&& PlanarDistanceSquared(destination, transform.position) > 9f)
						{
							Vector3 towardFinal = destination - transform.position;
							towardFinal.y = 0f;
							if (towardFinal.sqrMagnitude > 0.01f)
							{
								Vector3 rayOrigin = transform.position + Vector3.up
									* Mathf.Max(0.45f, characterController.height * 0.55f);
								float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
									Mathf.Max(0.05f, characterController.radius * 0.9f));
								float probe = Mathf.Max(0.5f, obstacleProbeDistance);
								if (IsObstacleBlockingAlong(rayOrigin, radius,
									towardFinal.normalized, probe)
									&& !IsOnlyTemporaryActorBlockingAlong(rayOrigin, radius,
										towardFinal.normalized, probe)
									&& (doorNavigation == null
										|| !doorNavigation.IsTraversableUnlockedDoorEdgeServer(
											rayOrigin, towardFinal.normalized, radius,
											Mathf.Min(2.5f, probe), obstacleMask)))
								{
									// ONLY a frontier physically reached with an
									// observed solid blocker enters this short-lived
									// personal memory. Six slots, same real goal,
									// no assumed building door or portal.
									RememberLocalNavigationDeadEnd(transform.position, destination, 36f);
									RememberReachedBlockedEntranceFrontier(destination);
								}
							}
						}
						hasPartialLocalFrontier = false;
						hasPhysicalThresholdRoute = false;
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
						// During a verified doorway approach, another nearby wall
						// must not invalidate the route merely because the generic
						// look-ahead sphere reaches beyond the CURRENT near-side
						// waypoint. Test only the segment the controller will walk.
						float segmentDistance = Mathf.Min(routeOffset.magnitude,
							Mathf.Max(0.5f, obstacleProbeDistance));
						if (hasPhysicalThresholdRoute
							&& routeWaypointIndex <= physicalThresholdApproachWaypointIndex)
							segmentDistance = Mathf.Min(segmentDistance,
								Mathf.Max(0.05f, routeOffset.magnitude - 0.15f));
						// A spherecast at chest height is NOT the same proof as
						// the character's standing capsule through a narrow opening.
						// Do not discard a threshold route because its broad probe
						// brushes a facade while the real-size corridor remains clear.
						bool validThresholdSegment = hasPhysicalThresholdRoute
							&& routeWaypointIndex >= physicalThresholdApproachWaypointIndex
							&& IsStandingCapsuleCorridorClearServer(transform.position,
								routeOffset.normalized, segmentDistance,
								Mathf.Max(0.05f, characterController.radius * 0.90f))
							&& HasContinuousLocalFloor(transform.position, movementDestination);
						if (!validThresholdSegment
							&& IsObstacleBlockingAlong(routeOrigin, routeRadius, routeOffset.normalized,
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
								if (hasPhysicalThresholdRoute && Time.time >= nextThresholdRouteStateLog)
								{
									nextThresholdRouteStateLog = Time.time + 1f;
									Debug.LogWarning("[MobileNPCThresholdTraverse] Abandoning "
										+ "verified aperture route after a NEW collider blocked "
										+ "the next segment npc=" + name
										+ " position=" + transform.position
										+ " waypoint=" + movementDestination, this);
								}
#endif
								hasPhysicalThresholdRoute = false;
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
					// Peitoril de janela: o raio do peito pode estar
					// livre, mas o CharacterController continua bloqueado.
					&& (IsObstacleBlocking(goalOffset.normalized)
						|| consecutiveStuckRecoveries >= 1))
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
				// When an NPC has physically stalled at a long facade, let
				// the macro planner spend at most five seconds discovering a
				// connected corner route. Do not keep pushing the capsule against
				// the very same wall while its sampled origin is being planned.
				waitingForGlobalPlan = routeWaypoints.Count == 0 && !hasDetourWaypoint
					&& !waitingForLearnedAccess && !waitingForLadderApproach
					&& consecutiveStuckRecoveries >= 2
					&& goalOffset.sqrMagnitude > 196f
					&& globalRoutePlanner.IsWithinBriefWaitWindow(Time.time);
				if (waitingForGlobalPlan)
					movementDestination = transform.position;
				Vector3 offset = movementDestination - transform.position;
				offset.y = 0.0f;
				// Waypoint arrival for a verified building opening is only
				// 0.4m, not the ordinary ~1m brain-goal stopping distance.
				// Otherwise the agent stops 0.5-1m before the threshold,
				// never increments the route index, and later marks the
				// geometrically valid entrance as a failed path.
				// Intermediate navigation waypoints need their own arrival
				// tolerance. Using the BRAIN'S destination stop radius here
				// could halt the controller 1-3m before a narrow entrance,
				// even though the route index has not advanced yet.
				float movementStop = stoppingDistance;
				if (routeWaypointIndex < routeWaypoints.Count)
				{
					movementStop = hasPhysicalThresholdRoute
						&& routeWaypointIndex >= physicalThresholdApproachWaypointIndex
						? 0.20f
						: routeWaypointIndex < routeWaypoints.Count - 1
							? 0.45f : 0.65f;
				}
				if (offset.sqrMagnitude > movementStop * movementStop)
				{
					planarDirection = offset.normalized;
				}
				else if (!HasPhysicallyReachedNavigationGoal(destination)
					&& TryChooseVerifiedFurnitureRunupServer(destination,
						out Vector3 runupDirection))
				{
					// Physically walk back from a nearby elevated top first;
					// the ordinary jump planner gets an achievable approach
					// rather than standing beneath the object indefinitely.
					planarDirection = runupDirection;
				}
				else if (hasBrainDestination)
				{
					// A detour node or A* intermediary is not the destination of
					// the brain's seek-cover, loot, investigate or pursue goal.
					// Do not acknowledge arrival until the *actual requested goal*
					// is physically within stopping distance, with no artificial
					// bridging across stairs or different floors.
					if (HasPhysicallyReachedNavigationGoal(destination))
						brainDriver.ReportDestinationReached();
				}
				else if (HasPhysicallyReachedNavigationGoal(destination))
				{
					// Movement success is not a successful search or world pickup.
					// Give the native loot scanner one immediate, physically local
					// inspection before issuing another speculative roaming leg.
					bool scavenging = autonomyPlanner != null
						&& autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge;
					if (scavenging)
					{
						MobileNPCInteriorSurvey survey = GetComponent<MobileNPCInteriorSurvey>();
						if (survey != null)
							survey.ReportArrivedServer(roamingDestination);
						MobileNPCLooting looting = GetComponent<MobileNPCLooting>();
						if (looting != null)
							looting.RequestRoutineInspectionServer(roamingDestination);
					}
					hasRoamingDestination = false;
					if (autonomyPlanner != null)
						autonomyPlanner.ReportRoamReachedServer(roamingDestination);
					nextRoamingDecisionTime = Time.time + (scavenging
						? Random.Range(4f, 7f)
						: Random.Range(Mathf.Max(1f, roamingPauseMin),
							Mathf.Max(Mathf.Max(1f, roamingPauseMin), roamingPauseMax)));
				}
			}

			if (hasDestination && hasRoamingDestination && !hasBrainDestination
				&& planarDirection.sqrMagnitude <= 0.0001f
				// A verified doorway waypoint or a physical detour is still
				// active route work. Do not discard roaming when close to
				// an intermediate waypoint rather than the final destination.
				&& routeWaypointIndex >= routeWaypoints.Count && !hasDetourWaypoint
				&& !waitingForGlobalPlan
				&& !waitingForDoorToOpen && !waitingForLearnedAccess
				&& !waitingForLadderApproach)
			{
				// Only an active roam with NO navigation work pending may be
				// discarded here. Reaching the target already clears that roam;
				// an async known-access/ladder request or a native door animation
				// is a deliberate pause, not evidence of a failed destination.
				// If genuinely unable to steer, abandon this optional waypoint
				// and sample another real ground point rather than freezing forever.
				MobileNPCInteriorSurvey survey = GetComponent<MobileNPCInteriorSurvey>();
				if (survey != null)
					survey.ReportRoomRouteBlockedServer(roamingDestination);
				if (autonomyPlanner != null)
					autonomyPlanner.ReportRoamBlockedServer(roamingDestination);
				hasRoamingDestination = false;
				hasDetourWaypoint = false;
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				nextRoamingDecisionTime = Time.time + Random.Range(0.4f, 1.2f);
			}

			// A suspicious footstep can suspend walking briefly while the survivor
			// listens. A confirmed threat or retreat pre-empts this immediately.
			// Listening to a noise must not immobilize a survivor with a
			// confirmed hostile or an active retreat/cover destination.
			bool urgentTacticalTravel = brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat
				|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.SeekCover;
			bool activeHostile = brainDriver.Target != null
				&& brainDriver.CurrentState == MobileNPCBrain.State.Pursuing;
			if (((brainDriver.IsListening || brainDriver.IsAmbushPreparing)
					&& !urgentTacticalTravel && !activeHostile)
				|| waitingForDoorToOpen || waitingForLearnedAccess
				|| waitingForLadderApproach)
				planarDirection = Vector3.zero;

			bool wantedToMove = planarDirection.sqrMagnitude > 0.0001f;
			bool verifiedLowOpening = false;
			if (wantedToMove)
			{
				Vector3 requestedDirection = planarDirection;
				// Only choose crouch-through when standing is obstructed
				// AND the crouched capsule plus actual floor are clear.
				// Do this before ordinary obstacle steering diverts the
				// requested route away from a real low doorway.
				verifiedLowOpening = !isUnderwater && !isPlannedJumping
					&& !IsTraversingLadder && !brainDriver.IsListening
					&& characterController.isGrounded
					&& TryRecognizeLowOpeningServer(requestedDirection);
				// A physically verified narrow facade opening has a centered,
				// two-sided crossing segment. Generic wall contour / recovery
				// steering must not turn away from it AFTER the approach was
				// reached. Only preserve the heading when the immediate capsule
				// sweep and floor continuity still pass; a newly closed or
				// obstructed entrance remains governed by ordinary avoidance.
				bool verifiedThresholdCrossing = false;
				if (!verifiedLowOpening && !isUnderwater && hasPhysicalThresholdRoute
					&& routeWaypointIndex >= physicalThresholdApproachWaypointIndex
					&& routeWaypointIndex < routeWaypoints.Count)
				{
					Vector3 crossingWaypoint = routeWaypoints[routeWaypointIndex];
					Vector3 crossingDelta = crossingWaypoint - transform.position;
					crossingDelta.y = 0f;
					float crossingDistance = crossingDelta.magnitude;
					float crossingRadius = Mathf.Clamp(obstacleProbeRadius, 0.15f,
						Mathf.Max(0.15f, characterController.radius * 0.9f));
					verifiedThresholdCrossing = crossingDistance > 0.2f
						&& crossingDistance <= 2.5f
						&& IsStandingCapsuleCorridorClearServer(transform.position,
							crossingDelta / crossingDistance, crossingDistance, crossingRadius)
						&& HasContinuousLocalFloor(transform.position, crossingWaypoint);
				}
				if (verifiedLowOpening || verifiedThresholdCrossing)
				{
					planarDirection = requestedDirection;
				}
				else if (useRecoverySteering && Time.time < recoverySteeringUntil)
				{
					// The new contact-normal wall-follow already committed to
					// one PHYSICAL side. Rotating the goal by a second 90 degrees
					// here would cancel that commitment and resume the original
					// left/right ping-pong around posts or house corners.
					if (hasWallContourCommit && Time.time < wallContourUntil)
						planarDirection = ApplyTerrainAwareSteering(requestedDirection, isUnderwater);
					else
					{
						Vector3 recoveryDirection = Quaternion.AngleAxis(recoverySteeringAngle, Vector3.up) * requestedDirection;
						planarDirection = IsObstacleBlocking(recoveryDirection)
							? ApplyTerrainAwareSteering(requestedDirection, isUnderwater)
							: ApplyTerrainAwareSteering(recoveryDirection, isUnderwater);
					}
				}
				else
				{
					planarDirection = ApplyTerrainAwareSteering(requestedDirection, isUnderwater);
				}
			}

			// Locomotion and gaze are DIFFERENT controls. Previously an NPC
			// retreating backwards rotated its whole skeleton 180 degrees
			// away from the visible zombie, while melee still fired a punch.
			// Keep real CharacterController movement away from danger but
			// continue watching the personally seen, selected attacker.
			if (visualPerception == null)
				visualPerception = GetComponent<MobileNPCHumanoidPerception>();
			if (meleeCombat == null)
				meleeCombat = GetComponent<MobileNPCHumanoidCombat>();
			Zombie combatZombie = visualPerception != null
				? visualPerception.VisibleZombie : null;
			bool hasCurrentZombieSight = combatZombie != null
				&& !combatZombie.isDead && brainDriver.Target != null
				&& (brainDriver.Target == combatZombie.transform
					|| brainDriver.Target.IsChildOf(combatZombie.transform));
			Vector3 toCombatZombie = Vector3.zero;
			float zombieDistance = float.PositiveInfinity;
			if (hasCurrentZombieSight)
			{
				toCombatZombie = combatZombie.transform.position - transform.position;
				toCombatZombie.y = 0f;
				zombieDistance = toCombatZombie.magnitude;
			}
			bool gunCanDefend = rangedCombat != null
				&& rangedCombat.SelectedGunAsset != null
				&& rangedCombat.RoundsRemaining > 0;
			if (hasCurrentZombieSight && !gunCanDefend
				&& meleeCombat != null && zombieDistance > 0.1f
				&& !isUnderwater && !isPlannedJumping && !IsTraversingLadder
				&& brainDriver.CurrentGoal == MobileNPCTactics.Goal.None)
			{
				float meleeReach = meleeCombat.ZombieMeleeReachServer;
				if (zombieDistance <= meleeReach + 0.18f)
				{
					// Stop advancing into the zombie during an imminent punch.
					// When crowded between attacks, make a SHORT physical
					// backward step while still facing it. No teleports or
					// blind escape into a wall/another known zombie.
					// Do not freeze in a zombie crowd just to swing a knife.
					// In Combat Only, continue the verified escape/steering
					// direction; a melee hit is merely an emergency action.
					if (!MobileNPCSettings.CombatOnly)
						planarDirection = Vector3.zero;
					if (zombieDistance < Mathf.Min(1.3f, meleeReach * 0.83f)
						&& (MobileNPCSettings.CombatOnly || !meleeCombat.IsAttackReady))
					{
						Vector3 away = -toCombatZombie / zombieDistance;
						Vector3 backstep = transform.position + away * 1.05f;
						if (CanSafelyShortcutRouteCorner(backstep)
							&& (zombieRisk == null
								|| zombieRisk.GetKnownRiskAtServer(backstep)
								<= zombieRisk.GetKnownRiskAtServer(transform.position) + 0.05f))
							planarDirection = away;
					}
				}
			}

			Vector3 facingDirection = planarDirection;
			bool faceZombieInsteadOfTravel = hasCurrentZombieSight
				&& zombieDistance > 0.1f
				&& zombieDistance <= (gunCanDefend ? 3.0f : 5.0f);
			if (faceZombieInsteadOfTravel)
				facingDirection = toCombatZombie;
			// Face a directly visible, selected hostile even between gun bursts
			// and during melee cooldown. Previously this only applied while
			// holdDeliberateFire, allowing path steering or a sound clue to
			// turn the whole body away from an actively engaged player.
			bool faceVisibleSelectedHostile = targetIsVisible
				&& brainDriver.Target != null
				&& brainDriver.CurrentState == MobileNPCBrain.State.Pursuing;
			if (faceVisibleSelectedHostile)
			{
				facingDirection = brainDriver.Target.position - transform.position;
				facingDirection.y = 0f;
			}
			if (!faceVisibleSelectedHostile && brainDriver.IsListening)
			{
				// Face the uncertain sound bearing, not the player's hidden live
				// position. Rotation is deliberately slower than combat aiming.
				facingDirection = brainDriver.ListeningClue - transform.position;
				facingDirection.y = 0f;
			}
			if (facingDirection.sqrMagnitude > 0.0001f)
			{
				Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
				float steeringSpeed = brainDriver.IsListening ? turnSpeed * 0.35f
					: faceZombieInsteadOfTravel || faceVisibleSelectedHostile ? Mathf.Max(220f, turnSpeed)
					: holdDeliberateFire ? Mathf.Min(110f, turnSpeed * 0.4f) : turnSpeed;
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
				Flanking = brainDriver.CurrentGoal == MobileNPCTactics.Goal.Flank,
				NearTarget = brainDriver.Target != null
					&& (brainDriver.Target.position - transform.position).sqrMagnitude < 100f,
				DestinationDistance = hasDestination ? Vector3.Distance(destination, transform.position) : 0f
			};
			MobileNPCLocomotionPolicy.Gait gait = locomotionPolicy.Decide(context, Time.time);
			// The combat visibility flag intentionally excludes peaceful players,
			// but they still provide positive visual evidence. A footstep is not
			// a reason to duck when that SAME nonhostile player is plainly visible.
			// Preserve deliberate stalking/cover against a DIFFERENT real enemy.
			// The runtime template adds Perception after Motor. Resolve once
			// after activation rather than GetComponent on every physics tick.
			if (visualPerception == null)
				visualPerception = GetComponent<MobileNPCHumanoidPerception>();
			bool seesNonThreateningPlayer = visualPerception != null
				&& visualPerception.VisiblePlayer != null
				&& brainDriver.Target == null;
			// Audio-only uncertainty is weaker than positive eye contact.
			// Do not remain physically crouched while an active combat target
			// is confirmed visible just because a prior footstep suggested
			// listening or stalking. Cover itself remains a tactical goal.
			// Gunfire sounds, listening and a Sneak gait are information or
			// movement decisions, NOT permission to crouch. Crouching without
			// geometrical necessity exposes the NPC in an incoherent stationary
			// pose, especially when shots are merely passing nearby.
			// Keep the real crouched capsule only for a validated low opening.
			UpdateCrouchStance(!isPlannedJumping && !IsTraversingLadder
				&& !isUnderwater && verifiedLowOpening);
			// Retreat and cover dashes are survival actions: never reduce
			// them to walking because the optional pursuit-running toggle is off.
			bool urgentDefensiveSprint = brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat
				|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.SeekCover;
			float speed = gait == MobileNPCLocomotionPolicy.Gait.Sprint
				&& (runWhenPursuing || urgentDefensiveSprint)
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
				// Entering water aborts the ground-only ballistic action.
				// Swimming remains owned by the existing native water physics.
				if (isPlannedJumping)
				{
					isPlannedJumping = false;
					plannedJumpLandsOnObjectTop = false;
					plannedJumpSupportCollider = null;
					suppressJourneyUntil = Time.time + 0.3f;
				}
				ClearVerifiedFurnitureSupport();
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
				velocity.y += Physics.gravity.y * Mathf.Clamp(gravityMultiplier, 0.5f, 2f) * deltaTime * 3.0f;
				velocity.y = Mathf.Max(-100.0f, velocity.y);
				motion.y = velocity.y;

				// Off-mesh low obstacle jump is a deliberately verified ACTION,
				// not a random vertical impulse every time steering sees a wall.
				// The route goal remains active and the CharacterController
				// executes the actual gravity-driven trajectory.
				if (isPlannedJumping && (!hasDestination
					|| (destination - plannedJumpDestinationSnapshot).sqrMagnitude > 4f
					|| Time.time - plannedJumpStartedAt > plannedJumpMaximumDuration))
				{
					isPlannedJumping = false;
					plannedJumpLandsOnObjectTop = false;
					plannedJumpSupportCollider = null;
					suppressJourneyUntil = Time.time + 0.3f;
				}
				if (isPlannedJumping && groundedBeforeMove
					&& Time.time - plannedJumpStartedAt >= 0.16f)
				{
					bool actuallyLanded = Vector3.Distance(transform.position,
						plannedJumpLanding) < 0.65f;
					// The predicted top is NOT valid until the capsule
					// actually touches the SAME physical supporting collider.
					bool expectedObjectTop = plannedJumpLandsOnObjectTop;
					bool verifiedTopLanding = actuallyLanded
						&& expectedObjectTop
						&& TryVerifyObjectSupportAtCurrentPosition(plannedJumpSupportCollider);
					if (verifiedTopLanding)
					{
						verifiedFurnitureSupportCollider = plannedJumpSupportCollider;
						verifiedFurnitureSupportY = transform.position.y;
						verifiedFurnitureSupportValidUntil = Time.time + 0.75f;
						nextVerifiedFurnitureSupportCheckTime = Time.time + 0.25f;
					}
					if (verifiedTopLanding
						&& Time.time >= nextRaisedObjectLandingDiagnosticTime)
					{
						// Report actual CharacterController ground contact,
						// not merely an AI intent to climb. Rate-limited.
						nextRaisedObjectLandingDiagnosticTime = Time.time + 12f;
						Debug.Log("[MobileNPCHumanoidMotor] Physical landing on an "
							+ "observed climbable object at " + transform.position
							+ "; NPC goal=" + destination + ".", this);
					}
					plannedJumpLandsOnObjectTop = false;
					plannedJumpSupportCollider = null;
					isPlannedJumping = false;
					suppressJourneyUntil = Time.time + 0.3f;
					if (!actuallyLanded || (expectedObjectTop && !verifiedTopLanding))
					{
						RememberLocalNavigationDeadEnd(transform.position, destination);
						noJumpUntil = Time.time + 6f;
					}
				}
				// Vault and ordinary hops are both physical, gravity-driven jumps.
				// The old TryStartServer-based animation traversal no longer exists:
				// the one costed ground/door/ladder arbitration below selects a
				// direction, then TryBeginPlannedLowJump / TryBeginVaultJump
				// validate the actual capsule trajectory and landing.
				Vector3 chosenJumpDirection = Vector3.zero;
				if (!isPlannedJumping && !IsTraversingLadder
					&& hasDestination && groundedBeforeMove
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
					&& brainDriver.CurrentGoal != MobileNPCTactics.Goal.SeekCover
					&& brainDriver.CurrentGoal != MobileNPCTactics.Goal.Retreat
					&& brainDriver.CurrentGoal != MobileNPCTactics.Goal.Stalk
					&& ShouldChooseObstacleJumpServer(destination,
						waitingForDoorToOpen, waitingForLearnedAccess,
						waitingForLadderApproach, out chosenJumpDirection)
					&& (TryBeginPlannedLowJump(chosenJumpDirection, destination)
						|| TryBeginVaultJump(chosenJumpDirection, destination)))
				{
					wantedToMove = true;
				}
				if (isPlannedJumping)
				{
					// Both a normal hop and a higher wall jump use the same
					// native gravity and controller collision. No floating
					// vertical "clamber"; the higher jump briefly delays
					// forward motion until its feet clear the wall top.
					float elapsed = Time.time - plannedJumpStartedAt;
					float traveled = Vector3.Dot(transform.position -
						plannedJumpStartPosition, plannedJumpDirection);
					float horizontalSpeed = elapsed >= plannedJumpHorizontalDelay
						&& traveled < plannedJumpForwardDistance - 0.07f
						? plannedJumpActualHorizontalSpeed : 0f;
					planarDirection = plannedJumpDirection;
					motion.x = plannedJumpDirection.x * horizontalSpeed;
					motion.z = plannedJumpDirection.z * horizontalSpeed;
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
			// The native non-pathfinding zombie uses a consistent downward
			// velocity (Physics.gravity.y * 2) even while walking. Our older
			// motor applied only one tiny gravity increment after resetting
			// grounded velocity, so the controller could briefly lose curb/
			// sidewalk floor contact and report a false navigation stall.
			// Snap ONLY ordinary grounded locomotion; never touch swimming,
			// airborne vault/jump arcs or deliberate ladder vertical motion.
			if (!isUnderwater && groundedBeforeMove && !isPlannedJumping
				&& !ladderMovingThisFrame && motion.y <= 0f)
				motion.y = Mathf.Min(motion.y, Physics.gravity.y * 2f);
			Vector3 positionBeforeMovement = transform.position;
			CollisionFlags collision = characterController.Move(motion * deltaTime);
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
				plannedJumpLandsOnObjectTop = false;
				plannedJumpSupportCollider = null;
				suppressJourneyUntil = Time.time + 0.3f;
				RememberLocalNavigationDeadEnd(transform.position, destination);
				noJumpUntil = Time.time + 6f; // prefer physical bypass after failed jump
			}
			IsMoving = wantedToMove && planarDisplacement.sqrMagnitude > 0.000001f;
			// Noise is based on actual movement, not merely the desired AI gait.
			// Crouching suppresses the zombie-alert call; running is louder than walking.
			// Native zombie perception remains responsible for deciding investigation.
			if (IsMoving && !isUnderwater && !isPlannedJumping
				&& !ladderMovingThisFrame
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
				&& wantedToMove, deltaTime);
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
		/// <summary>
		/// Replace only speculative walking waypoints after a physically
		/// authorized jump, without discrediting genuine past journey records.
		/// </summary>
		private void ClearWalkingPlanForVerifiedJump(Vector3 finalGoal)
		{
			routeWaypoints.Clear();
			routeWaypointIndex = 0;
			monitoringHybridPath = false;
			hybridPathMonitor.Clear();
			hasDetourWaypoint = false;
			detourGoalSnapshot = finalGoal;
			hasPendingKnownAccessPath = false;
			pendingKnownAccessEntryRouteId = -1;
			hasPlannedLadderApproach = false;
			activeRememberedRouteIndex = -1;
			activeContinuationRouteIndex = -1;
			learnedStairStartWaypointIndex = -1;
			learnedContinuationStartWaypointIndex = -1;
			activeAccessRouteIds.Clear();
			activeAccessWaypointStarts.Clear();
			activeAccessFinalWaypointIndex = -1;
			trackingActualJourney = false;
			actualTravelFootsteps.Clear();
			nextDetourAttemptTime = Time.time + 0.5f;
		}

		/// <summary>
		/// Costed choice between an existing GROUND route, a genuine unlocked
		/// door, local side exploration, and a possible physical jump.
		/// Called only for eligible grounded NPCs with a stable actual brain
		/// destination. It never bypasses a door, grants unknown walkable
		/// floor edges, or commits to an unverified jump.
		/// </summary>
		private bool TryChooseVerifiedFurnitureRunupServer(Vector3 goal,
			out Vector3 direction)
		{
			direction = Vector3.zero;
			if (routeWaypointIndex < routeWaypoints.Count || hasDetourWaypoint
				|| isPlannedJumping || IsTraversingLadder
				|| Time.time < nextDetourAttemptTime
				|| PlanarDistanceSquared(goal, transform.position)
					> stoppingDistance * stoppingDistance
				|| !IsObservedElevatedObjectGoalServer(goal))
				return false;

			Vector3 retreat = transform.position - goal;
			retreat.y = 0f;
			if (retreat.sqrMagnitude < 0.01f)
			{
				// At exactly the goal XZ, the player-style jump still
				// needs some horizontal clearance. A real physically
				// unblocked backwards approach is the only fallback.
				retreat = -transform.forward;
				retreat.y = 0f;
			}
			if (retreat.sqrMagnitude < 0.04f)
				return false;
			retreat.Normalize();
			Vector3 chest = transform.position + Vector3.up
				* Mathf.Max(0.45f, characterController.height * 0.55f);
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
				Mathf.Max(0.05f, characterController.radius * 0.9f));
			const float approachDistance = 1.8f;
			for (int i = 0; i < 3; ++i)
			{
				float degrees = i == 0 ? 0f : i == 1 ? -45f : 45f;
				Vector3 candidateDirection = Quaternion.AngleAxis(
					degrees, Vector3.up) * retreat;
				if (IsObstacleBlockingAlong(chest, radius,
					candidateDirection, approachDistance))
					continue;

				Vector3 candidate = transform.position
					+ candidateDirection * approachDistance;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
					candidate, 1.2f, 1.3f, roamingGroundMask, roamingMaximumSlope,
					out Vector3 ground, out Collider floorCollider, out _)
					|| IsFurnitureSurfaceCollider(floorCollider)
					|| Mathf.Abs(ground.y - transform.position.y)
					> characterController.stepOffset + 0.05f
					|| IsRouteNodeOccupied(ground, radius)
					|| !HasContinuousLocalFloor(transform.position, ground))
					continue;

				detourWaypoint = ground;
				detourGoalSnapshot = goal;
				hasDetourWaypoint = true;
				nextDetourAttemptTime = Time.time + 1.3f;
				direction = candidateDirection;
				return true;
			}
			nextDetourAttemptTime = Time.time + 1.5f;
			return false;
		}

		/// <summary>
		/// A tactical flank on a low object has a SMALLER distance than a
		/// normal 3m flank. The brain may accept it ONLY if the motor itself
		/// recently computed a complete physically verified Unturned jump.
		/// Candidate validity expires; it is not an omniscient furniture map.
		/// </summary>
		public bool HasValidatedRecentElevatedTacticalGoalServer(Vector3 candidate)
		{
			return Provider.isServer && hasInitialized
				&& Time.time < validatedElevatedTacticUntil
				&& (candidate - lastPhysicallyValidatedElevatedTacticGoal)
					.sqrMagnitude < 0.16f;
		}

		/// <summary>
		/// Propose a nearby REAL furniture-top firing position for a
		/// visually confirmed combat target, only after ordinary ground
		/// flanks were tried by the combat behavior. Never moves the
		/// NPC, changes tactics, or consumes an inventory item itself.
		/// At most one FULL jump preflight, global cooldown > 0.9sec.
		/// </summary>
		public bool TryFindObservedElevatedFiringPositionServer(Vector3 threatPosition,
			out Vector3 raisedPosition)
		{
			raisedPosition = Vector3.zero;
			validatedElevatedTacticUntil = 0f;
			if (!Provider.isServer || !hasInitialized
				|| brainDriver == null || brainDriver.Target == null
				|| !targetIsVisible || characterController == null
				|| !characterController.enabled || vaultTraversal == null
				|| isPlannedJumping || IsTraversingLadder || physicallyCrouched
				|| Time.time < nextElevatedTacticProbeTime
				|| Time.time < nextSharedElevatedTacticProbeTime
				|| injurySystem != null && injurySystem.MovementMultiplier < 0.95f
				|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat
				|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.SeekCover
				|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.Stalk
				|| !IsGroundedUsingPlayerProbe())
				return false;

			Vector3 toward = threatPosition - transform.position;
			toward.y = 0f;
			float distanceToThreat = toward.magnitude;
			if (distanceToThreat < 7f || distanceToThreat > 24f)
				return false;

			nextElevatedTacticProbeTime = Time.time + 4f;
			nextSharedElevatedTacticProbeTime = Time.time + 0.95f;
			Vector3 direction = toward / distanceToThreat;
			float bestScore = float.NegativeInfinity;
			Vector3 observedGoal = Vector3.zero;
			Vector3 observedDirection = Vector3.zero;
			// Three nearby candidates, NOT a map-wide search, and only
			// one expensive ballistic preflight after geometric filtering.
			for (int i = 0; i < 3; ++i)
			{
				float angle = i == 0 ? 0f : i == 1 ? -35f : 35f;
				Vector3 testDirection = Quaternion.AngleAxis(angle, Vector3.up)
					* direction;
				Vector3 probe = transform.position + Vector3.up * 0.28f;
				if (!Physics.Raycast(probe, testDirection, out RaycastHit side,
					0.95f, obstacleMask, QueryTriggerInteraction.Ignore)
					|| side.collider == null || IsOwnCollider(side.collider)
					|| side.collider.GetComponentInParent<InteractableDoor>() != null
					|| side.collider.GetComponentInParent<InteractableVehicle>() != null
					|| side.collider.GetComponentInParent<Player>() != null
					|| side.collider.GetComponentInParent<Zombie>() != null
					|| side.collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null
					|| side.collider.attachedRigidbody != null
						&& !side.collider.attachedRigidbody.isKinematic)
					continue;

				Vector3 above = side.point + testDirection * 0.10f
					+ Vector3.up * 1.65f;
				if (!Physics.Raycast(above, Vector3.down, out RaycastHit top,
					1.9f, obstacleMask, QueryTriggerInteraction.Ignore)
					|| !MobileNPCVaultTraversal.IsSamePhysicalObstacle(
						top.collider, side.collider)
					|| top.normal.y < 0.90f)
					continue;
				float height = top.point.y - transform.position.y;
				if (height < 0.42f || height > 1.02f)
					continue;
				Vector3 expected = top.point + Vector3.up * 0.05f;
				// Before calling expensive ballistic physics, check that
				// higher ground would actually unblock a firing line.
				Vector3 futureEye = expected + Vector3.up * 1.45f;
				Vector3 enemyEye = threatPosition + Vector3.up * 1.0f;
				Vector3 toEnemy = enemyEye - futureEye;
				float length = toEnemy.magnitude;
				if (length < 4f || length > 28f)
					continue;
				int hits = Physics.RaycastNonAlloc(futureEye,
					toEnemy / length, ObstacleHitBuffer, length,
					obstacleMask, QueryTriggerInteraction.Ignore);
				if (hits >= ObstacleHitBuffer.Length)
					continue;
				bool geometryOccluded = false;
				for (int h = 0; h < hits; ++h)
				{
					Collider hit = ObstacleHitBuffer[h].collider;
					if (hit == null || IsOwnCollider(hit)
						|| hit.GetComponentInParent<Player>() != null
						|| hit.GetComponentInParent<Zombie>() != null
						|| hit.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
						continue;
					if (ObstacleHitBuffer[h].distance < length - 0.65f)
					{
						geometryOccluded = true;
						break;
					}
				}
				if (geometryOccluded)
					continue;
				float score = height
					- Mathf.Abs(angle) * 0.006f
					- Vector3.Distance(transform.position, expected) * 0.1f;
				if (score > bestScore)
				{
					bestScore = score;
					observedGoal = expected;
					observedDirection = testDirection;
				}
			}
			if (bestScore == float.NegativeInfinity)
				return false;
			float acceleration = Mathf.Abs(Physics.gravity.y)
				* Mathf.Clamp(gravityMultiplier, 0.5f, 2f) * 3f;
			if (!vaultTraversal.TryPlanJumpServer(observedGoal,
				observedDirection, roamingGroundMask, roamingMaximumSlope,
				obstacleMask, acceleration, out MobileNPCVaultTraversal.JumpPlan plan)
				|| !plan.LandingOnObjectTop
				|| plan.LandingSupportCollider == null
				|| (plan.Landing - transform.position).sqrMagnitude < 0.75f
				|| !Level.checkSafeIncludingClipVolumes(plan.Landing))
				return false;

			raisedPosition = plan.Landing;
			lastPhysicallyValidatedElevatedTacticGoal = raisedPosition;
			validatedElevatedTacticUntil = Time.time + 0.95f;
			return true;
		}

		/// <summary>
		/// After a FAILED real pickup from the ground, suggest only a
		/// nearby observed, low STATIC item-support top as a possible
		/// landing goal. Pickup still happens separately through the
		/// authoritative ItemManager, and the jump STILL needs the native
		/// physical ballistic preflight before the actor can move.
		/// </summary>
		public bool TryFindObservedElevatedLootFootingServer(Vector3 itemPosition,
			out Vector3 proposedFooting)
		{
			proposedFooting = Vector3.zero;
			if (!Provider.isServer || !hasInitialized || characterController == null
				|| !characterController.enabled || vaultTraversal == null
				|| isPlannedJumping || IsTraversingLadder
				|| injurySystem != null && injurySystem.MovementMultiplier < 0.95f
				|| brainDriver == null || brainDriver.Target != null
				|| brainDriver.CurrentGoal != MobileNPCTactics.Goal.Loot
				|| PlanarDistanceSquared(itemPosition, transform.position) > 12.25f
				|| itemPosition.y - transform.position.y < 0.42f
				|| itemPosition.y - transform.position.y > 1.40f
				|| !Level.checkSafeIncludingClipVolumes(itemPosition))
				return false;

			Vector3 rayStart = itemPosition + Vector3.up * 0.28f;
			if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit top,
				0.82f, obstacleMask, QueryTriggerInteraction.Ignore)
				|| top.collider == null || IsOwnCollider(top.collider)
				|| top.collider.GetComponentInParent<Player>() != null
				|| top.collider.GetComponentInParent<Zombie>() != null
				|| top.collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null
				|| top.collider.GetComponentInParent<InteractableVehicle>() != null
				|| top.collider.GetComponentInParent<InteractableDoor>() != null
				|| top.collider.attachedRigidbody != null
					&& !top.collider.attachedRigidbody.isKinematic
				|| top.normal.y < 0.90f)
				return false;
			Vector3 footing = top.point + Vector3.up * 0.05f;
			float climbHeight = footing.y - transform.position.y;
			if (climbHeight < 0.42f || climbHeight > 1.07f
				|| Vector3.Distance(footing, itemPosition) > 0.60f
				|| !Level.checkSafeIncludingClipVolumes(footing)
				|| IsRouteNodeOccupied(footing,
					Mathf.Clamp(obstacleProbeRadius, 0.05f,
					characterController.radius * 0.9f)))
				return false;

			// A guessed shelf cannot authorize a climb. This merely
			// proposes a precise top for the normal motor obstacle
			// comparison and physics-only vault checks.
			proposedFooting = footing;
			return true;
		}

		private bool HasPhysicallyReachedNavigationGoal(Vector3 goal)
		{
			if (PlanarDistanceSquared(transform.position, goal)
				> stoppingDistance * stoppingDistance
				|| Mathf.Abs(goal.y - transform.position.y)
				> Mathf.Max(0.45f, characterController.stepOffset + 0.2f))
				return false;

			// Do not report item/cover reached from the GROUND just
			// because the same XZ is beneath an elevated crate top.
			// Only the observed actual XYZ/contact can complete it.
			if (goal.y - transform.position.y > 0.35f
				&& IsObservedElevatedObjectGoalServer(goal))
				return false;
			return true;
		}

		/// <summary>
		/// An elevated brain destination is a candidate for an off-mesh
		/// furniture jump ONLY when the requested landing is nearby and
		/// directly verified as a real horizontal static surface.
		/// Does not publish that surface as ground to the ordinary A*.
		/// </summary>
		private bool IsObservedElevatedObjectGoalServer(Vector3 goal)
		{
			if (!Provider.isServer || characterController == null
				|| !characterController.enabled
				|| PlanarDistanceSquared(transform.position, goal) > 12.25f
				|| goal.y - transform.position.y < 0.40f
				|| goal.y - transform.position.y > 1.10f)
				return false;

			Vector3 source = goal + Vector3.up * 0.32f;
			if (!Physics.Raycast(source, Vector3.down, out RaycastHit surface,
				0.68f, obstacleMask, QueryTriggerInteraction.Ignore))
				return false;
			Collider hit = surface.collider;
			if (hit == null || IsOwnCollider(hit)
				|| (hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic)
				|| hit.GetComponentInParent<Player>() != null
				|| hit.GetComponentInParent<Zombie>() != null
				|| hit.GetComponentInParent<MobileNPCHumanoidVitals>() != null
				|| hit.GetComponentInParent<InteractableVehicle>() != null
				|| hit.GetComponentInParent<InteractableDoor>() != null
				|| Vector3.Dot(surface.normal, Vector3.up) < 0.90f)
				return false;

			return Mathf.Abs(surface.point.y + 0.05f - goal.y) <= 0.18f;
		}

		private bool ShouldChooseObstacleJumpServer(Vector3 finalGoal,
			bool waitingForDoor, bool waitingForLearnedRoute,
			bool waitingForLadder, out Vector3 jumpDirection)
		{
			jumpDirection = Vector3.zero;
			if (!Provider.isServer || characterController == null
				|| !characterController.enabled || isPlannedJumping
				|| IsTraversingLadder || hasPlannedLadderApproach
				// A physically verified threshold route is already selected:
				// do not replace its grounded door/step crossing with a jump.
				|| hasPhysicalThresholdRoute
				|| waitingForDoor || waitingForLearnedRoute || waitingForLadder
				|| Time.time < noJumpUntil
				|| Time.time < nextJumpTime)
				return false;

			float goalHeight = finalGoal.y - transform.position.y;
			bool goalOnVerifiedObject = goalHeight > 0.35f
				&& IsObservedElevatedObjectGoalServer(finalGoal);
			if ((!goalOnVerifiedObject && Mathf.Abs(goalHeight) > 0.55f)
				|| goalHeight > 1.10f)
				return false;
			Vector3 direct = finalGoal - transform.position;
			direct.y = 0f;
			float remaining = direct.magnitude;
			if (remaining < (goalOnVerifiedObject ? 0.85f : 2.0f))
				return false;
			jumpDirection = direct / remaining;

			// Only a currently observed STATIC obstacle near the NPC can be
			// a jump candidate; never initiate a leap merely because a long
			// tactical detour appears in the planning graph.
			int hits = Physics.SphereCastNonAlloc(
				transform.position + Vector3.up * 0.32f, 0.12f,
				jumpDirection, ObstacleHitBuffer,
				Mathf.Min(1.0f, remaining), obstacleMask,
				QueryTriggerInteraction.Ignore);
			if (hits == 0 || hits >= ObstacleHitBuffer.Length)
				return false;
			bool hasStaticBlocker = false;
			for (int i = 0; i < hits; ++i)
			{
				Collider col = ObstacleHitBuffer[i].collider;
				if (col == null || IsOwnCollider(col))
					continue;
				if ((col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic)
					|| col.GetComponentInParent<Player>() != null
					|| col.GetComponentInParent<Zombie>() != null
					|| col.GetComponentInParent<MobileNPCHumanoidVitals>() != null
					|| col.GetComponentInParent<InteractableDoor>() != null
					|| col.GetComponentInParent<InteractableVehicle>() != null)
					return false;
				hasStaticBlocker = true;
			}
			if (!hasStaticBlocker)
				return false;

			// Native unlocked doors are interactions, NEVER obstacle jumps.
			Vector3 chest = transform.position + Vector3.up
				* Mathf.Max(0.45f, characterController.height * 0.55f);
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
				Mathf.Max(0.05f, characterController.radius * 0.9f));
			if (doorNavigation != null
				&& doorNavigation.IsTraversableUnlockedDoorEdgeServer(chest,
					jumpDirection, radius,
					Mathf.Min(1.3f, remaining), obstacleMask))
				return false;

			if (PlanarDistanceSquared(lastObservedObstacleGoal, finalGoal) > 4f
				|| Mathf.Abs(lastObservedObstacleGoal.y - finalGoal.y) > 0.5f)
			{
				lastObservedObstacleGoal = finalGoal;
				firstObservedObstacleTime = Time.time;
				nextObstacleComparisonTime = 0f;
			}

			// A known/physically calculated route is an actual alternative.
			// Prefer it if it adds only modest walking distance. A path that
			// ends at a partial frontier is NOT proof of reaching the goal;
			// try it first, and reconsider only if movement repeatedly fails.
			if (routeWaypointIndex < routeWaypoints.Count)
			{
				float walkingDistance = 0f;
				Vector3 previous = transform.position;
				for (int i = routeWaypointIndex; i < routeWaypoints.Count; ++i)
				{
					walkingDistance += Vector3.Distance(previous, routeWaypoints[i]);
					previous = routeWaypoints[i];
				}
				float unfinished = Vector3.Distance(previous, finalGoal);
				bool reachesGoal = unfinished <= 1.35f
					&& Mathf.Abs(previous.y - finalGoal.y) <= 0.65f;
				// A verified ground/doorway route remains authoritative
				// while the controller is making progress. Distance alone
				// must never force a jump through a window just because the
				// available doorway goes around a building.
				if (consecutiveStuckRecoveries < 2)
					return false;
				if (reachesGoal && (walkingDistance <= remaining + 3.0f
					|| walkingDistance <= remaining * 1.55f))
					return false;
			}
			else if (hasDetourWaypoint && consecutiveStuckRecoveries < 2)
			{
				return false; // let a chosen physical side route progress
			}
			else if (consecutiveStuckRecoveries == 0
				&& Time.time - firstObservedObstacleTime < 1.0f)
			{
				return false; // allow normal route and door discovery first
			}

			// Prefer physically verified ground access, including a longer
			// route through a discovered doorway, over a speculative vault.
			// Only request this when no active walking route exists and a
			// static obstruction has already been observed. Keep the
			// existing shared/per-NPC budgets to protect slower machines.
			if (routeWaypointIndex >= routeWaypoints.Count && !hasDetourWaypoint
				&& PlanarDistanceSquared(transform.position, finalGoal) <= 324f
				&& Time.time >= nextObstacleComparisonTime
				&& Time.time >= nextSharedObstacleComparisonTime)
			{
				nextObstacleComparisonTime = Time.time + 2.5f;
				nextSharedObstacleComparisonTime = Time.time + 0.22f;
				obstacleWalkComparisonScratch.Clear();
				bool exactGroundRoute = independentRouteProvider != null
					&& independentRouteProvider.IsAvailable
					&& independentRouteProvider.TryGetRoute(
						transform.position, finalGoal, obstacleWalkComparisonScratch);
				// The small, exact local route remains a fallback when the
				// independent planner is throttled or cannot reach the goal.
				if (!exactGroundRoute
					&& PlanarDistanceSquared(transform.position, finalGoal) <= 25f)
				{
					obstacleWalkComparisonScratch.Clear();
					exactGroundRoute = TryBuildLocalRoute(
						finalGoal, obstacleWalkComparisonScratch, true);
				}
				if (exactGroundRoute && obstacleWalkComparisonScratch.Count >= 2
					&& Vector3.Distance(
						obstacleWalkComparisonScratch[
							obstacleWalkComparisonScratch.Count - 1], finalGoal)
						<= 1.0f)
				{
					float distance = 0f;
					Vector3 previous = transform.position;
					for (int i = 0; i < obstacleWalkComparisonScratch.Count; ++i)
					{
						distance += Vector3.Distance(previous,
							obstacleWalkComparisonScratch[i]);
						previous = obstacleWalkComparisonScratch[i];
					}
					// A doorway route can be longer than a straight line
					// through a wall. Do not jump solely to save a few metres.
					if (distance <= remaining + 6.0f
						|| distance <= remaining * 1.8f)
					{
						// This route was validated by the independent physics A*
						// (or the exact local fallback). Never use a frontier
						// or a visual window opening as a speculative shortcut.
						routeWaypoints.Clear();
						routeWaypoints.AddRange(obstacleWalkComparisonScratch);
						routeWaypointIndex = 0;
						detourGoalSnapshot = finalGoal;
						hasDetourWaypoint = false;
						nextPlannedRouteValidationTime = 0f;
						return false;
					}
				}
			}

			// A jump is a LAST resort. If physical progress has not
			// failed, give the doorway/contour observer time to find
			// a floor-level entrance before testing the raised sill.
			// Stalled agents may bypass this wait for safety.
			if (consecutiveStuckRecoveries < 2
				&& Time.time - firstObservedObstacleTime < 2.5f)
				return false;

			// This only authorizes a feasibility test. The low-hop/parapet
			// planners STILL require a genuine, collision-free ballistic arc
			// and a walkable same-floor landing before launching.
			return true;
		}

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
				* Mathf.Clamp(gravityMultiplier, 0.5f, 2f) * 3f;
			if (acceleration < 0.01f)
				return false;
			// Match the actual native PlayerMovement jump impulse, not an
			// arbitrary value solved from horizontal travel time. The old
			// formula often produced <7 and rejected a viable normal jump.
			const float impulse = 7.0f;
			float heightChange = landing.y - transform.position.y;
			float discriminant = impulse * impulse
				- 2f * acceleration * heightChange;
			if (discriminant <= 0f)
				return false;
			float duration = (impulse + Mathf.Sqrt(discriminant)) / acceleration;
			if (travel / Mathf.Max(0.1f, plannedJumpHorizontalSpeed) >
				duration - 0.05f)
				return false;
			float apex = impulse * impulse / (2f * acceleration);
			if (apex < plannedJumpMaximumObstacleHeight + 0.10f)
				return false;
			// Perform limited swept full-body samples along the actual
			// parabolic flight; horizontal travel stops at the real landing
			// X/Z instead of the NPC continuing to sprint past it in midair.
			Vector3 previous = transform.position;
			for (int step = 1; step <= 16; ++step)
			{
				float t = duration * step / 17f;
				float along = Mathf.Min(travel, plannedJumpHorizontalSpeed * t);
				Vector3 next = transform.position + direction * along
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
			plannedJumpStartPosition = transform.position;
			plannedJumpActualHorizontalSpeed = plannedJumpHorizontalSpeed;
			plannedJumpHorizontalDelay = 0f;
			plannedJumpForwardDistance = travel;
			plannedJumpMaximumDuration = Mathf.Max(1.1f, duration + 0.25f);
			plannedJumpUsesVaultPlan = false;
			plannedJumpLandsOnObjectTop = false;
			plannedJumpSupportCollider = null;
			ClearVerifiedFurnitureSupport();
			plannedJumpStartedAt = Time.time;
			isPlannedJumping = true;
			velocity.y = impulse;
			nextJumpTime = Time.time + Mathf.Max(0.5f, jumpCooldown);
			ClearWalkingPlanForVerifiedJump(currentGoal);
			suppressJourneyUntil = Time.time + duration + 0.3f;
			return true;
		}

		/// <summary>
		/// Physics-only Unturned-style higher jump over a VERIFIED low parapet.
		/// No dedicated Vault animation, script-driven ascent, or teleport.
		/// Another jump can occur only after real ground contact and cooldown.
		/// </summary>
		private bool TryBeginVaultJump(Vector3 desired, Vector3 currentGoal)
		{
			if (vaultTraversal == null || physicallyCrouched || isPlannedJumping
				|| Time.time < nextJumpTime || Physics.gravity.y >= -1f)
				return false;
			float acceleration = Mathf.Abs(Physics.gravity.y)
				* Mathf.Clamp(gravityMultiplier, 0.5f, 2f) * 3f;
			if (!vaultTraversal.TryPlanJumpServer(currentGoal, desired,
				roamingGroundMask, roamingMaximumSlope, obstacleMask,
				acceleration, out MobileNPCVaultTraversal.JumpPlan plan))
				return false;

			plannedJumpLanding = plan.Landing;
			plannedJumpDirection = plan.Direction;
			plannedJumpDestinationSnapshot = currentGoal;
			plannedJumpStartPosition = transform.position;
			plannedJumpStartedAt = Time.time;
			plannedJumpActualHorizontalSpeed = plan.HorizontalSpeed;
			plannedJumpHorizontalDelay = plan.ForwardDelay;
			plannedJumpForwardDistance = plan.TravelDistance;
			plannedJumpMaximumDuration = plan.MaximumDuration;
			plannedJumpUsesVaultPlan = true;
			plannedJumpLandsOnObjectTop = plan.LandingOnObjectTop;
			plannedJumpSupportCollider = plan.LandingSupportCollider;
			ClearVerifiedFurnitureSupport();
			isPlannedJumping = true;
			velocity.y = plan.Impulse;
			// Permit an obstacle-by-obstacle sequence of real jumps after a
			// verified landing, without airborne bunnyhops or a 2.5s forced
			// delay between two adjacent low parapets.
			nextJumpTime = Time.time + Mathf.Max(0.8f, plan.MaximumDuration + 0.15f);

			ClearWalkingPlanForVerifiedJump(currentGoal);
			suppressJourneyUntil = Time.time + plan.MaximumDuration + 0.3f;
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
				(RayMasks.BLOCK_COLLISION | RayMasks.GROUND2),
				QueryTriggerInteraction.Ignore);
			for (int i = 0; i < hits.Length; ++i)
			{
				Collider hitCollider = hits[i].collider;
				if (hitCollider != null && !IsOwnCollider(hitCollider)
					&& Vector3.Angle(hits[i].normal, Vector3.up)
						<= characterController.slopeLimit)
				{
					return true;
				}
			}

			// Some terrain colliders are reported by a ray but missed by the short
			// sphere cast when the capsule is within its skin-width tolerance. The
			// Player.log confirms that BLOCK_COLLISION can see the terrain, so use a
			// short center ray as a second grounded signal before applying gravity.
			RaycastHit[] groundHits = Physics.RaycastAll(transform.position + Vector3.up * 0.15f,
				Vector3.down, 0.30f, (RayMasks.BLOCK_COLLISION | RayMasks.GROUND2),
				QueryTriggerInteraction.Ignore);
			for (int i = 0; i < groundHits.Length; ++i)
			{
				Collider hitCollider = groundHits[i].collider;
				if (hitCollider != null && !IsOwnCollider(hitCollider)
					&& Vector3.Angle(groundHits[i].normal, Vector3.up)
						<= characterController.slopeLimit)
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
				|| IsTraversingLadder || isPlannedJumping
				|| float.IsNaN(transform.position.y) || float.IsInfinity(transform.position.y))
			{
				return;
			}

			// Walking DOWN Washington's terrain from a higher spawn is
			// ordinary movement, not a fall-through-world incident. The old
			// unconditional per-frame Physics.RaycastAll generated 207 false
			// FALL DIAGNOSTIC entries in the supplied Player(4).log while the
			// CharacterController explicitly reported isGrounded=True.
			if (characterController.isGrounded)
				return;

			// First probe close to the NPC. This detects a short collision failure before
			// gravity can accelerate into an unrecoverable fall. RaycastAll lets us ignore
			// the NPC's own CharacterController and visual hierarchy explicitly.
			float currentY = transform.position.y;
			if (currentY < spawnPosition.y - 1.5f
				&& !characterController.isGrounded
				&& Time.time >= nextFallingDiagnosticTime)
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
			plannedJumpSupportCollider = null;
			ClearVerifiedFurnitureSupport();
			suppressJourneyUntil = Time.time + 0.35f;
			ResetObservedEntranceSurvey();
			System.Array.Clear(recentNavDeadEndExpiry, 0, recentNavDeadEndExpiry.Length);
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
			// Emergency recovery must use the SAME collidable world layers
			// as PlayerMovement.checkGround. Nav-only helper colliders in
			// Player.log cannot become a fake surface to teleport onto.
			int supportMask = ((int)roamingGroundMask) & RayMasks.BLOCK_COLLISION;
			if (supportMask == 0) return false;
			RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, distance,
				supportMask, QueryTriggerInteraction.Ignore);
			float bestVerticalDifference = float.PositiveInfinity;
			bool found = false;
			for (int i = 0; i < hits.Length; ++i)
			{
				RaycastHit candidate = hits[i];
				if (candidate.collider == null || IsOwnCollider(candidate.collider)
					|| IsFurnitureSurfaceCollider(candidate.collider)
					|| candidate.collider.GetComponentInParent<InteractableVehicle>() != null
					|| Vector3.Angle(candidate.normal, Vector3.up) >
						Mathf.Min(55f, characterController.slopeLimit)
					|| !Level.checkSafeIncludingClipVolumes(candidate.point))
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
		private void RememberLocalNavigationDeadEnd(Vector3 blockedPosition,
			Vector3 goal, float memorySeconds = 18f)
		{
			if (!Provider.isServer || !Level.checkSafeIncludingClipVolumes(blockedPosition))
				return;
			recentNavDeadEndPositions[nextNavDeadEndSlot] = blockedPosition;
			recentNavDeadEndGoals[nextNavDeadEndSlot] = goal;
			recentNavDeadEndExpiry[nextNavDeadEndSlot] = Time.time
				+ Mathf.Clamp(memorySeconds, 2f, 38f);
			nextNavDeadEndSlot = (nextNavDeadEndSlot + 1) % MaxRecentNavDeadEnds;
		}

		private void ResetObservedEntranceSurvey()
		{
			observedEntranceSurveySide = 0;
			observedEntranceSurveyUntil = 0f;
			observedEntranceSurveyEpisodeUntil = 0f;
			nextObservedEntranceSurveyStartTime = 0f;
			observedEntranceSurveyWasBrainGoal = false;
		}

		private bool IsObservingEntranceSurvey(Vector3 goal, Vector3 position)
		{
			return observedEntranceSurveySide != 0
				&& observedEntranceSurveyWasBrainGoal == (brainDriver != null && brainDriver.HasDestination)
				&& Time.time < observedEntranceSurveyUntil
				&& Time.time < observedEntranceSurveyEpisodeUntil
				&& PlanarDistanceSquared(goal, observedEntranceSurveyGoal) <= 2.25f
				&& Mathf.Abs(goal.y - observedEntranceSurveyGoal.y) <= 0.8f
				&& PlanarDistanceSquared(position, observedEntranceSurveyOrigin) <= 900f;
		}

		private void RememberReachedBlockedEntranceFrontier(Vector3 goal)
		{
			if (Time.time < nextObservedEntranceSurveyStartTime)
				return;
			bool sameGoalEpisode = observedEntranceSurveySide != 0
				&& observedEntranceSurveyWasBrainGoal == (brainDriver != null && brainDriver.HasDestination)
				&& PlanarDistanceSquared(goal, observedEntranceSurveyGoal) <= 2.25f
				&& Mathf.Abs(goal.y - observedEntranceSurveyGoal.y) <= 0.8f;
			if (sameGoalEpisode
				&& (Time.time >= observedEntranceSurveyEpisodeUntil
					|| PlanarDistanceSquared(transform.position, observedEntranceSurveyOrigin) > 900f))
			{
				// Bounded search of a long unknown perimeter: fail safely,
				// then let the existing A-Life/tactical recovery make a new
				// decision rather than circling the building indefinitely.
				observedEntranceSurveySide = 0;
				observedEntranceSurveyUntil = 0f;
				nextObservedEntranceSurveyStartTime = Time.time + 12f;
				return;
			}
			if (!sameGoalEpisode)
			{
				observedEntranceSurveyOrigin = transform.position;
				observedEntranceSurveyGoal = goal;
				observedEntranceSurveyWasBrainGoal = brainDriver != null && brainDriver.HasDestination;
				observedEntranceSurveySide = avoidanceSide >= 0 ? 1 : -1;
				observedEntranceSurveyEpisodeUntil = Time.time + 42f;
			}
			// Keep the same flank over successive physically observed
			// corners, but do not extend a single survey past 42s.
			observedEntranceSurveyUntil = Mathf.Min(
				observedEntranceSurveyEpisodeUntil, Time.time + 15f);
		}

		private void FlipObservedEntranceSurveyAfterRealStall(Vector3 goal)
		{
			if (!IsObservingEntranceSurvey(goal, transform.position))
				return;

			// The normal stuck recovery already rejected temporary actor
			// congestion before reaching here. Try the opposite side without
			// changing the real task or imagining a passage.
			observedEntranceSurveySide = -observedEntranceSurveySide;
			observedEntranceSurveyUntil = Mathf.Min(
				observedEntranceSurveyEpisodeUntil, Time.time + 10f);
		}

		private float LocalDeadEndPenalty(Vector3 candidate, Vector3 goal)
		{
			float penalty = 0f;
			for (int i = 0; i < MaxRecentNavDeadEnds; ++i)
			{
				if (Time.time > recentNavDeadEndExpiry[i]
					|| PlanarDistanceSquared(goal, recentNavDeadEndGoals[i]) > 9f
					|| Mathf.Abs(goal.y - recentNavDeadEndGoals[i].y) > 0.8f
					|| Mathf.Abs(candidate.y - recentNavDeadEndPositions[i].y) > 0.8f)
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
			bool trackingBrainGoal = brainDriver != null && brainDriver.HasDestination;
			// Roaming must use the same *goal-relative* progress evidence as
			// combat. A survivor walking left/right in front of a wall is
			// moving but NOT making progress toward an A-Life destination.
			if (trackingBrainGoal || hasRoamingDestination)
			{
				Vector3 currentGoal = trackingBrainGoal
					? brainDriver.Destination : roamingDestination;
				// A brain/roaming ownership switch or a different floor must not
				// inherit the last goal's stuck time even when XY is unchanged.
				if (!hasTrackedStuckGoal
					|| lastTrackedStuckWasBrainGoal != trackingBrainGoal
					|| PlanarDistanceSquared(currentGoal, lastTrackedStuckGoal) > 9f
					|| Mathf.Abs(currentGoal.y - lastTrackedStuckGoal.y) > 0.8f)
				{
					hasTrackedStuckGoal = true;
					lastTrackedStuckWasBrainGoal = trackingBrainGoal;
					lastTrackedStuckGoal = currentGoal;
					lastMeaningfulRouteProgressPosition = samplePosition;
					blockedMovementDuration = 0f;
					useRecoverySteering = false;
					hasWallContourCommit = false;
					return;
				}
				lastTrackedStuckGoal = currentGoal;
				float previousGoalDistance = Mathf.Sqrt(PlanarDistanceSquared(previousPosition, currentGoal));
				float currentGoalDistance = Mathf.Sqrt(PlanarDistanceSquared(samplePosition, currentGoal));
				float anchorDistance = Mathf.Sqrt(PlanarDistanceSquared(
					lastMeaningfulRouteProgressPosition, currentGoal));
				// Sidestepping a wall may temporarily fail to reduce goal distance.
				// Accept a substantial NET detour, not repeated small oscillations,
				// only if it has not carried the NPC significantly farther away
				// from the current brain OR actual roaming destination.
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

			// A teammate, player or zombie standing in a doorway is NOT
			// evidence that the map has a permanent wall or that a recorded
			// stair/entrance route has become invalid. Yield initially, then
			// request a different physical route without penalizing the
			// personal route memory or declaring a tactical goal unreachable.
			// This uses the exact same bounded collision masks as the motor.
			if (IsOnlyTemporaryActorBlockingCurrentApproach())
			{
				useRecoverySteering = false;
				if (blockedMovementDuration >= 5f)
				{
					// Crowd lasting several seconds: give the existing native
					// and local route planners another chance. No teleport,
					// synthetic portal, or false obstacle-learning event.
					if (hasRoamingDestination && !brainDriver.HasDestination)
					{
						// An occupied doorway is not a failed exploration of the
						// POI; release the squad's sector without poisoning its memory.
						if (autonomyPlanner != null)
							autonomyPlanner.ReleaseExplorationSectorServer(false, false);
						hasRoamingDestination = false;
						hasPartialLocalFrontier = false;
						nextRoamingDecisionTime = Time.time + Random.Range(1f, 2f);
					}
					else if (activeRememberedRouteIndex < 0
						&& activeAccessRouteIds.Count == 0)
					{
						// Replan only a disposable tactical path. A recorded
						// doorway/stair route is still valid when merely crowded,
						// and must not lose its access-phase identifiers.
						routeWaypoints.Clear();
						routeWaypointIndex = 0;
						hasPartialLocalFrontier = false;
						hasDetourWaypoint = false;
						nextRoutePlanTime = Time.time;
						nextLocalRoutePlanTime = Time.time;
						nextDetourAttemptTime = Time.time + 0.25f;
					}
					blockedMovementDuration = 0f;
				}
				return;
			}

			// A confirmed shared gateway can be physically blocked today even
			// though it was crossed earlier. Quarantine only an entrance
			// immediately implicated by the current waypoint, never the
			// unrelated distant tactical goal. Temporary actor blockers were
			// handled above, so they cannot poison this shared evidence.
			if (routeWaypointIndex < routeWaypoints.Count
				&& !isUnderwater && !IsTraversingLadder
				&& characterController.isGrounded)
			{
				HybridNPCObservedPortalRegistry.ReportBlockedApproach(
					samplePosition, routeWaypoints[routeWaypointIndex]);
			}

			// A sustained stall without temporary actor crowding is physical
			// feedback to the personal occupancy map. Keep this evidence
			// short-lived and never block the far strategic destination.
			if (!isUnderwater && !IsTraversingLadder && characterController.isGrounded)
			{
				Vector3 blockedToward = routeWaypointIndex < routeWaypoints.Count
					? routeWaypoints[routeWaypointIndex] - samplePosition
					: (brainDriver != null && brainDriver.HasDestination
						? brainDriver.Destination - samplePosition
						: roamingDestination - samplePosition);
				MobileNPCSpatialMemory sensorMemory = GetComponent<MobileNPCSpatialMemory>();
				if (sensorMemory != null)
					sensorMemory.RememberPhysicalMovementBlock(samplePosition, blockedToward);
			}

			// Preserve combat/investigation destinations. Only roaming waypoints are discarded;
			// tactical movement gets a temporary alternate steering angle instead.
			if (hasRoamingDestination && !brainDriver.HasDestination)
			{
				++consecutiveStuckRecoveries;
				// A patrol has the SAME physical right to try a corner route
				// as a tactical goal. The old branch immediately discarded
				// the destination on the first 1.5s stall, so an NPC at a
				// house wall could never learn which side led around it.
				// Only two bounded retries; after that choose another real
				// A-Life location instead of retrying the same inaccessible POI.
				if (consecutiveStuckRecoveries <= 2)
				{
					FlipObservedEntranceSurveyAfterRealStall(roamingDestination);
					RememberLocalNavigationDeadEnd(samplePosition, roamingDestination, 9f);
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
					hasDetourWaypoint = false;
					hasPartialLocalFrontier = false;
					nextDetourAttemptTime = Time.time;
					nextLocalRoutePlanTime = Time.time;
					nextRoutePlanTime = Time.time;
					blockedMovementDuration = 0f;
					// Keep physical side commitment when it exists.
					return;
				}
				MobileNPCInteriorSurvey survey = GetComponent<MobileNPCInteriorSurvey>();
				if (survey != null)
					survey.ReportRoomRouteBlockedServer(roamingDestination);
				if (autonomyPlanner != null)
					autonomyPlanner.ReportRoamBlockedServer(roamingDestination);
				hasRoamingDestination = false;
				hasDetourWaypoint = false;
				hasPartialLocalFrontier = false;
				consecutiveStuckRecoveries = 0;
				nextRoamingDecisionTime = Time.time + Random.Range(2f, 4f);
				blockedMovementDuration = 0f;
				useRecoverySteering = false;
				hasWallContourCommit = false;
				return;
			}

			if (brainDriver.HasDestination)
			{
				// Actual lack of movement is evidence that this local approach
				// failed, not that the global tactical goal has been completed.
				RememberLocalNavigationDeadEnd(samplePosition, brainDriver.Destination);
				// An unsuccessful wall contour must not keep its old side and
				// goal after a genuinely measured route stall.
				if (hasWallContourCommit
					&& PlanarDistanceSquared(samplePosition, wallContourStart) < 1.44f)
				{
					avoidanceSide = -avoidanceSide;
					hasWallContourCommit = false;
				}
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
				FlipObservedEntranceSurveyAfterRealStall(brainDriver.Destination);
				if ((consecutiveStuckRecoveries >= 4
						&& brainDriver.ReportTacticalDestinationUnreachableServer(brainDriver.Destination))
					|| (consecutiveStuckRecoveries >= 6
						&& brainDriver.ReportNonCombatNavigationExhaustedServer(
							brainDriver.Destination)))
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
				// One event every ~3 seconds per NPC keeps a 40-agent Player.log
				// readable while recording WHAT failed (and whether a real
				// route backend/waypoint was available), not just "blocked".
				if (Time.time >= nextBlockedDiagnosticAt)
				{
					nextBlockedDiagnosticAt = Time.time + 3f;
					string routeMode = routeWaypointIndex < routeWaypoints.Count
						? "planned-waypoint" : hasDetourWaypoint ? "physical-detour"
						: hasWallContourCommit ? "wall-contour" : "direct-no-path";
					Debug.LogWarning("[MobileNPCHumanoidMotor] Blocked at " + transform.position
						+ "; goal=" + brainDriver.Destination
						+ ", waypointMode=" + routeMode
						+ ", nativeAStarAvailable="
						+ (independentRouteProvider != null && independentRouteProvider.IsAvailable)
						+ ", steeringSide=" + avoidanceSide
						+ ", stuckRecoveries=" + consecutiveStuckRecoveries
						+ ", brainGoal=" + brainDriver.CurrentGoal + ".", this);
				}
				blockedMovementDuration = 0.25f;
			}
		}

		/// <summary>
		/// Distinguish a short-lived crowd from a genuine map obstruction
		/// before inserting failed wall coordinates into learned memory.
		/// Only the FIRST travel segment is sampled: a distant actor elsewhere
		/// in the building does not count as blocking this NPC right now.
		/// </summary>
		private bool IsOnlyTemporaryActorBlockingCurrentApproach()
		{
			if (characterController == null)
				return false;
			Vector3 next = routeWaypointIndex < routeWaypoints.Count
				? routeWaypoints[routeWaypointIndex]
				: hasDetourWaypoint ? detourWaypoint
				: brainDriver != null && brainDriver.HasDestination
					? brainDriver.Destination : roamingDestination;
			Vector3 delta = next - transform.position;
			delta.y = 0f;
			float distance = delta.magnitude;
			if (distance < 0.2f)
				return false;
			Vector3 origin = transform.position
				+ Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
					Mathf.Max(0.05f, characterController.radius * 0.9f));
			return IsOnlyTemporaryActorBlockingAlong(origin, radius,
				delta / distance, Mathf.Min(distance, Mathf.Max(0.5f, obstacleProbeDistance)));
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
			// Unlike long-range spawn-ground search, the walking probe must
			// resolve the SAME street/curb surface, not a roof several metres
			// above it. Only physics-walkable world layers count as ground.
			if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
				characterController.stepOffset + 0.35f, 1.1f,
				(RayMasks.BLOCK_COLLISION | RayMasks.GROUND2),
				Mathf.Min(characterController.slopeLimit, roamingMaximumSlope),
				out Vector3 ground, out Collider support, out _)
				|| IsFurnitureSurfaceCollider(support)
				|| support == null || support.GetComponentInParent<Player>() != null
				|| support.GetComponentInParent<Zombie>() != null
				|| support.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
				return false;
			float rise = ground.y - transform.position.y;
			// Ordinary CharacterController stepOffset can climb small curbs,
			// but tall ledges should still require a planned route.
			return rise <= Mathf.Max(0.35f, characterController.stepOffset + 0.05f)
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
			bool directlyBlocked = IsObstacleBlockingAlong(origin, probeRadius,
				desiredDirection, probeDistance);
			Vector3 currentRequestedGoal = brainDriver != null && brainDriver.HasDestination
				? brainDriver.Destination : roamingDestination;
			bool sameContourGoal = hasWallContourCommit
				&& PlanarDistanceSquared(currentRequestedGoal, wallContourGoal) <= 2.25f
				&& Mathf.Abs(currentRequestedGoal.y - wallContourGoal.y) <= 0.8f;
			if (!sameContourGoal)
				hasWallContourCommit = false;

			// At a corner the first 1.6m may become clear before the WHOLE
			// CharacterController has passed the wall. Keep the observed
			// tangent briefly until a longer real ray also clears; otherwise
			// direct-to-goal steering makes the agent bounce left/right.
			bool wallStillAhead = sameContourGoal
				&& Time.time < wallContourUntil
				&& IsObstacleBlockingAlong(origin, probeRadius, desiredDirection,
					Mathf.Max(probeDistance + 1.2f, probeDistance * 1.7f));
			if (!directlyBlocked && !wallStillAhead)
			{
				hasWallContourCommit = false;
				return desiredDirection;
			}

			if (routeWaypointIndex >= routeWaypoints.Count && !hasDetourWaypoint
				&& TryFollowObservedSolidObstacle(desiredDirection, origin,
					probeRadius, wallStillAhead ? probeDistance * 1.7f : probeDistance,
					currentRequestedGoal, out Vector3 contourDirection))
				return contourDirection;

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
		/// Follow the tangent of a physically hit wall/post rather than the
		/// sign of a random heading. This is deliberately bounded and does
		/// NOT claim a global path, breach walls, or overwrite a true A*
		/// route. Collision + safe-next-ground are checked before each move.
		/// </summary>
		private bool TryFollowObservedSolidObstacle(Vector3 desiredDirection,
			Vector3 origin, float radius, float probeDistance, Vector3 actualGoal,
			out Vector3 selected)
		{
			selected = Vector3.zero;
			int hits = Physics.SphereCastNonAlloc(origin, radius, desiredDirection,
				ObstacleHitBuffer, probeDistance, obstacleMask, QueryTriggerInteraction.Ignore);
			RaycastHit closest = default(RaycastHit);
			float nearest = float.PositiveInfinity;
			// Saturation must fail closed; a collider omitted by a full
			// nonalloc buffer must not be ignored as a clear passage.
			if (hits >= ObstacleHitBuffer.Length)
				return false;
			for (int i = 0; i < hits; ++i)
			{
				RaycastHit hit = ObstacleHitBuffer[i];
				Collider collider = hit.collider;
				if (collider == null || IsOwnCollider(collider)
					|| collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null
					|| collider.GetComponentInParent<Player>() != null
					|| collider.GetComponentInParent<Zombie>() != null)
					continue;
				if (hit.distance < nearest)
				{
					nearest = hit.distance;
					closest = hit;
				}
			}
			if (closest.collider == null)
				return false;
			Vector3 normal = closest.normal;
			normal.y = 0f;
			if (normal.sqrMagnitude < 0.12f)
				return false;
			normal.Normalize();

			if (!hasWallContourCommit || Time.time >= wallContourUntil)
			{
				// Flip only after an unsuccessful entire physical commitment,
				// not every time the NPC touches another piece of the same wall.
				if (hasWallContourCommit && Time.time >= wallContourUntil
					&& PlanarDistanceSquared(transform.position, wallContourStart) < 1.44f)
					avoidanceSide = -avoidanceSide;
				hasWallContourCommit = true;
				wallContourGoal = actualGoal;
				wallContourStart = transform.position;
				wallContourUntil = Time.time + 4f;
				avoidanceSideUntil = wallContourUntil;
			}
			Vector3 tangent = new Vector3(-normal.z, 0f, normal.x);
			for (int attempt = 0; attempt < 2; ++attempt)
			{
				int side = attempt == 0 ? avoidanceSide : -avoidanceSide;
				Vector3 direction = (tangent * side + normal * 0.27f).normalized;
				if (IsObstacleBlockingAlong(origin, radius, direction,
					Mathf.Clamp(probeDistance * 0.8f, 0.65f, 1.5f))
					|| !HasWalkableGroundAhead(direction))
					continue;
				// Do not keep flipping to the other wall face while the
				// chosen contour is still making actual physical progress.
				if (attempt != 0)
				{
					avoidanceSide = side;
					wallContourStart = transform.position;
					wallContourUntil = Time.time + 3.5f;
				}
				selected = direction;
				return true;
			}
			return false;
		}

		private bool TryResolveGlobalFootingServer(Vector3 estimate, out Vector3 foot)
		{
			foot = Vector3.zero;
			// Match the native PlayerMovement.checkGround layer set. Avoid
			// treating a Nav-only helper mesh or decoration as actual support.
			int physicalGroundMask = ((int)roamingGroundMask) & RayMasks.BLOCK_COLLISION;
			if (physicalGroundMask == 0
				|| !MobileNPCSpawnService.TryFindSafeGroundPosition(estimate,
				1.9f, 7.5f, physicalGroundMask,
				Mathf.Min(characterController.slopeLimit, roamingMaximumSlope),
				out Vector3 grounded, out Collider floor, out _)
				|| floor == null || IsFurnitureSurfaceCollider(floor)
				|| Mathf.Abs(grounded.y - transform.position.y) > 6f
				|| !Level.checkSafeIncludingClipVolumes(grounded))
				return false;
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.12f,
				Mathf.Max(0.12f, characterController.radius * 0.9f));
			if (IsRouteNodeOccupied(grounded, radius)
				&& !IsOnlyTemporaryActorOccupyingRouteNode(grounded, radius))
				return false;
			foot = grounded;
			return true;
		}

		private bool IsGlobalEdgePhysicallyTraversableServer(Vector3 from, Vector3 to)
		{
			Vector3 movement = to - from;
			float rise = Mathf.Abs(movement.y);
			movement.y = 0f;
			float distance = movement.magnitude;
			if (distance < 0.12f || distance > 4.4f)
				return false;
			float slopeLimit = Mathf.Min(characterController.slopeLimit,
				roamingMaximumSlope);
			float permittedRise = distance * Mathf.Tan(slopeLimit * Mathf.Deg2Rad)
				+ characterController.stepOffset;
			if (rise > permittedRise) return false;
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.12f,
				Mathf.Max(0.12f, characterController.radius * 0.9f));
			return IsStandingCapsuleCorridorClearServer(from,
				movement / distance, distance, radius)
				&& HasContinuousPlayerCollisionFloorServer(from, to);
		}

		private bool HasContinuousPlayerCollisionFloorServer(Vector3 from, Vector3 to)
		{
			// Long-horizon routes may cross auxiliary Nav colliders that
			// existing local discovery tolerates. Global edges must be built
			// only on the layers that support the real player's controller.
			int physicalGroundMask = ((int)roamingGroundMask) & RayMasks.BLOCK_COLLISION;
			if (physicalGroundMask == 0) return false;
			Vector3 flat = to - from;
			flat.y = 0f;
			int segments = Mathf.Max(2, Mathf.CeilToInt(flat.magnitude / 0.5f));
			float previousY = from.y;
			for (int i = 1; i <= segments; ++i)
			{
				Vector3 sample = Vector3.Lerp(from, to, (float)i / segments);
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(sample,
					0.7f, 0.8f, physicalGroundMask,
					Mathf.Min(characterController.slopeLimit, roamingMaximumSlope),
					out Vector3 floor, out Collider support, out _)
					|| support == null || IsFurnitureSurfaceCollider(support)
					|| !Level.checkSafeIncludingClipVolumes(floor)
					|| Mathf.Abs(floor.y - sample.y) > 0.35f
					|| floor.y - previousY > Mathf.Max(0.4f, characterController.stepOffset + 0.1f)
					|| previousY - floor.y > 0.55f)
					return false;
				previousY = floor.y;
			}
			return true;
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
			if (TryBuildIndependentRoute(destination))
			{
				detourGoalSnapshot = destination;
				hasDetourWaypoint = false;
				hasPartialLocalFrontier = false;
				return;
			}

			// The same native Seeker is asynchronous. Do not immediately
			// replace a pending global multi-room path with a short local
			// frontier; that could occupy routeWaypoints before the Seeker's
			// completed answer gets a chance to be consumed. If native A*
			// fails or times out, its existing retry cooldown permits the
			// normal local fallback below.
			if (independentRouteProvider != null
				&& independentRouteProvider.IsRequestPending)
				return;

			// A survivor who ACTUALLY landed on a crate may need to step
			// down into a lower room. Never plan this for an unseen rooftop
			// or make an A* node automatically traverse the object's wall.
			if (TryChooseVerifiedFurnitureGroundExit(destination))
				return;

			// Window- and doorway-sized architectural gaps are not an
			// InteractableDoor or a LevelObject switch. After a REAL wall
			// stall search the local facade for a physically traversable
			// free-space opening, then commit a two-sided threshold route.
			// Shared/per-actor caps prevent high NPC counts from spiking.
			// A blocked line toward a nearby indoor destination is already
			// physical evidence of a facade. Do not demand an actual collision
			// stall before even looking for an accessible doorway: that forces
			// the survivor to walk into the wall first. The threshold probe
			// itself is server-only and throttled per NPC and across the crowd.
			bool nearbyBlockedFacade = toGoal.sqrMagnitude <= 400f
				&& Mathf.Abs(destination.y - transform.position.y) <= 4.5f
				&& IsObstacleBlocking(toGoal.normalized);
			if ((consecutiveStuckRecoveries >= 1 || nearbyBlockedFacade)
				&& TryPlanObservedBuildingThresholdServer(destination))
				return;

			// After repeated REAL movement stalls on a long-distance task,
			// let the already-running global horizon search finish its short
			// CPU-budgeted window. Otherwise every local 81-node frontier
			// steals the route slot before the incremental search can return.
			// Nearby doorway goals and immediate physical access retain priority.
			if (consecutiveStuckRecoveries >= 2 && toGoal.sqrMagnitude > 196f
				&& globalRoutePlanner.IsWithinBriefWaitWindow(Time.time))
				return;

			// First attempt the normal 81-node, same-floor local A*.
			// A narrow real doorway can lie BETWEEN the 1.25m sample points:
			// if the coarse route fails or stops short of a nearby exact goal,
			// admit one finer 0.625m search, never a fabricated portal.
			bool coarseSearchAdmitted = Time.time >= nextLocalRoutePlanTime
				&& Time.time >= nextSharedUnknownRouteSlot;
			bool coarseRouteFound = TryBuildLocalRoute(destination);
			if (coarseSearchAdmitted && (coarseRouteFound || routeWaypoints.Count == 0)
				&& PlanarDistanceSquared(transform.position, destination) <= 6.25f
				&& Mathf.Abs(transform.position.y - destination.y) <= 0.65f)
			{
				bool coarseEndsShort = !coarseRouteFound;
				if (coarseRouteFound && routeWaypoints.Count > 0)
				{
					Vector3 last = routeWaypoints[routeWaypoints.Count - 1];
					coarseEndsShort = PlanarDistanceSquared(last, destination) > 0.81f
						|| Mathf.Abs(last.y - destination.y) > 0.45f;
				}
				if (coarseEndsShort && TryBuildNarrowPassageRoute(destination))
				{
					detourGoalSnapshot = destination;
					hasDetourWaypoint = false;
					return;
				}
			}
			if (coarseRouteFound)
			{
				// A local A* "success" can mean only a reachable frontier
				// OUTSIDE the building, not an actual path to its entrance.
				// When that same actor previously stalled at the facade,
				// compare a locally verified lateral opening before repeating
				// a deceptive partial route toward the same blocked wall.
				if (hasPartialLocalFrontier && consecutiveStuckRecoveries >= 1
					&& TryChooseVerifiedFacadeContourServer(destination))
				{
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
					hasPartialLocalFrontier = false;
					return;
				}
				detourGoalSnapshot = destination;
				hasDetourWaypoint = false;
				return;
			}

			// Fallback without the optional licensed native A* graph: when
			// physically stalled along a HOUSE facade, the NPC can choose
			// one VERIFIED lateral standing location toward an opening rather
			// than oscillate in front of the same wall/closed window. It
			// never crosses a wall, teleports, or assumes a fake portal.
			if (consecutiveStuckRecoveries >= 1
				&& TryChooseVerifiedFacadeContourServer(destination))
				return;

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
					roamingGroundMask, roamingMaximumSlope,
					out Vector3 resolved, out Collider floorCollider, out _)
					|| IsFurnitureSurfaceCollider(floorCollider))
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
				// The single-hop fallback must not immediately return to
				// a physically witnessed blocked approach. Reuse the SAME
				// personal short-lived dead-end evidence as local A*.
				float score = progress * 3f + goalDistanceImprovement * 0.5f
					- resolvedOffset.magnitude * 0.05f
					- LocalDeadEndPenalty(resolved, destination) * 0.75f;
				if (IsObservingEntranceSurvey(destination, transform.position))
				{
					Vector3 surveyHeading = observedEntranceSurveyGoal
						- observedEntranceSurveyOrigin;
					surveyHeading.y = 0f;
					if (surveyHeading.sqrMagnitude > 0.1f)
					{
						Vector3 lateral = new Vector3(-surveyHeading.z, 0f, surveyHeading.x).normalized
							* observedEntranceSurveySide;
						score += Mathf.Clamp(Vector3.Dot(resolvedOffset, lateral), -2f, 4f);
					}
				}
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
		/// Geometric THRESHOLD detection for doorless Unturned buildings.
		/// A real facade wall is hit first; lateral sampling then searches
		/// for a clear, grounded capsule passage FROM the present side to
		/// the goal side. A physically reachable near-side approach and
		/// continuous-floor cross-side leg become real route waypoints.
		/// No asset names, visual art guesses, scene door components, or
		/// invented window links. Raised-sill windows remain vault-only.
		/// </summary>
		private bool TryPlanObservedBuildingThresholdServer(Vector3 destination)
		{
			if (!Provider.isServer || characterController == null
				|| !characterController.enabled || !characterController.isGrounded
				|| Time.time < nextPhysicalThresholdScanTime
				|| Time.time < nextSharedPhysicalThresholdSlot
				// A destination upstairs/downstairs can still require a genuine
				// ground-floor doorway first. Rejecting the entire scan based on
				// the FINAL goal elevation prevented NPCs entering buildings to
				// reach interior stairs. The actual opening remains constrained
				// by near/far floor height, standing capsule and continuous floor.
				|| Mathf.Abs(destination.y - transform.position.y) > 4.5f)
				return false;
			Vector3 toGoal = destination - transform.position;
			toGoal.y = 0f;
			if (toGoal.sqrMagnitude < 4f)
				return false;
			// Only attempt after existing route recovery has diagnosed a
			// stall; do not add an architectural scan to every combat tick.
			nextPhysicalThresholdScanTime = Time.time + 2.0f;
			nextSharedPhysicalThresholdSlot = Time.time + 0.32f;
			Vector3 forward = toGoal.normalized;
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.15f,
				Mathf.Max(0.15f, characterController.radius * 0.9f));
			Vector3 origin = transform.position + Vector3.up * 1.05f;
			int hits = Physics.SphereCastNonAlloc(origin, radius, forward,
				ObstacleHitBuffer, 3.5f, obstacleMask,
				QueryTriggerInteraction.Ignore);
			if (hits == 0)
			{
				// A raised window can look clear at chest height even
				// while its solid sill physically blocks the lower body.
				// Only take this second low probe AFTER a measured stall.
				origin = transform.position + Vector3.up * 0.42f;
				hits = Physics.SphereCastNonAlloc(origin, radius, forward,
					ObstacleHitBuffer, 3.5f, obstacleMask,
					QueryTriggerInteraction.Ignore);
			}
			if (hits <= 0 || hits >= ObstacleHitBuffer.Length)
				return false;
			RaycastHit wall = default(RaycastHit);
			float nearest = float.PositiveInfinity;
			for (int i = 0; i < hits; ++i)
			{
				RaycastHit candidate = ObstacleHitBuffer[i];
				Collider collider = candidate.collider;
				if (collider == null || IsOwnCollider(collider)
					|| collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null
					|| collider.GetComponentInParent<Zombie>() != null
					|| collider.GetComponentInParent<Player>() != null
					|| collider.GetComponentInParent<InteractableVehicle>() != null
					|| collider.attachedRigidbody != null
						&& !collider.attachedRigidbody.isKinematic
					|| candidate.distance >= nearest)
					continue;
				wall = candidate;
				nearest = candidate.distance;
			}
			if (wall.collider == null)
				return false;
			Vector3 outward = wall.normal;
			outward.y = 0f;
			if (outward.sqrMagnitude < 0.45f)
				return false;
			outward.Normalize();
			// Only cross if the goal is behind this observed facade, not
			// merely elsewhere along the current side of the building.
			if (Vector3.Dot(destination - wall.point, outward) >= -0.35f)
				return false;
			Vector3 sideways = Vector3.Cross(Vector3.up, outward).normalized;
			Vector3 wallFoot = new Vector3(wall.point.x,
				transform.position.y, wall.point.z);
			// Match the same real curb/threshold step capability already
			// used by normal ground locomotion, without guessing door sizes.
			float thresholdStep = Mathf.Clamp(characterController.stepOffset + 0.05f,
				0.08f, 0.65f);
			float bestScore = float.NegativeInfinity;
			float secondScore = float.NegativeInfinity;
			Vector3 bestNear = Vector3.zero, bestFar = Vector3.zero;
			int groundedOpeningPairs = 0;
			int capsuleClearPairs = 0;
			Vector3 secondNear = Vector3.zero, secondFar = Vector3.zero;
			// Search at most 11.7m along each side; no global architecture
			// scan. One geometrically verified candidate then gets one
			// expensive exact local path to its near-side approach.
			for (int side = -1; side <= 1; side += 2)
			{
				for (int step = 0; step <= 18; ++step)
				{
					float lateral = step * 0.65f;
					Vector3 aperture = wallFoot + sideways * (side * lateral);
					Vector3 nearEstimate = aperture + outward * 0.95f;
					Vector3 farEstimate = aperture - outward * 1.15f;
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
						nearEstimate, 0.8f, 0.9f, roamingGroundMask,
						roamingMaximumSlope, out Vector3 near, out Collider nearFloor, out _)
						|| !MobileNPCSpawnService.TryFindSafeGroundPosition(
						farEstimate, 0.8f, 0.9f, roamingGroundMask,
						roamingMaximumSlope, out Vector3 far, out Collider farFloor, out _)
						|| nearFloor == null || farFloor == null
						|| IsFurnitureSurfaceCollider(nearFloor)
						|| IsFurnitureSurfaceCollider(farFloor)
						|| Mathf.Abs(near.y - transform.position.y) >
							Mathf.Max(0.50f, thresholdStep)
						|| Mathf.Abs(near.y - far.y) > thresholdStep
						// A local exact route can only approach nearby openings.
						|| PlanarDistanceSquared(transform.position, near) > 144f
						|| !Level.checkSafeIncludingClipVolumes(near)
						|| !Level.checkSafeIncludingClipVolumes(far)
						|| IsRouteNodeOccupied(near, radius)
						|| IsRouteNodeOccupied(far, radius)
						// A physically stalled entrance may still look clear to
						// geometric scans. Respect the shared short cooldown so
						// the NPC actually investigates a different opening.
						|| HybridNPCObservedPortalRegistry.IsTemporarilyBlocked(
							near, far))
						continue;

					++groundedOpeningPairs;
					Vector3 crossing = far - near;
					crossing.y = 0f;
					float length = crossing.magnitude;
					if (length < 0.4f || length > 3.0f
						|| !(Mathf.Abs(near.y - far.y) <= 0.12f
							? IsStandingCapsuleCorridorClearServer(
								near, crossing / length, length, radius)
							: IsActuallyWalkableStairConnection(near, far, radius))
						|| !HasContinuousLocalFloor(near, far))
						continue;
					++capsuleClearPairs;
					// Sample the middle quarters as well. The facade can
					// have two floors with a gap even when its two sides
					// happen to be walkable (windows, ledges, balconies).
					bool floorConnected = true;
					for (int sample = 1; sample <= 3; ++sample)
					{
						Vector3 samplePoint = Vector3.Lerp(near, far,
							sample * 0.25f);
						if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
							samplePoint, 0.55f, 0.65f, roamingGroundMask,
							roamingMaximumSlope, out Vector3 floor, out Collider floorHit, out _)
							|| floorHit == null || IsFurnitureSurfaceCollider(floorHit)
							|| Mathf.Abs(floor.y - samplePoint.y) >
								Mathf.Max(0.28f, thresholdStep * 0.55f))
						{
							floorConnected = false;
							break;
						}
					}
					if (!floorConnected)
						continue;
					// Prefer a shorter, lower-risk walking entry over a
					// difficult sill, but never trade physics clearance for
					// lower score. This also makes a genuine flat doorway
					// preferable to an unnecessarily stepped alternative.
					float score = -PlanarDistanceSquared(transform.position, near) * 0.22f
						- PlanarDistanceSquared(far, destination) * 0.018f
						- LocalDeadEndPenalty(near, destination)
						- Mathf.Abs(near.y - far.y) * 1.5f;
					if (zombieRisk != null)
						score -= zombieRisk.GetKnownRiskAtServer(far) * 4f;
					if (score > bestScore)
					{
						secondScore = bestScore;
						secondNear = bestNear;
						secondFar = bestFar;
						bestScore = score;
						bestNear = near;
						bestFar = far;
					}
					else if (score > secondScore
						&& PlanarDistanceSquared(near, bestNear) > 0.64f)
					{
						secondScore = score;
						secondNear = near;
						secondFar = far;
					}
				}
			}
			if (bestScore == float.NegativeInfinity)
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (Time.time >= nextPhysicalThresholdDiagnosticTime)
				{
					nextPhysicalThresholdDiagnosticTime = Time.time + 5f;
					Debug.Log("[MobileNPCGeometryThreshold] No traversable opening npc=" + name
						+ " groundedPairs=" + groundedOpeningPairs
						+ " capsuleClearPairs=" + capsuleClearPairs
						+ " wall=" + wall.point + " goal=" + destination, this);
				}
#endif
				return false;
			}

			// EXACT physical approach. The fallback's "closest reachable
			// frontier" is not sufficient proof of reaching this entrance.
			// The 9x9 local grid reaches only about five metres; a doorway
			// geometrically observed farther along a facade must not be
			// rejected when the ENTIRE route to its near-side threshold is
			// actually walkable and capsule-clear. This is a verified straight
			// corridor, NOT an assumed portal or through-wall shortcut.
			narrowPassageScratch.Clear();
			bool firstReachable = CanReachThresholdApproachDirectlyServer(bestNear, radius)
				|| CanSafelyShortcutRouteCorner(bestNear)
				|| TryBuildLocalRoute(bestNear, narrowPassageScratch, true);
			if (!firstReachable)
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				if (Time.time >= nextPhysicalThresholdDiagnosticTime)
				{
					nextPhysicalThresholdDiagnosticTime = Time.time + 5f;
					Debug.Log("[MobileNPCGeometryThreshold] Opening found but near-side route rejected npc="
						+ name + " near=" + bestNear + " secondary=" + secondNear
						+ " actor=" + transform.position, this);
				}
#endif
				// A clear opening is not a valid route if furniture
				// or a wall blocks its near-side approach. Bounded to
				// a single runner-up exact-path evaluation.
				if (secondScore == float.NegativeInfinity)
					return false;
				narrowPassageScratch.Clear();
				if (!CanReachThresholdApproachDirectlyServer(secondNear, radius)
					&& !CanSafelyShortcutRouteCorner(secondNear)
					&& !TryBuildLocalRoute(secondNear, narrowPassageScratch, true))
					return false;
				bestNear = secondNear;
				bestFar = secondFar;
			}
			routeWaypoints.Clear();
			if (narrowPassageScratch.Count > 0)
				routeWaypoints.AddRange(narrowPassageScratch);
			// Exact-grid routes can legally finish up to 0.92m away from
			// the requested point. That's wider than a human doorway.
			// Add the actual tested approach with a 0.4m arrival radius.
			if (routeWaypoints.Count == 0
				|| PlanarDistanceSquared(routeWaypoints[routeWaypoints.Count - 1],
					bestNear) > 0.09f)
				routeWaypoints.Add(bestNear);
			physicalThresholdApproachWaypointIndex = routeWaypoints.Count - 1;
			// Commit to crossing the FULL physical wall threshold before
			// replanning toward the original investigation/combat goal.
			routeWaypoints.Add(bestFar);
			physicalThresholdBeyondWaypoint = bestFar;
			physicalThresholdEntryWaypoint = bestNear;
			// Register only the aperture accepted by the motor's actual
			// floor, capsule and reachable-approach checks.
			HybridNPCObservedPortalRegistry.Remember(bestNear, bestFar);
			if (navigationTelemetry != null && navigationTelemetry.LoggingEnabled)
				navigationTelemetry.RecordPortal(GetInstanceID().ToString(), "level",
					bestNear, bestFar, -1, -1, -1, true, "verified_facade_opening");
			hasPhysicalThresholdRoute = true;
			physicalThresholdOwner = brainDriver != null
				? brainDriver.CurrentMovementOwner : MobileNPCBrainDriver.MovementOwner.Routine;
			routeWaypointIndex = 0;
			hasDetourWaypoint = false;
			hasPartialLocalFrontier = false;
			detourGoalSnapshot = destination;
			nextPlannedRouteValidationTime = 0f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (Time.time >= nextPhysicalThresholdDiagnosticTime)
			{
				nextPhysicalThresholdDiagnosticTime = Time.time + 5f;
				Debug.Log("[MobileNPCGeometryThreshold] Valid physical gap npc="
					+ name + " approach=" + bestNear + " beyond=" + bestFar
					+ " goal=" + destination + ".", this);
			}
#endif
			return true;
		}

		/// <summary>
		/// Extend an observed doorway approach beyond the tiny local A* grid
		/// only along a physically unobstructed, continuously supported route.
		/// No distance-only decision can manufacture a crossing through a wall.
		/// </summary>
		private bool CanReachThresholdApproachDirectlyServer(Vector3 near, float radius)
		{
			Vector3 delta = near - transform.position;
			if (Mathf.Abs(delta.y) > 0.5f || !Level.checkSafeIncludingClipVolumes(near))
				return false;
			delta.y = 0f;
			float distance = delta.magnitude;
			return distance >= 0.2f && distance <= 12f
				&& IsStandingCapsuleCorridorClearServer(transform.position,
					delta / distance, distance, radius)
				&& HasContinuousLocalFloor(transform.position, near);
		}

		/// <summary>
		/// Without the native ASPFP DLL, the small local grid may terminate
		/// at a wall rather than at the distant entrance. Physically observe
		/// the nearest STATIC facade, then try two short walkable sideways
		/// positions using the motor's real capsule/continuous-floor tests.
		/// This never authorizes walking through a door or window.
		/// </summary>
		private bool TryChooseVerifiedFacadeContourServer(Vector3 destination)
		{
			if (!Provider.isServer || characterController == null
				|| !characterController.enabled
				|| PlanarDistanceSquared(destination, transform.position) < 2.5f * 2.5f
				|| Mathf.Abs(destination.y - transform.position.y) > 0.9f)
				return false;
			Vector3 toward = destination - transform.position;
			toward.y = 0f;
			float length = toward.magnitude;
			if (length < 0.3f) return false;
			Vector3 origin = transform.position + Vector3.up * 1.05f;
			Vector3 forward = toward / length;
			int count = Physics.SphereCastNonAlloc(origin, 0.2f, forward,
				ObstacleHitBuffer, Mathf.Min(2.2f, length), obstacleMask,
				QueryTriggerInteraction.Ignore);
			if (count <= 0 || count >= ObstacleHitBuffer.Length)
				return false;
			float nearestDist = float.PositiveInfinity;
			Vector3 wallNormal = Vector3.zero;
			for (int i = 0; i < count; ++i)
			{
				Collider col = ObstacleHitBuffer[i].collider;
				if (col == null || IsOwnCollider(col)
					|| col.GetComponentInParent<InteractableDoor>() != null
					|| col.GetComponentInParent<Player>() != null
					|| col.GetComponentInParent<Zombie>() != null
					|| col.GetComponentInParent<MobileNPCHumanoidVitals>() != null
					|| col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic
					|| ObstacleHitBuffer[i].distance >= nearestDist)
					continue;
				nearestDist = ObstacleHitBuffer[i].distance;
				wallNormal = ObstacleHitBuffer[i].normal;
			}
			wallNormal.y = 0f;
			if (wallNormal.sqrMagnitude < 0.3f)
				return false;
			wallNormal.Normalize();
			Vector3 tangent = Vector3.Cross(Vector3.up, wallNormal).normalized;
			Vector3 best = Vector3.zero;
			float bestScore = float.NegativeInfinity;
			bool bestObservedGap = false;
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
				Mathf.Max(0.05f, characterController.radius * 0.9f));
			// Test both sides of the actually observed facade at several
			// short distances, including a doorway only 2-3m sideways.
			// The previous 7m goal threshold could never activate for a
			// small room on the opposite side of the very same wall.
			for (int side = -1; side <= 1; side += 2)
			{
				for (int step = 0; step < 3; ++step)
				{
					float sideways = 2.2f + step * 0.75f;
					Vector3 estimate = transform.position
						+ tangent * (side * sideways) + wallNormal * 0.25f;
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(estimate,
						1.5f, 1.5f, roamingGroundMask, roamingMaximumSlope,
						out Vector3 ground, out _)
						|| !IsValidScoutingFootingServer(ground)
						|| !CanSafelyShortcutRouteCorner(ground)
						|| zombieRisk != null
							&& zombieRisk.GetKnownRiskAtServer(ground) >= 0.75f
						|| autonomyPlanner != null
							&& !autonomyPlanner.IsCandidateAllowedServer(ground))
						continue;
					Vector3 towardRoom = destination - ground;
					towardRoom.y = 0f;
					float toRoom = towardRoom.magnitude;
					// Seeing a clear capsule-width approach toward the goal
					// distinguishes an actual gap from another piece of wall.
					// Final traversal still uses the normal motor collision.
					bool observedGap = toRoom > 0.1f
						&& IsStandingCapsuleCorridorClearServer(
							ground, towardRoom / toRoom,
							Mathf.Min(2.2f, toRoom), radius)
						&& HasWalkableGroundAheadFromServer(ground,
							towardRoom / toRoom, Mathf.Min(1.2f, toRoom));
					float score = Vector3.Distance(transform.position, destination)
						- Vector3.Distance(ground, destination)
						- LocalDeadEndPenalty(ground, destination)
						+ (observedGap ? 5.0f : 0f)
						- (step * 0.10f);
					if (score > bestScore)
					{
						bestScore = score;
						best = ground;
						bestObservedGap = observedGap;
					}
				}
			}
			if (bestScore == float.NegativeInfinity)
				return false;
			detourWaypoint = best;
			detourGoalSnapshot = destination;
			hasDetourWaypoint = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.Log("[MobileNPCFacade] verified contour npc=" + name
				+ " next=" + best + " goal=" + destination
				+ " gapObserved=" + bestObservedGap
				+ " (no portal assumed).", this);
#endif
			return true;
		}

		/// <summary>
		/// Observe one physically possible, floor-level LOW opening ahead:
		/// standing capsule is blocked by a lintel, crouched capsule is
		/// unobstructed and a true near-level floor exists beyond. This
		/// is NOT a through-wall shortcut or a chest-height "window ray."
		/// Raised windows with solid sills still need a verified vault.
		/// </summary>
		private bool TryRecognizeLowOpeningServer(Vector3 desired)
		{
			if (!Provider.isServer || characterController == null
				|| !characterController.enabled || standingHeight < 1.1f)
				return false;
			desired.y = 0f;
			if (desired.sqrMagnitude < 0.5f)
				return false;
			desired.Normalize();
			if (Time.time < lowOpeningVerifiedUntil
				&& Vector3.Dot(lowOpeningHeading, desired) > 0.95f)
				return true; // Brief committed physical traversal.
			if (Time.time < nextLowOpeningProbeAt
				|| Time.time < nextSharedLowOpeningProbeAt)
				return false;
			nextLowOpeningProbeAt = Time.time + 0.55f;
			nextSharedLowOpeningProbeAt = Time.time + 0.065f;
			float radius = Mathf.Clamp(characterController.radius * 0.82f, 0.18f, 0.34f);
			float probe = 1.05f;
			// Do not shrink the capsule merely to fit alongside a solid
			// wall or in an already-clear hallway.
			if (IsStandingCapsuleCorridorClearServer(transform.position,
				desired, probe, radius))
				return false;
			float crouchedHeight = Mathf.Max(characterController.radius * 2.2f,
				standingHeight * crouchedHeightFraction);
			int count = Physics.CapsuleCastNonAlloc(
				transform.position + Vector3.up * radius,
				transform.position + Vector3.up * (crouchedHeight - radius),
				radius, desired, ObstacleHitBuffer, probe,
				obstacleMask, QueryTriggerInteraction.Ignore);
			if (count >= ObstacleHitBuffer.Length)
				return false;
			for (int i = 0; i < count; ++i)
			{
				Collider collider = ObstacleHitBuffer[i].collider;
				if (collider != null && !IsOwnCollider(collider))
					return false;
			}
			if (!HasWalkableGroundAheadFromServer(transform.position, desired, 0.9f))
				return false;
			lowOpeningHeading = desired;
			lowOpeningVerifiedUntil = Time.time + 0.35f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (Time.time >= nextLowOpeningDiagnosticAt)
			{
				nextLowOpeningDiagnosticAt = Time.time + 8f;
				Debug.Log("[MobileNPCLowOpening] Physically checked crouch-through npc="
					+ name + " at=" + transform.position + ".", this);
			}
#endif
			return true;
		}

		/// <summary>Detect a real standing-width passage in the nearby
		/// facade; a chest-height ray alone can see through a window while
		/// missing the solid sill that blocks the controller's legs.</summary>
		private bool IsStandingCapsuleCorridorClearServer(Vector3 foot,
			Vector3 direction, float distance, float radius)
		{
			if (characterController == null || distance <= 0.1f)
				return false;
			// Match the actual CharacterController, not a smaller probe
			// cylinder beginning at the presumed foot origin. In particular
			// NPC prefabs may have a non-default center or capsule radius:
			// validating a narrower/offset path creates false doorways which
			// CharacterController.Move then physically cannot traverse.
			float castRadius = Mathf.Max(radius, characterController.radius
				- Mathf.Min(0.015f, characterController.skinWidth * 0.25f));
			float halfHeight = Mathf.Max(characterController.height * 0.5f,
				castRadius + 0.01f);
			Vector3 center = foot + characterController.center;
			Vector3 bottom = center + Vector3.up * (-halfHeight + castRadius);
			Vector3 top = center + Vector3.up * (halfHeight - castRadius);
			int count = Physics.CapsuleCastNonAlloc(
				bottom, top, castRadius, direction, ObstacleHitBuffer,
				distance, obstacleMask, QueryTriggerInteraction.Ignore);
			if (count >= ObstacleHitBuffer.Length)
				return false;
			for (int i = 0; i < count; ++i)
				if (ObstacleHitBuffer[i].collider != null
					&& !IsOwnCollider(ObstacleHitBuffer[i].collider))
					return false;
			return true;
		}

		private bool HasWalkableGroundAheadFromServer(Vector3 foot,
			Vector3 direction, float distance)
		{
			Vector3 predicted = foot + direction * distance;
			return MobileNPCSpawnService.TryFindSafeGroundPosition(predicted,
				0.65f, 0.65f, roamingGroundMask,
				Mathf.Min(characterController.slopeLimit, roamingMaximumSlope),
				out Vector3 actual, out Collider support, out _)
				&& support != null && !IsFurnitureSurfaceCollider(support)
				&& Mathf.Abs(actual.y - foot.y) <= 0.35f
				&& Level.checkSafeIncludingClipVolumes(actual);
		}

		/// <summary>
		/// A one-hop exit from a physically occupied, low object into a
		/// lower, open patch of genuine floor. CharacterController.Move and
		/// gravity still execute the ENTIRE descent; no warp/off-mesh link.
		/// This is not called unless normal direct travel hit a blocker.
		/// </summary>
		private bool TryChooseVerifiedFurnitureGroundExit(Vector3 goal)
		{
			if (verifiedFurnitureSupportCollider == null
				|| Time.time >= verifiedFurnitureSupportValidUntil
				|| Time.time < nextSharedFurnitureExitSlot
				|| goal.y >= transform.position.y - 0.35f
				|| PlanarDistanceSquared(goal, transform.position) < 1.0f)
				return false;
			nextSharedFurnitureExitSlot = Time.time + 0.3f;

			Vector3 toGoal = goal - transform.position;
			toGoal.y = 0f;
			Vector3 desired = toGoal.normalized;
			float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
				Mathf.Max(0.05f, characterController.radius * 0.9f));
			Vector3 chest = transform.position + Vector3.up
				* Mathf.Max(0.45f, characterController.height * 0.55f);

			for (int i = 0; i < FurnitureExitAngles.Length; ++i)
			{
				Vector3 direction = Quaternion.AngleAxis(
					FurnitureExitAngles[i], Vector3.up) * desired;
				for (int j = 0; j < FurnitureExitDistances.Length; ++j)
				{
					float step = FurnitureExitDistances[j];
					if (IsObstacleBlockingAlong(chest, radius, direction, step))
						continue;

					Vector3 candidate = transform.position + direction * step;
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
						candidate, 1.0f, 1.4f, roamingGroundMask,
						roamingMaximumSlope, out Vector3 ground,
						out Collider groundCollider, out _)
						|| groundCollider == null
						|| IsFurnitureSurfaceCollider(groundCollider)
						|| MobileNPCVaultTraversal.IsSamePhysicalObstacle(
							groundCollider, verifiedFurnitureSupportCollider)
						|| !Level.checkSafeIncludingClipVolumes(ground))
						continue;
					float drop = transform.position.y - ground.y;
					if (drop < 0.35f || drop > 1.05f
						|| IsRouteNodeOccupied(ground, radius))
						continue;

					// The actual edge must lie at least 0.75m BEHIND
					// the stopping point. Otherwise the motor would stop
					// while still on the crate and never step off.
					Vector3 behind = transform.position + direction * (step - 0.75f);
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
						behind, 1.0f, 1.4f, roamingGroundMask,
						roamingMaximumSlope, out Vector3 edgeFloor,
						out Collider edgeCollider, out _)
						|| edgeCollider == null
						|| MobileNPCVaultTraversal.IsSamePhysicalObstacle(
							edgeCollider, verifiedFurnitureSupportCollider)
						|| Mathf.Abs(edgeFloor.y - ground.y) > 0.25f)
						continue;

					// This is just a locally observed safe walking goal.
					// No physics or task success is faked by setting it.
					detourWaypoint = ground;
					detourGoalSnapshot = goal;
					hasDetourWaypoint = true;
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Same motor and local A* with half-size cells, only at close range.
		/// This extra, capped attempt is exact-goal only: a reachable cell on
		/// the WRONG side of a wall is never treated as a valid doorway.
		/// </summary>
		private bool TryBuildNarrowPassageRoute(Vector3 destination)
		{
			if (!Provider.isServer || characterController == null
				|| !characterController.enabled
				|| Time.time < nextSharedNarrowPassageSlot
				|| PlanarDistanceSquared(transform.position, destination) > 6.25f
				|| Mathf.Abs(transform.position.y - destination.y) > 0.65f)
				return false;

			// Across 20-40 NPCs avoid a second 81-node physics search in
			// every planning frame. Normal/broad A* already has its own cap.
			nextSharedNarrowPassageSlot = Time.time + 0.35f;
			narrowPassageScratch.Clear();
			if (!TryBuildLocalRoute(destination, narrowPassageScratch,
				true, RouteGridCellSize * 0.5f))
				return false;

			routeWaypoints.Clear();
			routeWaypoints.AddRange(narrowPassageScratch);
			routeWaypointIndex = 0;
			hasPartialLocalFrontier = false;
			nextPlannedRouteValidationTime = 0f;
			return routeWaypoints.Count > 0;
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
				|| IsTraversingLadder || Time.time < suppressJourneyUntil)
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
				|| Time.time < suppressJourneyUntil
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
				if (independentRouteProvider == null
					|| !independentRouteProvider.IsAvailable)
				{
					knownAccessEntryRetryUntil[entryRouteIndex] = Time.time + 5f;
					hasPendingKnownAccessPath = false;
					pendingKnownAccessEntryRouteId = -1;
					return false;
				}
				if (!independentRouteProvider.TryGetRoute(
					transform.position, entrance, learnedAccessApproach))
				{
					hasPendingKnownAccessPath = independentRouteProvider.IsRequestPending;
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
				if (independentRouteProvider == null
					|| !independentRouteProvider.IsAvailable)
				{
					hasPlannedLadderApproach = false;
					nextLadderApproachPlanTime = Time.time + 4f;
					return false;
				}
				if (!independentRouteProvider.TryGetRoute(transform.position,
					plannedLadderEntrance, ladderApproachScratch))
				{
					waitingForNativeLadderPath = independentRouteProvider.IsRequestPending;
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

		private bool TryBuildIndependentRoute(Vector3 destination)
		{
			if (Time.time < nativeNearObstructionRetryUntil
				|| independentRouteProvider == null
				|| !independentRouteProvider.TryGetRoute(transform.position, destination, routeWaypoints))
				return false;

			routeWaypointIndex = 0;
			if (routeWaypoints.Count > 0
				&& (routeWaypoints[0] - transform.position).sqrMagnitude <= 0.36f)
				routeWaypoints.RemoveAt(0);

			if (routeWaypoints.Count == 0)
				return false;

			// A globally calculated route may not contain a newly placed
			// dresser, bed, cupboard or barricade. Reject only an IMMEDIATE,
			// physically blocked step, not a distant path behind real doors.
			// Later segments remain subject to periodic motor validation.
			Vector3 next = routeWaypoints[0];
			Vector3 difference = next - transform.position;
			if (Mathf.Abs(difference.y) <= 0.8f)
			{
				difference.y = 0f;
				float distance = difference.magnitude;
				if (distance > 0.2f)
				{
					Vector3 origin = transform.position
						+ Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
					float radius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
						Mathf.Max(0.05f, characterController.radius * 0.9f));
					float sample = Mathf.Min(distance, Mathf.Max(0.5f, obstacleProbeDistance));
					Vector3 direction = difference / distance;
					if (IsObstacleBlockingAlong(origin, radius, direction, sample)
						&& !IsOnlyTemporaryActorBlockingAlong(origin, radius, direction, sample)
						&& (doorNavigation == null
							|| !doorNavigation.IsTraversableUnlockedDoorEdgeServer(
								origin, direction, radius,
								Mathf.Min(sample, 2.5f), obstacleMask)))
					{
						routeWaypoints.Clear();
						routeWaypointIndex = 0;
						hasPartialLocalFrontier = false;
						nativeNearObstructionRetryUntil = Time.time + 6f;
						nextLocalRoutePlanTime = Time.time;
						if (Time.time >= nextNativeObstructionDiagnosticAt)
						{
							nextNativeObstructionDiagnosticAt = Time.time + 6f;
							Debug.LogWarning("[MobileNPCHumanoidMotor] Native A* first step "
								+ "physically blocked (check interior furniture, barricades or walls). "
								+ "Local collision-aware route will be attempted; backend="
								+ independentRouteProvider.ObservedBackendName
								+ ", status=" + independentRouteProvider.DiagnosticStatus
								+ ", position=" + transform.position + ".", this);
						}
						return false;
					}
				}
			}
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
							|| IsFurnitureSurfaceCollider(collider)
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
						// A curb is a discrete rise onto flat support; only larger
						// level changes must obey the ramp/stair slope criterion.
						float permittedSlopeRise = flat * Mathf.Tan(Mathf.Min(
							characterController.slopeLimit, roamingMaximumSlope) * Mathf.Deg2Rad);
						if (flat < 0.2f || (Mathf.Abs(edge.y) > permittedSlopeRise
							&& (edge.y > characterController.stepOffset + 0.02f
								|| edge.y < -0.55f)))
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
			int samples = Mathf.Max(1, Mathf.CeilToInt(planarLength / 0.35f));
			// A ramp is not a succession of vertical steps: permit the
			// physically climbable rise along each short inclined segment.
			// The ground ray already confirms that every sample has a
			// surface below the controller's own slope limit.
			float segmentLength = planarLength / samples;
			float maximumRise = Mathf.Max(characterController.stepOffset + 0.05f,
				segmentLength * Mathf.Tan(Mathf.Min(characterController.slopeLimit,
					roamingMaximumSlope) * Mathf.Deg2Rad) + 0.03f);
			float lastY = from.y;
			for (int i = 1; i <= samples; ++i)
			{
				float t = (float)i / samples;
				Vector3 desired = Vector3.Lerp(from, to, t);
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(desired,
					0.46f, 0.55f, roamingGroundMask,
					Mathf.Min(characterController.slopeLimit, roamingMaximumSlope),
					out Vector3 actualFloor, out Collider floorCollider, out _)
					|| IsFurnitureSurfaceCollider(floorCollider)
					|| Mathf.Abs(actualFloor.y - desired.y) > 0.30f
					|| actualFloor.y - lastY > maximumRise
					|| lastY - actualFloor.y > Mathf.Max(0.52f, maximumRise))
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
		private void ClearVerifiedFurnitureSupport()
		{
			verifiedFurnitureSupportCollider = null;
			verifiedFurnitureSupportY = 0f;
			verifiedFurnitureSupportValidUntil = 0f;
			nextVerifiedFurnitureSupportCheckTime = 0f;
		}

		private bool TryVerifyObjectSupportAtCurrentPosition(Collider expected)
		{
			if (!Provider.isServer || expected == null || !expected.enabled
				|| characterController == null || !characterController.enabled)
				return false;
			// Probe the true support from just above the capsule's feet:
			// a ray from above the furniture could mistake a remote shelf
			// for what the NPC is actually standing on.
			Vector3 origin = transform.position + Vector3.up * 0.34f;
			int count = Physics.RaycastNonAlloc(origin, Vector3.down,
				furnitureSupportProbeHits, 0.63f, obstacleMask,
				QueryTriggerInteraction.Ignore);
			if (count <= 0 || count >= furnitureSupportProbeHits.Length)
				return false;
			Collider closestCollider = null;
			float closest = float.PositiveInfinity;
			RaycastHit nearest = default(RaycastHit);
			for (int i = 0; i < count; ++i)
			{
				RaycastHit hit = furnitureSupportProbeHits[i];
				if (hit.collider == null || IsOwnCollider(hit.collider))
					continue;
				if (hit.distance < closest)
				{
					closest = hit.distance;
					closestCollider = hit.collider;
					nearest = hit;
				}
			}
			return closestCollider != null
				&& MobileNPCVaultTraversal.IsSamePhysicalObstacle(
					closestCollider, expected)
				&& nearest.normal.y >= 0.88f
				&& Mathf.Abs(nearest.point.y + 0.05f - transform.position.y) <= 0.18f;
		}

		private void RefreshVerifiedFurnitureSupportServer()
		{
			nextVerifiedFurnitureSupportCheckTime = Time.time + 0.3f;
			if (verifiedFurnitureSupportCollider == null
				|| isPlannedJumping || IsTraversingLadder
				|| !IsGroundedUsingPlayerProbe()
				|| !TryVerifyObjectSupportAtCurrentPosition(
					verifiedFurnitureSupportCollider))
			{
				ClearVerifiedFurnitureSupport();
				return;
			}
			verifiedFurnitureSupportY = transform.position.y;
			verifiedFurnitureSupportValidUntil = Time.time + 0.6f;
		}

		private bool CanUsePhysicallyReachedFurnitureFloor(
			Collider candidate, Vector3 ground)
		{
			return candidate != null
				&& verifiedFurnitureSupportCollider != null
				&& Time.time < verifiedFurnitureSupportValidUntil
				&& MobileNPCVaultTraversal.IsSamePhysicalObstacle(
					candidate, verifiedFurnitureSupportCollider)
				&& Mathf.Abs(ground.y - verifiedFurnitureSupportY) <= 0.2f
				&& PlanarDistanceSquared(transform.position, ground) <= 25f;
		}

		private static bool IsFurnitureSurfaceCollider(Collider collider)
		{
			if (collider == null)
				return false;
			// A storage cupboard / display, bed, mannequin or sentry is
			// furniture/barricade, not architectural flooring. Checking the
			// actual HIT collider avoids relying on asset names or guessing
			// nearby world props; ordinary floors/ramps/platforms remain valid.
			Interactable interactive = collider.GetComponentInParent<Interactable>();
			return interactive is InteractableStorage
				|| interactive is InteractableBed
				|| interactive is InteractableMannequin
				|| interactive is InteractableSentry;
		}

		private bool HasContinuousLocalFloor(Vector3 from, Vector3 to)
		{
			// A midpoint alone can miss a gap beside a doorway jamb or at
			// either end of a diagonal. Sample the entire edge at <=0.45m
			// intervals; every adjacent pair must be traversable by the
			// actual CharacterController's configured step height.
			Vector3 planar = to - from;
			planar.y = 0f;
			int segments = Mathf.Max(2, Mathf.CeilToInt(planar.magnitude / 0.45f));
			float previousY = from.y;
			float segmentLength = planar.magnitude / segments;
			float riseLimit = Mathf.Max(characterController.stepOffset + 0.05f,
				segmentLength * Mathf.Tan(Mathf.Min(characterController.slopeLimit,
					roamingMaximumSlope) * Mathf.Deg2Rad) + 0.03f);
			for (int i = 1; i < segments; ++i)
			{
				Vector3 sample = Vector3.Lerp(from, to, (float)i / segments);
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(sample,
					0.55f, 0.65f, roamingGroundMask,
					Mathf.Min(characterController.slopeLimit, roamingMaximumSlope),
					out Vector3 floor, out Collider floorCollider, out _)
					|| (IsFurnitureSurfaceCollider(floorCollider)
						&& !CanUsePhysicallyReachedFurnitureFloor(floorCollider, floor))
					|| Mathf.Abs(floor.y - sample.y) > 0.35f
					|| floor.y - previousY > riseLimit
					|| previousY - floor.y > 0.55f
					|| !Level.checkSafeIncludingClipVolumes(floor))
					return false;
				previousY = floor.y;
			}
			return to.y - previousY <= riseLimit
				&& previousY - to.y <= 0.55f;
		}

		private bool TryBuildLocalRoute(Vector3 destination,
			List<Vector3> alternateOutput = null, bool requireExactLocalGoal = false,
			float requestedCellSize = 0f)
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

			// Repeated PHYSICAL stalls behind long walls require a wider search
			// horizon, not a smaller random steering twitch. Preserve 81 nodes
			// and all collision/floor/door checks but sample at 2m spacing
			// when the fine 1.25m grid has failed at least twice. Exact
			// cover/door/ladder access checks always keep fine resolution.
			float localCellSize = requestedCellSize > 0f
				? Mathf.Clamp(requestedCellSize, RouteGridCellSize * 0.5f, RouteGridCellSize)
				: !requireExactLocalGoal
					&& consecutiveStuckRecoveries >= 2 && goalOffset.sqrMagnitude > 36f
						? (autonomyPlanner != null
							&& autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge
							&& goalOffset.sqrMagnitude > 196f ? 3.0f : 2.0f)
						: RouteGridCellSize;

			Vector3 localGoal = transform.position + Vector3.ClampMagnitude(goalOffset, RouteGridCenter * localCellSize);
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
						(x - RouteGridCenter) * localCellSize, 0f,
						(z - RouteGridCenter) * localCellSize);
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
						1.6f, 2.2f, roamingGroundMask, roamingMaximumSlope,
						out Vector3 resolved, out Collider floorCollider, out _)
						|| (IsFurnitureSurfaceCollider(floorCollider)
							&& !CanUsePhysicallyReachedFurnitureFloor(
								floorCollider, resolved)))
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
			int goalX = Mathf.Clamp(RouteGridCenter + Mathf.RoundToInt(goalFromBase.x / localCellSize), 0, RouteGridSide - 1);
			int goalZ = Mathf.Clamp(RouteGridCenter + Mathf.RoundToInt(goalFromBase.z / localCellSize), 0, RouteGridSide - 1);
			int preferredGoal = goalZ * RouteGridSide + goalX;
			// When this preferred cell is the approach where the survivor just
			// got stuck, do not stop A* there or select it again. Explore
			// physically connected lateral frontiers before retrying that wall.
			bool surveyingObservedWall = !requireExactLocalGoal
				&& IsObservingEntranceSurvey(destination, basePosition);
			bool preferredRecentlyBlocked = !requireExactLocalGoal
				&& walkable[preferredGoal]
				&& LocalDeadEndPenalty(positions[preferredGoal], destination) >= 2.5f;
			// Only during firsthand exploration of an observed solid wall
			// does a reachable target-facing cell lose its early-exit
			// privilege. All 81 nodes remain collision/ground validated.
			bool evaluateAllSurveyFrontiers = surveyingObservedWall
				&& PlanarDistanceSquared(destination, basePosition) > 16f;

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
			// Exact approach routes (doorways, cover, ladder access) must not
			// fail prematurely because broad roaming used their edge budget.
			// Keep both searches bounded; exact paths receive a modest extra
			// allowance while the shared roaming admission still throttles crowds.
			int continuousFloorProbeBudget = requireExactLocalGoal ? 160 : 100;

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
					&& !preferredRecentlyBlocked && !evaluateAllSurveyFrontiers)
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
					// A continuous ramp can rise farther than stepOffset
					// across one grid cell. Restrict by the actual controller
					// slope limit instead; the sampled continuous-floor probe
					// below still rejects walls, ledges and disconnected floors.
					edge.y = 0f;
					float edgeLength = edge.magnitude;
					if (edgeLength < 0.1f)
						continue;
					float riseLimit = edgeLength * Mathf.Tan(Mathf.Min(
						characterController.slopeLimit, roamingMaximumSlope) * Mathf.Deg2Rad)
						+ characterController.stepOffset;
					if (Mathf.Abs(verticalChange) > riseLimit)
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
					// The route planner must avoid a currently personally KNOWN
					// zombie cluster, not merely avoid a dangerous destination.
					// Short-lived firsthand risk adds a traversal cost but never
					// grants knowledge of zombies hidden across the whole map.
					float observedZombieExposure = zombieRisk != null
						? zombieRisk.GetKnownRiskAtServer(positions[next]) : 0f;
					float zombieCost = observedZombieExposure * 5.0f;
					float tentative = costs[current] + edgeLength + exposureCost + zombieCost;
					if (tentative < costs[next])
					{
						costs[next] = tentative;
						previous[next] = current;
					}
				}
			}

			int goal = -1;
			float bestFrontierScore = float.PositiveInfinity;
			Vector3 surveyTangent = Vector3.zero;
			if (surveyingObservedWall)
			{
				Vector3 toSurveyGoal = observedEntranceSurveyGoal - observedEntranceSurveyOrigin;
				toSurveyGoal.y = 0f;
				if (toSurveyGoal.sqrMagnitude > 0.1f)
					surveyTangent = new Vector3(-toSurveyGoal.z, 0f, toSurveyGoal.x).normalized
						* observedEntranceSurveySide;
			}
			for (int i = 0; i < RouteGridNodeLimit; ++i)
			{
				if (!closed[i] || !walkable[i] || i == start
					|| previous[i] < 0 || float.IsInfinity(costs[i]))
					continue;
				if (i == preferredGoal && !preferredRecentlyBlocked
					&& !evaluateAllSurveyFrontiers)
				{
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
				// Score only ALREADY connected physical frontiers:
				// the preference cannot grant a passage through a wall.
				float surveyLateral = surveyingObservedWall
					? Mathf.Clamp(Vector3.Dot(positions[i] - basePosition, surveyTangent),
						-2f, 4f) : 0f;
				float frontierScore = forwardDistance
					+ Mathf.Max(0f, costs[i]) * 0.12f
					+ LocalDeadEndPenalty(positions[i], destination)
					- (boundary ? 0.65f : 0f)
					- surveyLateral * 1.4f;
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
				// Never tag exact-cover, obstacle comparison, or known access
				// scratch routes as speculative frontiers. The marker is
				// tied to this specific endpoint and its real final objective.
				hasPartialLocalFrontier = !requireExactLocalGoal
					&& PlanarDistanceSquared(positions[goal], destination) > 9f;
				partialLocalFrontier = positions[goal];
				partialLocalTarget = destination;
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

		/// <summary>
		/// The SAME body-sized occupancy test used by actual local A*,
		/// unlike Physics.CheckCapsule, ignores our own CharacterController
		/// and keeps a small floor clearance. This is just a destination
		/// preflight: it never proves an A* route or authorizes a jump.
		/// </summary>
		public bool IsValidScoutingFootingServer(Vector3 grounded)
		{
			return Provider.isServer && hasInitialized
				&& characterController != null && characterController.enabled
				&& Level.checkSafeIncludingClipVolumes(grounded)
				&& !IsRouteNodeOccupied(grounded,
					Mathf.Clamp(obstacleProbeRadius, 0.05f,
						characterController.radius * 0.9f));
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

		/// <summary>
		/// Resume a firsthand A-Life scouting intention after the native brain
		/// finishes an interruption. It may have become occupied, blocked,
		/// too distant, on another floor, or assigned to another teammate.
		/// Reverify all facts instead of teleporting or reusing stale A*.
		/// </summary>
		private bool TryResumeSuspendedRoutineWaypointServer()
		{
			if (!hasSuspendedRoutineWaypoint || autonomyPlanner == null)
				return false;
			hasSuspendedRoutineWaypoint = false;
			if (Time.time >= suspendedRoutineExpiresAt
				|| autonomyPlanner.CurrentJob != suspendedRoutineJob
				|| !IsWithinHomeTerritory
				|| !Level.checkSafeIncludingClipVolumes(suspendedRoutineWaypoint)
				|| !autonomyPlanner.IsCandidateAllowedServer(suspendedRoutineWaypoint)
				|| PlanarDistanceSquared(suspendedRoutineWaypoint, spawnPosition)
					> HomeTerritoryRadius * HomeTerritoryRadius
				|| PlanarDistanceSquared(suspendedRoutineWaypoint, transform.position)
					< stoppingDistance * stoppingDistance)
				return false;
			if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
				suspendedRoutineWaypoint, 0.95f, 1.25f,
				(RayMasks.BLOCK_COLLISION | RayMasks.GROUND2),
				Mathf.Min(75f, characterController.slopeLimit),
				out Vector3 ground, out Collider support, out _)
				|| support == null
				|| support.GetComponentInParent<Player>() != null
				|| support.GetComponentInParent<Zombie>() != null
				|| support.GetComponentInParent<MobileNPCHumanoidVitals>() != null
				|| Mathf.Abs(ground.y - suspendedRoutineWaypoint.y) > 0.45f
				|| !autonomyPlanner.TryClaimObservedExplorationSectorServer(ground))
				return false;
			roamingDestination = ground;
			hasRoamingDestination = true;
			return true;
		}

		/// <summary>Execute a director's coarse bearing using this motor's
		/// normal real-world ground/path verification. Director never selects
		/// the destination and never tells us whether an item actually exists.
		/// </summary>
		private bool TryUseNoviceExplorationBearingServer(float radius)
		{
			if (autonomyPlanner == null || noviceDirectorHintsUsed >= 2
				|| Time.time < nextNoviceBearingAttemptAt
				|| (autonomyPlanner.CurrentJob != MobileNPCAutonomyPlanner.Job.Patrol
					&& autonomyPlanner.CurrentJob != MobileNPCAutonomyPlanner.Job.Scavenge))
				return false;
			// The optional director is sampled at most once per bounded interval.
			// A failed hint or blocked world geometry must NOT spend the survivor's
			// lifetime experience budget nor burn physics probes every roam cycle.
			nextNoviceBearingAttemptAt = Time.time + Random.Range(18f, 28f);
			if (!MobileNPCEncounterDirector.TrySuggestNoviceExplorationBearingServer(
				brainDriver, transform.position, out Vector3 bearing))
				return false;
			for (int i = 0; i < 5; ++i)
			{
				Vector3 direction = Quaternion.AngleAxis(Random.Range(-20f, 20f),
					Vector3.up) * bearing;
				float length = Random.Range(22f, Mathf.Min(58f, Mathf.Max(23f, radius)));
				Vector3 estimate = transform.position + direction * length;
				if (PlanarDistanceSquared(estimate, spawnPosition)
					> HomeTerritoryRadius * HomeTerritoryRadius
					|| PlanarDistanceSquared(estimate, spawnPosition) > radius * radius)
					continue;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(estimate,
					Mathf.Max(1f, roamingSearchHeight),
					Mathf.Min(5f, Mathf.Max(0f, roamingMaximumDrop)),
					roamingGroundMask, Mathf.Min(55f, roamingMaximumSlope),
					out Vector3 destination, out _)
					|| Mathf.Abs(destination.y - transform.position.y) > 3.5f
					|| PlanarDistanceSquared(destination, spawnPosition)
						> HomeTerritoryRadius * HomeTerritoryRadius
					|| !autonomyPlanner.IsCandidateAllowedServer(destination))
					continue;
				// An approximate direction is not firsthand discovery:
				// do NOT create a learned loot site or reserve the observed
				// sector until the NPC physically reaches and inspects it.
				roamingDestination = destination;
				hasRoamingDestination = true;
				lastRoamingDirection = direction;
				hasLastRoamingDirection = true;
				// A hint is used only after real grounded destination validation.
				// It is never counted from a failed proposal or mere map spawn.
				++noviceDirectorHintsUsed;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				Debug.Log("[MobileNPCHumanoidMotor] A-Life novice director hinted a "
					+ "general search bearing for " + name
					+ "; walking requires normal physical path, no loot guaranteed.", this);
#endif
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
			Vector3 routineAnchor = spawnPosition;
			float routineMinimumStep = 24f;
			float routineMaximumStep = 72f;
			float structurePreference = 0.55f;
			if (autonomyPlanner != null)
			{
				autonomyPlanner.GetRoamingProfileServer(spawnPosition,
					out routineAnchor, out routineMinimumStep, out routineMaximumStep,
					out structurePreference);
				if (autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Recover)
				{
					nextRoamingDecisionTime = Time.time + 2f;
					return;
				}
			}
			// Guards operate close to their real base, scavengers search a
			// wider neighborhood, and explorers retain the original long
			// range. Do not accidentally shrink all A-Life exploration to
			// the length of one patrol step.
			if (autonomyPlanner != null
				&& autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.GuardSettlement)
				effectiveRoamingRadius = Mathf.Min(effectiveRoamingRadius, 26f);
			else if (autonomyPlanner != null
				&& autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge)
				effectiveRoamingRadius = Mathf.Min(effectiveRoamingRadius, 110f);

			// A full courier's REAL trade delivery outranks optional exploring.
			// This is still collision-aware ordinary walking, not a teleport.
			if (TryChooseTraderDeliveryApproachServer())
				return;

			// Continue an ALREADY visited urban site before randomly selecting
			// a different building, but only if no tactical/combat goal owns
			// movement and all proposed floor/collision checks pass.
			MobileNPCInteriorSurvey interiorSurvey = GetComponent<MobileNPCInteriorSurvey>();
			if (interiorSurvey != null && interiorSurvey.IsWaitingForInspectionServer)
			{
				// The NPC is actually inspecting the arrived room. Do not
				// immediately overwrite the site with another random point.
				nextRoamingDecisionTime = Time.time + 0.6f;
				return;
			}
			if (autonomyPlanner != null
				&& autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge
				&& interiorSurvey != null
				&& interiorSurvey.TryChooseNextRoomServer(effectiveRoamingRadius,
					spawnPosition, HomeTerritoryRadius,
					roamingGroundMask, roamingMaximumSlope,
					out Vector3 nextRoom))
			{
				if (autonomyPlanner.TryClaimObservedExplorationSectorServer(nextRoom))
				{
					interiorSurvey.ConfirmNextRoomAssignedServer(nextRoom);
					roamingDestination = nextRoom;
					hasRoamingDestination = true;
					return;
				}
				// A squadmate reserved this room; do not claim progress.
				interiorSurvey.DeferUnclaimedRoomServer();
			}
			if (interiorSurvey != null && interiorSurvey.IsActiveSurveyServer
				&& autonomyPlanner != null
				&& autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge)
			{
				// Do not spawn a NEW random destination while a site survey is
				// waiting for a scan, lease retry, or short candidate cooldown.
				// Combat and medical/radio brain goals are handled above.
				nextRoamingDecisionTime = Time.time + 0.6f;
				return;
			}

			// A survivor may finish combat 80m from home and *then* be
			// assigned guard duty. Without an explicit physical return trip,
			// every nearby guard waypoint violates the small job radius,
			// leaving the NPC idle forever inside its 220m home territory.
			// The anchor was already validated when the group/site was made;
			// the ordinary motor must still find a real path to get there.
			if (PlanarDistanceSquared(transform.position, routineAnchor)
				> effectiveRoamingRadius * effectiveRoamingRadius)
			{
				if (Level.checkSafeIncludingClipVolumes(routineAnchor))
				{
					roamingDestination = routineAnchor;
					hasRoamingDestination = true;
					return;
				}
				nextRoamingDecisionTime = Time.time + 6f;
				return;
			}

			// A scavenger searches nearby MAP-AUTHORED inspection spots
			// before trying arbitrary building centers. This is not a loot
			// detector: only native ItemSpawnpoint geometry is consulted, never
			// ItemManager availability, item IDs, inaccessible containers or
			// any enemy position. The path must still be physically traversed.
			if (autonomyPlanner != null
				&& autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge
				&& TryChooseLocalLootInspectionSiteServer(effectiveRoamingRadius,
					out Vector3 inspectionSite)
				&& (autonomyPlanner.TryClaimObservedExplorationSectorServer(inspectionSite)))
			{
				roamingDestination = inspectionSite;
				hasRoamingDestination = true;
				if (interiorSurvey != null)
					interiorSurvey.TryBeginAtAuthoredSiteServer(inspectionSite);
				Vector3 direction = inspectionSite - transform.position;
				direction.y = 0f;
				if (direction.sqrMagnitude > 0.01f)
				{
					lastRoamingDirection = direction.normalized;
					hasLastRoamingDirection = true;
				}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				Debug.Log("[MobileNPCUrbanScavenge] NPC=" + name
					+ " selected an authored local loot-search site at "
					+ inspectionSite + " (availability unknown; route unverified).", this);
#endif
				return;
			}

			// A survivor ALREADY in an urban area first inspects nearby
			// buildings. A broad novice director compass bearing should NOT
			// pull him out of town before he has checked the local structures.
			// Prefer actual map structures, but keep searching over open terrain when a
			// beach, field or remote island has no nearby large objects.
			// Scavenging is a committed search job, not a random roll. Always
			// inspect an observed structure before falling back to open terrain.
			// Other presets retain their patrol/guard structure preference.
			if ((autonomyPlanner != null
				&& autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge
				|| Random.value < structurePreference)
				&& TryChooseStructureExplorationPoint(effectiveRoamingRadius, out Vector3 structurePoint)
				&& PlanarDistanceSquared(structurePoint, spawnPosition)
					<= HomeTerritoryRadius * HomeTerritoryRadius
				&& PlanarDistanceSquared(structurePoint, routineAnchor)
					<= effectiveRoamingRadius * effectiveRoamingRadius
				&& (autonomyPlanner == null || autonomyPlanner.IsCandidateAllowedServer(structurePoint))
				// Reserve only AFTER the native ground, actual object and territory
				// checks pass. Other group members won't rush the same structure.
				&& (autonomyPlanner == null || autonomyPlanner.TryClaimObservedExplorationSectorServer(structurePoint)))
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

			// Only if no reachable/novel structure could be nominated,
			// consult the coarse novice regional guide. This is not a loot oracle.
			if (TryUseNoviceExplorationBearingServer(effectiveRoamingRadius))
				return;

			// Continue in a roughly straight line for a while, validating each prospective
			// ground point. If the NPC approaches the edge of its exploration radius, turn
			// back inward instead of walking to the old spawn point and repeatedly restarting.
			Vector3 fromHome = transform.position - routineAnchor;
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
				float distance = Random.Range(routineMinimumStep, routineMaximumStep);
				Vector3 candidate = transform.position + direction * distance;
				Vector3 fromHomeCandidate = candidate - routineAnchor;
				fromHomeCandidate.y = 0f;
				if (fromHomeCandidate.sqrMagnitude > effectiveRoamingRadius * effectiveRoamingRadius)
					continue;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, Mathf.Max(1f, roamingSearchHeight),
					Mathf.Min(8f, Mathf.Max(0f, roamingMaximumDrop)), roamingGroundMask,
					Mathf.Clamp(roamingMaximumSlope, 25f, 42f), out Vector3 resolved, out _))
					continue;
				if (Mathf.Abs(resolved.y - transform.position.y) > 8f)
					continue;
				if (autonomyPlanner != null && !autonomyPlanner.IsCandidateAllowedServer(resolved))
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

		/// <summary>
		/// Bounded urban inspection of local native loot-spawn geometry.
		/// Spawn sites are weak hints about where to LOOK, not knowledge
		/// that a specific item currently exists. Never reads ItemManager.
		/// </summary>
		/// <summary>
		/// A courier's delivery destination is a real verified standing area
		/// within conversational reach of a living group vendor. No teleport,
		/// path guarantee or remote stock transfer is allowed. If geometry
		/// rejects the approach, ordinary scouting/restock policy remains.
		/// </summary>
		private bool TryChooseTraderDeliveryApproachServer()
		{
			if (!Provider.isServer || MobileNPCSettings.CombatMode
				|| MobileNPCArchetypePolicy.ForNPC(gameObject)
					!= MobileNPCArchetypePolicy.Preset.Trader)
				return false;
			MobileNPCInventory pack = GetComponent<MobileNPCInventory>();
			MobileNPCGroupMember member = GetComponent<MobileNPCGroupMember>();
			if (pack == null || pack.FreeSlots > 2 || member == null
				|| !member.HasSettlement
				|| !member.TryGetLivingLeaderServer(out MobileNPCGroupMember seller)
				|| seller == null || seller == member
				|| MobileNPCArchetypePolicy.ForNPC(seller.gameObject)
					!= MobileNPCArchetypePolicy.Preset.Trader
				|| PlanarDistanceSquared(seller.transform.position, member.GroupAnchor) > 9f * 9f
				|| PlanarDistanceSquared(seller.transform.position, spawnPosition)
					> HomeTerritoryRadius * HomeTerritoryRadius)
				return false;
			// Delivery is handled by MobileNPCTraderLogistics when the
			// survivor gets within 3.5m. Do not steal every idle tick when
			// already within transfer reach.
			if ((seller.transform.position - transform.position).sqrMagnitude <= 3f * 3f)
				return false;
			for (int attempt = 0; attempt < 6; ++attempt)
			{
				float bearing = Mathf.PI * 2f * attempt / 6f;
				Vector3 candidate = seller.transform.position
					+ new Vector3(Mathf.Cos(bearing), 0f, Mathf.Sin(bearing)) * 2.1f;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
					Mathf.Max(1f, roamingSearchHeight),
					Mathf.Min(4f, Mathf.Max(0f, roamingMaximumDrop)),
					roamingGroundMask, Mathf.Min(55f, roamingMaximumSlope),
					out Vector3 ground, out _)
					|| Mathf.Abs(ground.y - seller.transform.position.y) > 1.25f
					|| PlanarDistanceSquared(ground, spawnPosition)
						> HomeTerritoryRadius * HomeTerritoryRadius
					|| !IsValidScoutingFootingServer(ground))
					continue;
				roamingDestination = ground;
				hasRoamingDestination = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				Debug.Log("[MobileNPCTraderLogistics] Courier " + name
					+ " returning physically to vendor " + seller.name
					+ " at " + ground, this);
#endif
				return true;
			}
			return false;
		}

		private bool TryChooseLocalLootInspectionSiteServer(float radius,
			out Vector3 destination)
		{
			destination = Vector3.zero;
			if (!Provider.isServer || LevelItems.spawns == null || autonomyPlanner == null)
				return false;
			Vector2Int center = Regions.GetCoordinateVector2Int(transform.position);
			int scanned = 0;
			int physicalProbes = 0;
			float bestScore = float.PositiveInfinity;
			float range = Mathf.Min(radius, 84f);
			for (int regionIndex = 0;
				regionIndex < UrbanRegionOffsets.Length && scanned < 144;
				++regionIndex)
			{
				Vector2Int offset = UrbanRegionOffsets[regionIndex];
				int x = center.x + offset.x, z = center.y + offset.y;
				if (x < 0 || z < 0 || x >= Regions.WORLD_SIZE
					|| z >= Regions.WORLD_SIZE)
					continue;
				List<ItemSpawnpoint> sites = LevelItems.spawns[x, z];
				if (sites == null || sites.Count == 0)
					continue;
				int quota = Mathf.Min(144 - scanned, regionIndex == 0 ? 48 : 12);
				int stride = Mathf.Max(1, Mathf.CeilToInt(sites.Count / (float)quota));
				int checkedInRegion = 0;
				for (int i = 0; i < sites.Count && checkedInRegion < quota; i += stride)
				{
					++scanned;
					++checkedInRegion;
						ItemSpawnpoint site = sites[i];
						if (site == null)
							continue;
						Vector3 hint = site.point;
						float distanceSq = PlanarDistanceSquared(hint, transform.position);
						if (distanceSq < 5f * 5f || distanceSq > range * range
							|| PlanarDistanceSquared(hint, spawnPosition)
								> HomeTerritoryRadius * HomeTerritoryRadius
							|| Mathf.Abs(hint.y - transform.position.y) > 2.6f
							|| !autonomyPlanner.IsCandidateAllowedServer(hint))
							continue;
						// Limit expensive terrain/capsule probes to twelve per
						// roaming decision, even on dense city maps.
						if (physicalProbes++ >= 12)
								break;
						if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
							hint, 1.25f, 2.0f, roamingGroundMask,
							Mathf.Min(55f, roamingMaximumSlope),
							out Vector3 ground, out _)
							|| Mathf.Abs(ground.y - hint.y) > 1.7f
							|| PlanarDistanceSquared(ground, transform.position) < 4f * 4f
							|| !autonomyPlanner.IsCandidateAllowedServer(ground)
							|| !IsValidScoutingFootingServer(ground))
							continue;
						float knownRisk = zombieRisk != null
							? zombieRisk.GetKnownRiskAtServer(ground) : 0f;
						if (knownRisk >= 0.75f)
							continue; // postpone loot until local known threat clears
						float score = distanceSq * Random.Range(0.78f, 1.22f)
							+ knownRisk * 70f;
						if (score < bestScore)
						{
							bestScore = score;
							destination = ground;
						}
				}
			}
			return bestScore < float.PositiveInfinity;
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
					// No 6-metre exclusion in towns: a NPC spawned INSIDE
					// or beside the building is exactly who should inspect it.
					if (offset.sqrMagnitude > radius * radius
						|| fromHome.sqrMagnitude > HomeTerritoryRadius * HomeTerritoryRadius)
						continue;

					explorationObjects.Add(levelObject);
				}
			}

			if (explorationObjects.Count == 0)
				return false;

			// Focus on nearby REAL large map objects (especially in towns),
			// not a uniformly random building at the edge of a 110m radius.
			// At most six structures and six candidate footprints per decision.
			// Ground/nav/door obstacles remain authoritative; nothing teleports.
			bool activelyScavenging = autonomyPlanner != null
				&& autonomyPlanner.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge;
			int attempts = Mathf.Min(6, explorationObjects.Count);
			for (int attempt = 0; attempt < attempts; ++attempt)
			{
				int chosenIndex = -1;
				float bestScore = float.PositiveInfinity;
				for (int i = 0; i < explorationObjects.Count; ++i)
				{
					LevelObject obj = explorationObjects[i];
					if (obj == null || obj.transform == null) continue;
					float planar = PlanarDistanceSquared(transform.position, obj.transform.position);
					// Occasional variance prevents one blocked nearby facade
					// from indefinitely beating every adjacent building.
					float score = planar * Random.Range(0.75f, 1.25f);
					if (score < bestScore) { bestScore = score; chosenIndex = i; }
				}
				if (chosenIndex < 0) break;
				LevelObject levelObject = explorationObjects[chosenIndex];
				explorationObjects.RemoveAt(chosenIndex);
				if (levelObject == null || levelObject.transform == null)
					continue;

				Vector3 center = levelObject.transform.position;
				// Try several distinct spots within the real object's local
				// footprint before sampling an exterior facade. In a house,
				// this gives an actual interior room route which the existing
				// door/portal/A* motor must physically traverse. A tree or
				// inaccessible solid geometry simply fails the capsule test.
				for (int sample = 0; sample < (activelyScavenging ? 6 : 3); ++sample)
				{
					float distance = activelyScavenging && sample < 4
						? (sample == 0 ? 0f : Random.Range(1f, 3.1f))
						: Random.Range(3f, 7f);
					Vector2 shift = Random.insideUnitCircle.normalized * distance;
					Vector3 candidate = center + new Vector3(shift.x, 0f, shift.y);
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
						Mathf.Max(1f, roamingSearchHeight),
						Mathf.Max(0f, roamingMaximumDrop), roamingGroundMask,
						roamingMaximumSlope, out Vector3 grounded, out _)
						|| Mathf.Abs(grounded.y - transform.position.y) > 2.2f
						|| PlanarDistanceSquared(grounded, transform.position) < 3.5f * 3.5f
						|| PlanarDistanceSquared(grounded, spawnPosition)
							> HomeTerritoryRadius * HomeTerritoryRadius
						|| !IsValidScoutingFootingServer(grounded)
						|| autonomyPlanner != null
							&& !autonomyPlanner.IsCandidateAllowedServer(grounded))
						continue;
					// This is only a proposal; the caller FIRST verifies its
					// home/job radius and THEN requests the group lease exactly
					// once. Invalid suggestions never reserve a work sector.
					destination = grounded;
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
