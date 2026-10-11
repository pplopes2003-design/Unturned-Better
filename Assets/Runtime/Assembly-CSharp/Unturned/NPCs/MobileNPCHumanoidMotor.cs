////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
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

		[Header("Autonomous roaming (local prototype)")]
		[SerializeField] private bool roamingEnabled = true;
		[SerializeField, Min(2f)] private float roamingRadius = 18f;
		[SerializeField, Min(1f)] private float roamingPauseMin = 2f;
		[SerializeField, Min(1f)] private float roamingPauseMax = 6f;
		[SerializeField, Min(1f)] private float roamingSearchHeight = 50f;
		[SerializeField, Min(0f)] private float roamingMaximumDrop = 70f;
		[SerializeField, Range(0f, 60f)] private float roamingMaximumSlope = 38f;
		[SerializeField] private LayerMask roamingGroundMask = ~0;

		[Header("Local obstacle avoidance (not full pathfinding)")]
		[SerializeField, Min(0.5f)] private float obstacleProbeDistance = 1.6f;
		[SerializeField, Min(0.05f)] private float obstacleProbeRadius = 0.28f;
		[SerializeField, Range(15f, 80f)] private float obstacleAvoidanceAngle = 48f;
		[SerializeField] private LayerMask obstacleMask = ~0;

		private CharacterController characterController;
		private MobileNPCBrainDriver brainDriver;
		private Vector3 spawnPosition;
		private Vector3 velocity;
		private bool hasInitialized;
		private bool targetIsVisible;
		private bool hasRoamingDestination;
		private Vector3 roamingDestination;
		private float nextRoamingDecisionTime;

		public bool IsInitialized => hasInitialized;
		public bool IsMoving { get; private set; }

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

			brainDriver.TickServer(targetIsVisible);
			Vector3 planarDirection = Vector3.zero;
			bool hasBrainDestination = brainDriver.HasDestination;
			if (hasBrainDestination)
			{
				// Combat, investigation and tactical goals always take precedence over roaming.
				hasRoamingDestination = false;
			}
			else if (roamingEnabled && Time.time >= nextRoamingDecisionTime && !hasRoamingDestination)
			{
				TryChooseRoamingDestination();
			}

			bool hasDestination = hasBrainDestination || hasRoamingDestination;
			Vector3 destination = hasBrainDestination ? brainDriver.Destination : roamingDestination;
			if (hasDestination)
			{
				Vector3 offset = destination - transform.position;
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

			if (planarDirection.sqrMagnitude > 0.0001f)
			{
				planarDirection = ApplyLocalObstacleAvoidance(planarDirection);
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
			if (!Physics.SphereCast(origin, probeRadius, desiredDirection, out _, probeDistance,
				obstacleMask, QueryTriggerInteraction.Ignore))
			{
				return desiredDirection;
			}

			// Probe both sides and prefer the clear direction that remains most aligned with the goal.
			// This is deliberately short-range steering: it cannot route around large buildings.
			Vector3 left = Quaternion.AngleAxis(-obstacleAvoidanceAngle, Vector3.up) * desiredDirection;
			Vector3 right = Quaternion.AngleAxis(obstacleAvoidanceAngle, Vector3.up) * desiredDirection;
			bool leftBlocked = Physics.SphereCast(origin, probeRadius, left, out _, probeDistance,
				obstacleMask, QueryTriggerInteraction.Ignore);
			bool rightBlocked = Physics.SphereCast(origin, probeRadius, right, out _, probeDistance,
				obstacleMask, QueryTriggerInteraction.Ignore);

			if (!leftBlocked && !rightBlocked)
			{
				return Vector3.Dot(left, desiredDirection) >= Vector3.Dot(right, desiredDirection) ? left : right;
			}
			if (!leftBlocked)
			{
				return left;
			}
			if (!rightBlocked)
			{
				return right;
			}

			return Vector3.zero;
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
				float radius = Random.Range(Mathf.Max(2f, roamingRadius * 0.35f), Mathf.Max(2f, roamingRadius));
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
