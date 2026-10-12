////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

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

		[Header("Home territory")]
		[SerializeField, Min(5f)] private float homeTerritoryRadius = 45f;

		[Header("Autonomous roaming (local prototype)")]
		[SerializeField] private bool roamingEnabled = true;
		[SerializeField, Min(2f)] private float roamingRadius = 18f;
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
		private float blockedMovementDuration;
		private bool useRecoverySteering;
		private float recoverySteeringUntil;
		private float recoverySteeringAngle;
		private bool hasDetourWaypoint;
		private Vector3 detourWaypoint;
		private Vector3 detourGoalSnapshot;
		private float nextDetourAttemptTime;
		private readonly List<Vector3> routeWaypoints = new List<Vector3>(16);
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

			// Low-frequency guard against transient terrain-collision failures. Normal movement still uses CharacterController.Move.
			if (Time.time >= nextGroundSafetyCheckTime)
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

			bool wantedToMove = planarDirection.sqrMagnitude > 0.0001f;
			if (wantedToMove)
			{
				Vector3 requestedDirection = planarDirection;
				if (useRecoverySteering && Time.time < recoverySteeringUntil)
				{
					Vector3 recoveryDirection = Quaternion.AngleAxis(recoverySteeringAngle, Vector3.up) * requestedDirection;
					planarDirection = IsObstacleBlocking(recoveryDirection)
						? ApplyLocalObstacleAvoidance(requestedDirection)
						: recoveryDirection;
				}
				else
				{
					planarDirection = ApplyLocalObstacleAvoidance(requestedDirection);
				}
			}

			if (planarDirection.sqrMagnitude > 0.0001f)
			{
				Quaternion targetRotation = Quaternion.LookRotation(planarDirection, Vector3.up);
				transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * deltaTime);
			}

			bool shouldRun = runWhenPursuing && brainDriver.CurrentState == MobileNPCBrain.State.Pursuing
				|| brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat;
			float speed = shouldRun ? runSpeed : walkSpeed;
			Vector3 motion = planarDirection * speed;

			if (characterController.isGrounded && velocity.y < 0.0f)
			{
				velocity.y = -2.0f;
			}
			velocity.y -= gravity * deltaTime;
			motion.y = velocity.y;

			CollisionFlags collision = characterController.Move(motion * deltaTime);
			IsMoving = planarDirection.sqrMagnitude > 0.0001f && (collision & CollisionFlags.Sides) == 0;
			UpdateStuckRecovery(wantedToMove, deltaTime);
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

			// First try a bounded local grid search. The grid is centered on the NPC,
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
				float bestCost = float.PositiveInfinity;
				for (int i = 0; i < RouteGridNodeLimit; ++i)
				{
					if (!closed[i] && walkable[i] && costs[i] < bestCost)
					{
						bestCost = costs[i];
						current = i;
					}
				}
				if (current < 0)
					break;
				closed[current] = true;

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

			// Use bounded attempts and the same ground/slope/clip-volume checks as spawn placement.
			// This is waypoint roaming, not navmesh pathfinding; walls can still block a route.
			for (int attempt = 0; attempt < 8; ++attempt)
			{
				float angle = Random.Range(0f, Mathf.PI * 2f);
				float effectiveRoamingRadius = Mathf.Min(Mathf.Max(2f, roamingRadius), HomeTerritoryRadius);
				float radius = Random.Range(Mathf.Max(2f, effectiveRoamingRadius * 0.35f), effectiveRoamingRadius);
				Vector3 candidate = spawnPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
				if (MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, Mathf.Max(1f, roamingSearchHeight),
					Mathf.Max(0f, roamingMaximumDrop), roamingGroundMask, roamingMaximumSlope,
					out Vector3 resolved, out _))
				{
					roamingDestination = resolved;
					hasRoamingDestination = true;
					return;
				}
			}

			// Back off when this terrain patch has no valid candidate; do not search every frame.
			nextRoamingDecisionTime = Time.time + Random.Range(3f, 7f);
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
