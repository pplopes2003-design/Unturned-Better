////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Lightweight server-authoritative perception for opt-in mobile humanoids.
	/// Finds the nearest living player inside range, field of view, and unobstructed line of sight.
	/// Network spawning/replication remains the responsibility of the host entity system.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	public sealed class MobileNPCHumanoidPerception : MonoBehaviour
	{
		[SerializeField, Min(1f)] private float sightRange = 32f;
		[SerializeField, Range(1f, 360f)] private float fieldOfView = 120f;
		[SerializeField, Min(0.02f)] private float scanInterval = 0.2f;
		[SerializeField, Min(0.1f)] private float eyeHeight = 1.55f;
		[SerializeField, Min(0.1f)] private float targetHeight = 1.0f;
		[SerializeField] private LayerMask obstructionMask = ~0;

		private MobileNPCBrainDriver brain;
		private MobileNPCHumanoidMotor motor;
		private MobileNPCHumanoidDisposition disposition;
		private float nextScanTime;
		private Player visiblePlayer;
		private MobileNPCHumanoidDisposition visibleNPC;
		private readonly RaycastHit[] sightHits = new RaycastHit[16];
		// Shared scratch storage avoids FindObjectsOfType allocations for every NPC scan.
		// Unity gameplay updates run on the main thread, so this list is not concurrently accessed.
		private static readonly List<MobileNPCBrainDriver> candidateDrivers = new List<MobileNPCBrainDriver>(32);

		public Player VisiblePlayer => Provider.isServer ? visiblePlayer : null;
		public MobileNPCHumanoidDisposition VisibleNPC => Provider.isServer ? visibleNPC : null;

		private void Awake()
		{
			brain = GetComponent<MobileNPCBrainDriver>();
			motor = GetComponent<MobileNPCHumanoidMotor>();
			disposition = GetComponent<MobileNPCHumanoidDisposition>();
		}

		private void Update()
		{
			if (!Provider.isServer || brain == null || Time.time < nextScanTime)
			{
				return;
			}

			nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
			if (disposition == null || !disposition.CanEngageMobileNPCs && !disposition.CanEngagePlayers)
			{
				visiblePlayer = null;
				visibleNPC = null;
				brain.ResetServer();
				if (motor != null)
				{
					motor.SetTargetVisibleFromServer(false);
				}
				return;
			}

			Player playerCandidate = disposition.CanEngagePlayers ? FindVisiblePlayer() : null;
			MobileNPCHumanoidDisposition npcCandidate = disposition.CanEngageMobileNPCs ? FindVisibleNPC() : null;
			if (npcCandidate != null && (playerCandidate == null
				|| (npcCandidate.transform.position - transform.position).sqrMagnitude
				< (playerCandidate.transform.position - transform.position).sqrMagnitude))
			{
				visiblePlayer = null;
				visibleNPC = npcCandidate;
				brain.ReportVisibleTarget(npcCandidate.transform);
			}
			else
			{
				visiblePlayer = playerCandidate;
				visibleNPC = null;
				if (playerCandidate != null)
				{
					brain.ReportVisibleTarget(playerCandidate.transform);
				}
			}

			if (motor != null)
			{
				motor.SetTargetVisibleFromServer(visiblePlayer != null || visibleNPC != null);
			}
		}

		private MobileNPCHumanoidDisposition FindVisibleNPC()
		{
			MobileNPCRegistry.CopyActiveDrivers(candidateDrivers);
			MobileNPCHumanoidDisposition best = null;
			float bestDistanceSquared = sightRange * sightRange;
			Vector3 eye = transform.position + Vector3.up * eyeHeight;

			for (int i = 0; i < candidateDrivers.Count; ++i)
			{
				MobileNPCBrainDriver candidateDriver = candidateDrivers[i];
				MobileNPCHumanoidDisposition candidate = candidateDriver != null ? candidateDriver.GetComponent<MobileNPCHumanoidDisposition>() : null;
				if (candidate == null || candidate == disposition || !candidate.isActiveAndEnabled
					|| !candidate.gameObject.activeInHierarchy || !disposition.IsHostileTo(candidate))
				{
					continue;
				}

				MobileNPCHumanoidVitals vitals = candidate.GetComponent<MobileNPCHumanoidVitals>();
				if (vitals == null || vitals.IsDead)
				{
					continue;
				}

				Vector3 target = candidate.transform.position + Vector3.up * targetHeight;
				Vector3 offset = target - eye;
				float distanceSquared = offset.sqrMagnitude;
				if (distanceSquared < 0.01f || distanceSquared > bestDistanceSquared)
				{
					continue;
				}

				Vector3 direction = offset.normalized;
				if (Vector3.Angle(transform.forward, direction) > fieldOfView * 0.5f)
				{
					continue;
				}

				Transform nearestTransform = FindNearestSightTransform(eye, direction, Mathf.Sqrt(distanceSquared));

				// Fail closed when the ray did not confirm a collider before the target.
				// Treating an empty result as visible can make agents shoot through walls
				// when the prefab's obstruction mask is misconfigured.
				if (nearestTransform == null)
				{
					continue;
				}

				if (nearestTransform != null)
				{
					MobileNPCHumanoidDisposition hitNPC = nearestTransform.GetComponent<MobileNPCHumanoidDisposition>();
					if (hitNPC == null)
					{
						hitNPC = nearestTransform.GetComponentInParent<MobileNPCHumanoidDisposition>();
					}
					if (hitNPC != candidate)
					{
						continue;
					}
				}

				best = candidate;
				bestDistanceSquared = distanceSquared;
			}

			return best;
		}

		private Player FindVisiblePlayer()
		{
			Player[] players = FindObjectsOfType<Player>();
			Player best = null;
			float bestDistanceSquared = sightRange * sightRange;
			Vector3 eye = transform.position + Vector3.up * eyeHeight;

			for (int i = 0; i < players.Length; ++i)
			{
				Player candidate = players[i];
				if (candidate == null || candidate == GetComponent<Player>()
					|| candidate.life == null || candidate.life.isDead
					|| candidate.gameObject == null || !candidate.gameObject.activeInHierarchy)
				{
					continue;
				}

				Vector3 target = candidate.transform.position + Vector3.up * targetHeight;
				Vector3 offset = target - eye;
				float distanceSquared = offset.sqrMagnitude;
				if (distanceSquared < 0.01f || distanceSquared > bestDistanceSquared)
				{
					continue;
				}

				Vector3 direction = offset.normalized;
				if (Vector3.Angle(transform.forward, direction) > fieldOfView * 0.5f)
				{
					continue;
				}

				Transform nearestTransform = FindNearestSightTransform(eye, direction, Mathf.Sqrt(distanceSquared));

				// Require positive line-of-sight evidence instead of accepting a target
				// when the ray query returns no colliders.
				if (nearestTransform == null)
				{
					continue;
				}

				if (nearestTransform != null)
				{
					Player hitPlayer = nearestTransform.GetComponent<Player>();
					if (hitPlayer == null)
					{
						hitPlayer = nearestTransform.GetComponentInParent<Player>();
					}

					if (hitPlayer != candidate)
					{
						continue;
					}
				}

				best = candidate;
				bestDistanceSquared = distanceSquared;
			}

			return best;
		}
		/// <summary>
		/// Returns the nearest collider along the sight ray, excluding this NPC's own rig.
		/// The allocating fallback is used only when the reusable hit buffer saturates,
		/// because a full NonAlloc buffer does not guarantee it contains the nearest hit.
		/// </summary>
		private Transform FindNearestSightTransform(Vector3 origin, Vector3 direction, float distance)
		{
			int hitCount = Physics.RaycastNonAlloc(origin, direction, sightHits, distance,
				obstructionMask, QueryTriggerInteraction.Ignore);
			float nearestDistance = float.PositiveInfinity;
			Transform nearestTransform = null;

			if (hitCount == sightHits.Length)
			{
				RaycastHit[] overflowHits = Physics.RaycastAll(origin, direction, distance,
					obstructionMask, QueryTriggerInteraction.Ignore);
				for (int i = 0; i < overflowHits.Length; ++i)
				{
					Transform hitTransform = overflowHits[i].transform;
					if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
					{
						continue;
					}
					if (overflowHits[i].distance < nearestDistance)
					{
						nearestDistance = overflowHits[i].distance;
						nearestTransform = hitTransform;
					}
				}
				return nearestTransform;
			}

			for (int i = 0; i < hitCount; ++i)
			{
				Transform hitTransform = sightHits[i].transform;
				if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
				{
					continue;
				}
				if (sightHits[i].distance < nearestDistance)
				{
					nearestDistance = sightHits[i].distance;
					nearestTransform = hitTransform;
				}
			}
			return nearestTransform;
		}

	}
}