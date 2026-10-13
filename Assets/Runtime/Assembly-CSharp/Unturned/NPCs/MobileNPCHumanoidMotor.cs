////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using SDG.Framework.Water;
using UnityEngine;
using UnityEngine.AI;

namespace SDG.Unturned
{
	/// <summary>
	/// First movement/scene-entity integration for a mobile humanoid NPC.
	/// Attach to a server-spawned humanoid prefab with a CharacterController and
	/// MobileNPCBrainDriver. This motor intentionally moves only on the server;
	/// network replication and client interpolation are a separate integration step.
	/// It provides collision-aware local movement, not pathfinding around obstacles.
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

		[Header("Local obstacle avoidance and bounded indoor route planning")]
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
		private Vector3 spawnPosition;
		private Vector3 velocity;
		private bool hasInitialized;
		private bool targetIsVisible;
		private bool hasRoamingDestination;
		private Vector3 roamingDestination;
		private float nextRoamingDecisionTime;
		private Vector3 lastStuckCheckPosition;
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
		// Unity NavMesh is an optional global planner: Unturned's own ASPFP data is not exposed through this API.
		[SerializeField] private bool useUnityNavMeshWhenAvailable = true;
		[SerializeField, Min(0.5f)] private float navMeshSampleRadius = 3.0f;
		private float nextGlobalRoutePlanTime;
		private readonly NavMeshPath globalNavMeshPath = new NavMeshPath();
		private Vector3 lastRoamingDirection;
		private bool hasLastRoamingDirection;

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
			brainDriver.InitializeServer(spawnPosition);
			hasRoamingDestination = false;
			nextRoamingDecisionTime = Time.time + Random.Range(2f, 5f);
			lastStuckCheckPosition = transform.position;
			nextStuckCheckTime = Time.time + 0.75f;
			blockedMovementDuration = 0f;
			useRecoverySteering = false;
			hasInitialized = true;
		}

		private void Awake()
		{
			characterController = GetComponent<CharacterController>();
			brainDriver = GetComponent<MobileNPCBrainDriver>();
			injurySystem = GetComponent<MobileNPCInjurySystem>();
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
			if (hasDestination)
			{
				if (!hasDetourWaypoint || (destination - detourGoalSnapshot).sqrMagnitude > 9f)
				{
					hasDetourWaypoint = false;
					routeWaypoints.Clear();
					routeWaypointIndex = 0;
				}
				if (routeWaypointIndex < routeWaypoints.Count
					&& (transform.position - routeWaypoints[routeWaypointIndex]).sqrMagnitude <= 1.0f)
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
				Vector3 movementDestination = routeWaypointIndex < routeWaypoints.Count
					? routeWaypoints[routeWaypointIndex]
					: (hasDetourWaypoint ? detourWaypoint : destination);

				// Routes are snapshots of the scene. If a door closes or an entity blocks
				// the next segment after planning, discard the stale route and try again
				// through the existing cooldown-limited detour path.
				if (routeWaypointIndex < routeWaypoints.Count)
				{
					Vector3 routeOffset = movementDestination - transform.position;
					routeOffset.y = 0f;
					if (routeOffset.sqrMagnitude > 0.1f)
					{
						Vector3 routeOrigin = transform.position
							+ Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
						float routeRadius = Mathf.Clamp(obstacleProbeRadius, 0.05f,
							Mathf.Max(0.05f, characterController.radius * 0.9f));
						if (IsObstacleBlockingAlong(routeOrigin, routeRadius, routeOffset.normalized,
							Mathf.Min(routeOffset.magnitude, Mathf.Max(0.5f, obstacleProbeDistance))))
						{
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
					brainDriver.ReportDestinationReached();
				}
				else
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

			if (planarDirection.sqrMagnitude > 0.0001f)
			{
				Quaternion targetRotation = Quaternion.LookRotation(planarDirection, Vector3.up);
				transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * deltaTime);
			}

			bool shouldRun = runWhenPursuing && brainDriver.CurrentState == MobileNPCBrain.State.Pursuing
				|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat;
			float injuryMovementMultiplier = injurySystem != null ? injurySystem.MovementMultiplier : 1f;
			// A fractured leg is a walking/limping state, not a full-speed sprint animation.
			if (injuryMovementMultiplier <= 0.5f)
				shouldRun = false;
			float speed = shouldRun ? runSpeed : walkSpeed;
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
			}

			CollisionFlags collision = characterController.Move(motion * deltaTime);
			if ((collision & CollisionFlags.Below) != 0 && velocity.y < 0.0f)
			{
				velocity.y = -2.0f;
			}
			IsMoving = planarDirection.sqrMagnitude > 0.0001f && (collision & CollisionFlags.Sides) == 0;
			UpdateStuckRecovery(wantedToMove, deltaTime);
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
			float movedDistance = Vector3.Distance(transform.position, lastStuckCheckPosition);
			lastStuckCheckPosition = transform.position;

			if (!wantedToMove || movedDistance >= 0.12f)
			{
				blockedMovementDuration = 0f;
				useRecoverySteering = false;
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
				recoverySteeringAngle = Random.value < 0.5f ? -90f : 90f;
				useRecoverySteering = true;
				recoverySteeringUntil = Time.time + 1.1f;
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

		private bool HasWalkableGroundAhead(Vector3 direction)
		{
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.0001f)
				return false;
			direction.Normalize();
			Vector3 candidate = transform.position + direction * Mathf.Clamp(obstacleProbeDistance + 0.6f, 1.5f, 3.0f);
			if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, 5f, 3.5f,
				roamingGroundMask, Mathf.Clamp(roamingMaximumSlope, 35f, 48f), out Vector3 ground, out _))
				return false;
			return Mathf.Abs(ground.y - transform.position.y) <= 2.25f;
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
				// Avoid a permanent left/right bias when both short routes are open.
				return Random.value < 0.5f ? left : right;
			}
			if (!leftBlocked)
			{
				return left;
			}
			if (!rightBlocked)
			{
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

			// Prefer a real global route if Unity NavMesh data is present in this scene.
			// Most Unturned maps use a separate pathfinding backend, so absence of Unity
			// NavMesh is an expected fallback case, not a reason to fail NPC movement.
			if (TryBuildUnityNavMeshRoute(destination))
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
		/// Attempts a map-wide route using Unity's baked NavMesh, but only when both
		/// endpoints can be sampled and the resulting route is complete. This is an
		/// optional backend; it does not pretend to read Unturned's private ASPFP data.
		/// </summary>
		private bool TryBuildUnityNavMeshRoute(Vector3 destination)
		{
			if (!useUnityNavMeshWhenAvailable || Time.time < nextGlobalRoutePlanTime)
				return false;
			nextGlobalRoutePlanTime = Time.time + 1.25f;

			float sampleRadius = Mathf.Max(0.5f, navMeshSampleRadius);
			if (!NavMesh.SamplePosition(transform.position, out NavMeshHit startHit, sampleRadius, NavMesh.AllAreas)
				|| !NavMesh.SamplePosition(destination, out NavMeshHit goalHit, sampleRadius, NavMesh.AllAreas))
				return false;

			globalNavMeshPath.ClearCorners();
			if (!NavMesh.CalculatePath(startHit.position, goalHit.position, NavMesh.AllAreas, globalNavMeshPath)
				|| globalNavMeshPath.status != NavMeshPathStatus.PathComplete)
				return false;

			Vector3[] corners = globalNavMeshPath.corners;
			if (corners == null || corners.Length < 2)
				return false;

			routeWaypoints.Clear();
			routeWaypointIndex = 0;
			for (int i = 1; i < corners.Length; ++i)
			{
				if ((corners[i] - transform.position).sqrMagnitude > 0.36f)
					routeWaypoints.Add(corners[i]);
			}

			if (routeWaypoints.Count == 0)
				return false;

			return true;
		}

		private bool TryBuildLocalRoute(Vector3 destination)
		{
			if (Time.time < nextRoutePlanTime)
				return false;
			nextRoutePlanTime = Time.time + 0.75f;

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

					Vector3 mid = resolved + Vector3.up * Mathf.Max(0.45f, characterController.height * 0.55f);
					Vector3 horizontal = resolved - basePosition;
					horizontal.y = 0f;
					if (horizontal.sqrMagnitude > 0.2f
						&& IsObstacleBlockingAlong(mid, routeRadius, horizontal.normalized,
							Mathf.Min(RouteGridCellSize * 0.35f, horizontal.magnitude)))
						continue;

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
				if (i == 0 && Vector3.Distance(point, localGoal) < 1.0f)
					point = localGoal;
				routeWaypoints.Add(point);
			}
			routeWaypointIndex = 0;
			return routeWaypoints.Count > 0;
		}

		private bool IsRouteNodeOccupied(Vector3 groundPosition, float radius)
		{
			float height = Mathf.Max(radius * 2f, characterController.height);
			Vector3 center = groundPosition + Vector3.up * (height * 0.5f);
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
