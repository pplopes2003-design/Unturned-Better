////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
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
		private float nextScanTime;
		private Player visiblePlayer;
		private readonly RaycastHit[] sightHits = new RaycastHit[16];

		public Player VisiblePlayer => Provider.isServer ? visiblePlayer : null;

		private void Awake()
		{
			brain = GetComponent<MobileNPCBrainDriver>();
			motor = GetComponent<MobileNPCHumanoidMotor>();
		}

		private void Update()
		{
			if (!Provider.isServer || brain == null || Time.time < nextScanTime)
			{
				return;
			}

			nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
			Player candidate = FindVisiblePlayer();
			visiblePlayer = candidate;

			if (candidate != null)
			{
				brain.ReportVisibleTarget(candidate.transform);
			}

			if (motor != null)
			{
				motor.SetTargetVisibleFromServer(candidate != null);
			}
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

				int hitCount = Physics.RaycastNonAlloc(eye, direction, sightHits, Mathf.Sqrt(distanceSquared),
					obstructionMask, QueryTriggerInteraction.Ignore);
				float nearestDistance = float.PositiveInfinity;
				Transform nearestTransform = null;

				for (int hitIndex = 0; hitIndex < hitCount; ++hitIndex)
				{
					Transform hitTransform = sightHits[hitIndex].transform;
					if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
					{
						continue;
					}

					if (sightHits[hitIndex].distance < nearestDistance)
					{
						nearestDistance = sightHits[hitIndex].distance;
						nearestTransform = hitTransform;
					}
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
	}
}