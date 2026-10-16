////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Opt-in server-side decision adapter for a future mobile humanoid entity.
	/// The host remains responsible for perception, navigation, damage and replication.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCBrainDriver : MonoBehaviour
	{
		private MobileNPCBrain brain;
		private MobileNPCTactics tactics;
		private readonly MobileNPCPlayerKnowledge playerKnowledge = new MobileNPCPlayerKnowledge();
		private bool investigatingPlayerClue;
		// A heard footstep is a provisional hypothesis, not a persistent
		// crouch/stealth command after actual visual recognition.
		private bool investigatingFootstepOnly;
		private float nextDefensiveReplanAt;
		private Vector3 lastDefensiveThreatPosition;
		private float nextStalkPlanAt;
		private float ambushArmedUntil;
		private float ambushAttackUnlockAt;
		private Transform ambushTarget;
		public enum AmbushMethod { None, Ranged, CloseAssault }
		private AmbushMethod ambushMethod;
		private float ambushMethodExpiresAt;
		public AmbushMethod CurrentAmbushMethod =>
			ambushMethod != AmbushMethod.None && Time.time < ambushMethodExpiresAt
			? ambushMethod : AmbushMethod.None;
		public bool HoldRangedFireForCloseAssault => CurrentAmbushMethod == AmbushMethod.CloseAssault
			&& brain != null && brain.Target == ambushTarget;
		/// <summary>Combat must wait for a finite visual confirmation/ready interval.</summary>
		public bool IsAmbushPreparing => Provider.isServer && hasInitialized && ambushTarget != null
			&& Time.time < ambushAttackUnlockAt
			&& tactics != null && tactics.CurrentGoal != MobileNPCTactics.Goal.Retreat
			&& tactics.CurrentGoal != MobileNPCTactics.Goal.SeekCover;
		private bool hasInitialized;
		private bool hasPublishedDecision;
		private bool lastHasDestination;
		private Vector3 lastDestination;
		private MobileNPCBrain.State lastState;
		private MobileNPCTactics.Goal lastGoal;

		public event System.Action<MobileNPCBrain.State, MobileNPCTactics.Goal, bool, Vector3> DecisionUpdated;

		public MobileNPCBrain.State CurrentState => brain != null ? brain.CurrentState : MobileNPCBrain.State.Idle;
		public MobileNPCTactics.Goal CurrentGoal => tactics != null ? tactics.CurrentGoal : MobileNPCTactics.Goal.None;
		public Transform Target => brain != null ? brain.Target : null;
		public bool HasDestination => tactics != null && tactics.HasTacticalDestination || brain != null && brain.HasDestination;
		public Vector3 Destination => tactics != null && tactics.HasTacticalDestination
			? tactics.TacticalDestination
			: brain != null && brain.HasDestination ? brain.Destination : transform.position;

		public void InitializeServer(Vector3 spawnPosition)
		{
			if (!Provider.isServer)
			{
				return;
			}

			if (brain == null)
			{
				brain = new MobileNPCBrain();
				tactics = new MobileNPCTactics();
			}

			brain.SetSpawnPosition(spawnPosition);
			brain.Reset();
			playerKnowledge.Clear();
			investigatingPlayerClue = false;
			investigatingFootstepOnly = false;
			listenUntil = 0f;
			ambushArmedUntil = 0f;
			ambushAttackUnlockAt = 0f;
			ambushTarget = null;
			ambushMethod = AmbushMethod.None;
			ambushMethodExpiresAt = 0f;
			tactics.ClearGoal();
			hasInitialized = true;
			MobileNPCRegistry.Register(this);
			hasPublishedDecision = false;
			PublishDecisionIfChanged();
		}

		public void ReportVisibleTarget(Transform target)
		{
			if (!CanAcceptServerInput || target == null)
			{
				return;
			}

			// This observation is sourced exclusively from confirmed server vision.
			// Other NPCs/zombies still use the existing brain without player hints.
			Player visiblePlayer = target.GetComponent<Player>();
			if (visiblePlayer == null)
				visiblePlayer = target.GetComponentInParent<Player>();
			if (visiblePlayer != null && visiblePlayer.life != null && !visiblePlayer.life.isDead)
			{
				playerKnowledge.ObserveSight(visiblePlayer.transform.position, Time.time);
				MobileNPCRadioOutbox radioOutbox = GetComponent<MobileNPCRadioOutbox>();
				if (radioOutbox != null)
					radioOutbox.TryQueueObservedContact(visiblePlayer.transform.position);
				MobileNPCHumanoidDisposition ownDisposition = GetComponent<MobileNPCHumanoidDisposition>();
				if (ownDisposition != null && ownDisposition.CanEngagePlayers)
				{
					MobileNPCGroupMember group = GetComponent<MobileNPCGroupMember>();
					if (group != null)
						group.BroadcastPlayerContactServer(visiblePlayer.transform.position);
				}
			}

			// Ambush readiness is only armed by an actual concealed approach.
			// Repeated perception scans do not reset its countdown.
			bool wasStalking = tactics.CurrentGoal == MobileNPCTactics.Goal.Stalk;
			if (wasStalking)
			{
				tactics.ClearGoal();
				ambushArmedUntil = Time.time + 2.0f;
			}
			if (Time.time < ambushArmedUntil && ambushTarget == null)
			{
				MobileNPCHumanoidDisposition disposition = GetComponent<MobileNPCHumanoidDisposition>();
				Vector3 delta = target.position - transform.position;
				delta.y = 0f;
				if (disposition != null
					&& disposition.CurrentRole == MobileNPCHumanoidDisposition.Role.Hostile
					&& delta.sqrMagnitude <= 18f * 18f
					&& delta.sqrMagnitude >= 2f * 2f)
				{
					MobileNPCHumanoidRangedCombat ranged = GetComponent<MobileNPCHumanoidRangedCombat>();
					bool gunUsable = ranged != null && ranged.SelectedGunAsset != null
						&& ranged.RoundsRemaining > 0;
					bool closeEnough = delta.sqrMagnitude <= 6f * 6f;
					if (gunUsable && !closeEnough)
						ambushMethod = AmbushMethod.Ranged;
					else if (closeEnough)
						ambushMethod = AmbushMethod.CloseAssault;
					else
						ambushMethod = AmbushMethod.None;

					// No usable attack opportunity means abandon the planned ambush.
					// Ordinary pursuit can still begin if the enemy is confirmed.
					if (ambushMethod != AmbushMethod.None)
					{
						ambushTarget = target;
						ambushMethodExpiresAt = Time.time + 4f;
						ambushAttackUnlockAt = Time.time + UnityEngine.Random.Range(0.45f, 0.85f);
						Debug.Log("[MobileNPCBrainDriver] Visible-target ambush method="
							+ ambushMethod + " with brief ready delay.", this);
					}
					ambushArmedUntil = 0f;
				}
			}
			investigatingPlayerClue = false;
			listenUntil = 0f;
			brain.ReportVisibleTarget(target, Time.time);
			// A healthy armed survivor who has now CONFIRMED the target may
			// end an earlier fallback retreat caused by hearing an unseen shot.
			// Do not cancel cover, low-health escape, or an unrelated objective.
			if (tactics.CurrentGoal == MobileNPCTactics.Goal.Retreat)
			{
				MobileNPCHumanoidVitals ownVitals = GetComponent<MobileNPCHumanoidVitals>();
				MobileNPCHumanoidRangedCombat weapon = GetComponent<MobileNPCHumanoidRangedCombat>();
				if (ownVitals != null && ownVitals.HealthFraction > 0.55f
					&& weapon != null && weapon.SelectedGunAsset != null
					&& weapon.RoundsRemaining > 0)
					tactics.ClearGoal();
			}
			// A real current visual target plus a genuine recent wound can
			// justify a HELP request. A past unseen attacker, mere sound clue,
			// or group-wide knowledge never creates this radio message.
			MobileNPCRadioOutbox supportRadio = GetComponent<MobileNPCRadioOutbox>();
			supportRadio?.TryQueueCombatReinforcementsServer(target);
			PublishDecisionIfChanged();
		}

		public void ReportNoise(Vector3 position, float duration = 25.0f)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			investigatingPlayerClue = false;
			investigatingFootstepOnly = false;
			brain.ReportNoise(position, Time.time, duration);
			PublishDecisionIfChanged();
		}

		private float nextFootstepInvestigationAt;
		private float nextGroupContactAt;
		private float listenUntil;
		private Vector3 listeningClue;

		/// <summary>Briefly stop and crouch to discriminate an uncertain sound before searching.</summary>
		public bool IsListening => Provider.isServer && hasInitialized && Time.time < listenUntil
			&& (GetComponent<MobileNPCHumanoidPerception>() is MobileNPCHumanoidPerception perception
				&& perception.VisiblePlayer == null)
			&& brain != null && brain.CurrentState != MobileNPCBrain.State.Pursuing
			&& tactics != null && tactics.CurrentGoal != MobileNPCTactics.Goal.Retreat
			&& tactics.CurrentGoal != MobileNPCTactics.Goal.SeekCover;
		public Vector3 ListeningClue => listeningClue;

		/// <summary>
		/// Respond to an audible *approximate* clue. This never assigns a visible
		/// target or supplies the player's live Transform to the combat brain.
		/// Repeated footfalls cannot perpetually reset an active search timer.
		/// </summary>
		public void ReportPlayerFootstepClue(Vector3 approximatePosition, float confidence)
		{
			if (!CanAcceptServerInput || Time.time < nextFootstepInvestigationAt
				|| float.IsNaN(confidence) || float.IsInfinity(confidence)
				|| confidence < 0.12f)
				return;
			MobileNPCHumanoidVitals vitals = GetComponent<MobileNPCHumanoidVitals>();
			if (vitals != null && vitals.IsDead)
				return;
			if (brain.CurrentState == MobileNPCBrain.State.Pursuing && brain.Target != null)
				return;
			// The visible player is stronger evidence than a guessed footstep.
			// In particular peaceful NPCs must not duck merely because their
			// visible, nonhostile neighbor takes a step.
			MobileNPCHumanoidPerception perception = GetComponent<MobileNPCHumanoidPerception>();
			if (perception != null && perception.VisiblePlayer != null)
				return;
			if (tactics.CurrentGoal == MobileNPCTactics.Goal.Retreat
				|| tactics.CurrentGoal == MobileNPCTactics.Goal.SeekCover
				|| tactics.CurrentGoal == MobileNPCTactics.Goal.Stalk)
				return;

			// Quiet, ambiguous sound: listen and wait for further evidence rather
			// than always walking to a potentially false position.
			float clarity = Mathf.Clamp01(confidence);
			nextFootstepInvestigationAt = Time.time + Mathf.Lerp(4f, 2.5f, clarity);
			listenUntil = Time.time + Mathf.Lerp(1.4f, 0.55f, clarity);
			listeningClue = approximatePosition;
			playerKnowledge.ObserveSound(approximatePosition, clarity, Time.time);
			if (clarity >= 0.25f)
			{
				brain.ReportNoise(approximatePosition, Time.time, 10f);
				investigatingPlayerClue = true;
				investigatingFootstepOnly = true;
			}
			PublishDecisionIfChanged();
		}

		/// <summary>
		/// A visually identified nonhostile player cancels ONLY an investigation
		/// that began from uncertain footsteps. It does not revoke a genuine
		/// defensive goal, a hostile target or an unrelated supply/escort task.
		/// </summary>
		public void ReportNonThreateningPlayerSightServer()
		{
			if (!CanAcceptServerInput)
				return;
			listenUntil = 0f;
			if (investigatingFootstepOnly
				&& brain.CurrentState == MobileNPCBrain.State.Investigating
				&& tactics.CurrentGoal == MobileNPCTactics.Goal.None)
			{
				brain.Reset();
				investigatingPlayerClue = false;
			}
			investigatingFootstepOnly = false;
			PublishDecisionIfChanged();
		}

		/// <summary>
		/// Consumes an ally's approximate verbal report, not a visual acquisition.
		/// May orient/investigate only while free to do so; cannot interrupt an
		/// active fight, protective cover, retreat or companion following.
		/// </summary>
		public void ReportGroupPlayerContactClue(Vector3 approximatePosition, float confidence)
		{
			if (!CanAcceptServerInput || Time.time < nextGroupContactAt
				|| float.IsNaN(confidence) || float.IsInfinity(confidence)
				|| confidence < 0.2f
				|| float.IsNaN(approximatePosition.x) || float.IsInfinity(approximatePosition.x)
				|| float.IsNaN(approximatePosition.y) || float.IsInfinity(approximatePosition.y)
				|| float.IsNaN(approximatePosition.z) || float.IsInfinity(approximatePosition.z)
				|| brain.CurrentState == MobileNPCBrain.State.Pursuing
				|| brain.CurrentState == MobileNPCBrain.State.Following
				|| tactics.CurrentGoal != MobileNPCTactics.Goal.None)
				return;

			MobileNPCHumanoidVitals vitals = GetComponent<MobileNPCHumanoidVitals>();
			if (vitals != null && vitals.IsDead)
				return;
			// A firsthand sighting outranks uncertain verbal directions. If
			// the same player is visibly harmless, no stealth/listening
			// should be triggered just because a teammate shouts a bearing.
			MobileNPCHumanoidPerception perception = GetComponent<MobileNPCHumanoidPerception>();
			if (perception != null && perception.VisiblePlayer != null)
				return;
			nextGroupContactAt = Time.time + 6f;
			playerKnowledge.ObserveSound(approximatePosition, confidence, Time.time);
			brain.ReportNoise(approximatePosition, Time.time, 12f);
			investigatingPlayerClue = true;
			listeningClue = approximatePosition;
			listenUntil = Time.time + 0.65f;
			PublishDecisionIfChanged();
		}

		/// <summary>
		/// Squad advance is a short, speculative movement toward an ally's
		/// approximate verbal bearing, NOT pursuit of a target Transform. Require
		/// safe ground, clear close-range corridor and cover from the reported area.
		/// Failing to validate any candidate leaves the normal investigation intact.
		/// </summary>
		public bool TryStartSquadAdvance(Vector3 approximateClue)
		{
			if (!CanAcceptServerInput || brain.CurrentState != MobileNPCBrain.State.Investigating
				|| tactics.CurrentGoal != MobileNPCTactics.Goal.None
				|| IsListening || !IsFiniteSquadPoint(approximateClue))
				return false;
			Vector3 direction = approximateClue - transform.position;
			direction.y = 0f;
			if (direction.sqrMagnitude < 64f || direction.sqrMagnitude > 1225f)
				return false;
			direction.Normalize();
			Vector3 lateral = Vector3.Cross(Vector3.up, direction).normalized;
			for (int i = 0; i < 4; ++i)
			{
				float side = i % 2 == 0 ? 1f : -1f;
				float forward = i < 2 ? 4.5f : 3f;
				Vector3 proposed = transform.position + direction * forward + lateral * side * 2.5f;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(proposed, 5f, 5f,
					RayMasks.BLOCK_COLLISION, 40f, out Vector3 ground, out _))
					continue;
				Vector3 corridor = ground - transform.position;
				corridor.y = 0f;
				float length = corridor.magnitude;
				if (length < 2.5f || length > 7f)
					continue;
				RaycastHit[] pathHits = Physics.SphereCastAll(transform.position + Vector3.up,
					0.3f, corridor.normalized, length,
					RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
				bool blocked = false;
				foreach (RaycastHit hit in pathHits)
				{
					Collider collider = hit.collider;
					if (collider == null || collider.transform == transform
						|| collider.transform.IsChildOf(transform)
						|| collider.GetComponentInParent<Player>() != null
						|| collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
						continue;
					if (hit.distance < length - 0.65f)
					{
						blocked = true;
						break;
					}
				}
				if (blocked)
					continue;
				Vector3 eye = approximateClue + Vector3.up * 1.35f;
				Vector3 hideAt = ground + Vector3.up * 1.0f;
				Vector3 coverVector = hideAt - eye;
				if (coverVector.sqrMagnitude < 0.1f)
					continue;
				bool covered = false;
				RaycastHit[] coverHits = Physics.RaycastAll(eye, coverVector.normalized,
					coverVector.magnitude, RayMasks.BLOCK_COLLISION,
					QueryTriggerInteraction.Ignore);
				foreach (RaycastHit hit in coverHits)
				{
					Collider collider = hit.collider;
					if (collider == null || collider.GetComponentInParent<Player>() != null
						|| collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
						continue;
					if (hit.distance < coverVector.magnitude - 0.6f
						&& Vector3.Angle(hit.normal, Vector3.up) > 45f)
					{
						covered = true;
						break;
					}
				}
				if (covered && tactics.TryStartFlank(ground, Time.time))
				{
					PublishDecisionIfChanged();
					return true;
				}
			}
			return false;
		}

		private static bool IsFiniteSquadPoint(Vector3 value)
		{
			return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
				&& !float.IsNaN(value.y) && !float.IsInfinity(value.y)
				&& !float.IsNaN(value.z) && !float.IsInfinity(value.z);
		}

		/// <summary>Update server-authoritative health without implying a new damage event.</summary>
		public void ReportHealth(float currentHealth, float maximumHealth)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			tactics.ReportHealth(currentHealth, maximumHealth);
			PublishDecisionIfChanged();
		}

		/// <summary>Call only after server damage handling has updated health.</summary>
		public void ReportDamage(float currentHealth, float maximumHealth)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			tactics.ReportDamage(currentHealth, maximumHealth, Time.time);
			CancelAmbush();
			PublishDecisionIfChanged();
		}

		/// <summary>Call when server perception detects incoming gunfire without a known source.</summary>
		public void ReportIncomingFire()
		{
			ReportIncomingFire(transform.position, null);
		}

		/// <summary>
		/// Reacts to a gunshot stimulus: suppresses aim, investigates the shooter's last
		/// known position, and attempts to move behind nearby geometry instead of standing exposed.
		/// </summary>
		public void ReportIncomingFire(Vector3 threatPosition, Transform threatTransform)
		{
			if (!CanAcceptServerInput)
				return;

			// Identified shooter evidence may update player knowledge, but a
			// source-less gunshot does not magically identify the player.
			Player shooter = threatTransform != null ? threatTransform.GetComponent<Player>() : null;
			if (shooter == null && threatTransform != null)
				shooter = threatTransform.GetComponentInParent<Player>();
			if (shooter != null)
				playerKnowledge.ObserveSound(threatPosition, 0.8f, Time.time);

			listenUntil = 0f;
			CancelAmbush();
			tactics.ReportIncomingFire(Time.time);
			// Keep a valid cover/retreat destination long enough to travel toward it.
			// Repeated nearby gunshots previously reset the target every frame/shot.
			bool alreadyDefending = tactics.CurrentGoal == MobileNPCTactics.Goal.SeekCover
				|| tactics.CurrentGoal == MobileNPCTactics.Goal.Retreat;
			Vector3 threatDelta = threatPosition - lastDefensiveThreatPosition;
			threatDelta.y = 0f;
			bool keepDefense = alreadyDefending && tactics.HasTacticalDestination
				&& Time.time < nextDefensiveReplanAt && threatDelta.sqrMagnitude < 100f;
			MobileNPCHumanoidAim aim = GetComponent<MobileNPCHumanoidAim>();
			if (aim != null)
				aim.ReportSuppression();

			// Preserve an already-visible combat target; otherwise search the gunshot source.
			if (brain.CurrentState != MobileNPCBrain.State.Pursuing || brain.Target == null)
				brain.ReportNoise(threatPosition, Time.time, 25f);

			if (keepDefense)
			{
				PublishDecisionIfChanged();
				return;
			}

			lastDefensiveThreatPosition = threatPosition;
			nextDefensiveReplanAt = Time.time + 2.5f;
			Vector3? coverPoint = TryFindNearbyCoverPoint(threatPosition);
			// Hearsay alone never permits retaliation. Actual current server
			// perception must confirm THIS attacker before choosing to fight.
			MobileNPCHumanoidPerception eyes = GetComponent<MobileNPCHumanoidPerception>();
			bool visuallyConfirmed = eyes != null && threatTransform != null
				&& (eyes.VisiblePlayer != null && threatTransform.IsChildOf(eyes.VisiblePlayer.transform)
					|| eyes.VisibleNPC != null && threatTransform.IsChildOf(eyes.VisibleNPC.transform)
					|| eyes.VisibleZombie != null && threatTransform.IsChildOf(eyes.VisibleZombie.transform));
			MobileNPCHumanoidRangedCombat weapon = GetComponent<MobileNPCHumanoidRangedCombat>();
			bool canReturnFire = visuallyConfirmed
				&& weapon != null && weapon.SelectedGunAsset != null && weapon.RoundsRemaining > 0;
			if (tactics.TryChooseDefensiveGoal(transform.position, coverPoint, threatPosition, Time.time, canReturnFire)
				&& tactics.CurrentGoal == MobileNPCTactics.Goal.Retreat && !tactics.HasTacticalDestination)
			{
				Vector3 away = transform.position - threatPosition;
				away.y = 0f;
				if (away.sqrMagnitude > 0.01f)
				{
					Vector3 candidate = transform.position + away.normalized * 9f;
					if (MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, 12f, 8f, ~0, 38f,
						out Vector3 safeRetreat, out _))
					{
						tactics.ReportValidatedRetreatPoint(safeRetreat, Time.time);
					}
					else
					{
						// If the map offers no safe retreat point, avoid blindly charging the shooter.
						brain.ReportNoise(transform.position, Time.time, 4f);
					}
				}
			}
			PublishDecisionIfChanged();
		}

		// Navigation failures are learned per actor, never broadcast as magical
		// global path knowledge. Avoid reselecting the exact failed cover point
		// until the player/NPC had time to change the local situation.
		private Vector3 recentlyUnreachableCover;
		private float recentlyUnreachableCoverUntil;

		/// <summary>
		/// Called only after the motor has genuinely failed several physical
		/// route attempts for the SAME tactical destination. This does not
		/// terminate a live pursuit or invent another reachable route.
		/// </summary>
		public bool ReportTacticalDestinationUnreachableServer(Vector3 failedPoint)
		{
			if (!CanAcceptServerInput || tactics == null || !tactics.HasTacticalDestination
				|| (tactics.CurrentGoal != MobileNPCTactics.Goal.SeekCover
					&& tactics.CurrentGoal != MobileNPCTactics.Goal.Flank
					&& tactics.CurrentGoal != MobileNPCTactics.Goal.Loot
					&& tactics.CurrentGoal != MobileNPCTactics.Goal.Investigate
					&& tactics.CurrentGoal != MobileNPCTactics.Goal.Reinforce)
				|| (tactics.TacticalDestination - failedPoint).sqrMagnitude > 4f)
				return false;
			if (tactics.CurrentGoal == MobileNPCTactics.Goal.SeekCover)
			{
				MobileNPCAdaptiveMemory memory = GetComponent<MobileNPCAdaptiveMemory>();
				if (memory != null)
					memory.RecordOwnOutcomeServer(
						MobileNPCAdaptiveMemory.ExperienceKind.CoverAccess,
						tactics.TacticalDestination, false);
			}
			// Unreachable optional errands should end rather than steer
			// into the same post/house indefinitely. Only tactical cover
			// and flank need the specialized short-lived cover blacklist.
			if (tactics.CurrentGoal == MobileNPCTactics.Goal.SeekCover
				|| tactics.CurrentGoal == MobileNPCTactics.Goal.Flank)
			{
				recentlyUnreachableCover = tactics.TacticalDestination;
				recentlyUnreachableCoverUntil = Time.time + 14f;
			}
			tactics.ClearGoal();
			PublishDecisionIfChanged();
			return true;
		}

		/// <summary>
		/// The motor has already spent several physically verified attempts
		/// on this noncombat clue or impossible return point. A failed route
		/// cannot be marked "arrived", and must not lock the brain forever in
		/// Investigating/Returning without a traversable destination.
		/// Tactical priorities, live enemies and companions are NOT cancelled.
		/// </summary>
		public bool ReportNonCombatNavigationExhaustedServer(Vector3 inaccessiblePoint)
		{
			if (!CanAcceptServerInput || brain == null || tactics == null
				|| tactics.CurrentGoal != MobileNPCTactics.Goal.None
				|| brain.Target != null || !brain.HasDestination
				|| (brain.CurrentState != MobileNPCBrain.State.Investigating
					&& brain.CurrentState != MobileNPCBrain.State.Returning)
				|| (brain.Destination - inaccessiblePoint).sqrMagnitude > 4f)
				return false;

			// Abort just the impossible sensory/return task. This does not
			// claim that the sound source was seen, or that the NPC went home.
			// Future firsthand evidence can legitimately start a NEW search.
			MobileNPCEncounterDirector.ReleaseOptionalEvidenceSearchServer(this);
			brain.Reset();
			investigatingPlayerClue = false;
			investigatingFootstepOnly = false;
			listenUntil = 0f;
			playerKnowledge.Clear();
			CancelAmbush();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.Log("[MobileNPCNavigation] Exhausted blocked noncombat goal for "
				+ name + " at " + inaccessiblePoint + "; resumed Idle without teleport.", this);
#endif
			PublishDecisionIfChanged();
			return true;
		}

		private Vector3? TryFindNearbyCoverPoint(Vector3 threatPosition)
		{
			Vector3 away = transform.position - threatPosition;
			away.y = 0f;
			if (away.sqrMagnitude < 0.01f)
				away = -transform.forward;
			away.Normalize();

			// Sample a compact fan behind and to either side of the NPC. A candidate is
			// accepted only when safe ground exists and world geometry blocks the threat ray.
			float[] angles = { -70f, -40f, -20f, 0f, 20f, 40f, 70f };
			float[] distances = { 4f, 7f, 10f };
			MobileNPCAdaptiveMemory memory = GetComponent<MobileNPCAdaptiveMemory>();
			// Contextual epsilon-greedy cover selection over physically validated
			// candidates. Individual first-hand/overheard successes influence score;
			// a small chance to explore prevents permanently avoiding an old failure.
			const int maxCheckedCoverCandidates = 10;
			// Complex corner routes cost a bounded 9x9 A* per candidate.
			// Only test up to three per defensive decision; otherwise this
			// would cause a CPU spike during a 40-NPC urban firefight.
			const int maxComplexCornerCoverChecks = 3;
			int complexCornerCoverChecks = 0;
			MobileNPCHumanoidMotor motor = GetComponent<MobileNPCHumanoidMotor>();
			const float explorationProbability = 0.12f;
			int checkedCoverCandidates = 0;
			float bestCoverScore = float.NegativeInfinity;
			Vector3? bestCover = null;
			Vector3? explorationCover = null;
			for (int d = 0; d < distances.Length; ++d)
			{
				for (int a = 0; a < angles.Length; ++a)
				{
					Vector3 direction = Quaternion.AngleAxis(angles[a], Vector3.up) * away;
					Vector3 rawCandidate = transform.position + direction * distances[d];
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(rawCandidate, 12f, 8f, ~0, 38f,
						out Vector3 candidate, out _))
						continue;
					if (Time.time < recentlyUnreachableCoverUntil
						&& (candidate - recentlyUnreachableCover).sqrMagnitude < 9f)
						continue;

					Vector3 rayOrigin = threatPosition + Vector3.up * 1.4f;
					Vector3 rayTarget = candidate + Vector3.up * 1.0f;
					Vector3 rayOffset = rayTarget - rayOrigin;
					if (rayOffset.sqrMagnitude < 0.1f)
						continue;

					// The shooter-to-cover ray may first hit the shooter's own
					// collider. Inspect the full segment and accept only actual world
					// geometry, not either participant's body.
					RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayOffset.normalized,
						rayOffset.magnitude, ~0, QueryTriggerInteraction.Ignore);
					bool protectedByWorld = false;
					for (int h = 0; h < hits.Length; ++h)
					{
						Collider obstruction = hits[h].collider;
						if (obstruction == null || obstruction.transform == transform
							|| obstruction.transform.IsChildOf(transform))
							continue;
						if (obstruction.GetComponentInParent<Player>() != null
							|| obstruction.GetComponentInParent<MobileNPCHumanoidVitals>() != null
							|| obstruction.GetComponentInParent<Zombie>() != null)
							continue;
						if (hits[h].distance < rayOffset.magnitude - 0.75f
							&& Vector3.Angle(hits[h].normal, Vector3.up) > 50f)
						{
							protectedByWorld = true;
							break;
						}
					}
					if (!protectedByWorld || (candidate - transform.position).sqrMagnitude <= 1f)
						continue;

					// A covered point is useless if a wall blocks the route to it.
					// Check a chest-height corridor; the motor still performs actual
					// navigation and local obstacle avoidance during movement.
					Vector3 approach = candidate - transform.position;
					approach.y = 0f;
					if (approach.sqrMagnitude < 0.5f)
						continue;
					Vector3 routeOrigin = transform.position + Vector3.up * 1f;
					RaycastHit[] approachHits = Physics.SphereCastAll(routeOrigin, 0.25f,
						approach.normalized, approach.magnitude, ~0, QueryTriggerInteraction.Ignore);
					bool blockedApproach = false;
					for (int h = 0; h < approachHits.Length; ++h)
					{
						Collider obstruction = approachHits[h].collider;
						if (obstruction == null || obstruction.transform == transform
							|| obstruction.transform.IsChildOf(transform))
							continue;
						if (obstruction.GetComponentInParent<Player>() != null
							|| obstruction.GetComponentInParent<MobileNPCHumanoidVitals>() != null
							|| obstruction.GetComponentInParent<Zombie>() != null)
							continue;
						if (approachHits[h].distance < approach.magnitude - 0.5f)
						{
							blockedApproach = true;
							break;
						}
					}
					// In urban spaces the best covered place is often *around*
					// a corner, with a wall blocking the direct corridor. A raw
					// straight-line SphereCast would reject all such valid cover.
					// Request a side-effect-free bounded local A* probe only for a
					// few blocked candidates; never accept an unreachable endpoint.
					bool verifiedCornerRoute = false;
					if (blockedApproach && motor != null
						&& complexCornerCoverChecks < maxComplexCornerCoverChecks)
					{
						++complexCornerCoverChecks;
						verifiedCornerRoute = motor.CanReachNearbyCoverServer(candidate);
					}
					if (!blockedApproach || verifiedCornerRoute)
					{
						// Every cover still needs world shielding, genuine ground
						// and a direct physical corridor OR verified local route.
						// Individual success/failure history then biases its score.
						float learnedScore = memory != null
							? memory.GetCoverAccessAdjustment(candidate) : 0f;
						float distanceCost = (candidate - transform.position).magnitude * 0.5f;
						float score = learnedScore - distanceCost;
						if (score > bestCoverScore)
						{
							bestCoverScore = score;
							bestCover = candidate;
						}
						// Reservoir-sampled exploratory option: no List allocations,
						// unbiased among the bounded set of verified candidates.
						++checkedCoverCandidates;
						if (UnityEngine.Random.Range(0, checkedCoverCandidates) == 0)
							explorationCover = candidate;
						if (checkedCoverCandidates >= maxCheckedCoverCandidates)
							break;
					}
				}
				if (checkedCoverCandidates >= maxCheckedCoverCandidates)
					break;
			}
			return explorationCover.HasValue && UnityEngine.Random.value < explorationProbability
				? explorationCover : bestCover;
		}

		/// <summary>
		/// Chooses a defensive goal. Cover must already be checked for reachability and
		/// protection by the host. If retreat is selected, submit a validated safe point.
		/// </summary>
		public bool TryChooseDefensiveGoal(Vector3? validatedCoverPoint, Vector3 threatPosition)
		{
			if (!CanAcceptServerInput)
			{
				return false;
			}

			bool selected = tactics.TryChooseDefensiveGoal(transform.position, validatedCoverPoint, threatPosition, Time.time);
			PublishDecisionIfChanged();
			return selected;
		}

		/// <summary>
		/// Starts a flank only after the caller validates safe ground, line of fire,
		/// and a meaningful displacement from the current position.
		/// </summary>
		public bool TryStartFlank(Vector3 validatedPosition)
		{
			if (!CanAcceptServerInput
				|| float.IsNaN(validatedPosition.x) || float.IsInfinity(validatedPosition.x)
				|| float.IsNaN(validatedPosition.y) || float.IsInfinity(validatedPosition.y)
				|| float.IsNaN(validatedPosition.z) || float.IsInfinity(validatedPosition.z)
				|| (validatedPosition - transform.position).sqrMagnitude < 9f)
				return false;

			bool selected = tactics.TryStartFlank(validatedPosition, Time.time);
			PublishDecisionIfChanged();
			return selected;
		}

		/// <summary>
		/// Exceptional SHORT raised flank: a nearby crate is NOT a normal
		/// navigable floor. Accept ONLY a fresh motor-validated ballistic
		/// landing while an actual target is confirmed and the NPC is still
		/// able to choose a flank. Never accept a guessed higher vantage.
		/// </summary>
		public bool TryStartObservedElevatedFlankServer(Vector3 verifiedLanding)
		{
			if (!CanAcceptServerInput || brain == null || brain.Target == null
				|| brain.CurrentState != MobileNPCBrain.State.Pursuing
				|| tactics == null || tactics.CurrentGoal != MobileNPCTactics.Goal.None
				|| float.IsNaN(verifiedLanding.x) || float.IsInfinity(verifiedLanding.x)
				|| float.IsNaN(verifiedLanding.y) || float.IsInfinity(verifiedLanding.y)
				|| float.IsNaN(verifiedLanding.z) || float.IsInfinity(verifiedLanding.z)
				|| verifiedLanding.y - transform.position.y < 0.40f
				|| verifiedLanding.y - transform.position.y > 1.10f
				|| (verifiedLanding - transform.position).sqrMagnitude > 8f)
				return false;
			MobileNPCHumanoidMotor motor = GetComponent<MobileNPCHumanoidMotor>();
			if (motor == null || !motor.HasValidatedRecentElevatedTacticalGoalServer(
				verifiedLanding))
				return false;

			bool selected = tactics.TryStartFlank(verifiedLanding, Time.time);
			PublishDecisionIfChanged();
			return selected;
		}

		public void ReportValidatedRetreatPoint(Vector3 safePoint)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			tactics.ReportValidatedRetreatPoint(safePoint, Time.time);
			PublishDecisionIfChanged();
		}

		/// <summary>
		/// Starts moving toward a loot item only when the host has validated that item
		/// and confirmed inventory capacity. Actual pickup is a separate server action.
		/// </summary>
		public bool TryStartLoot(Vector3 itemPosition)
		{
			if (!CanAcceptServerInput || !tactics.TryStartLoot(itemPosition, Time.time))
			{
				return false;
			}

			PublishDecisionIfChanged();
			return true;
		}

		/// <summary>Travel to a personally remembered supply site only while safe/idle.
		/// This is an intent to investigate, not confirmation of any new loot.</summary>
		public bool TryStartRememberedSupplySearch(Vector3 validatedPlace)
		{
			if (!CanAcceptServerInput || brain.Target != null
				|| brain.CurrentState != MobileNPCBrain.State.Idle
				|| (float.IsNaN(validatedPlace.x) || float.IsInfinity(validatedPlace.x)
				|| float.IsNaN(validatedPlace.y) || float.IsInfinity(validatedPlace.y)
				|| float.IsNaN(validatedPlace.z) || float.IsInfinity(validatedPlace.z)
				|| !Level.checkSafeIncludingClipVolumes(validatedPlace))
				|| !tactics.TryStartSupplySearch(validatedPlace, Time.time))
				return false;
			PublishDecisionIfChanged();
			return true;
		}

		/// <summary>
		/// Move toward a validated APPROXIMATE wounded ally rendezvous, never
		/// toward an enemy target. Normal motor/pathfinder performs movement;
		/// visual perception must discover any hostile again for actual combat.
		/// </summary>
		public bool TryStartRadioReinforcementServer(Vector3 safeRendezvous)
		{
			if (!CanAcceptServerInput || brain.Target != null
				|| brain.CurrentState != MobileNPCBrain.State.Idle
				|| tactics.CurrentGoal != MobileNPCTactics.Goal.None
				|| !IsFiniteSquadPoint(safeRendezvous)
				|| !Level.checkSafeIncludingClipVolumes(safeRendezvous)
				|| !tactics.TryStartRadioReinforcement(safeRendezvous, Time.time))
				return false;
			PublishDecisionIfChanged();
			return true;
		}

		public void ReportDestinationReached()
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			if (tactics.CurrentGoal == MobileNPCTactics.Goal.SeekCover)
			{
				MobileNPCAdaptiveMemory memory = GetComponent<MobileNPCAdaptiveMemory>();
				if (memory != null && tactics.HasTacticalDestination)
					memory.RecordOwnOutcomeServer(MobileNPCAdaptiveMemory.ExperienceKind.CoverAccess,
						tactics.TacticalDestination, true);
			}
			if (tactics.CurrentGoal == MobileNPCTactics.Goal.Flank)
			{
				// Keep the NPC at its alternate firing position for the bounded flank
				// window instead of immediately resuming direct pursuit and crowding the player.
				return;
			}
			if (tactics.CurrentGoal != MobileNPCTactics.Goal.None)
			{
				bool finishedStalk = tactics.CurrentGoal == MobileNPCTactics.Goal.Stalk;
				tactics.ReportGoalReached();
				if (finishedStalk)
				{
					// Ambush opportunity expires without visual confirmation.
					// Never fire at the remembered position itself.
					ambushArmedUntil = Time.time + 5f;
					ambushTarget = null;
					ambushMethod = AmbushMethod.None;
					ambushMethodExpiresAt = 0f;
					listenUntil = Time.time + 1.5f;
					listeningClue = brain.Destination;
				}
			}
			else
			{
				brain.ReportDestinationReached();
				if (investigatingPlayerClue)
					TryUsePlayerKnowledgeHint();
			}

			PublishDecisionIfChanged();
		}

		public void TickServer(bool targetIsVisible)
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			if (ambushTarget != null && (!targetIsVisible
				|| (brain.Target != null && brain.Target != ambushTarget)
				|| Time.time >= ambushMethodExpiresAt))
			{
				CancelAmbush();
			}
			else if (ambushTarget != null && Time.time >= ambushAttackUnlockAt)
				ambushAttackUnlockAt = 0f;
			MobileNPCBrain.State previousState = brain.CurrentState;
			Transform previousTarget = brain.Target;
			brain.Tick(Time.time, targetIsVisible);
			// When the ordinary brain loses the remembered target, its second
			// advisory layer can propose a nearby search, not reveal live position.
			if (previousState == MobileNPCBrain.State.Investigating
				&& brain.CurrentState != MobileNPCBrain.State.Investigating)
				MobileNPCEncounterDirector.ReleaseOptionalEvidenceSearchServer(this);
			if (previousState == MobileNPCBrain.State.Pursuing
				&& brain.CurrentState == MobileNPCBrain.State.Investigating)
			{
				investigatingPlayerClue = previousTarget != null
					&& previousTarget.GetComponentInParent<Player>() != null;
				if (investigatingPlayerClue)
					TryUsePlayerKnowledgeHint();
			}
			else if (brain.CurrentState != MobileNPCBrain.State.Investigating)
				investigatingPlayerClue = false;

			if (tactics.IsGoalExpired(Time.time))
			{
				// Failure here means the validated cover destination was not reached
				// before its deadline, NOT that cover would have stopped incoming fire.
				if (tactics.CurrentGoal == MobileNPCTactics.Goal.SeekCover && tactics.HasTacticalDestination)
				{
					MobileNPCAdaptiveMemory memory = GetComponent<MobileNPCAdaptiveMemory>();
					if (memory != null)
						memory.RecordOwnOutcomeServer(MobileNPCAdaptiveMemory.ExperienceKind.CoverAccess,
							tactics.TacticalDestination, false);
				}
				tactics.ClearGoal();
			}
			// An active clue is evaluated at a bounded rate and only when no more
			// urgent tactical action or confirmed pursuit is underway.
			if (investigatingPlayerClue && brain.CurrentState == MobileNPCBrain.State.Investigating
				&& tactics.CurrentGoal == MobileNPCTactics.Goal.None
				&& !IsListening && Time.time >= nextStalkPlanAt)
			{
				nextStalkPlanAt = Time.time + 5f;
				TryPlanConcealedApproach();
			}
			PublishDecisionIfChanged();
		}

		/// <summary>Updates a companion follow destination without overriding an active combat target.</summary>
		public void SetFollowDestinationServer(Vector3 destination)
		{
			if (!CanAcceptServerInput || brain == null
				|| float.IsNaN(destination.x) || float.IsInfinity(destination.x)
				|| float.IsNaN(destination.y) || float.IsInfinity(destination.y)
				|| float.IsNaN(destination.z) || float.IsInfinity(destination.z)
				|| brain.CurrentState == MobileNPCBrain.State.Pursuing && brain.Target != null)
				return;

			brain.SetFollowDestination(destination);
			PublishDecisionIfChanged();
		}

		/// <summary>Updates the return-home anchor without resetting current combat state.</summary>
		public void SetHomePositionServer(Vector3 homePosition)
		{
			if (!CanAcceptServerInput
				|| float.IsNaN(homePosition.x) || float.IsInfinity(homePosition.x)
				|| float.IsNaN(homePosition.y) || float.IsInfinity(homePosition.y)
				|| float.IsNaN(homePosition.z) || float.IsInfinity(homePosition.z))
			{
				return;
			}

			brain.SetSpawnPosition(homePosition);
		}

		public void ResetServer()
		{
			if (!CanAcceptServerInput)
			{
				return;
			}

			MobileNPCEncounterDirector.ReleaseOptionalEvidenceSearchServer(this);
			brain.Reset();
			tactics.ClearGoal();
			playerKnowledge.Clear();
			investigatingPlayerClue = false;
			listenUntil = 0f;
			CancelAmbush();
			PublishDecisionIfChanged();
		}

		private void CancelAmbush()
		{
			ambushArmedUntil = 0f;
			ambushAttackUnlockAt = 0f;
			ambushTarget = null;
			ambushMethod = AmbushMethod.None;
			ambushMethodExpiresAt = 0f;
		}

		/// <summary>
		/// Find a sheltered point that can be reached by an unobstructed short walk.
		/// The point is chosen relative to an *aged evidence estimate*, never a
		/// hidden player's Transform. A blocked route is rejected instead of guessed.
		/// </summary>
		private void TryPlanConcealedApproach()
		{
			if (!CanAcceptServerInput || !investigatingPlayerClue)
				return;
			MobileNPCHumanoidDisposition disposition = GetComponent<MobileNPCHumanoidDisposition>();
			if (disposition == null
				|| disposition.CurrentRole != MobileNPCHumanoidDisposition.Role.Hostile
				|| !playerKnowledge.TryGetEvidence(Time.time, out Vector3 clue,
					out float confidence, out float uncertainty, out _)
				|| confidence < 0.34f || uncertainty > 12f
				// Director arbitrates only BONUS speculative stalking. Current
				// direct visual combat, heard shot/footsteps and legitimate
				// retreat/cover remain entirely unchanged.
				|| !MobileNPCEncounterDirector.AllowsOptionalEvidenceSearchServer(this))
				return;

			Vector3 toClue = clue - transform.position;
			toClue.y = 0f;
			if (toClue.sqrMagnitude < 16f || toClue.sqrMagnitude > 900f)
				return;

			float bestScore = float.NegativeInfinity;
			Vector3 selected = Vector3.zero;
			Vector3 direction = toClue.normalized;
			for (int i = -2; i <= 2; ++i)
			{
				Vector3 lateral = Quaternion.AngleAxis(i * 27f, Vector3.up) * direction;
				Vector3 rawPoint = transform.position + lateral * 5f;
				if (!MobileNPCSpawnService.TryFindSafeGroundPosition(rawPoint, 5f, 5f,
					RayMasks.BLOCK_COLLISION, 40f, out Vector3 ground, out _))
					continue;

				Vector3 approach = ground - transform.position;
				approach.y = 0f;
				float length = approach.magnitude;
				if (length < 2.5f || length > 7f)
					continue;

				Vector3 chestOrigin = transform.position + Vector3.up * 1f;
				RaycastHit[] pathHits = Physics.SphereCastAll(chestOrigin, 0.28f,
					approach.normalized, length, RayMasks.BLOCK_COLLISION,
					QueryTriggerInteraction.Ignore);
				bool blocked = false;
				for (int h = 0; h < pathHits.Length; ++h)
				{
					Collider obstacle = pathHits[h].collider;
					if (obstacle == null || obstacle.transform == transform
						|| obstacle.transform.IsChildOf(transform)
						|| obstacle.GetComponentInParent<Player>() != null
						|| obstacle.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
						continue;
					if (pathHits[h].distance < length - 0.6f)
					{
						blocked = true;
						break;
					}
				}
				if (blocked)
					continue;

				// Concealment: environment geometry interrupts the line from
				// last-known player area to this candidate. No actor qualifies as cover.
				Vector3 sightOrigin = clue + Vector3.up * 1.35f;
				Vector3 sightTarget = ground + Vector3.up * 1.1f;
				Vector3 sightOffset = sightTarget - sightOrigin;
				if (sightOffset.sqrMagnitude < 0.2f)
					continue;
				RaycastHit[] coverHits = Physics.RaycastAll(sightOrigin,
					sightOffset.normalized, sightOffset.magnitude,
					RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
				bool sheltered = false;
				for (int h = 0; h < coverHits.Length; ++h)
				{
					Collider obstacle = coverHits[h].collider;
					if (obstacle == null || obstacle.transform == transform
						|| obstacle.transform.IsChildOf(transform)
						|| obstacle.GetComponentInParent<Player>() != null
						|| obstacle.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
						continue;
					if (coverHits[h].distance < sightOffset.magnitude - 0.7f
						&& Vector3.Angle(coverHits[h].normal, Vector3.up) > 45f)
					{
						sheltered = true;
						break;
					}
				}
				if (!sheltered)
					continue;

				Vector3 candidateOffset = clue - ground;
				candidateOffset.y = 0f;
				float score = -candidateOffset.magnitude
					+ length * 0.2f + UnityEngine.Random.Range(-0.35f, 0.35f);
				if (score > bestScore)
				{
					bestScore = score;
					selected = ground;
				}
			}

			if (bestScore > float.NegativeInfinity
				&& tactics.TryStartStalk(selected, Time.time))
				Debug.Log("[MobileNPCBrainDriver] Cautious concealed approach chosen for "
					+ name + "; evidenceConfidence=" + confidence.ToString("F2") + ".", this);
		}

		/// <summary>
		/// The normal decision brain remains authoritative. Hypotheses are useful
		/// only while investigating; each hint must also pass safe-ground validation.
		/// </summary>
		private void TryUsePlayerKnowledgeHint()
		{
			if (brain == null || brain.CurrentState != MobileNPCBrain.State.Investigating)
				return;
			// Hypotheses do NOT gain perfect player coordinates. When too many
			// squadmates are already searching the same legitimate contact,
			// stick to the base brain's original sensory search instead.
			if (!MobileNPCEncounterDirector.AllowsOptionalEvidenceSearchServer(this))
				return;

			for (int attempt = 0; attempt < 2; ++attempt)
			{
				if (!playerKnowledge.TrySuggestSearchPoint(Time.time, out Vector3 hint))
					return;
				if (MobileNPCSpawnService.TryFindSafeGroundPosition(hint, 5f, 6f,
					RayMasks.BLOCK_COLLISION, 40f, out Vector3 ground, out _)
					&& brain.TrySetInvestigationHint(ground, Time.time))
					return;
			}
		}

		private void OnEnable()
		{
			if (hasInitialized)
			{
				MobileNPCRegistry.Register(this);
			}
		}

		private void OnDisable()
		{
			MobileNPCEncounterDirector.ReleaseOptionalEvidenceSearchServer(this);
			MobileNPCRegistry.Unregister(this);
		}

		private bool CanAcceptServerInput => Provider.isServer && hasInitialized && brain != null && tactics != null;

		private void PublishDecisionIfChanged()
		{
			if (brain == null || tactics == null)
			{
				return;
			}

			bool tacticalDestination = tactics.HasTacticalDestination;
			bool hasDestination = tacticalDestination || brain.HasDestination;
			Vector3 destination = tacticalDestination ? tactics.TacticalDestination : brain.Destination;
			MobileNPCBrain.State state = brain.CurrentState;
			MobileNPCTactics.Goal goal = tactics.CurrentGoal;

			bool changed = !hasPublishedDecision
				|| state != lastState
				|| goal != lastGoal
				|| hasDestination != lastHasDestination
				|| (hasDestination && (destination - lastDestination).sqrMagnitude > 0.04f);

			if (!changed)
			{
				return;
			}

			hasPublishedDecision = true;
			lastState = state;
			lastGoal = goal;
			lastHasDestination = hasDestination;
			lastDestination = destination;
			DecisionUpdated?.Invoke(state, goal, hasDestination, destination);
		}
	}
}
