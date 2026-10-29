////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Tactical movement support for armed mobile NPCs: hold a useful firing distance,
	/// seek cover during reloads, and attempt a lateral flank when the target stays occluded.
	/// All decisions and destination changes are server-authoritative.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	[RequireComponent(typeof(MobileNPCHumanoidPerception))]
	[RequireComponent(typeof(MobileNPCHumanoidRangedCombat))]
	public sealed class MobileNPCTacticalCombatBehavior : MonoBehaviour
	{
		[SerializeField, Min(2f)] private float preferredMinimumRange = 7f;
		[SerializeField, Min(3f)] private float preferredMaximumRange = 24f;
		[SerializeField, Min(0.2f)] private float blockedLineOfFireDelay = 1.6f;
		[SerializeField, Min(1f)] private float flankRadius = 8f;
		[SerializeField, Min(0.5f)] private float flankRetryCooldown = 5f;
		[SerializeField, Min(0.1f)] private float tacticalUpdateInterval = 0.35f;
		[SerializeField, Min(0.1f)] private float eyeHeight = 1.45f;
		[SerializeField, Min(0.1f)] private float targetHeight = 1.0f;
		[SerializeField] private LayerMask lineOfFireMask = ~0;

		private MobileNPCBrainDriver brain;
		private MobileNPCHumanoidPerception perception;
		private MobileNPCHumanoidRangedCombat rangedCombat;
		private MobileNPCHumanoidVitals vitals;
		private MobileNPCInventory inventory;
		private MobileNPCGroupMember groupMember;
		private MobileNPCHumanoidMotor motor;
		private float nextUpdateTime;
		private float blockedSince = -1f;
		private float nextFlankTime;
		private float nextCasualtyWithdrawalAt;
		private float nextShotQualityRepositionAt;
		private float nextReloadCoverAttemptAt;
		private float visibleEngagementSince = -1f;
		private Transform subscribedTarget;
		private readonly Vector3[] candidateOffsets = new Vector3[8];

		private void Awake()
		{
			brain = GetComponent<MobileNPCBrainDriver>();
			perception = GetComponent<MobileNPCHumanoidPerception>();
			rangedCombat = GetComponent<MobileNPCHumanoidRangedCombat>();
			vitals = GetComponent<MobileNPCHumanoidVitals>();
			inventory = GetComponent<MobileNPCInventory>();
			groupMember = GetComponent<MobileNPCGroupMember>();
			motor = GetComponent<MobileNPCHumanoidMotor>();

			candidateOffsets[0] = new Vector3(1f, 0f, 0f);
			candidateOffsets[1] = new Vector3(-1f, 0f, 0f);
			candidateOffsets[2] = new Vector3(0.7071f, 0f, 0.7071f);
			candidateOffsets[3] = new Vector3(-0.7071f, 0f, 0.7071f);
			candidateOffsets[4] = new Vector3(0.7071f, 0f, -0.7071f);
			candidateOffsets[5] = new Vector3(-0.7071f, 0f, -0.7071f);
			candidateOffsets[6] = new Vector3(0f, 0f, 1f);
			candidateOffsets[7] = new Vector3(0f, 0f, -1f);
		}

		private void OnEnable()
		{
			if (rangedCombat == null)
				rangedCombat = GetComponent<MobileNPCHumanoidRangedCombat>();
			if (rangedCombat != null)
			{
				rangedCombat.ReloadStarted += HandleReloadStarted;
				rangedCombat.ReloadFinished += HandleReloadFinished;
			}
		}

		private void OnDisable()
		{
			if (rangedCombat != null)
			{
				rangedCombat.ReloadStarted -= HandleReloadStarted;
				rangedCombat.ReloadFinished -= HandleReloadFinished;
			}
			subscribedTarget = null;
		}

		private void Update()
		{
			if (!Provider.isServer || brain == null || perception == null || rangedCombat == null
				|| Time.time < nextUpdateTime || brain.CurrentState != MobileNPCBrain.State.Pursuing)
				return;

			nextUpdateTime = Time.time + Mathf.Max(0.1f, tacticalUpdateInterval);
			Transform target = brain.Target;
			if (target == null)
			{
				ResetOcclusion();
				return;
			}

			if (subscribedTarget != target)
			{
				subscribedTarget = target;
				ResetOcclusion();
			}

			// Never plan a flank from the live transform of an occluded target.
			// The intelligence/memory layer owns investigation after contact is lost.
			Player observedPlayer = target.GetComponent<Player>();
			if (observedPlayer == null)
				observedPlayer = target.GetComponentInParent<Player>();
			MobileNPCHumanoidDisposition observedNPC = target.GetComponent<MobileNPCHumanoidDisposition>();
			if (observedNPC == null)
				observedNPC = target.GetComponentInParent<MobileNPCHumanoidDisposition>();
			Zombie observedZombie = target.GetComponent<Zombie>();
			if (observedZombie == null)
				observedZombie = target.GetComponentInParent<Zombie>();
			bool perceptionConfirmed = observedPlayer != null && perception.VisiblePlayer == observedPlayer
				|| observedNPC != null && perception.VisibleNPC == observedNPC
				|| observedZombie != null && perception.VisibleZombie == observedZombie;
			if (!perceptionConfirmed)
			{
				visibleEngagementSince = -1f;
				ResetOcclusion();
				return;
			}

			if (groupMember == null)
				groupMember = GetComponent<MobileNPCGroupMember>();
			MobileNPCGroupMember.SquadDirective order = groupMember != null
				? groupMember.CurrentSquadDirective
				: MobileNPCGroupMember.SquadDirective.Independent;
			if (order == MobileNPCGroupMember.SquadDirective.Withdraw
				&& Time.time >= nextCasualtyWithdrawalAt
				&& brain.CurrentGoal != MobileNPCTactics.Goal.SeekCover
				&& brain.CurrentGoal != MobileNPCTactics.Goal.Retreat)
			{
				nextCasualtyWithdrawalAt = Time.time + 10f;
				if (brain.TryStartCautiousSquadWithdrawalServer())
				{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					Debug.Log("[MobileNPCTacticalCombatBehavior] " + name
						+ " accepted local leader retreat order after personal sight"
						+ " and physically verified safe escape.", this);
#endif
					return;
				}
			}

			// A local comrade's death may prompt voluntary short withdrawal,
			// but ONLY if this individual still personally sees its opponent.
			// An armed healthy squad remains able to stand and fight. The
			// individual brain validates real cover and local travel before
			// overriding a less urgent flank goal; this never grants knowledge
			// of the killer, damage immunity, or teleportation.
			if (groupMember == null)
				groupMember = GetComponent<MobileNPCGroupMember>();
			if (groupMember != null && groupMember.RecentlyHeardSquadmateFallServer
				&& Time.time >= nextCasualtyWithdrawalAt
				&& brain.CurrentGoal != MobileNPCTactics.Goal.SeekCover
				&& brain.CurrentGoal != MobileNPCTactics.Goal.Retreat
				&& (vitals != null && vitals.HealthFraction
					< (MobileNPCSettings.CombatMode ? 0.38f : 0.60f)
					|| rangedCombat.SelectedGunAsset == null
					|| !rangedCombat.IsReloading && rangedCombat.RoundsRemaining <= 2))
			{
				nextCasualtyWithdrawalAt = Time.time + 11f;
				if (brain.TryStartCautiousSquadWithdrawalServer())
				{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					Debug.Log("[MobileNPCTacticalCombatBehavior] " + name
						+ " chose casualty-driven fallback after own visual confirmation"
						+ " and verified local retreat route.", this);
#endif
					return;
				}
			}

			// Holding a group firing line forbids opportunistic solo
			// flanks. Shooting still depends on this individual's own
			// perception, ammo, cover and friendly-fire checks.
			if (order == MobileNPCGroupMember.SquadDirective.Hold
				|| groupMember != null && groupMember.CurrentSquadRole
					== MobileNPCGroupMember.SquadRole.Covering)
				return;

			// Reloading takes priority over flank and spacing. ReportIncomingFire uses the
			// existing server-side cover validation and does not apply any extra damage.
			if (rangedCombat.IsReloading)
			{
				// An exposed survivor may seek nearby REAL shielding while
				// changing magazines, but must not invent incoming fire or
				// become helpless in the open when no accessible cover exists.
				if (brain.CurrentGoal == MobileNPCTactics.Goal.None
					&& Time.time >= nextReloadCoverAttemptAt
					&& (target.position - transform.position).sqrMagnitude > 49f)
				{
					nextReloadCoverAttemptAt = Time.time + 4.5f;
					if (Random.value < 0.72f)
						brain.TrySeekReloadCoverServer(target.position);
				}
				return;
			}

			Vector3 targetPoint = target.position + Vector3.up * targetHeight;
			Vector3 origin = transform.position + Vector3.up * eyeHeight;
			Vector3 toTarget = targetPoint - origin;
			float distance = toTarget.magnitude;
			bool visibleNow = IsClearLineOfFire(origin, targetPoint, target);
			bool targetConfirmed = IsCurrentTargetConfirmed(target);

			if (!targetConfirmed)
			{
				ResetOcclusion();
				return;
			}

			if (visibleNow)
			{
				blockedSince = -1f;
				if (visibleEngagementSince < 0f)
					visibleEngagementSince = Time.time;
				// Holding line of sight indefinitely is not automatically tactically useful.
				// After sustained exposure, cautiously seek a nearby VERIFIED firing
				// position, without overriding cover, retreat, squad holds or reload.
				if (Time.time - visibleEngagementSince >= 5f
					&& Time.time >= nextShotQualityRepositionAt
					&& brain.CurrentGoal == MobileNPCTactics.Goal.None
					&& order != MobileNPCGroupMember.SquadDirective.Hold
					&& rangedCombat.SelectedGunAsset != null
					&& rangedCombat.RoundsRemaining > 0
					&& distance >= preferredMinimumRange
					&& distance <= preferredMaximumRange)
				{
					nextShotQualityRepositionAt = Time.time + 9f;
					if (TryShortCombatRepositionServer(target))
					{
						visibleEngagementSince = Time.time;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
						Debug.Log("[MobileNPCTacticalCombatBehavior] Verified firing-lane reposition after sustained visible engagement npc=" + name, this);
#endif
						return;
					}
				}

				// An unobstructed shot against our committed target is an
				// opportunity, even at point-blank distance. Do not launch an
				// optional flank merely because the enemy moved closer; the
				// native gun and melee executors determine the actual attack.
				// Urgent cover, retreat and physical collision recovery remain
				// independent of this optional firing-position planner.
				return;
			}

			visibleEngagementSince = -1f;
			// An empty or unusable gun cannot benefit from a firing-angle
			// flank. Preserve the target for pursuit, melee or real reload.
			if (rangedCombat.SelectedGunAsset == null
				|| rangedCombat.RoundsRemaining <= 0)
			{
				blockedSince = -1f;
				return;
			}

			if (blockedSince < 0f)
				blockedSince = Time.time;

			if (Time.time - blockedSince >= blockedLineOfFireDelay
					* (MobileNPCSettings.CombatMode ? 0.65f : 1f)
				&& Time.time >= nextFlankTime)
				TryChooseFlank(target, false);
		}

		/// <summary>
		/// A trained survivor who has repeatedly missed may seek a short real
		/// alternate firing position rather than endlessly wasting ammunition.
		/// This never grants an enemy Transform: brain/perception have already
		/// identified the same *currently visible* target. Navigation validates
		/// every candidate; failing all checks leaves existing combat intact.
		/// </summary>
		public bool TryImproveFiringPositionServer()
		{
			if (!Provider.isServer || brain == null || perception == null
				|| motor == null || rangedCombat == null
				|| Time.time < nextShotQualityRepositionAt
				|| brain.CurrentState != MobileNPCBrain.State.Pursuing
				|| brain.CurrentGoal != MobileNPCTactics.Goal.None
				|| rangedCombat.IsReloading)
				return false;
			Transform enemy = brain.Target;
			if (enemy == null) return false;
			Player player = enemy.GetComponentInParent<Player>();
			MobileNPCHumanoidDisposition npc =
				enemy.GetComponentInParent<MobileNPCHumanoidDisposition>();
			Zombie zombie = enemy.GetComponentInParent<Zombie>();
			bool seen = player != null && perception.VisiblePlayer == player
				|| npc != null && perception.VisibleNPC == npc
				|| zombie != null && perception.VisibleZombie == zombie;
			if (!seen) return false;
			// Geometry probing is capped and staggered across survivors.
			nextShotQualityRepositionAt = Time.time + Random.Range(7f, 11f);
			Vector3 toEnemy = enemy.position - transform.position;
			toEnemy.y = 0f;
			if (toEnemy.sqrMagnitude < 16f * 16f) return false;
			Vector3 lateral = Vector3.Cross(Vector3.up, toEnemy.normalized);
			Vector3[] attempts =
			{
				transform.position + lateral * 5f,
				transform.position - lateral * 5f,
				transform.position + (lateral + toEnemy.normalized * 0.3f).normalized * 6f,
				transform.position + (-lateral + toEnemy.normalized * 0.3f).normalized * 6f
			};
			for (int i = 0; i < attempts.Length; ++i)
			{
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(attempts[i],
					1.5f, 1.4f, RayMasks.BLOCK_COLLISION | RayMasks.GROUND2, 45f,
					out Vector3 safe, out _)
					|| Mathf.Abs(safe.y - transform.position.y) > 0.65f
					|| !motor.CanReachNearbyCoverServer(safe)
					|| !IsClearLineOfFire(safe + Vector3.up * eyeHeight,
						enemy.position + Vector3.up * targetHeight, enemy))
					continue;
				if (brain.TryStartFlank(safe)) return true;
			}
			return false;
		}

		private bool IsCurrentTargetConfirmed(Transform target)
		{
			Player player = target.GetComponent<Player>();
			if (player == null) player = target.GetComponentInParent<Player>();
			if (player != null)
				return player.life != null && !player.life.isDead;

			MobileNPCHumanoidDisposition npc = target.GetComponent<MobileNPCHumanoidDisposition>();
			if (npc == null) npc = target.GetComponentInParent<MobileNPCHumanoidDisposition>();
			if (npc != null)
			{
				MobileNPCHumanoidVitals targetVitals = npc.GetComponent<MobileNPCHumanoidVitals>();
				return targetVitals != null && !targetVitals.IsDead
					&& MobileNPCHumanoidDisposition.AreHostile(GetComponent<MobileNPCHumanoidDisposition>(), npc);
			}

			Zombie zombie = target.GetComponent<Zombie>();
			if (zombie == null) zombie = target.GetComponentInParent<Zombie>();
			return zombie != null && !zombie.isDead;
		}

		private bool IsClearLineOfFire(Vector3 origin, Vector3 targetPoint, Transform target)
		{
			Vector3 offset = targetPoint - origin;
			if (offset.sqrMagnitude < 0.01f)
				return true;

			// Ignore our own hitbox and resolve the first external obstacle.
			// A single Physics.Raycast can otherwise misclassify our own collider
			// as blocked line of fire and force endless flanking.
			RaycastHit[] hits = Physics.RaycastAll(origin, offset.normalized, offset.magnitude,
				lineOfFireMask, QueryTriggerInteraction.Ignore);
			float nearestDistance = float.PositiveInfinity;
			Transform nearest = null;
			for (int i = 0; i < hits.Length; ++i)
			{
				Transform hitTransform = hits[i].transform;
				if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
					continue;
				if (hits[i].distance < nearestDistance)
				{
					nearestDistance = hits[i].distance;
					nearest = hitTransform;
				}
			}
			if (nearest == null)
				return true;
			return nearest == target || target.IsChildOf(nearest) || nearest.IsChildOf(target);
		}

		/// <summary>
		/// Small verified side-step / withdrawal for armed combatants.
		/// Ground, real continuous route and a clear firing lane are
		/// all required before the existing tactical brain accepts it.
		/// Neither target location nor route knowledge is invented.
		/// </summary>
		private bool TryShortCombatRepositionServer(Transform target)
		{
			if (!Provider.isServer || motor == null || target == null)
				return false;
			Vector3 fromThreat = transform.position - target.position;
			fromThreat.y = 0f;
			if (fromThreat.sqrMagnitude < 0.04f)
				fromThreat = -transform.forward;
			fromThreat.Normalize();
			Vector3 sideways = Vector3.Cross(Vector3.up, fromThreat).normalized;
			MobileNPCZombieRiskAssessment risk =
				GetComponent<MobileNPCZombieRiskAssessment>();
			Vector3 best = Vector3.zero;
			float bestScore = float.NegativeInfinity;
			for (int i = 0; i < 4; ++i)
			{
				Vector3 bearing = i == 0 ? sideways
					: i == 1 ? -sideways
					: i == 2 ? (fromThreat + sideways).normalized
					: (fromThreat - sideways).normalized;
				Vector3 guess = transform.position + bearing * 4.1f;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(guess,
					1.2f, 1.5f, RayMasks.BLOCK_COLLISION | RayMasks.GROUND2,
					50f, out Vector3 position, out Collider support, out _)
					|| support == null
					|| !Level.checkSafeIncludingClipVolumes(position)
					|| !motor.CanReachNearbyCoverServer(position)
					|| risk != null && risk.GetKnownRiskAtServer(position) >= 0.85f)
					continue;
				Vector3 eye = position + Vector3.up * eyeHeight;
				if (!IsClearLineOfFire(eye, target.position
					+ Vector3.up * targetHeight, target))
					continue;
				float spacing = Vector3.Distance(position, target.position);
				if (spacing < 4.5f || spacing > preferredMaximumRange)
					continue;
				float score = -Mathf.Abs(spacing - Mathf.Max(8f, preferredMinimumRange))
					+ (risk != null ? -risk.GetKnownRiskAtServer(position) * 5f : 0f)
					+ NearbySolidCoverPreference(position, target.position)
					+ Random.Range(0f, 0.3f);
				if (score <= bestScore)
					continue;
				best = position;
				bestScore = score;
			}
			return bestScore > float.NegativeInfinity && brain.TryStartFlank(best);
		}

		private void TryChooseFlank(Transform target, bool targetVisible)
		{
			// Halo-style engage behavior: a flank is a supporting movement plan,
			// not a replacement for an already committed tactical objective.
			// Never start competing candidate searches while cover, retreat,
			// a prior flank or any other explicit tactical order is active.
			// Arrival / genuine route failure / expiration releases that goal.
			if (brain == null || target == null
				|| brain.CurrentGoal != MobileNPCTactics.Goal.None)
				return;

			// In the combat sandbox, an immediately reachable 4m lateral
			// shift is more useful than committing to a speculative 12m
			// flank across a building when the native ASPFP is absent.
			if (MobileNPCSettings.CombatOnly
				&& TryShortCombatRepositionServer(target))
			{
				nextFlankTime = Time.time + Mathf.Max(0.5f, flankRetryCooldown);
				ResetOcclusion();
				return;
			}

			Vector3 targetPosition = target.position;
			Vector3 awayFromTarget = transform.position - targetPosition;
			awayFromTarget.y = 0f;
			if (awayFromTarget.sqrMagnitude < 0.01f)
				awayFromTarget = -transform.forward;
			awayFromTarget.Normalize();

			Vector3 lateral = Vector3.Cross(Vector3.up, awayFromTarget).normalized;
			// Prefer a lateral reposition first; then test diagonal positions around the target.
			Vector3[] directions =
			{
				lateral, -lateral,
				(awayFromTarget + lateral).normalized,
				(awayFromTarget - lateral).normalized,
				(-awayFromTarget + lateral).normalized,
				(-awayFromTarget - lateral).normalized
			};

			float bestScore = float.NegativeInfinity;
			Vector3 bestPosition = Vector3.zero;
			bool found = false;
			for (int i = 0; i < directions.Length; ++i)
			{
				Vector3 raw = targetPosition + directions[i] * flankRadius;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(raw, 5f, 4f, ~0, 38f,
					out Vector3 candidate, out Collider groundCollider, out _)
					|| (groundCollider != null
						&& (groundCollider.GetComponentInParent<InteractableStorage>() != null
							|| groundCollider.GetComponentInParent<InteractableBed>() != null
							|| groundCollider.GetComponentInParent<InteractableMannequin>() != null)))
					continue;

				if ((candidate - transform.position).sqrMagnitude < 9f)
					continue;

				// Clear firing lanes are not enough: a point across a house wall
				// is useless without a physically traversable route. Reject it
				// before committing the flank so the NPC does not circle buildings
				// toward unreachable tactical destinations.
				if (motor == null || !motor.CanReachNearbyCoverServer(candidate))
					continue;

				Vector3 candidateOrigin = candidate + Vector3.up * eyeHeight;
				Vector3 candidateTarget = targetPosition + Vector3.up * targetHeight;
				if (!IsClearLineOfFire(candidateOrigin, candidateTarget, target))
					continue;

				float candidateDistance = Vector3.Distance(candidate, targetPosition);
				if (candidateDistance < preferredMinimumRange || candidateDistance > preferredMaximumRange)
					continue;

				// Favor side/rear displacement and avoid a candidate that is almost the same
				// direction from the target as the NPC's current position.
				Vector3 candidateRadial = candidate - targetPosition;
				candidateRadial.y = 0f;
				candidateRadial.Normalize();
				float angularChange = 1f - Mathf.Clamp(Vector3.Dot(awayFromTarget, candidateRadial), -1f, 1f);
				float score = angularChange * 5f - Mathf.Abs(candidateDistance - 14f) * 0.15f
					+ NearbySolidCoverPreference(candidate, targetPosition);
				if (score > bestScore)
				{
					bestScore = score;
					bestPosition = candidate;
					found = true;
				}
			}

			nextFlankTime = Time.time + Mathf.Max(0.5f, flankRetryCooldown
				* (MobileNPCSettings.CombatMode ? 0.75f : 1f));
			if (found)
			{
				brain.TryStartFlank(bestPosition);
				ResetOcclusion();
				return;
			}

			// Exceptional high ground is ONLY considered when all ordinary
			// lateral flanks are unavailable. The threat must remain
			// directly confirmed by current perception in Update, and the
			// actual firing line must improve. No surprise roof searches,
			// omniscient target positions, or needless exposed jumping.
			if (!targetVisible
				&& brain.CurrentGoal == MobileNPCTactics.Goal.None
				&& rangedCombat.SelectedGunAsset != null
				&& rangedCombat.RoundsRemaining > 0 && !rangedCombat.IsReloading
				&& vitals != null && vitals.HealthFraction >= 0.70f
				&& motor != null
				&& motor.TryFindObservedElevatedFiringPositionServer(
					targetPosition, out Vector3 observedRaised)
				&& IsClearLineOfFire(observedRaised + Vector3.up * eyeHeight,
					targetPosition + Vector3.up * targetHeight, target)
				&& brain.TryStartObservedElevatedFlankServer(observedRaised))
			{
				ResetOcclusion();
				return;
			}
			if (!targetVisible)
			{
				// No physically validated better firing location exists;
				// preserve ordinary target memory and path recovery.
				blockedSince = Time.time;
			}
		}

		/// <summary>Soft preference for a firing lane with nearby solid
		/// shielding to fall behind during a later reload. Shooting remains
		/// possible from the open: this never filters candidate positions.
		/// At most three short, nonallocating static-geometry ray tests.</summary>
		private float NearbySolidCoverPreference(Vector3 firingPosition, Vector3 enemyPosition)
		{
			Vector3 away = firingPosition - enemyPosition;
			away.y = 0f;
			if (away.sqrMagnitude < 1f)
				return 0f;
			away.Normalize();
			Vector3 side = Vector3.Cross(Vector3.up, away);
			Vector3 enemyEye = enemyPosition + Vector3.up * targetHeight;
			for (int i = 0; i < 3; ++i)
			{
				Vector3 pocket = firingPosition + (i == 0 ? side * 1.6f
					: i == 1 ? -side * 1.6f : away * 1.8f);
				Vector3 delta = pocket + Vector3.up * 1.05f - enemyEye;
				float distance = delta.magnitude;
				if (distance < 0.5f)
					continue;
				RaycastHit hit;
				if (!Physics.Raycast(enemyEye, delta / distance, out hit,
					distance - 0.35f, RayMasks.BLOCK_COLLISION,
					QueryTriggerInteraction.Ignore))
					continue;
				Collider blocker = hit.collider;
				if (blocker == null || blocker.transform.IsChildOf(transform)
					|| blocker.attachedRigidbody != null
						&& !blocker.attachedRigidbody.isKinematic
					|| blocker.GetComponentInParent<Player>() != null
					|| blocker.GetComponentInParent<Zombie>() != null
					|| blocker.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
					continue;
				return 2.2f;
			}
			return 0f;
		}

		private void HandleReloadStarted()
		{
			if (!Provider.isServer || brain == null || perception == null || brain.Target == null)
				return;

			// Reload protection may use a source position only when the target is
			// *currently* visible to this NPC. The old implementation queried the
			// live hidden Transform and effectively granted X-ray cover planning.
			Transform target = brain.Target;
			Player player = target.GetComponentInParent<Player>();
			MobileNPCHumanoidDisposition npc = target.GetComponentInParent<MobileNPCHumanoidDisposition>();
			Zombie zombie = target.GetComponentInParent<Zombie>();
			bool confirmed = player != null && perception.VisiblePlayer == player
				|| npc != null && perception.VisibleNPC == npc
				|| zombie != null && perception.VisibleZombie == zombie;
			// Reloading is NOT evidence of incoming hostile fire. Calling
			// ReportIncomingFire here fabricated a suppression stimulus and
			// could repeatedly replace a valid attack with SeekCover/Retreat.
			// Real damage and audible hostile gunfire reach that routine through
			// their own server-side observation paths. Keep the current target
			// and any already-active tactical goal while the native reload runs.
			if (confirmed)
				nextFlankTime = Mathf.Max(nextFlankTime, Time.time + 0.75f);
		}

		private void HandleReloadFinished()
		{
			// The existing tactical goal expires or clears when its destination is reached.
			// Keep a short cooldown so the NPC does not oscillate between cover and firing.
			nextFlankTime = Mathf.Max(nextFlankTime, Time.time + 1.5f);
		}

		private void ResetOcclusion()
		{
			blockedSince = -1f;
		}
	}
}
