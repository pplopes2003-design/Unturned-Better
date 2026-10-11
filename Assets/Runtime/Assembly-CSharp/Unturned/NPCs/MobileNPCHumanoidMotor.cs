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

		private CharacterController characterController;
		private MobileNPCBrainDriver brainDriver;
		private Vector3 spawnPosition;
		private Vector3 velocity;
		private bool hasInitialized;
		private bool targetIsVisible;

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
			bool hasDestination = brainDriver.HasDestination;
			Vector3 destination = brainDriver.Destination;
			if (hasDestination)
			{
				Vector3 offset = destination - transform.position;
				offset.y = 0.0f;
				if (offset.sqrMagnitude > stoppingDistance * stoppingDistance)
				{
					planarDirection = offset.normalized;
				}
				else
				{
					brainDriver.ReportDestinationReached();
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
