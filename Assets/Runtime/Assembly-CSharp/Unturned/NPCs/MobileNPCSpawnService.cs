////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Explicit server-side spawn entry point for opt-in mobile humanoid prefabs.
	/// This is not a network spawn: client visibility must not be assumed until the
	/// entity has been integrated with the game's generated replication protocol.
	/// Legacy/static NPC prefabs are never loaded or modified by this service.
	/// </summary>
	public static class MobileNPCSpawnService
	{
		// Reused scratch list for main-thread server spawn checks; never exposed to callers.
		private static readonly List<MobileNPCBrainDriver> activeDriverScratch = new List<MobileNPCBrainDriver>(32);
		private static readonly Collider[] spawnOverlapScratch = new Collider[32];
		private static readonly Color[] SkinTones =
		{
			new Color(0.96f, 0.78f, 0.62f, 1f),
			new Color(0.84f, 0.62f, 0.45f, 1f),
			new Color(0.69f, 0.46f, 0.32f, 1f),
			new Color(0.48f, 0.31f, 0.23f, 1f),
			new Color(0.91f, 0.70f, 0.53f, 1f)
		};

		/// <summary>
		/// Resolves a candidate point to walkable ground on the current map. This is a
		/// map-agnostic placement primitive for a future population director, not an
		/// automatic spawn system. The caller must choose candidate X/Z coordinates and
		/// provide a ground mask appropriate to the loaded map.
		/// </summary>
		public static bool TryFindSafeGroundPosition(Vector3 candidate, float searchHeight,
			float maximumDropDistance, LayerMask groundMask, float maximumSlopeAngle,
			out Vector3 position, out string error)
		{
			// Backward-compatible call: spawns and AI behaviors do not need
			// source-collider metadata. Navigation can opt in below.
			return TryFindSafeGroundPosition(candidate, searchHeight,
				maximumDropDistance, groundMask, maximumSlopeAngle,
				out position, out _, out error);
		}

		/// <summary>
		/// Return the actual collider hit as well as the grounded point so
		/// navigation can refuse treating storage or decorative barricades
		/// as walkable floor. No assumption about authored building geometry.
		/// </summary>
		public static bool TryFindSafeGroundPosition(Vector3 candidate, float searchHeight,
			float maximumDropDistance, LayerMask groundMask, float maximumSlopeAngle,
			out Vector3 position, out Collider groundCollider, out string error)
		{
			position = Vector3.zero;
			groundCollider = null;
			error = null;

			if (!Provider.isServer)
			{
				error = "Ground placement can only be resolved by the server.";
				return false;
			}

			if (!IsFinite(candidate.x) || !IsFinite(candidate.y) || !IsFinite(candidate.z)
				|| float.IsNaN(searchHeight) || float.IsInfinity(searchHeight) || searchHeight <= 0f
				|| float.IsNaN(maximumDropDistance) || float.IsInfinity(maximumDropDistance) || maximumDropDistance < 0f
				|| float.IsNaN(maximumSlopeAngle) || float.IsInfinity(maximumSlopeAngle))
			{
				error = "Candidate coordinates and ground-search parameters must be finite, with positive search height and non-negative drop distance.";
				return false;
			}

			Vector3 rayOrigin = candidate + Vector3.up * searchHeight;
			float rayDistance = searchHeight + maximumDropDistance;
			if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, rayDistance,
				groundMask, QueryTriggerInteraction.Ignore))
			{
				error = "No ground surface was found below the candidate point.";
				return false;
			}

			float allowedSlope = Mathf.Clamp(maximumSlopeAngle, 0f, 89f);
			if (Vector3.Angle(hit.normal, Vector3.up) > allowedSlope)
			{
				error = "The candidate ground is too steep for a humanoid NPC.";
				return false;
			}

			Vector3 resolved = hit.point + Vector3.up * 0.05f;
			if (!Level.checkSafeIncludingClipVolumes(resolved))
			{
				error = "The resolved ground point is outside safe level bounds or inside a player clip volume.";
				return false;
			}

			position = resolved;
			groundCollider = hit.collider;
			return true;
		}

		private static bool IsVisibleFromObserver(Transform observer, Vector3 targetPosition)
		{
			if (observer == null)
			{
				return false;
			}

			Vector3 origin = observer.position + Vector3.up * 1.6f;
			Vector3 target = targetPosition + Vector3.up * 1.1f;
			Vector3 offset = target - origin;
			float distance = offset.magnitude;
			if (distance <= 1.25f)
			{
				return true;
			}

			Vector3 direction = offset / distance;
			float rayDistance = distance - 0.75f;
			if (!Physics.Raycast(origin, direction, out RaycastHit hit, rayDistance,
				~0, QueryTriggerInteraction.Ignore))
			{
				return true;
			}

			if (hit.transform == observer || hit.transform.IsChildOf(observer))
			{
				float advance = Mathf.Min(hit.distance + 0.15f, rayDistance);
				origin += direction * advance;
				rayDistance -= advance;
				if (rayDistance <= 0.1f)
				{
					return true;
				}

				return !Physics.Raycast(origin, direction, rayDistance, ~0, QueryTriggerInteraction.Ignore);
			}

			return false;
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}

		/// <summary>
		/// Rejects candidate locations whose humanoid capsule intersects existing colliders.
		/// This is an additional placement guard; it does not replace map bounds or nav checks.
		/// </summary>
		private static bool IsSpawnCapsuleClear(CharacterController prefabController, Vector3 position,
			Quaternion rotation, out string error)
		{
			error = null;
			if (prefabController == null)
			{
				error = "A root CharacterController is required for spawn occupancy checks.";
				return false;
			}

			// CharacterController dimensions are local-space values. Account for the
			// prefab's scale and the candidate yaw so occupancy matches the spawned rig.
			Vector3 scale = prefabController.transform.lossyScale;
			float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
			float verticalScale = Mathf.Abs(scale.y);
			float radius = Mathf.Max(0.05f, prefabController.radius * horizontalScale);
			float height = Mathf.Max(radius * 2f, prefabController.height * verticalScale);
			Vector3 scaledCenter = Vector3.Scale(prefabController.center, scale);
			Vector3 center = position + rotation * scaledCenter;
			float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
			Vector3 bottom = center - Vector3.up * halfSegment;
			Vector3 top = center + Vector3.up * halfSegment;

			int hitCount = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, spawnOverlapScratch,
				~0, QueryTriggerInteraction.Ignore);
			if (hitCount == spawnOverlapScratch.Length)
			{
				// Saturation can hide colliders, so prefer a rare allocation over accepting
				// a potentially occupied spawn point.
				Collider[] overflow = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);
				if (overflow.Length > 0)
				{
					error = "Candidate position intersects an existing collider.";
					return false;
				}
				return true;
			}

			if (hitCount > 0)
			{
				error = "Candidate position intersects an existing collider.";
				return false;
			}
			return true;
		}
		public static bool TrySpawnServer(GameObject prefab, Vector3 position, Quaternion rotation,
			MobileNPCHumanoidDisposition.Role role, out GameObject instance, out string error)
		{
			instance = null;
			error = null;

			if (!Provider.isServer)
			{
				error = "Mobile humanoid NPCs can only be spawned by the server.";
				return false;
			}

			if (prefab == null)
			{
				error = "A mobile humanoid prefab is required.";
				return false;
			}

			if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
			{
				error = "The requested NPC position must contain finite coordinates.";
				return false;
			}

			if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w)
				|| rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w < 0.0001f)
			{
				error = "The requested NPC rotation must be a finite, non-zero quaternion.";
				return false;
			}

			if (role != MobileNPCHumanoidDisposition.Role.Hostile
				&& role != MobileNPCHumanoidDisposition.Role.Friendly
				&& role != MobileNPCHumanoidDisposition.Role.Neutral)
			{
				error = "The requested mobile NPC faction role is invalid.";
				return false;
			}

			// Unity rotations should be unit quaternions. Normalize valid caller input so
			// small numeric drift does not leak into the spawned transform.
			rotation = rotation.normalized;

			if (!Level.checkSafeIncludingClipVolumes(position))
			{
				error = "The requested NPC position is outside safe level bounds or inside a player clip volume.";
				return false;
			}

			// Validate the prefab before instantiating it, so invalid assets do not leave
			// partially configured objects in the scene.
			if (prefab.GetComponent<CharacterController>() == null
				|| prefab.GetComponent<MobileNPCBrainDriver>() == null
				|| prefab.GetComponent<MobileNPCHumanoidMotor>() == null
				|| prefab.GetComponent<MobileNPCHumanoidPerception>() == null
				|| prefab.GetComponent<MobileNPCHumanoidDisposition>() == null)
			{
				error = "Prefab must include CharacterController, MobileNPCBrainDriver, MobileNPCHumanoidMotor, MobileNPCHumanoidPerception and MobileNPCHumanoidDisposition on its root.";
				return false;
			}

			GameObject spawned = Object.Instantiate(prefab, position, rotation);
			// Runtime bootstrap templates stay inactive so they never appear in the world.
			// Activate the clone before initialization so Awake and OnEnable run normally.
			if (!spawned.activeSelf)
			{
				spawned.SetActive(true);
			}

			// NPC_Client's HumanClothes component normally receives the skin tone from
			// PlayerLife/character customization. Mobile NPCs have no PlayerLife, so give
			// each spawned humanoid an explicit native material skin color.
			HumanClothes[] clothesComponents = spawned.GetComponentsInChildren<HumanClothes>(true);
			if (clothesComponents.Length > 0)
			{
				Color skinTone = SkinTones[Random.Range(0, SkinTones.Length)];
				for (int i = 0; i < clothesComponents.Length; ++i)
				{
					if (clothesComponents[i] != null)
					{
						clothesComponents[i].isVisual = true;
						clothesComponents[i].skin = skinTone;
					}
				}
			}
			else
			{
				Debug.LogWarning("[MobileNPCSpawnService] NPC_Client clone has no HumanClothes component; native skin/material initialization cannot run.", spawned);
			}

			CharacterController controller = spawned.GetComponent<CharacterController>();
			MobileNPCBodyHitbox[] anatomy =
				spawned.GetComponentsInChildren<MobileNPCBodyHitbox>(true);
			Transform boneRoot = spawned.transform.Find("HumanoidVisual/Root/Skeleton");
			bool damageVolumesValid = controller != null && controller.enabled
				&& boneRoot != null
				&& anatomy.Length == MobileNPCBodyHitbox.ExpectedAnatomicalVolumes;
			int[] regionCounts = new int[MobileNPCBodyHitbox.ExpectedAnatomicalVolumes];
			string invalidReason = damageVolumesValid ? null
				: "missing CharacterController/Skeleton or wrong hitbox count";
			for (int i = 0; i < anatomy.Length; ++i)
			{
				MobileNPCBodyHitbox marker = anatomy[i];
				BoxCollider hitbox = marker != null
					? marker.GetComponent<BoxCollider>() : null;
				string regionProblem = null;
				if (hitbox == null || !hitbox.enabled || hitbox.isTrigger
					|| !hitbox.gameObject.activeInHierarchy
					|| marker == null || !marker.isActiveAndEnabled)
					regionProblem = "missing/disabled/trigger BoxCollider or hitbox marker";
				else if (boneRoot == null || !hitbox.transform.IsChildOf(boneRoot))
					regionProblem = "hitbox not parented under NPC_Client Skeleton";
				else if (hitbox.size.x <= 0f || hitbox.size.y <= 0f
					|| hitbox.size.z <= 0f)
					regionProblem = "zero-size local BoxCollider";
				else if (hitbox.gameObject.layer != LayerMasks.ENTITY)
					regionProblem = "wrong hitbox layer " + hitbox.gameObject.layer
						+ " (expected Entity " + LayerMasks.ENTITY
						+ "; visual Medium=" + LayerMasks.MEDIUM + ")";
				else if ((RayMasks.DAMAGE_CLIENT & RayMasks.ENTITY) == 0)
					regionProblem = "DAMAGE_CLIENT mask excludes Entity; mask=0x"
						+ RayMasks.DAMAGE_CLIENT.ToString("X8");
				else if ((RayMasks.ENTITY & (1 << LayerMasks.ENTITY)) == 0)
					regionProblem = "RayMasks.ENTITY does not match LayerMasks.ENTITY";

				if (regionProblem != null)
				{
					damageVolumesValid = false;
					// Do NOT rely on Collider.bounds in the first frame:
					// PhysX broadphase bounds can be stale before transforms
					// are synchronized, even when authored box.size is valid.
					if (invalidReason == null)
						invalidReason = marker != null ? marker.name + ": " + regionProblem
							: "null hitbox marker";
					continue;
				}

				int part = (int)marker.BodyPart;
				if (part < 0 || part >= regionCounts.Length)
				{
					damageVolumesValid = false;
					if (invalidReason == null)
						invalidReason = marker.name + ": invalid body part " + part;
					continue;
				}
				regionCounts[part]++;
				// On an inactive template IgnoreCollision is discarded by
				// Unity. Reapply on EACH live player-equivalent bone hitbox.
				if (controller != null)
					Physics.IgnoreCollision(controller, hitbox, true);
			}

			for (int part = 0; part < regionCounts.Length; ++part)
			{
				if (regionCounts[part] == 1)
					continue;
				damageVolumesValid = false;
				if (invalidReason == null)
					invalidReason = "body part " + ((MobileNPCInjurySystem.BodyPart)part)
						+ " has " + regionCounts[part] + " hitboxes; expected exactly one";
			}

			if (!damageVolumesValid)
			{
				error = "Spawn refused: native Player_Server anatomy requires "
					+ "exactly SIX active animated BoxColliders, one each for "
					+ "Skull, Spine, Left_Arm, Right_Arm, Left_Leg and Right_Leg. "
					+ "First error: " + invalidReason + ".";
				Debug.LogError("[MobileNPCSpawnService] " + error
					+ " Instance=" + spawned.name
					+ " hitboxCount=" + anatomy.Length + ".", spawned);
				Object.Destroy(spawned);
				return false;
			}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.Log("[MobileNPCDamageInvariant] PASS at spawn: "
				+ anatomy.Length + " Player_Server-equivalent anatomical BoxColliders, "
				+ "all six limbs verified on actual bones and ray masks.", spawned);
