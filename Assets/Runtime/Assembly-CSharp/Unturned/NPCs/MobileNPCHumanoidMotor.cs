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
		[SerializeField, Min(0.1f)] private float jumpImpulse = 4.6f;
		[SerializeField, Min(0.5f)] private float jumpCooldown = 2.5f;
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
		private MobileNPCNativeAStarRouteProvider nativeAStarRouteProvider;
		private Vector3 spawnPosition;
		private Vector3 velocity;
		private bool hasInitialized;
		private bool targetIsVisible;
		private readonly MobileNPCLocomotionPolicy locomotionPolicy = new MobileNPCLocomotionPolicy();
		private float nextJumpTime;
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
		private readonly List<Vector3> routeWaypoints = new List<Vector3>(16);
		private readonly List<RegionCoordinate> explorationRegions = new List<RegionCoordinate>(16);
		private readonly List<LevelObject> explorationObjects = new List<LevelObject>(64);
		// Reuse the bounded search workspace so several blocked NPCs do not allocate
		// five arrays and a reconstruction list on every route-planning attempt.
		private readonly Vector3[] routeSearchPositions = new Vector3[RouteGridNodeLimit];
		private readonly bool[] routeSearchWalkable = new bool[RouteGridNodeLimit];
		private readonly float[] routeSearchCosts = new float[RouteGridNodeLimit];
		private readonly int[] routeSearchPrevious = new int[RouteGridNodeLimit];
		private readonly bool[] routeSearchClosed = new bool[RouteGridNodeLimit];
		private readonly List<Vector3> reversedRouteScratch = new List<Vector3>(RouteGridSide);
		private int routeWaypointIndex;
		private float nextRoutePlanTime;
		private float nextLocalRoutePlanTime;
		private float nextPlannedRouteValidationTime;
		private Vector3 lastRoamingDirection;
		private bool hasLastRoamingDirection;
		private int avoidanceSide = 1;
		private float avoidanceSideUntil;
		private int consecutiveStuckRecoveries;

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
			nextLocalRoutePlanTime = 0f;
			nextPlannedRouteValidationTime = 0f;
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
			avoidanceSide = Random.value < 0.5f ? -1 : 1;
			hasInitialized = true;
		}

		private void Awake()
		{
			characterController = GetComponent<CharacterController>();
			brainDriver = GetComponent<MobileNPCBrainDriver>();
			injurySystem = GetComponent<MobileNPCInjurySystem>();
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

			bool hasDestination = hasBrainDestination || hasRoamingDestination;
			Vector3 destination = hasBrainDestination ? brainDriver.Destination : roamingDestination;
			if (!hasDestination)
			{
				// Completed/expired goals must not leave the previous route waiting to
				// steer the next unrelated investigation or roaming destination.
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				hasDetourWaypoint = false;
				useRecoverySteering = false;
			}
			if (hasDestination)
			{
				// Keep a planned route while its goal remains stable. The previous condition
				// cleared routeWaypoints whenever hasDetourWaypoint was false, which is also
				// the normal state for a multi-corner native A* route.
				if (PlanarDistanceSquared(destination, detourGoalSnapshot) > 2.25f)
				{
					hasDetourWaypoint = false;
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
				}
				else if (routeWaypoints.Count == 0 && !hasDetourWaypoint)
				{
					routeWaypointIndex = 0;
				}

				// Ask for a map-wide path before the NPC gets close enough to collide with
				// something. This is essential for open-world goals separated by buildings,
				// fences or terrain; local obstacle probing alone cannot discover such routes.
				Vector3 globalGoalOffset = destination - transform.position;
				globalGoalOffset.y = 0f;
				if (!isUnderwater && routeWaypoints.Count == 0 && !hasDetourWaypoint
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
				Vector3 movementDestination = routeWaypointIndex < routeWaypoints.Count
					? routeWaypoints[routeWaypointIndex]
					: (hasDetourWaypoint ? detourWaypoint : destination);

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
							// A real wall/closed door invalidates the route. A passing
							// person or zombie does not invalidate a route through a
							// doorway: local collision avoidance may yield to them.
							routeWaypoints.Clear();
							routeWaypointIndex = 0;
							hasDetourWaypoint = false;
							nextDetourAttemptTime = Time.time;
							movementDestination = destination;
						}
					}
				}

				Vector3 goalOffset = destination - transform.position;
				goalOffset.y = 0f;
				if (!hasDetourWaypoint && routeWaypoints.Count == 0
					&& goalOffset.sqrMagnitude > stoppingDistance * stoppingDistance
					&& Time.time >= nextDetourAttemptTime
					&& IsObstacleBlocking(goalOffset.normalized))
				{
					TryCreateDetourWaypoint(destination);
					if (routeWaypointIndex < routeWaypoints.Count)
						movementDestination = routeWaypoints[routeWaypointIndex];
					else if (hasDetourWaypoint)
						movementDestination = detourWaypoint;
				}
				Vector3 offset = movementDestination - transform.position;
				offset.y = 0.0f;
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
			if (brainDriver.IsListening || brainDriver.IsAmbushPreparing)
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
			UpdateCrouchStance((gait == MobileNPCLocomotionPolicy.Gait.Sneak
				|| brainDriver.IsListening || brainDriver.IsAmbushPreparing) && !isUnderwater);
			float speed = gait == MobileNPCLocomotionPolicy.Gait.Sprint && runWhenPursuing
				? runSpeed : gait == MobileNPCLocomotionPolicy.Gait.Sneak ? sneakSpeed : walkSpeed;
			speed *= injuryMovementMultiplier;
			Vector3 motion = planarDirection * speed;

			// Match the player's grounded handling: CharacterController.isGrounded alone can be
			// one frame behind, so confirm contact with the same short BLOCK_COLLISION sphere cast.
			bool groundedBeforeMove = !isUnderwater && IsGroundedUsingPlayerProbe();
			if (isUnderwater)
			{
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

				// Jump only when a genuinely low obstacle blocks the walking capsule,
				// ground is confirmed, and the direction/landing area remain valid.
				// No bunny hopping while sneaking, badly injured or under cover.
				if (wantedToMove && groundedBeforeMove && Time.time >= nextJumpTime
					&& gait != MobileNPCLocomotionPolicy.Gait.Sneak
					&& injuryMovementMultiplier > 0.5f
					&& ShouldJumpOverLowObstacle(planarDirection))
				{
					velocity.y = Mathf.Max(velocity.y, jumpImpulse);
					motion.y = velocity.y;
					nextJumpTime = Time.time + Mathf.Max(0.5f, jumpCooldown);
				}
			}

			Vector3 positionBeforeMovement = transform.position;
			CollisionFlags collision = characterController.Move(motion * deltaTime);
			if ((collision & CollisionFlags.Below) != 0 && velocity.y < 0.0f)
			{
				velocity.y = -2.0f;
			}
			// A side contact can coexist with genuine slide-along-wall progress.
			// Use actual planar displacement rather than treating every side
			// collision as zero movement for animation/footstep decisions.
			Vector3 planarDisplacement = transform.position - positionBeforeMovement;
			planarDisplacement.y = 0f;
			IsMoving = wantedToMove && planarDisplacement.sqrMagnitude > 0.000001f;
			// Noise is based on actual movement, not merely the desired AI gait.
			// Crouching suppresses the zombie-alert call; running is louder than walking.
			// Native zombie perception remains responsible for deciding investigation.
			if (IsMoving && !isUnderwater && Time.time >= nextFootstepNoiseAt)
			{
				float audibleRadius = IsCrouched ? 0f : IsSprinting ? 9f : 3.5f;
				nextFootstepNoiseAt = Time.time + (IsSprinting ? 0.36f : 0.55f);
				if (audibleRadius > 0f)
					ZombieManager.alertZombiesToNoise(transform.position, audibleRadius);
			}
			UpdateStuckRecovery(wantedToMove, deltaTime);
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
		private bool ShouldJumpOverLowObstacle(Vector3 direction)
		{
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.01f)
				return false;
			direction.Normalize();
			Vector3 lower = transform.position + Vector3.up * 0.28f;
			Vector3 upper = transform.position + Vector3.up * 1.25f;
			float distance = Mathf.Max(0.7f, characterController.radius + 0.45f);
			// Check only world obstacles, excluding our own hitbox.
			bool lowHit = IsObstacleBlockingAlong(lower, 0.12f, direction, distance);
			bool highHit = IsObstacleBlockingAlong(upper, 0.12f, direction, distance);
			return lowHit && !highHit && HasWalkableGroundAhead(direction);
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
				// A failed detour must not keep pulling the NPC toward the same unusable point.
				// Clear it so the next bounded attempt can choose a different side.
				hasDetourWaypoint = false;
				routeWaypoints.Clear();
				routeWaypointIndex = 0;
				nextDetourAttemptTime = Time.time + 0.2f;
				// Do not randomly flip left/right each time the agent touches a wall.
				// Keep one side until a genuine route attempt has had time to progress.
				consecutiveStuckRecoveries++;
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

		private bool TryBuildLocalRoute(Vector3 destination)
		{
			if (Time.time < nextLocalRoutePlanTime)
				return false;
			nextLocalRoutePlanTime = Time.time + 0.75f;

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
						&& IsRouteNodeOccupied(resolved, routeRadius))
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
				if (current == preferredGoal && current != start)
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
					if (edgeLength < 0.1f || IsObstacleBlockingAlong(
						positions[current] + Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f),
						routeRadius, edge.normalized, edgeLength))
						continue;
					float tentative = costs[current] + edgeLength;
					if (tentative < costs[next])
					{
						costs[next] = tentative;
						previous[next] = current;
					}
				}
			}

			int goal = -1;
			float bestGoalDistance = float.PositiveInfinity;
			for (int i = 0; i < RouteGridNodeLimit; ++i)
			{
				if (!closed[i] || !walkable[i] || i == start)
					continue;
				Vector3 delta = positions[i] - localGoal;
				delta.y = 0f;
				float distance = delta.sqrMagnitude;
				// Prefer the intended cell when reachable; otherwise route to the
				// closest node that is actually connected to the NPC's start cell.
				if (i == preferredGoal)
				{
					goal = i;
					break;
				}
				if (distance < bestGoalDistance)
				{
					bestGoalDistance = distance;
					goal = i;
				}
			}
			if (goal < 0 || previous[goal] < 0)
				return false;

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

			routeWaypoints.Clear();
			for (int i = reversed.Count - 1; i >= 0; --i)
			{
				Vector3 point = reversed[i];
				// This point is grounded and physically validated. Never
				// substitute untested localGoal (potentially in a wall or void).
				routeWaypoints.Add(point);
			}
			routeWaypointIndex = 0;
			nextPlannedRouteValidationTime = 0f;
			return routeWaypoints.Count > 0;
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
