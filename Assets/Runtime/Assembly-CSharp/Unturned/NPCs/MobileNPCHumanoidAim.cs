////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-side humanized aiming profile for a mobile NPC. This component does not
	/// fire a weapon; the weapon integration must consume TryGetShotDirection.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	public sealed class MobileNPCHumanoidAim : MonoBehaviour
	{
		[SerializeField, Min(0.0f)] private float minimumReactionTime = 0.25f;
		[SerializeField, Min(0.0f)] private float maximumReactionTime = 0.85f;
		[SerializeField, Min(0.0f)] private float suppressionSpreadDegrees = 3.5f;
		[SerializeField, Min(0.0f)] private float suppressionDuration = 1.2f;
		[SerializeField, Min(0.0f)] private float baseSpreadDegrees = 3.0f;
		[SerializeField, Min(0.0f)] private float movingSpreadDegrees = 2.5f;
		[SerializeField, Min(0.0f)] private float maximumSpreadDegrees = 12.0f;
		[SerializeField, Min(0.0f)] private float spreadPerShotDegrees = 1.2f;
		[SerializeField, Min(0.0f)] private float shotSpreadRecoveryPerSecond = 3.0f;
		[SerializeField, Min(0.0f)] private float leadTime = 0.12f;

		private MobileNPCBrainDriver brainDriver;
		private Transform trackedTarget;
		private Vector3 lastTargetPosition;
		private Vector3 estimatedTargetVelocity;
		private float targetAcquiredAt;
		private float currentReactionTime;
		private float suppressedUntil;
		private float lastAimUpdateTime;
		private float accumulatedShotSpread;
		private bool hasTarget;

		private void Awake()
		{
			brainDriver = GetComponent<MobileNPCBrainDriver>();
		}

		private void Update()
		{
			if (!Provider.isServer || brainDriver == null)
			{
				return;
			}

			float now = Time.time;
			float delta = Mathf.Max(0.0f, now - lastAimUpdateTime);
			lastAimUpdateTime = now;
			accumulatedShotSpread = Mathf.Max(0.0f, accumulatedShotSpread - shotSpreadRecoveryPerSecond * delta);

			Transform currentTarget = brainDriver.Target;
			if (currentTarget == null)
			{
				ClearTarget();
				return;
			}

			if (!hasTarget || trackedTarget != currentTarget)
			{
				trackedTarget = currentTarget;
				lastTargetPosition = currentTarget.position;
				estimatedTargetVelocity = Vector3.zero;
				targetAcquiredAt = now;
				currentReactionTime = Random.Range(Mathf.Min(minimumReactionTime, maximumReactionTime), Mathf.Max(minimumReactionTime, maximumReactionTime));
				hasTarget = true;
				return;
			}

			if (delta > 0.0001f)
			{
				Vector3 measuredVelocity = (currentTarget.position - lastTargetPosition) / delta;
				estimatedTargetVelocity = Vector3.Lerp(estimatedTargetVelocity, measuredVelocity, Mathf.Clamp01(delta * 5.0f));
			}

			lastTargetPosition = currentTarget.position;
		}

		/// <summary>
		/// Returns false until reaction time has elapsed. The caller supplies the weapon
		/// muzzle and remains responsible for ammo, fire rate, line of sight and damage.
		/// Skill ranges from 0 (inaccurate) to 1 (accurate).
		/// </summary>
		public bool TryGetShotDirection(Vector3 muzzlePosition, bool isMoving, float skill, out Vector3 direction)
		{
			direction = transform.forward;
			if (!Provider.isServer || !hasTarget || trackedTarget == null || Time.time - targetAcquiredAt < currentReactionTime)
			{
				return false;
			}

			skill = Mathf.Clamp01(skill);
			Vector3 aimPoint = trackedTarget.position + estimatedTargetVelocity * Mathf.Max(0.0f, leadTime);
			Vector3 toTarget = aimPoint - muzzlePosition;
			if (toTarget.sqrMagnitude < 0.0001f)
			{
				return false;
			}

			float spread = baseSpreadDegrees + (1.0f - skill) * maximumSpreadDegrees;
			if (isMoving)
			{
				spread += movingSpreadDegrees;
			}
			if (Time.time < suppressedUntil)
			{
				spread += suppressionSpreadDegrees;
			}

			spread = Mathf.Min(maximumSpreadDegrees + movingSpreadDegrees, spread + accumulatedShotSpread);
			direction = (Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0.0f) * toTarget.normalized).normalized;
			accumulatedShotSpread += spreadPerShotDegrees;
			return true;
		}

		/// <summary>Called by authoritative combat/perception when nearby incoming fire is detected.</summary>
		public void ReportSuppression()
		{
			if (Provider.isServer)
			{
				suppressedUntil = Mathf.Max(suppressedUntil, Time.time + Mathf.Max(0.0f, suppressionDuration));
			}
		}

		public void ClearTarget()
		{
			trackedTarget = null;
			estimatedTargetVelocity = Vector3.zero;
			hasTarget = false;
			targetAcquiredAt = 0.0f;
			currentReactionTime = 0.0f;
			accumulatedShotSpread = 0.0f;
		}
	}
}