#endif
			// Zombie.tick() performs a one-time downward CharacterController.Move after
			// spawning so the capsule resolves against Unturned's Agent/world collision.
			// Do this before capturing the motor's home/spawn position.
			if (controller != null && controller.enabled)
			{
				controller.Move(Vector3.down);
			}

			MobileNPCHumanoidDisposition disposition = spawned.GetComponent<MobileNPCHumanoidDisposition>();
			MobileNPCHumanoidMotor motor = spawned.GetComponent<MobileNPCHumanoidMotor>();
			MobileNPCHumanoidVitals vitals = spawned.GetComponent<MobileNPCHumanoidVitals>();
			disposition.SetRoleServer(role);
			motor.InitializeServer();
			// Initialize health synchronously so callers never receive a spawned NPC with
			// uninitialized server vitals while waiting for Unity's Start callback.
			if (vitals != null)
			{
				vitals.InitializeServer();
			}

			MobileNPCGroupMember groupMember = spawned.GetComponent<MobileNPCGroupMember>();
			if (groupMember == null)
			{
				groupMember = spawned.AddComponent<MobileNPCGroupMember>();
			}
			groupMember.InitializeServer();

			if (!motor.IsInitialized)
			{
				Object.Destroy(spawned);
				error = "The mobile humanoid motor failed to initialize on the server.";
				return false;
			}

            // Emit a compact runtime audit so a successful spawn with an invisible
            // model can be distinguished from a failed placement or disabled entity.
            Renderer[] renderers = spawned.GetComponentsInChildren<Renderer>(true);
            int enabledRendererCount = 0;
            bool hasValidBounds = false;
            Bounds combinedBounds = new Bounds(spawned.transform.position, Vector3.zero);
            for (int i = 0; i < renderers.Length; ++i)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff)
                {
                    continue;
                }

                ++enabledRendererCount;
                if (!float.IsNaN(renderer.bounds.size.x) && !float.IsInfinity(renderer.bounds.size.x)
                    && renderer.bounds.size.sqrMagnitude > 0.0001f)
                {
                    if (!hasValidBounds)
                    {
                        combinedBounds = renderer.bounds;
                        hasValidBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(renderer.bounds);
                    }
                }
            }

            Debug.Log("[MobileNPCSpawnService] Spawn audit: name=" + spawned.name
                + ", active=" + spawned.activeInHierarchy
                + ", position=" + spawned.transform.position
                + ", scale=" + spawned.transform.lossyScale
                + ", controllerEnabled=" + spawned.GetComponent<CharacterController>().enabled
                + ", renderers=" + renderers.Length
                + ", enabledRenderers=" + enabledRendererCount
                + ", validRendererBounds=" + hasValidBounds
                + ", combinedBounds=" + (hasValidBounds ? combinedBounds.ToString() : "none") + ".", spawned);

            // Register before returning: a lethal hit in the same frame must not
            // disappear from lifecycle tracking when its brain unregisters.
            MobileNPCPopulationDirector director = Object.FindObjectOfType<MobileNPCPopulationDirector>();
            if (director != null)
                director.RegisterSpawnedNPC(spawned);
            instance = spawned;
            return true;
		}

		/// <summary>
		/// Attempts a bounded batch of server-local spawns in a circular region around
		/// an explicit center. A map integration must supply valid ground layers and
		/// choose the center; this method does not discover map bounds or replicate NPCs.
		/// Returns true when at least one NPC was spawned; spawnedCount reports partial
		/// success when some candidate positions are unsuitable.
		/// </summary>
		public static bool TrySpawnBatchAroundServer(GameObject prefab, Vector3 center,
			float radius, int desiredCount, int maximumAttempts, float searchHeight,
			float maximumDropDistance, LayerMask groundMask, float maximumSlopeAngle,
			MobileNPCHumanoidDisposition.Role role, System.Collections.Generic.List<GameObject> spawnedInstances,
			out int spawnedCount, out string error)
		{
			return TrySpawnBatchAroundServer(prefab, center, radius, desiredCount, maximumAttempts,
				searchHeight, maximumDropDistance, groundMask, maximumSlopeAngle, role,
				spawnedInstances, null, out spawnedCount, out error);
		}

		/// <summary>
		/// Optional visibility-aware overload. When an observer is supplied, candidate points
		/// with a clear ray from the observer's approximate eye position are rejected before
		/// instantiation, preventing visible spawn flashes without spawn-then-despawn.
		/// </summary>
		public static bool TrySpawnBatchAroundServer(GameObject prefab, Vector3 center,
			float radius, int desiredCount, int maximumAttempts, float searchHeight,
			float maximumDropDistance, LayerMask groundMask, float maximumSlopeAngle,
			MobileNPCHumanoidDisposition.Role role, System.Collections.Generic.List<GameObject> spawnedInstances,
			Transform avoidVisibleFrom, out int spawnedCount, out string error)
		{
			spawnedCount = 0;
			error = null;

			if (!Provider.isServer)
			{
				error = "Mobile humanoid NPC batches can only be spawned by the server.";
				return false;
			}

			if (spawnedInstances == null)
			{
				error = "A caller-owned list is required to collect spawned instances.";
				return false;
			}

			spawnedInstances.Clear();
			if (!IsFinite(center.x) || !IsFinite(center.y) || !IsFinite(center.z)
				|| !IsFinite(radius) || radius <= 0f || desiredCount < 1 || maximumAttempts < desiredCount
				|| maximumAttempts > 1000)
			{
				error = "Batch center/radius and attempt limits are invalid; radius must be positive and attempts must be between desired count and 1000.";
				return false;
			}

			if (prefab == null)
			{
				error = "A mobile humanoid prefab is required.";
				return false;
			}

			CharacterController prefabController = prefab.GetComponent<CharacterController>();
			if (prefabController == null)
			{
				error = "Prefab must include a root CharacterController.";
				return false;
			}

			// Keep agents in the same batch from being created on top of one another.
			// This is a horizontal spacing check, not a full capsule/obstacle overlap test.
			float minimumSpacing = Mathf.Max(1.25f, prefabController.radius * 2f + 0.5f);
			float minimumSpacingSquared = minimumSpacing * minimumSpacing;

			for (int attempt = 0; attempt < maximumAttempts && spawnedCount < desiredCount; ++attempt)
			{
				float angle = Random.Range(0f, Mathf.PI * 2f);
				float distance = Mathf.Sqrt(Random.value) * radius;
				Vector3 candidate = center + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);

				if (!TryFindSafeGroundPosition(candidate, searchHeight, maximumDropDistance,
					groundMask, maximumSlopeAngle, out Vector3 groundPosition, out string groundError))
				{
					error = groundError;
					continue;
				}

				if (avoidVisibleFrom != null && IsVisibleFromObserver(avoidVisibleFrom, groundPosition))
				{
					error = "Candidate position was rejected because it is visible from the player.";
					continue;
				}

				Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
				if (!IsSpawnCapsuleClear(prefabController, groundPosition, rotation, out string occupancyError))
				{
					error = occupancyError;
					continue;
				}

				bool tooCloseToExistingNPC = false;
				MobileNPCRegistry.CopyActiveDrivers(activeDriverScratch);
				for (int i = 0; i < activeDriverScratch.Count; ++i)
				{
					MobileNPCBrainDriver existingDriver = activeDriverScratch[i];
					if (existingDriver == null || existingDriver.gameObject == null
						|| !existingDriver.gameObject.activeInHierarchy)
					{
						continue;
					}

					Vector3 offset = existingDriver.transform.position - groundPosition;
					offset.y = 0f;
					if (offset.sqrMagnitude < minimumSpacingSquared)
					{
						tooCloseToExistingNPC = true;
						break;
					}
				}

				if (tooCloseToExistingNPC)
				{
					error = "Candidate position was rejected because it is too close to another active mobile NPC.";
					continue;
				}

				if (!TrySpawnServer(prefab, groundPosition, rotation, role, out GameObject instance, out string spawnError))
				{
					error = spawnError;
					continue;
				}

				spawnedInstances.Add(instance);
				++spawnedCount;
			}

			if (spawnedCount > 0)
			{
				if (spawnedCount < desiredCount && string.IsNullOrEmpty(error))
				{
					error = "The batch partially succeeded because not enough safe candidate positions were found.";
				}
				return true;
			}

			if (string.IsNullOrEmpty(error))
			{
				error = "No safe position was found within the configured attempt limit.";
			}
			return false;
		}

		/// <summary>
		/// Destroys a mobile NPC owned by the server. This is local server lifecycle
		/// cleanup only; it does not broadcast a network despawn to clients.
		/// </summary>
		public static bool TryDespawnServer(GameObject instance, out string error)
		{
			error = null;

			if (!Provider.isServer)
			{
				error = "Mobile humanoid NPCs can only be despawned by the server.";
				return false;
			}

			if (instance == null)
			{
				error = "A mobile humanoid NPC instance is required.";
				return false;
			}

			// Require the mobile-NPC component signature rather than allowing this
			// helper to destroy an arbitrary GameObject or a legacy static NPC.
			if (instance.GetComponent<MobileNPCBrainDriver>() == null
				|| instance.GetComponent<MobileNPCHumanoidMotor>() == null
				|| instance.GetComponent<MobileNPCHumanoidDisposition>() == null)
			{
				error = "The instance is not a recognized mobile humanoid NPC.";
				return false;
			}

			Object.Destroy(instance);
			return true;
		}
	}
}
