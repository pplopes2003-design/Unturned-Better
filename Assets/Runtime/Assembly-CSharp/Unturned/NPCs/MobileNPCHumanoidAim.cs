////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
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
		[SerializeField, Min(0.0f)] private float targetAimHeight = 1.0f;

		private MobileNPCBrainDriver brainDriver;
		private MobileNPCHumanoidPerception perception;
		private MobileNPCHumanoidDisposition disposition;
		private Transform trackedTarget;
		private Vector3 lastTargetPosition;
		private Vector3 estimatedTargetVelocity;
		private float targetAcquiredAt;
		private float currentReactionTime;
		private float suppressedUntil;
		private float lastAimUpdateTime;
		private float accumulatedShotSpread;
		private bool hasTarget;
		private const int MaxBallisticRecords = 36;
		private const int MaxTrainingObservations = 28;
		private readonly List<BallisticRecord> ballisticExperience = new List<BallisticRecord>(MaxBallisticRecords);

		[System.Serializable]
		public sealed class BallisticRecord
		{
			public ushort gunId;
			public byte band;
			public byte profile;
			public int attempts;
			public int hits;
		}

		/// <summary>Individual evidence, never a shared squad-wide aiming solver.</summary>
		public List<BallisticRecord> ExportBallisticExperienceServer()
		{
			List<BallisticRecord> result = new List<BallisticRecord>(ballisticExperience.Count);
			if (!Provider.isServer) return result;
			foreach (BallisticRecord b in ballisticExperience)
				result.Add(new BallisticRecord { gunId = b.gunId, band = b.band,
					profile = b.profile, attempts = b.attempts, hits = b.hits });
			return result;
		}

		public void RestoreBallisticExperienceServer(List<BallisticRecord> records)
		{
			if (!Provider.isServer) return;
			ballisticExperience.Clear();
			if (records == null) return;
			foreach (BallisticRecord b in records)
			{
				if (b == null || b.gunId == 0 || b.band > 2 || b.profile > 8
					|| b.attempts < 0 || b.attempts > MaxTrainingObservations
					|| b.hits < 0 || b.hits > b.attempts
					|| ballisticExperience.Count >= MaxBallisticRecords)
					continue;
				ballisticExperience.Add(new BallisticRecord { gunId = b.gunId,
					band = b.band, profile = b.profile, attempts = b.attempts, hits = b.hits });
			}
		}

		private static byte GetBallisticBand(float distance)
		{
			return (byte)(distance < 35f ? 0 : distance < 75f ? 1 : 2);
		}

		private BallisticRecord FindBallisticRecord(ushort gunId, byte band, int profile)
		{
			for (int i = 0; i < ballisticExperience.Count; ++i)
			{
				BallisticRecord r = ballisticExperience[i];
				if (r.gunId == gunId && r.band == band && r.profile == profile)
					return r;
			}
			return null;
		}

		/// <summary>
		/// Choose a small imperfect lead/drop guess, not the mathematically optimal
		/// impact point. A few real hits can shift preferences among nine presets.
		/// Every preset retains error and exploration; no perfect targeting.
		/// </summary>
		private int SelectBallisticProfile(ushort gunId, float distance)
		{
			byte band = GetBallisticBand(distance);
			if (Random.value < 0.23f)
				return Random.Range(0, 9);
			int bestProfile = 4;
			float bestScore = float.NegativeInfinity;
			for (int i = 0; i < 9; ++i)
			{
				BallisticRecord record = FindBallisticRecord(gunId, band, i);
				float score = record == null ? 0.49f : (record.hits + 1f) / (record.attempts + 2f);
				// Untried choices are plausible, not immediately trusted.
				score += Random.Range(-0.065f, 0.065f);
				if (i == 4) score += 0.045f;
				if (score > bestScore) { bestScore = score; bestProfile = i; }
			}
			return bestProfile;
		}

		/// <summary>
		/// Estimate a finite-ammunition engagement from this survivor's own
		/// observed impacts. The cautious decision never learns exact victim
		/// locations or turns an unseen target into permission to fire.
		/// Unknown weapons have a neutral prior, not a perfect hit chance.
		/// </summary>
		public float GetLearnedHitLikelihoodServer(ushort gunId, float distance,
			out int observations)
		{
			observations = 0;
			if (!Provider.isServer || gunId == 0)
				return 0.5f;
			byte band = GetBallisticBand(distance);
			int hits = 0;
			for (int i = 0; i < ballisticExperience.Count; ++i)
			{
				BallisticRecord r = ballisticExperience[i];
				if (r.gunId != gunId || r.band != band) continue;
				observations += r.attempts;
				hits += r.hits;
			}
			return (hits + 2f) / (observations + 4f);
		}

		/// <summary>Only genuine resolved ballistic shot feedback is training data.</summary>
		public void RecordBallisticOutcomeServer(ushort gunId, float distance, int profile, bool hit)
		{
			if (!Provider.isServer || gunId == 0 || profile < 0 || profile > 8)
				return;
			byte band = GetBallisticBand(distance);
			BallisticRecord record = FindBallisticRecord(gunId, band, profile);
			if (record == null)
			{
				if (ballisticExperience.Count >= MaxBallisticRecords)
					ballisticExperience.RemoveAt(0);
				record = new BallisticRecord { gunId = gunId, band = band,
					profile = (byte)profile };
				ballisticExperience.Add(record);
			}
			if (record.attempts >= MaxTrainingObservations)
			{
				record.attempts = Mathf.Max(1, record.attempts / 2);
				record.hits = Mathf.Min(record.attempts, record.hits / 2);
			}
			++record.attempts;
			if (hit) ++record.hits;
		}

		private void Awake()
		{
			brainDriver = GetComponent<MobileNPCBrainDriver>();
			perception = GetComponent<MobileNPCHumanoidPerception>();
			disposition = GetComponent<MobileNPCHumanoidDisposition>();
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

			// Visual awareness is NOT permission to aim at a peaceful player.
			// Only a hostile role or the NPC's own recent verified attacker
			// can become an aim target, and the combat driver still confirms
			// the target before a shot.
			// The brain owns target commitment. Perception confirms that the
			// SAME opponent is currently seen and still legally engageable.
			// Otherwise aim would lock onto a different nearby opponent,
			// repeatedly resetting reaction time while the weapon executor
			// correctly tried to fire at the committed victim.
			Transform selectedTarget = brainDriver.Target;
			Transform currentTarget = null;
			if (selectedTarget != null)
			{
				Player selectedPlayer = selectedTarget.GetComponentInParent<Player>();
				MobileNPCHumanoidDisposition selectedNPC =
					selectedTarget.GetComponentInParent<MobileNPCHumanoidDisposition>();
				Zombie selectedZombie = selectedTarget.GetComponentInParent<Zombie>();
				if (perception == null)
					currentTarget = selectedTarget;
				else if (selectedPlayer != null && perception.VisiblePlayer == selectedPlayer
					&& disposition != null && disposition.CanEngagePlayer(selectedPlayer))
					currentTarget = selectedPlayer.transform;
				else if (selectedNPC != null && perception.VisibleNPC == selectedNPC)
					currentTarget = selectedNPC.transform;
				else if (selectedZombie != null && perception.VisibleZombie == selectedZombie)
					currentTarget = selectedZombie.transform;
			}
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
				currentReactionTime = Random.Range(Mathf.Min(minimumReactionTime, maximumReactionTime), Mathf.Max(minimumReactionTime, maximumReactionTime))
                    * MobileNPCSettings.ReactionTimeMultiplier;
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
			return TryGetShotDirection(muzzlePosition, isMoving, skill, 0f, out direction);
		}

		/// <summary>Weapon-aware overload that incorporates the selected firearm's authored spread.</summary>
		public bool TryGetShotDirection(Vector3 muzzlePosition, bool isMoving, float skill, float weaponSpreadDegrees, out Vector3 direction)
		{
			direction = transform.forward;
			if (!Provider.isServer || !hasTarget || trackedTarget == null || Time.time - targetAcquiredAt < currentReactionTime)
			{
				return false;
			}

			skill = Mathf.Clamp01(skill);
			// Aim above the root pivot: humanoid target transforms commonly sit at the feet.
			// Keeping this offset configurable avoids every rifle firing toward the ground.
			Vector3 aimPoint = trackedTarget.position + Vector3.up * Mathf.Max(0.0f, targetAimHeight)
				+ estimatedTargetVelocity * Mathf.Max(0.0f, leadTime);
			Vector3 toTarget = aimPoint - muzzlePosition;
			if (toTarget.sqrMagnitude < 0.0001f)
			{
				return false;
			}

			float spread = (baseSpreadDegrees + (1.0f - skill) * maximumSpreadDegrees + Mathf.Max(0f, weaponSpreadDegrees))
                * MobileNPCSettings.AimSpreadMultiplier;
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

		/// <summary>
		/// A human-like ballistic estimate based only on a currently seen target's
		/// sampled velocity and the actual firearm's velocity/gravity. Scoped aiming
		/// improves precision but never gives live future target coordinates.
		/// </summary>
		public bool TryGetBallisticShotDirection(Vector3 muzzlePosition, bool isMoving,
			float skill, float weaponSpreadDegrees, float bulletSpeed, float gravityScale,
			float opticZoom, ushort gunId, out Vector3 direction, out int chosenProfile)
		{
			direction = transform.forward;
			chosenProfile = -1;
			if (!Provider.isServer || !hasTarget || trackedTarget == null
				|| Time.time - targetAcquiredAt < currentReactionTime
				|| bulletSpeed <= 0.01f)
				return false;
			float distance = Vector3.Distance(muzzlePosition,
				trackedTarget.position + Vector3.up * targetAimHeight);
			if (distance < 0.5f) return false;
			bool scoped = opticZoom >= 2.5f && !isMoving;
			// Focusing through a real optical sight requires extra settling time.
			if (scoped && Time.time - targetAcquiredAt < currentReactionTime + 0.65f)
				return false;
			chosenProfile = SelectBallisticProfile(gunId, distance);
			float estimatedFlight = Mathf.Min(1.8f, distance / bulletSpeed
				* Random.Range(0.90f, 1.12f));
			float leadFraction = 0.35f + (chosenProfile % 3) * 0.23f;
			float dropFraction = 0.70f + (chosenProfile / 3) * 0.18f;
			// Estimated velocity comes solely from prior visual samples.
			// Unsteady, moving or suppressed aim learns nothing about hidden actors.
			Vector3 aimPoint = trackedTarget.position
				+ Vector3.up * Mathf.Max(0f, targetAimHeight);
			if (!isMoving)
				aimPoint += Vector3.ClampMagnitude(estimatedTargetVelocity, 6f)
					* estimatedFlight * leadFraction;
			if (Provider.modeConfigData.Gameplay.Ballistics)
				aimPoint.y += Mathf.Max(0f, -Physics.gravity.y * gravityScale)
					* 0.5f * estimatedFlight * estimatedFlight * dropFraction;

			Vector3 delta = aimPoint - muzzlePosition;
			if (delta.sqrMagnitude < 0.01f) return false;
			skill = Mathf.Clamp01(skill);
			float spread = scoped
				? 0.55f + (1f - skill) * 1.75f
				: baseSpreadDegrees + (1f - skill) * maximumSpreadDegrees;
			spread += Mathf.Max(0f, weaponSpreadDegrees);
			spread += Mathf.Clamp((distance - 35f) * 0.011f, 0f, 1.1f);
			if (isMoving) spread += movingSpreadDegrees;
			if (Time.time < suppressedUntil) spread += suppressionSpreadDegrees;
			spread = Mathf.Clamp((spread + accumulatedShotSpread)
				* MobileNPCSettings.AimSpreadMultiplier, 0.45f, maximumSpreadDegrees + movingSpreadDegrees);
			// Per-shot hand tremor is independent of the velocity/drop prediction.
			direction = (Quaternion.Euler(Random.Range(-spread, spread),
				Random.Range(-spread, spread), 0f) * delta.normalized).normalized;
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