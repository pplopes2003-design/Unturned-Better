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
			position = Vector3.zero;
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
			return true;
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
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

			if (!motor.IsInitialized)
			{
				Object.Destroy(spawned);
				error = "The mobile humanoid motor failed to initialize on the server.";
				return false;
			}

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
					error = "Candidate position was rejected because it is too close to another NPC in this batch.";
					continue;
				}

				Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
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
