////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt file for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Runtime-only group membership for mobile NPCs. A group exists while at least one
	/// living member remains; when its last member dies or is destroyed, the group is removed.
	/// This registry is not saved to disk and does not provide multiplayer replication.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	public sealed class MobileNPCGroupMember : MonoBehaviour
	{
		private static readonly Dictionary<int, GroupRecord> groups = new Dictionary<int, GroupRecord>();
		private static readonly Dictionary<int, int> restoredGroupIdMap = new Dictionary<int, int>();
		private static int nextGroupId = 1;
		private const float GroupJoinRadius = 140f;
		private const int MaximumGroupSize = 4;

		private sealed class GroupRecord
		{
			public int Id;
			public Vector3 Anchor;
			public readonly HashSet<MobileNPCGroupMember> Members = new HashSet<MobileNPCGroupMember>();
			public MobileNPCGroupMember Leader;
			public float LastLeaderObservedCombatAt;
			public float LastLeaderHeardContactAt;
			public bool HasSettlement;
			public int SettlementPiecesPlaced;
			public int SettlementPerimeterPiecesPlaced;
			public bool SettlementCentralStoragePlaced;
			// Logical enemy-site OCCUPATION after real local control.
			// These are NOT native ownership ids or a right to salvage structures.
			public uint CapturedStorageInstanceId;
			public Vector3 CapturedStoragePosition;
			public int CapturedFromGroupId;
			public MobileNPCHumanoidDisposition.Role Role;
			public MobileNPCFaction.Id Faction;
			// Smart-terrain-inspired job claims belong to the living group's
			// current settlement, not to a static map registry. At most two
			// guards get distinct physically grounded watch sectors.
			public readonly MobileNPCGroupMember[] GuardPostOwners = new MobileNPCGroupMember[2];
			public readonly Vector3[] GuardPostPoints = new Vector3[2];
			public readonly bool[] GuardPostValidated = new bool[2];
			// Bounded online A-Life work leases, not a second map/faction registry.
			// A lease is created only for a location this member independently
			// validated through native structures or its own learned supply site.
			public readonly MobileNPCGroupMember[] WorkSectorOwners = new MobileNPCGroupMember[4];
			public readonly Vector3[] WorkSectorLocations = new Vector3[4];
			public readonly float[] WorkSectorLeaseExpires = new float[4];
			public readonly float[] WorkSectorRevisitAfter = new float[4];
		}

		private MobileNPCHumanoidVitals vitals;
		private MobileNPCHumanoidMotor motor;
		private MobileNPCHumanoidDisposition disposition;
		private bool initialized;
		private bool leftGroup;
		private float nextContactBroadcastAt;
		private float nextKnowledgeConversationAt;
		private float nextThreatBroadcastAt;
		private float nextAidAssessmentAt;
		private float nearbyAllyLostUntil;
		private float nextLeaderDirectiveAt;
		private float nextRegroupExecutionAt;
		private float directiveUntil;
		private SquadDirective currentDirective;
		public enum SquadDirective { Independent, Hold, Probe, Withdraw, Regroup }
		public SquadDirective CurrentSquadDirective => Provider.isServer && !leftGroup
			&& Time.time < directiveUntil ? currentDirective : SquadDirective.Independent;
		private MobileNPCGroupMember assignedCoverer;
		public enum SquadRole { None, Covering, Advancing }
		private SquadRole squadRole;
		private float squadRoleUntil;
		private float pendingAdvanceAt;
		private Vector3 pendingAdvanceClue;
		public SquadRole CurrentSquadRole => Provider.isServer && !leftGroup
			&& Time.time < squadRoleUntil ? squadRole : SquadRole.None;
		// Audible shouts may travel farther than ordinary speech; crouched
		// survivors speak softly to avoid revealing their position.
		private const float AlertShoutRange = 24f;
		private const float QuietVoiceRange = 7f;
		private const float WallHearingMultiplier = 0.42f;
		private const float ContactBroadcastCooldown = 4f;

		public bool RecentlyHeardSquadmateFallServer => Provider.isServer
			&& initialized && !leftGroup && Time.time < nearbyAllyLostUntil;

		public int GroupId { get; private set; }
		public Vector3 GroupAnchor { get; private set; }
		public bool CanPlanSettlement => initialized && !leftGroup && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord currentGroup) && currentGroup.Leader == this;
		public bool HasSettlement => initialized && !leftGroup && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord settlementGroup) && settlementGroup.HasSettlement;
		public int SettlementPiecesPlaced => initialized && !leftGroup && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord progressGroup) ? progressGroup.SettlementPiecesPlaced : 0;
		public int SettlementPerimeterPiecesPlaced => initialized && !leftGroup && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord perimeterGroup) ? perimeterGroup.SettlementPerimeterPiecesPlaced : 0;
		public bool SettlementCentralStoragePlaced => initialized && !leftGroup && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord storageGroup) && storageGroup.SettlementCentralStoragePlaced;
		public uint CapturedStorageInstanceId => Provider.isServer && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord claim) ? claim.CapturedStorageInstanceId : 0u;
		public Vector3 CapturedStoragePosition => Provider.isServer && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord claim) ? claim.CapturedStoragePosition : Vector3.zero;
		public int CapturedFromGroupId => Provider.isServer && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord claim) ? claim.CapturedFromGroupId : 0;

		private void Awake()
		{
			vitals = GetComponent<MobileNPCHumanoidVitals>();
			motor = GetComponent<MobileNPCHumanoidMotor>();
			disposition = GetComponent<MobileNPCHumanoidDisposition>();
		}

		private void Update()
		{
			// Local conversation is rate-limited independently of squad advance orders.
			// Group identity alone never copies knowledge to absent group members.
			TryShareExperienceByLocalSpeechServer();
			TryAidNearbyWoundedMemberServer();
			MaintainCoverAdvancePairServer();
			TryIssueLeaderDirectiveServer();
			if (!Provider.isServer || CurrentSquadRole != SquadRole.Advancing
				|| pendingAdvanceAt <= 0f || Time.time < pendingAdvanceAt)
				return;
			pendingAdvanceAt = 0f;
			if (vitals != null && vitals.IsDead)
				return;
			MobileNPCBrainDriver brain = GetComponent<MobileNPCBrainDriver>();
			if (assignedCoverer == null || !assignedCoverer.CanActuallyCoverAdvanceServer()
				|| vitals != null && vitals.HealthFraction < 0.45f
				|| brain == null || !brain.TryStartSquadAdvance(pendingAdvanceClue))
			{
				// A failed physical advance was never a successful squad
				// maneuver. Release the paired role immediately.
				if (assignedCoverer != null && assignedCoverer.assignedCoverer == this)
					assignedCoverer.ClearSquadRole();
				ClearSquadRole();
			}
		}

		// Revalidate the paired maneuver while it is underway, not only
		// at the delayed moment the advance begins. A coverer who reloads,
		// loses sight, gets wounded or withdraws cannot keep an ally in a
		// fictitious protected advance. Never cancel the other NPC's genuine
		// independent defense; just release the temporary squad assignment.
		private float nextPairValidationAt;
		private void MaintainCoverAdvancePairServer()
		{
			if (!Provider.isServer || Time.time < nextPairValidationAt
				|| squadRole == SquadRole.None && assignedCoverer == null)
				return;
			nextPairValidationAt = Time.time + 0.65f;
			MobileNPCGroupMember partner = assignedCoverer;
			bool expired = CurrentSquadRole == SquadRole.None;
			bool invalid = partner == null || partner.assignedCoverer != this
				|| partner.GroupId != GroupId || partner.leftGroup
				|| partner.vitals != null && partner.vitals.IsDead;
			if (!expired && !invalid && squadRole == SquadRole.Advancing)
				invalid = !partner.CanActuallyCoverAdvanceServer()
					|| vitals != null && vitals.HealthFraction < 0.45f;
			if (!expired && !invalid && squadRole == SquadRole.Covering)
				invalid = !CanActuallyCoverAdvanceServer()
					|| partner.CurrentSquadRole != SquadRole.Advancing;
			if (!expired && !invalid)
				return;
			if (partner != null && partner.assignedCoverer == this)
				partner.ClearSquadRole();
			ClearSquadRole();
		}

		/// <summary>
		/// Living leader's small-group coordination: surveys ONLY teammates
		/// actually within audible local speech. A faction ID never implies
		/// automatic long-range communication or shared targets.
		/// </summary>
		private void TryIssueLeaderDirectiveServer()
		{
			if (!Provider.isServer || !initialized || leftGroup || GroupId <= 0
				|| Time.time < nextLeaderDirectiveAt || vitals != null && vitals.IsDead
				|| !groups.TryGetValue(GroupId, out GroupRecord group)
				|| group.Leader != this || group.Members.Count < 2)
				return;
			nextLeaderDirectiveAt = Time.time + Random.Range(3.5f, 5.0f);
			MobileNPCBrainDriver brain = GetComponent<MobileNPCBrainDriver>();
			MobileNPCHumanoidPerception perception = GetComponent<MobileNPCHumanoidPerception>();
			if (brain == null || perception == null)
				return;
			Transform target = brain.Target;
			bool currentlySeen = target != null &&
				(perception.VisiblePlayer != null && target == perception.VisiblePlayer.transform
				|| perception.VisibleNPC != null && target == perception.VisibleNPC.transform
				|| perception.VisibleZombie != null && target == perception.VisibleZombie.transform);
			if (currentlySeen)
				group.LastLeaderObservedCombatAt = Time.time;
			bool heardContact = group.LastLeaderHeardContactAt > 0f
				&& Time.time - group.LastLeaderHeardContactAt <= 8f;
			bool recentlyEngaged = group.LastLeaderObservedCombatAt > 0f
				&& Time.time - group.LastLeaderObservedCombatAt < 26f;
			if (!currentlySeen && !recentlyEngaged && !heardContact)
				return;

			int nearby = 1;
			int wounded = vitals != null && vitals.HealthFraction < 0.40f ? 1 : 0;
			MobileNPCHumanoidRangedCombat ownWeapon = GetComponent<MobileNPCHumanoidRangedCombat>();
			int armed = ownWeapon != null && ownWeapon.SelectedGunAsset != null
				&& ownWeapon.RoundsRemaining > 2 && !ownWeapon.IsReloading ? 1 : 0;
			foreach (MobileNPCGroupMember ally in group.Members)
			{
				if (ally == null || ally == this || !CanExchangeOrdersServer(ally))
					continue;
				++nearby;
				if (ally.vitals != null && ally.vitals.HealthFraction < 0.40f)
					++wounded;
				MobileNPCHumanoidRangedCombat weapon = ally.GetComponent<MobileNPCHumanoidRangedCombat>();
				if (weapon != null && weapon.SelectedGunAsset != null
					&& weapon.RoundsRemaining > 2 && !weapon.IsReloading)
					++armed;
			}
			if (nearby < 2)
				return;
			SquadDirective desired;
			if (!currentlySeen)
				desired = heardContact ? SquadDirective.Hold : SquadDirective.Regroup;
			else if (wounded >= 2 || wounded >= 1 && armed == 0)
				desired = SquadDirective.Withdraw;
			else if (armed >= 2 && wounded == 0 && ownWeapon != null
				&& ownWeapon.SelectedGunAsset != null
				&& ownWeapon.RoundsRemaining > 2 && !ownWeapon.IsReloading
				&& vitals != null && vitals.HealthFraction >= 0.55f
				&& HasPhysicalLineToObservedThreatServer(target.position))
				// At least one KNOWN supporting shooter (the leader) must
				// be able to cover a probe. Two guns somewhere in the squad
				// do not imply a clear or safe supporting firing lane.
				desired = SquadDirective.Probe;
			else
				desired = SquadDirective.Hold;

			// Strategy is collective, execution is individual. A blanket
			// Probe order made every armed NPC abandon its firing lane to
			// charge/flank together. Nominate at most one physically present,
			// independently sighted, healthy shooter as the maneuver element;
			// all other audible members hold and provide ordinary return fire.
			MobileNPCGroupMember maneuverMember = null;
			float maneuverScore = float.NegativeInfinity;
			if (desired == SquadDirective.Probe)
			{
				foreach (MobileNPCGroupMember ally in group.Members)
				{
					if (ally == null || ally == this || !CanExchangeOrdersServer(ally)
						|| ally.CurrentSquadRole == SquadRole.Covering)
						continue;
					MobileNPCHumanoidRangedCombat gun = ally.GetComponent<MobileNPCHumanoidRangedCombat>();
					MobileNPCBrainDriver allyBrain = ally.GetComponent<MobileNPCBrainDriver>();
					MobileNPCHumanoidPerception allyEyes = ally.GetComponent<MobileNPCHumanoidPerception>();
					if (gun == null || gun.SelectedGunAsset == null || gun.IsReloading
						|| gun.RoundsRemaining <= 3 || ally.vitals == null
						|| ally.vitals.HealthFraction < 0.65f || allyBrain == null
						|| allyBrain.CurrentState != MobileNPCBrain.State.Pursuing
						|| allyBrain.CurrentGoal != MobileNPCTactics.Goal.None
						|| allyBrain.Target == null || allyEyes == null)
						continue;
					Transform enemy = allyBrain.Target;
					bool ownSight = allyEyes.VisiblePlayer != null
						&& enemy == allyEyes.VisiblePlayer.transform
						|| allyEyes.VisibleNPC != null
						&& enemy == allyEyes.VisibleNPC.transform
						|| allyEyes.VisibleZombie != null
						&& enemy == allyEyes.VisibleZombie.transform;
					if (!ownSight)
						continue;
					float score = ally.CurrentSquadRole == SquadRole.Advancing ? 100f : 0f;
					score += ally.vitals.HealthFraction * 10f
						+ Mathf.Min(15, gun.RoundsRemaining) * 0.25f;
					if (score > maneuverScore)
					{
						maneuverScore = score;
						maneuverMember = ally;
					}
				}
			}
			ApplyLocalDirectiveServer(desired == SquadDirective.Probe
				? SquadDirective.Hold : desired, this);
			foreach (MobileNPCGroupMember ally in group.Members)
			{
				if (ally != null && ally != this && CanExchangeOrdersServer(ally))
					ApplyLocalDirectiveServer(desired == SquadDirective.Probe
						? ally == maneuverMember ? SquadDirective.Probe : SquadDirective.Hold
						: desired, ally);
			}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.Log("[MobileNPCGroupMember] Group " + GroupId + " leader "
				+ name + " issued local " + desired + " to " + nearby
				+ " audible member(s): visibly fighting=" + currentlySeen
				+ ", injured=" + wounded + ", armed=" + armed
				+ ". Individual vision and route validation still apply.", this);
#endif
		}

		private void ApplyLocalDirectiveServer(SquadDirective requested, MobileNPCGroupMember member)
		{
			if (member == null || member.leftGroup
				|| member.vitals != null && member.vitals.IsDead)
				return;
			member.currentDirective = requested;
			member.directiveUntil = Time.time + 6.5f;
			if (requested == SquadDirective.Withdraw)
				member.ClearSquadRole();
			if (requested != SquadDirective.Regroup || member == this
				|| Time.time < member.nextRegroupExecutionAt)
				return;
			MobileNPCBrainDriver brain = member.GetComponent<MobileNPCBrainDriver>();
			if (brain == null || brain.Target != null
				|| brain.CurrentState != MobileNPCBrain.State.Idle
				|| brain.CurrentGoal != MobileNPCTactics.Goal.None)
				return;
			member.nextRegroupExecutionAt = Time.time + 12f;
			// A few metres apart is enough for regroup; actual destination
			// may be rejected due to terrain, door or other collision.
			if ((member.transform.position - transform.position).sqrMagnitude > 5f * 5f)
				brain.TryStartSquadRegroupServer(transform.position);
		}

		private bool CanExchangeOrdersServer(MobileNPCGroupMember ally)
		{
			if (ally == null || ally == this || ally.leftGroup || !ally.initialized
				|| ally.GroupId != GroupId || ally.vitals != null && ally.vitals.IsDead)
				return false;
			float range = motor != null && motor.IsCrouched ? QuietVoiceRange : 14f;
			Vector3 start = transform.position + Vector3.up * 1.45f;
			Vector3 end = ally.transform.position + Vector3.up * 1.45f;
			Vector3 delta = end - start;
			float distance = delta.magnitude;
			if (distance <= 0.1f || distance > range)
				return false;
			RaycastHit[] hits = Physics.RaycastAll(start, delta / distance, distance,
				RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
			bool blocked = false;
			for (int i = 0; i < hits.Length; ++i)
			{
				Collider hit = hits[i].collider;
				if (hit == null || hit.transform.IsChildOf(transform)
					|| hit.transform.IsChildOf(ally.transform)
					|| hit.GetComponentInParent<Player>() != null
					|| hit.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
					continue;
				blocked = true;
				break;
			}
			return !blocked || distance <= range * WallHearingMultiplier;
		}

		/// <summary>Assigns this newly spawned individual to the nearest living group, or creates one.</summary>
		public void InitializeServer()
		{
			if (!Provider.isServer || initialized || motor == null || vitals != null && vitals.IsDead)
				return;

			initialized = true;
			MobileNPCHumanoidDisposition.Role role = disposition != null
				? disposition.CurrentRole : MobileNPCHumanoidDisposition.Role.Neutral;
			MobileNPCFaction.Id faction = GetCurrentFaction(role);
			GroupRecord group = FindNearestGroup(transform.position, role, faction);
			bool createdGroup = group == null;
			if (group == null)
			{
				group = new GroupRecord
				{
					Id = nextGroupId++,
					Anchor = motor.HomePosition,
					Role = role,
					Faction = faction
				};
				groups.Add(group.Id, group);
			}

			GroupId = group.Id;
			GroupAnchor = group.Anchor;
			group.Members.Add(this);
			if (group.Leader == null)
				group.Leader = this;
			if (motor != null && (group.Anchor - motor.HomePosition).sqrMagnitude > 0.25f)
				motor.TrySetHomePositionServer(group.Anchor);

			Debug.Log("[MobileNPCGroupMember] NPC " + name + (createdGroup ? " created group " : " joined group ") + GroupId
				+ " (" + group.Members.Count + "/" + MaximumGroupSize + ", role " + role + ").");

			if (vitals != null)
				vitals.Died += OnDied;
		}

		/// <summary>
		/// Reassigns membership after a server-side role or faction change so the NPC
		/// cannot remain in a group whose recorded identity no longer matches its own.
		/// </summary>
		public void RefreshGroupMembershipServer()
		{
			if (!Provider.isServer || !initialized || leftGroup)
				return;

			if (vitals != null)
				vitals.Died -= OnDied;

			RemoveFromCurrentGroupServer(false);
			initialized = false;
			InitializeServer();
		}

		/// <summary>
		/// A living member verbally reports a freshly seen hostile player to
		/// nearby living members of its *own* group. Reports are intentionally
		/// approximate, short-lived hints: recipients never inherit a visible
		/// target, weapon firing authorization or the player's live Transform.
		/// </summary>
		public void BroadcastPlayerContactServer(Vector3 observedPosition)
		{
			if (!Provider.isServer || !initialized || leftGroup || GroupId <= 0
				|| Time.time < nextContactBroadcastAt
				|| vitals != null && vitals.IsDead
				|| !groups.TryGetValue(GroupId, out GroupRecord group)
				|| group.Faction != GetCurrentFaction(disposition != null
					? disposition.CurrentRole : MobileNPCHumanoidDisposition.Role.Neutral)
				|| !IsFinitePosition(observedPosition))
				return;

			MobileNPCHumanoidMotor voiceMotor = GetComponent<MobileNPCHumanoidMotor>();
			bool whispering = voiceMotor != null && voiceMotor.IsCrouched;
			float speakingRange = whispering ? QuietVoiceRange : AlertShoutRange;
			nextContactBroadcastAt = Time.time + ContactBroadcastCooldown;
			MobileNPCHumanoidRangedCombat ownWeapon = GetComponent<MobileNPCHumanoidRangedCombat>();
			bool canCover = CurrentSquadRole == SquadRole.None
				&& ownWeapon != null && ownWeapon.SelectedGunAsset != null
				&& ownWeapon.RoundsRemaining > 0 && !ownWeapon.IsReloading
				&& vitals != null && vitals.HealthFraction >= 0.55f
				&& HasPhysicalLineToObservedThreatServer(observedPosition);
			MobileNPCGroupMember advancingAlly = null;
			Vector3 advancingClue = Vector3.zero;
			float nearestAdvanceDistance = float.PositiveInfinity;
			foreach (MobileNPCGroupMember ally in group.Members)
			{
				if (ally == null || ally == this || !ally.initialized || ally.leftGroup
					|| ally.vitals != null && ally.vitals.IsDead)
					continue;
				Vector3 toAlly = ally.transform.position - transform.position;
				float hearingDistance = toAlly.magnitude;
				if (hearingDistance > speakingRange || hearingDistance < 0.1f)
					continue;
				// Geometry attenuates a spoken contact report. Ignore actors' own
				// hitboxes; otherwise a nearby NPC could block every utterance.
				Vector3 mouth = transform.position + Vector3.up * 1.45f;
				Vector3 ear = ally.transform.position + Vector3.up * 1.4f;
				Vector3 ray = ear - mouth;
				RaycastHit[] barriers = Physics.RaycastAll(mouth, ray.normalized,
					ray.magnitude, RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
				bool behindWall = false;
				for (int h = 0; h < barriers.Length; ++h)
				{
					Collider blocker = barriers[h].collider;
					if (blocker == null || blocker.transform.IsChildOf(transform)
						|| blocker.transform.IsChildOf(ally.transform)
						|| blocker.GetComponentInParent<Player>() != null
						|| blocker.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
						continue;
					behindWall = true;
					break;
				}
				float effectiveRange = speakingRange * (behindWall ? WallHearingMultiplier : 1f);
				if (hearingDistance > effectiveRange)
					continue;

				MobileNPCBrainDriver allyBrain = ally.GetComponent<MobileNPCBrainDriver>();
				if (allyBrain == null)
					continue;

				// Verbal bearing is inherently uncertain. Individual listeners
				// receive different estimates, never perfect synchronized tracking.
				float clarity = Mathf.Clamp01(1f - hearingDistance / (effectiveRange + 0.01f));
				float confidence = Mathf.Clamp(0.32f + 0.5f * clarity, 0.24f, 0.78f);
				float errorRadius = Mathf.Lerp(6f, 1.4f, confidence);
				Vector2 error = Random.insideUnitCircle * errorRadius;
				Vector3 clue = observedPosition + new Vector3(error.x, 0f, error.y);
				allyBrain.ReportGroupPlayerContactClue(clue, confidence);
				if (ally == group.Leader)
					group.LastLeaderHeardContactAt = Time.time;

				// One physically fire-capable observer can cover one cautious
				// advance. This NEVER generates suppressive bullets: the native
				// firearm must still see its own target, aim and debit ammunition.
				MobileNPCHumanoidVitals allyVitals = ally.vitals;
				if (!canCover || ally.CurrentSquadRole != SquadRole.None
					|| allyVitals != null && allyVitals.HealthFraction < 0.45f
					|| allyBrain.CurrentState != MobileNPCBrain.State.Investigating
					|| toAlly.sqrMagnitude >= nearestAdvanceDistance)
					continue;
				nearestAdvanceDistance = toAlly.sqrMagnitude;
				advancingAlly = ally;
				advancingClue = clue;
			}
			if (canCover && advancingAlly != null)
			{
				squadRole = SquadRole.Covering;
				squadRoleUntil = Time.time + 7f;
				advancingAlly.squadRole = SquadRole.Advancing;
				advancingAlly.squadRoleUntil = Time.time + 7f;
				advancingAlly.pendingAdvanceClue = advancingClue;
				assignedCoverer = advancingAlly;
				advancingAlly.assignedCoverer = this;
				// Wait for the listener to finish checking the sound before
				// requesting a short covered movement.
				advancingAlly.pendingAdvanceAt = Time.time + 1.25f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				Debug.Log("[MobileNPCGroupMember] Group " + GroupId
					+ " assigned one COVERING and one ADVANCING member after audible firsthand player contact. No wallhack or suppression-fire shortcut.", this);
#endif
			}
		}


		/// <summary>
		/// Covering is a genuine fire-capable assignment, not merely being
		/// nearby with a gun. The caller personally saw the player and the
		/// straight firing corridor must not cross world geometry or an ally.
		/// </summary>
		private bool HasPhysicalLineToObservedThreatServer(Vector3 observedPosition)
		{
			Vector3 muzzle = transform.position + Vector3.up * 1.45f;
			Vector3 point = observedPosition + Vector3.up * 1.0f;
			Vector3 delta = point - muzzle;
			float distance = delta.magnitude;
			if (distance < 2.5f || distance > 35f
				|| !IsSquadFireLaneSafeServer(muzzle, delta / distance, distance))
				return false;
			RaycastHit[] hits = Physics.RaycastAll(muzzle, delta / distance,
				distance, RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < hits.Length; ++i)
			{
				Collider collider = hits[i].collider;
				if (collider == null || collider.transform.IsChildOf(transform))
					continue;
				if (collider.GetComponentInParent<Player>() != null)
					continue;
				if (collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
					return false;
				return false;
			}
			return true;
		}

		/// <summary>
		/// Cheap per-shot friendly-fire guard. Members still fire with the
		/// original imperfect ballistics and real ammo, but should not shoot
		/// directly through a nearby living squadmate's head/body. This
		/// intentionally does not grant target knowledge or aim correction.
		/// </summary>
		public bool IsSquadFireLaneSafeServer(Vector3 muzzle, Vector3 direction,
			float distance)
		{
			if (!Provider.isServer || !initialized || leftGroup || GroupId <= 0
				|| !groups.TryGetValue(GroupId, out GroupRecord record))
				return true;
			if (distance <= 0.1f || direction.sqrMagnitude < 0.5f)
				return false;
			foreach (MobileNPCGroupMember ally in record.Members)
			{
				if (ally == null || ally == this || ally.leftGroup
					|| ally.vitals != null && ally.vitals.IsDead)
					continue;
				// Two torso/head samples approximate the known humanoid
				// envelope, including allies about to cross the fire lane.
				for (int sample = 0; sample < 2; ++sample)
				{
					Vector3 body = ally.transform.position
						+ Vector3.up * (sample == 0 ? 0.95f : 1.55f);
					float along = Vector3.Dot(body - muzzle, direction);
					if (along < 0.25f || along > distance)
						continue;
					Vector3 closest = muzzle + direction * along;
					if ((body - closest).sqrMagnitude < 0.82f * 0.82f)
						return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Emergency stock redistribution AFTER combat: a nearby wounded
		/// survivor may receive a truly owned rag/bandage/splint from a
		/// healthy teammate. Uses normal inventory ownership transactions;
		/// treatment remains the existing MedicalBehavior's decision.
		/// </summary>
		private void TryAidNearbyWoundedMemberServer()
		{
			if (!Provider.isServer || !Level.isLoaded || !initialized || leftGroup
				|| GroupId <= 0 || vitals == null || vitals.IsDead
				|| vitals.HealthFraction < 0.65f || Time.time < nextAidAssessmentAt
				|| !groups.TryGetValue(GroupId, out GroupRecord record))
				return;
			nextAidAssessmentAt = Time.time + Random.Range(4.0f, 6.5f);
			MobileNPCBrainDriver donorBrain = GetComponent<MobileNPCBrainDriver>();
			if (donorBrain == null || donorBrain.Target != null
				|| donorBrain.CurrentGoal != MobileNPCTactics.Goal.None)
				return;
			MobileNPCInjurySystem donorInjuries = GetComponent<MobileNPCInjurySystem>();
			if (donorInjuries != null && (donorInjuries.CurrentBleeding
					!= MobileNPCInjurySystem.BleedingSeverity.None
					|| donorInjuries.HasFracturedLeg))
				return;
			MobileNPCInventory ownInventory = GetComponent<MobileNPCInventory>();
			if (ownInventory == null)
				return;
			foreach (MobileNPCGroupMember ally in record.Members)
			{
				if (ally == null || ally == this || ally.leftGroup || !ally.initialized
					|| ally.vitals == null || ally.vitals.IsDead)
					continue;
				Vector3 separation = ally.transform.position - transform.position;
				if (separation.sqrMagnitude > 2.4f * 2.4f
					|| Mathf.Abs(separation.y) > 1.0f
					|| !HasClearHandOffLineServer(ally))
					continue;
				MobileNPCBrainDriver patientBrain = ally.GetComponent<MobileNPCBrainDriver>();
				if (patientBrain == null || patientBrain.Target != null)
					continue;
				MobileNPCMedicalBehavior patientMedical = ally.GetComponent<MobileNPCMedicalBehavior>();
				MobileNPCInjurySystem patientInjuries = ally.GetComponent<MobileNPCInjurySystem>();
				MobileNPCInventory patientInventory = ally.GetComponent<MobileNPCInventory>();
				if (patientMedical == null || patientInjuries == null
					|| patientInventory == null || !patientInventory.HasSpace
					|| !patientMedical.NeedsMedicalSupplies)
					continue;
				bool bandage = patientInjuries.CurrentBleeding
					!= MobileNPCInjurySystem.BleedingSeverity.None
					&& !patientInventory.ContainsItemID(393)
					&& !patientInventory.ContainsItemID(95)
					&& !patientInventory.ContainsItemID(394);
				bool splint = patientInjuries.HasFracturedLeg
					&& !patientInventory.ContainsItemID(96);
				if (!bandage && !splint)
					continue;
				for (int slot = ownInventory.UsedSlots - 1; slot >= 0; --slot)
				{
					if (!ownInventory.TryGetItemSnapshot(slot, out Item offered)
						|| offered == null
						|| !(bandage && (offered.id == 393 || offered.id == 95
							|| offered.id == 394) || splint && offered.id == 96))
						continue;
					// Same reservation/rollback discipline as native inventory
					// transactions. A failed handoff must not consume medicine.
					if (!patientInventory.TryReserveSlotServer())
						return;
					if (!ownInventory.TryTakeItemServer(slot, out Item transferred))
					{
						patientInventory.ReleaseReservedSlotServer();
						continue;
					}
					if (!patientInventory.TryStoreReservedItemServer(transferred))
					{
						patientInventory.ReleaseReservedSlotServer();
						if (!ownInventory.TryStoreTransferredItem(transferred))
							Debug.LogError("[MobileNPCGroupMember] Medical item transfer rollback"
								+ " failed; preserving error for investigation.", this);
						return;
					}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					Debug.Log("[MobileNPCGroupMember] Group " + GroupId
						+ " transferred one owned medical item " + transferred.id
						+ " to physically nearby injured survivor. No health granted.", this);
#endif
					return;
				}
			}
		}

		/// <summary>
		/// An actual covering assignment must remain able to fire on the
		/// personally perceived player before its partner starts a flank.
		/// Losing sight or reloading releases the proposed coordinated role.
		/// </summary>
		private bool CanActuallyCoverAdvanceServer()
		{
			if (CurrentSquadRole != SquadRole.Covering || vitals == null
				|| vitals.IsDead || vitals.HealthFraction < 0.55f)
				return false;
			MobileNPCBrainDriver ownBrain = GetComponent<MobileNPCBrainDriver>();
			MobileNPCHumanoidPerception eyes = GetComponent<MobileNPCHumanoidPerception>();
			MobileNPCHumanoidRangedCombat weapon =
				GetComponent<MobileNPCHumanoidRangedCombat>();
			return ownBrain != null && ownBrain.Target != null
				&& ownBrain.CurrentGoal != MobileNPCTactics.Goal.Retreat
				&& eyes != null && eyes.VisiblePlayer != null
				&& weapon != null && weapon.SelectedGunAsset != null
				&& weapon.RoundsRemaining > 0 && !weapon.IsReloading
				&& HasPhysicalLineToObservedThreatServer(eyes.VisiblePlayer.transform.position);
		}

		private bool HasClearHandOffLineServer(MobileNPCGroupMember recipient)
		{
			if (recipient == null)
				return false;
			Vector3 from = transform.position + Vector3.up * 1.15f;
			Vector3 to = recipient.transform.position + Vector3.up * 1.15f;
			Vector3 delta = to - from;
			float length = delta.magnitude;
			if (length < 0.1f || length > 2.8f)
				return false;
			RaycastHit[] blockers = Physics.RaycastAll(from, delta / length, length,
				RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < blockers.Length; ++i)
			{
				Collider hit = blockers[i].collider;
				if (hit == null || hit.transform.IsChildOf(transform)
					|| hit.transform.IsChildOf(recipient.transform))
					continue;
				if (hit.GetComponentInParent<Player>() != null
					|| hit.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
					continue;
				return false;
			}
			return true;
		}

		/// <summary>
		/// Firsthand witnessed zombie/hostile-NPC danger may be SHOUTED to
		/// close squadmates. Recipients only receive a noisy position to
		/// investigate; they must personally see the actor before engaging.
		/// Never broadcasts its Transform/target, through walls at full
		/// volume or across the whole faction.
		/// </summary>
		public void BroadcastLocalThreatServer(Vector3 observedPosition)
		{
			if (!Provider.isServer || !initialized || leftGroup || GroupId <= 0
				|| Time.time < nextThreatBroadcastAt || !IsFinitePosition(observedPosition)
				|| vitals != null && vitals.IsDead
				|| !groups.TryGetValue(GroupId, out GroupRecord group))
				return;
			nextThreatBroadcastAt = Time.time + 5f;
			float range = motor != null && motor.IsCrouched ? QuietVoiceRange : AlertShoutRange;
			int notified = 0;
			foreach (MobileNPCGroupMember ally in group.Members)
			{
				if (ally == null || ally == this || !ally.initialized || ally.leftGroup
					|| ally.vitals != null && ally.vitals.IsDead)
					continue;
				Vector3 voice = transform.position + Vector3.up * 1.45f;
				Vector3 ear = ally.transform.position + Vector3.up * 1.40f;
				Vector3 delta = ear - voice;
				float distance = delta.magnitude;
				if (distance <= 0.1f || distance > range)
					continue;
				RaycastHit[] hits = Physics.RaycastAll(voice, delta / distance, distance,
					RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
				bool blocked = false;
				for (int i = 0; i < hits.Length; ++i)
				{
					Collider obstacle = hits[i].collider;
					if (obstacle == null || obstacle.transform.IsChildOf(transform)
						|| obstacle.transform.IsChildOf(ally.transform)
						|| obstacle.GetComponentInParent<Player>() != null
						|| obstacle.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
						continue;
					blocked = true;
					break;
				}
				if (blocked && distance > range * WallHearingMultiplier)
					continue;
				MobileNPCBrainDriver listener = ally.GetComponent<MobileNPCBrainDriver>();
				if (listener == null || listener.Target != null
					|| listener.CurrentGoal != MobileNPCTactics.Goal.None)
					continue;
				// Do not pass a possibly moving zombie/NPC reference.
				Vector2 uncertainty = Random.insideUnitCircle
					* Mathf.Lerp(1.5f, 5.5f, Mathf.Clamp01(distance / range));
				listener.ReportNoise(observedPosition
					+ new Vector3(uncertainty.x, 0f, uncertainty.y), 12f);
				if (ally == group.Leader)
					group.LastLeaderHeardContactAt = Time.time;
				++notified;
			}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (notified > 0)
				Debug.Log("[MobileNPCGroupMember] Group " + GroupId
					+ " communicated verified local non-player threat to "
					+ notified + " listener(s). No exact target shared.", this);
#endif
		}

		/// <summary>
		/// A relaxed living NPC may tell one firsthand lesson to group mates who can
		/// actually hear it. No voice conversation => no shared knowledge. This is
		/// NOT radio traffic, remote squad telepathy, or a player-visible subtitle.
		/// </summary>
		private void TryShareExperienceByLocalSpeechServer()
		{
			if (!Provider.isServer || !initialized || leftGroup || GroupId <= 0
				|| Time.time < nextKnowledgeConversationAt
				|| vitals != null && vitals.IsDead
				|| !groups.TryGetValue(GroupId, out GroupRecord group))
				return;
			// Avoid speaking while pursuing an active enemy. Keep pending knowledge if
			// no one can hear it rather than broadcasting into the group registry.
			MobileNPCBrainDriver ownBrain = GetComponent<MobileNPCBrainDriver>();
			if (ownBrain == null || ownBrain.Target != null)
				return;
			nextKnowledgeConversationAt = Time.time + Random.Range(15f, 22f);
			MobileNPCAdaptiveMemory ownMemory = GetComponent<MobileNPCAdaptiveMemory>();
			MobileNPCAdaptiveMemory.SpokenLesson report = default(MobileNPCAdaptiveMemory.SpokenLesson);
			bool hasCombatLesson = ownMemory != null && ownMemory.TryGetUnspokenLessonServer(out report);
			MobileNPCSupplyKnowledge ownSupply = GetComponent<MobileNPCSupplyKnowledge>();
			MobileNPCSupplyKnowledge.SpokenSiteLesson siteReport = default(MobileNPCSupplyKnowledge.SpokenSiteLesson);
			bool hasSupplyLesson = ownSupply != null && ownSupply.TryGetUnspokenSiteLessonServer(out siteReport);
			if (!hasCombatLesson && !hasSupplyLesson)
				return;

			float normalVoiceRange = motor != null && motor.IsCrouched ? 4f : 7f;
			int combatRecipients = 0;
			int supplyRecipients = 0;
			foreach (MobileNPCGroupMember ally in group.Members)
			{
				if (ally == null || ally == this || !ally.initialized || ally.leftGroup
					|| ally.GroupId != GroupId
					|| ally.vitals != null && ally.vitals.IsDead)
					continue;
				MobileNPCBrainDriver allyBrain = ally.GetComponent<MobileNPCBrainDriver>();
				if (allyBrain == null || allyBrain.Target != null)
					continue;

				Vector3 mouth = transform.position + Vector3.up * 1.45f;
				Vector3 ear = ally.transform.position + Vector3.up * 1.4f;
				Vector3 sound = ear - mouth;
				float distance = sound.magnitude;
				if (distance < 0.1f || distance > normalVoiceRange)
					continue;

				// Walls reduce intelligible speech; NPC/player collision shells do not.
				bool blocked = false;
				RaycastHit[] hits = Physics.RaycastAll(mouth, sound.normalized, distance,
					RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
				for (int h = 0; h < hits.Length; ++h)
				{
					Collider obstacle = hits[h].collider;
					if (obstacle == null || obstacle.transform.IsChildOf(transform)
						|| obstacle.transform.IsChildOf(ally.transform)
						|| obstacle.GetComponentInParent<Player>() != null
						|| obstacle.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
						continue;
					blocked = true;
					break;
				}
				if (blocked && distance > normalVoiceRange * 0.42f)
					continue;

				if (hasCombatLesson)
				{
					MobileNPCAdaptiveMemory listener = ally.GetComponent<MobileNPCAdaptiveMemory>();
					if (listener != null && listener.ReceiveSpokenLessonServer(report))
						combatRecipients++;
				}
				if (hasSupplyLesson)
				{
					MobileNPCSupplyKnowledge listener = ally.GetComponent<MobileNPCSupplyKnowledge>();
					if (listener != null && listener.ReceiveSpokenSiteLessonServer(siteReport))
						supplyRecipients++;
				}
			}
			if (combatRecipients > 0)
				ownMemory.ConfirmLessonSpokenServer(report.Sequence);
			if (supplyRecipients > 0)
				ownSupply.ConfirmSpokenSiteLessonServer(siteReport.Sequence);
			if (combatRecipients > 0 || supplyRecipients > 0)
			{
				Debug.Log("[MobileNPCGroupMember] Local experience conversation: speaker="
					+ name + ", group=" + GroupId + ", combatListeners=" + combatRecipients
					+ ", supplyListeners=" + supplyRecipients
					+ ". Real audible range; no radio/remote relay.", this);
			}
		}

		private static bool IsFinitePosition(Vector3 value)
		{
			return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
				&& !float.IsNaN(value.y) && !float.IsInfinity(value.y)
				&& !float.IsNaN(value.z) && !float.IsInfinity(value.z);
		}

		/// <summary>Require the SAME saved native storage instance, never a
		/// nearby empty house or a player's private item. Tiny occasional
		/// queries are okay on capture/reload, not on every NPC update.</summary>
		private static bool IsNativeUnownedStorageAtServer(Vector3 site, uint instanceID)
		{
			if (!Provider.isServer || !Level.isLoaded || instanceID == 0
				|| !IsFinitePosition(site))
				return false;
			List<Transform> nearby = new List<Transform>(16);
			BarricadeManager.getBarricadesInRadius(site, 2.2f * 2.2f, nearby);
			for (int i = 0; i < nearby.Count; ++i)
			{
				BarricadeDrop drop = BarricadeDrop.FindByTransformFastMaybeNull(nearby[i]);
				BarricadeData data = drop != null ? drop.GetServersideData() : null;
				if (drop == null || data == null || drop.instanceID != instanceID
					|| data.owner != 0UL || data.group != 0UL
					|| !(drop.asset is ItemBarricadeAsset asset)
					|| asset.build != EBuild.STORAGE
					|| drop.model == null || !drop.model.gameObject.activeInHierarchy)
					continue;
				return true;
			}
			return false;
		}

		public bool TryRecordCapturedSectorServer(Vector3 site, uint instanceID,
			int formerGroupId)
		{
			if (!Provider.isServer || !MobileNPCSettings.CombatMode
				|| !CanPlanSettlement || instanceID == 0 || formerGroupId <= 0
				|| formerGroupId == GroupId
				|| (site - transform.position).sqrMagnitude > 9f * 9f
				|| !IsNativeUnownedStorageAtServer(site, instanceID)
				|| !groups.TryGetValue(GroupId, out GroupRecord record))
				return false;
			foreach (GroupRecord other in groups.Values)
			{
				if (other.Id != GroupId && other.CapturedStorageInstanceId == instanceID)
				{
					// Last physical occupier wins; actual world storage remains
					// owner 0. Never claim unrelated vanilla player structures.
					other.CapturedStorageInstanceId = 0;
					other.CapturedFromGroupId = 0;
				}
			}
			record.CapturedStorageInstanceId = instanceID;
			record.CapturedStoragePosition = site;
			record.CapturedFromGroupId = formerGroupId;
			return true;
		}

		/// <summary>Old JSON saves default 0; invalid/destroyed physical
		/// storage never resurrects a territory. Survivors only.</summary>
		public void RestoreCapturedSectorServer(uint instanceID, Vector3 site, int formerGroupId)
		{
			if (!Provider.isServer || !initialized || leftGroup || GroupId <= 0
				|| formerGroupId <= 0 || instanceID == 0
				|| !IsNativeUnownedStorageAtServer(site, instanceID)
				|| !groups.TryGetValue(GroupId, out GroupRecord record))
				return;
			record.CapturedStorageInstanceId = instanceID;
			record.CapturedStoragePosition = site;
			record.CapturedFromGroupId = formerGroupId;
		}

		public bool TryGetCapturedSectorServer(out Vector3 site)
		{
			site = Vector3.zero;
			if (!Provider.isServer || GroupId <= 0
				|| !groups.TryGetValue(GroupId, out GroupRecord record)
				|| record.CapturedStorageInstanceId == 0)
				return false;
			if (!IsNativeUnownedStorageAtServer(record.CapturedStoragePosition,
				record.CapturedStorageInstanceId))
			{
				record.CapturedStorageInstanceId = 0;
				record.CapturedFromGroupId = 0;
				return false;
			}
			site = record.CapturedStoragePosition;
			return true;
		}

		/// <summary>Moves the living group's shared home anchor when its planner chooses a base site.</summary>
		public void SetGroupAnchorServer(Vector3 anchor)
		{
			if (!Provider.isServer || !initialized || leftGroup
				|| float.IsNaN(anchor.x) || float.IsInfinity(anchor.x)
				|| float.IsNaN(anchor.y) || float.IsInfinity(anchor.y)
				|| float.IsNaN(anchor.z) || float.IsInfinity(anchor.z)
				|| !groups.TryGetValue(GroupId, out GroupRecord group))
				return;

			group.Anchor = anchor;
			group.HasSettlement = true;
			// A new actual settlement must not inherit watch points from an
			// unrelated previous map position.
			for (int slot = 0; slot < group.GuardPostOwners.Length; ++slot)
			{
				group.GuardPostOwners[slot] = null;
				group.GuardPostValidated[slot] = false;
			}
			foreach (MobileNPCGroupMember member in group.Members)
			{
				if (member == null || member.vitals != null && member.vitals.IsDead)
					continue;
				member.GroupAnchor = anchor;
				if (member.motor != null)
					member.motor.TrySetHomePositionServer(anchor);
			}
		}


		/// <summary>
		/// Require the leader's actually registered, active native base storage,
		/// not merely the old HasSettlement flag (which is already true when
		/// the first construction site is only PLANNED). Prevent job selection
		/// around imaginary finished bases or the player's private storage.
		/// </summary>
		private static bool HasPhysicalSettlementPostAnchorServer(GroupRecord record)
		{
			if (record == null || !record.HasSettlement || record.Leader == null
				|| record.Leader.vitals != null && record.Leader.vitals.IsDead)
				return false;
			MobileNPCSettlementBuilder builder =
				record.Leader.GetComponent<MobileNPCSettlementBuilder>();
			return builder != null
				&& builder.TryGetCentralStorageForRadioServer(out _);
		}

		/// <summary>
		/// A group settlement offers at most two named, exclusive watch duties.
		/// Eligibility is checked by the existing A-Life routine utility. A
		/// reservation does NOT imply that the NPC reached the post.
		/// </summary>
		public bool HasAvailableGuardPostServer
		{
			get
			{
				if (!Provider.isServer || !HasSettlement
					|| !groups.TryGetValue(GroupId, out GroupRecord group)
					|| !HasPhysicalSettlementPostAnchorServer(group))
					return false;
				PruneDeadMembers(group);
				for (int slot = 0; slot < group.GuardPostOwners.Length; ++slot)
					if (group.GuardPostOwners[slot] == null
						|| group.GuardPostOwners[slot] == this)
						return true;
				return false;
			}
		}

		public bool TryReserveGuardPostServer(out Vector3 physicalPost)
		{
			physicalPost = Vector3.zero;
			if (!Provider.isServer || !HasSettlement || vitals != null && vitals.IsDead
				|| !groups.TryGetValue(GroupId, out GroupRecord group)
				|| !HasPhysicalSettlementPostAnchorServer(group)
				|| !group.Members.Contains(this))
				return false;
			PruneDeadMembers(group);

			// Prefer own slot after combat; never release a claim just because
			// the guard briefly pursued an enemy or went for cover.
			int chosen = -1;
			for (int slot = 0; slot < group.GuardPostOwners.Length; ++slot)
				if (group.GuardPostOwners[slot] == this) { chosen = slot; break; }
			if (chosen < 0)
			{
				for (int slot = 0; slot < group.GuardPostOwners.Length; ++slot)
					if (group.GuardPostOwners[slot] == null) { chosen = slot; break; }
			}
			if (chosen < 0)
				return false;

			if (!group.GuardPostValidated[chosen])
			{
				// Only ground NEAR an actually established group settlement is
				// eligible; no imagined guard towers, building placement or
				// magical entrances. Distinct opposite approaches avoid crowding.
				float bearing = (group.Id * 53f + chosen * 180f) * Mathf.Deg2Rad;
				Vector3 direction = new Vector3(Mathf.Cos(bearing), 0f,
					Mathf.Sin(bearing));
				bool found = false;
				for (int attempt = 0; attempt < 3; ++attempt)
				{
					float distance = 9f + attempt * 1.5f;
					Vector3 candidate = group.Anchor + direction * distance;
					if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
						32f, 32f, ~0, 28f, out Vector3 ground, out _)
						|| !Level.checkSafeIncludingClipVolumes(ground)
						|| Mathf.Abs(ground.y - group.Anchor.y) > 2.1f
						|| Physics.CheckSphere(ground + Vector3.up * 0.95f,
							0.45f, ~0, QueryTriggerInteraction.Ignore))
						continue;
					group.GuardPostPoints[chosen] = ground;
					group.GuardPostValidated[chosen] = true;
					found = true;
					break;
				}
				if (!found)
					return false;
			}
			group.GuardPostOwners[chosen] = this;
			physicalPost = group.GuardPostPoints[chosen];
			return true;
		}

		/// <summary>Release only THIS NPC's job claim. Other members keep theirs.</summary>
		public void ReleaseGuardPostServer()
		{
			if (!Provider.isServer || GroupId <= 0
				|| !groups.TryGetValue(GroupId, out GroupRecord group))
				return;
			for (int slot = 0; slot < group.GuardPostOwners.Length; ++slot)
				if (group.GuardPostOwners[slot] == this)
					group.GuardPostOwners[slot] = null;
		}

		/// <summary>
		/// Read-only candidate filter for the EXISTING demand-weighted personal
		/// supply-site scorer. This does not disclose teammates' unknown sites
		/// or claim a job. The actual claim is atomic in TryClaimActivitySector.
		/// </summary>
		public bool IsActivitySectorAvailableServer(Vector3 candidate)
		{
			if (!Provider.isServer || !Level.isLoaded || !initialized || leftGroup
				|| vitals != null && vitals.IsDead || !IsFinitePosition(candidate)
				|| !groups.TryGetValue(GroupId, out GroupRecord group)
				|| !group.Members.Contains(this))
				return false;
			PruneDeadMembers(group);
			bool hasVacancy = false;
			for (int i = 0; i < group.WorkSectorOwners.Length; ++i)
			{
				MobileNPCGroupMember owner = group.WorkSectorOwners[i];
				if (owner == this)
				{
					hasVacancy = true;
					continue;
				}
				if (owner == null)
					hasVacancy = true;
				if ((owner != null || Time.time < group.WorkSectorRevisitAfter[i])
					&& (candidate - group.WorkSectorLocations[i]).sqrMagnitude
						<= 22f * 22f)
					return false;
			}
			return hasVacancy;
		}

		/// <summary>
		/// Exclusive, expiring claim for an ALREADY known/physically verified
		/// sector. Both the existing structure-roaming motor and the existing
		/// remembered-supply search use this one shared group ledger.
		/// No site lookup, loot assignment or background simulation occurs here.
		/// </summary>
		public bool TryClaimActivitySectorServer(Vector3 candidate, float leaseSeconds)
		{
			if (!Provider.isServer || !Level.isLoaded || !initialized || leftGroup
				|| vitals != null && vitals.IsDead || !IsFinitePosition(candidate)
				|| !Level.checkSafeIncludingClipVolumes(candidate)
				|| (candidate - transform.position).sqrMagnitude > 260f * 260f
				|| !groups.TryGetValue(GroupId, out GroupRecord group)
				|| !group.Members.Contains(this))
				return false;

			PruneDeadMembers(group);
			int currentSlot = -1;
			int freeSlot = -1;
			int recyclableSlot = -1;
			float earliestRevisit = float.PositiveInfinity;
			MobileNPCAutonomyPlanner routine = GetComponent<MobileNPCAutonomyPlanner>();
			bool searchingRooms = routine != null
				&& routine.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge;
			// Scavengers should inspect separate rooms within the same house;
			// patrols still reserve whole broad sectors against squad crowding.
			float sectorRadius = searchingRooms ? 7f : 22f;
			float sameSectorSqr = sectorRadius * sectorRadius;
			for (int i = 0; i < group.WorkSectorOwners.Length; ++i)
			{
				MobileNPCGroupMember owner = group.WorkSectorOwners[i];
				if (owner == this)
				{
					currentSlot = i;
					continue;
				}
				if (owner == null)
				{
					if (Time.time >= group.WorkSectorRevisitAfter[i] && freeSlot < 0)
						freeSlot = i;
					// Recycling the oldest inactive record is allowed ONLY
					// when the new site is not within its cooldown radius.
					if (group.WorkSectorRevisitAfter[i] < earliestRevisit)
					{
						earliestRevisit = group.WorkSectorRevisitAfter[i];
						recyclableSlot = i;
					}
				}
				// Prevent squadmates from crowding active work and prevent
				// immediate repeat investigation of an actually visited site.
				if ((owner != null || Time.time < group.WorkSectorRevisitAfter[i])
					&& (candidate - group.WorkSectorLocations[i]).sqrMagnitude <= sameSectorSqr)
					return false;
			}
			// All four cooldown records may be full of DIFFERENT, already
			// checked areas. Allow scouting a genuinely new sector without
			// ever stealing a teammate's active work claim.
			int chosen = currentSlot >= 0 ? currentSlot
				: freeSlot >= 0 ? freeSlot : recyclableSlot;
			if (chosen < 0)
				return false;

			// A member may hold only one travel-sector task at a time.
			if (currentSlot >= 0 && currentSlot != chosen)
				group.WorkSectorOwners[currentSlot] = null;
			group.WorkSectorOwners[chosen] = this;
			group.WorkSectorLocations[chosen] = candidate;
			group.WorkSectorLeaseExpires[chosen] =
				Time.time + Mathf.Clamp(leaseSeconds, 10f, 95f);
			group.WorkSectorRevisitAfter[chosen] = 0f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.Log("[MobileNPCA-Life] Sector claimed npc=" + name
				+ " group=" + GroupId + " position=" + candidate, this);
#endif
			return true;
		}

		/// <summary>
		/// Retain a short group memory ONLY after reaching or physically
		/// failing a sector. Danger/interruptions simply relinquish ownership.
		/// </summary>
		public void ReleaseActivitySectorServer(bool visited, bool blocked)
		{
			if (!Provider.isServer || !groups.TryGetValue(GroupId, out GroupRecord group))
				return;
			for (int i = 0; i < group.WorkSectorOwners.Length; ++i)
			{
				if (group.WorkSectorOwners[i] != this)
					continue;
				group.WorkSectorOwners[i] = null;
				MobileNPCAutonomyPlanner routine = GetComponent<MobileNPCAutonomyPlanner>();
				bool inspectedRoom = routine != null
					&& routine.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge;
				group.WorkSectorRevisitAfter[i] = Time.time
					+ (visited ? inspectedRoom ? 28f : 70f
						: blocked ? 40f : 0f);
			}
		}

		private MobileNPCFaction.Id GetCurrentFaction(MobileNPCHumanoidDisposition.Role fallbackRole)
		{
			MobileNPCFaction faction = GetComponent<MobileNPCFaction>();
			if (faction != null)
				return faction.CurrentFaction;
			switch (fallbackRole)
			{
				case MobileNPCHumanoidDisposition.Role.Hostile: return MobileNPCFaction.Id.Raiders;
				case MobileNPCHumanoidDisposition.Role.Friendly: return MobileNPCFaction.Id.Traders;
				default: return MobileNPCFaction.Id.Scavengers;
			}
		}

		/// <summary>
		/// Resolve the CURRENT role/faction of a group only while at least one
		/// live individual remains. A replacement never revives a dissolved group
		/// or inherits any deceased individual's inventory, identity or memories.
		/// </summary>
		public static bool TryGetSurvivingGroupIdentityServer(int groupId,
			out MobileNPCHumanoidDisposition.Role role, out MobileNPCFaction.Id faction)
		{
			role = MobileNPCHumanoidDisposition.Role.Neutral;
			faction = MobileNPCFaction.Id.Unaffiliated;
			if (!Provider.isServer || groupId <= 0
				|| !groups.TryGetValue(groupId, out GroupRecord group))
				return false;

			PruneDeadMembers(group);
			if (group.Members.Count == 0 || group.Members.Count >= MaximumGroupSize)
				return false;

			role = group.Role;
			faction = group.Faction;
			return true;
		}

		/// <summary>
		/// Finds the current living leader only for a member of the registered group.
		/// A radio station cannot be used through a dissolved or foreign group.
		/// </summary>
		public bool TryGetLivingLeaderServer(out MobileNPCGroupMember leader)
		{
			leader = null;
			if (!Provider.isServer || !initialized || leftGroup || GroupId <= 0
				|| vitals != null && vitals.IsDead
				|| !groups.TryGetValue(GroupId, out GroupRecord group))
				return false;
			PruneDeadMembers(group);
			if (!group.Members.Contains(this) || group.Leader == null
				|| !group.Members.Contains(group.Leader)
				|| group.Leader.GroupId != GroupId)
				return false;
			leader = group.Leader;
			return true;
		}

		private static GroupRecord FindNearestGroup(Vector3 position, MobileNPCHumanoidDisposition.Role role, MobileNPCFaction.Id faction)
		{
			GroupRecord nearest = null;
			float bestDistanceSquared = GroupJoinRadius * GroupJoinRadius;
			foreach (GroupRecord group in groups.Values)
			{
				PruneDeadMembers(group);
				if (group.Members.Count == 0 || group.Members.Count >= MaximumGroupSize || group.Role != role || group.Faction != faction)
					continue;

				Vector3 offset = group.Anchor - position;
				offset.y = 0f;
				float distanceSquared = offset.sqrMagnitude;
				if (distanceSquared <= bestDistanceSquared)
				{
					bestDistanceSquared = distanceSquared;
					nearest = group;
				}
			}
			return nearest;
		}

		private static void PruneDeadMembers(GroupRecord group)
		{
			group.Members.RemoveWhere(member => member == null || member.leftGroup
				|| member.vitals != null && member.vitals.IsDead);
			for (int slot = 0; slot < group.GuardPostOwners.Length; ++slot)
			{
				MobileNPCGroupMember owner = group.GuardPostOwners[slot];
				if (owner != null && (!group.Members.Contains(owner)
					|| owner.vitals != null && owner.vitals.IsDead))
					group.GuardPostOwners[slot] = null;
			}
			for (int slot = 0; slot < group.WorkSectorOwners.Length; ++slot)
			{
				MobileNPCGroupMember owner = group.WorkSectorOwners[slot];
				if (owner != null && (!group.Members.Contains(owner)
					|| owner.vitals != null && owner.vitals.IsDead
					|| Time.time >= group.WorkSectorLeaseExpires[slot]))
					group.WorkSectorOwners[slot] = null;
			}
		}

		/// <summary>
		/// A fallen comrade changes the attitude of LOCAL survivors only.
		/// No universal group/faction death telepathy, no location broadcast
		/// to members behind distant walls, and no knowledge of the killer.
		/// </summary>
		private void ShareAudibleAllyLossServer()
		{
			if (!Provider.isServer || !initialized || leftGroup
				|| !groups.TryGetValue(GroupId, out GroupRecord group))
				return;
			int heard = 0;
			foreach (MobileNPCGroupMember ally in group.Members)
			{
				if (ally == null || ally == this || ally.leftGroup
					|| ally.vitals != null && ally.vitals.IsDead)
					continue;
				Vector3 mouth = transform.position + Vector3.up * 1.2f;
				Vector3 ear = ally.transform.position + Vector3.up * 1.25f;
				Vector3 delta = ear - mouth;
				float distance = delta.magnitude;
				if (distance <= 0.1f || distance > AlertShoutRange)
					continue;
				RaycastHit[] hits = Physics.RaycastAll(mouth, delta / distance, distance,
					RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
				bool blocked = false;
				for (int i = 0; i < hits.Length; ++i)
				{
					Collider obstacle = hits[i].collider;
					if (obstacle == null || obstacle.transform.IsChildOf(transform)
						|| obstacle.transform.IsChildOf(ally.transform)
						|| obstacle.GetComponentInParent<MobileNPCHumanoidVitals>() != null
						|| obstacle.GetComponentInParent<Player>() != null)
						continue;
					blocked = true;
					break;
				}
				if (blocked && distance > AlertShoutRange * WallHearingMultiplier)
					continue;
				ally.nearbyAllyLostUntil = Mathf.Max(ally.nearbyAllyLostUntil,
					Time.time + Random.Range(12f, 20f));
				++heard;
			}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (heard > 0)
				Debug.Log("[MobileNPCGroupMember] Group " + GroupId
					+ " nearby loss heard by " + heard
					+ " surviving teammate(s): cautious behavior, no shooter knowledge.", this);
#endif
		}

		private void OnDied()
		{
			ShareAudibleAllyLossServer();
			ClearSquadRole();
			LeaveGroupServer();
		}

		private void OnDestroy()
		{
			if (vitals != null)
				vitals.Died -= OnDied;
			LeaveGroupServer();
		}

		/// <summary>
		/// Reassigns a newly spawned individual to the group whose member triggered this
		/// replacement. Returns false when that group has dissolved, changed role, or is full.
		/// </summary>
		public bool TryJoinExistingGroupServer(int preferredGroupId)
		{
			if (!Provider.isServer || !initialized || leftGroup || preferredGroupId <= 0)
				return false;
			if (GroupId == preferredGroupId)
				return true;
			if (!groups.TryGetValue(preferredGroupId, out GroupRecord target))
				return false;

			PruneDeadMembers(target);
			MobileNPCHumanoidDisposition.Role role = disposition != null
				? disposition.CurrentRole : MobileNPCHumanoidDisposition.Role.Neutral;
			MobileNPCFaction.Id faction = GetCurrentFaction(role);
			if (target.Members.Count == 0 || target.Members.Count >= MaximumGroupSize
				|| target.Role != role || target.Faction != faction)
				return false;

			RemoveFromCurrentGroupServer(false);
			GroupId = target.Id;
			GroupAnchor = target.Anchor;
			target.Members.Add(this);
			if (target.Leader == null)
				target.Leader = this;
			if (motor != null)
				motor.TrySetHomePositionServer(target.Anchor);

			Debug.Log("[MobileNPCGroupMember] NPC " + name + " rejoined surviving group " + GroupId
				+ " after a delayed population replacement (" + target.Members.Count + "/" + MaximumGroupSize + ").");
			return true;
		}

		private void LeaveGroupServer()
		{
			if (!Provider.isServer || leftGroup)
				return;
			RemoveFromCurrentGroupServer(true);
		}

		private void RemoveFromCurrentGroupServer(bool permanently)
		{
			ClearSquadRole();
			if (permanently)
				leftGroup = true;

			if (groups.TryGetValue(GroupId, out GroupRecord group))
			{
				group.Members.Remove(this);
				for (int slot = 0; slot < group.GuardPostOwners.Length; ++slot)
				{
					if (group.GuardPostOwners[slot] == this)
						group.GuardPostOwners[slot] = null;
				}
				// Death, faction change and replacement MUST release the old
				// individual's sector; a newly spawned member knows none of it.
				for (int slot = 0; slot < group.WorkSectorOwners.Length; ++slot)
				{
					if (group.WorkSectorOwners[slot] == this)
					{
						group.WorkSectorOwners[slot] = null;
						group.WorkSectorRevisitAfter[slot] = 0f;
					}
				}
				PruneDeadMembers(group);
				if (group.Leader == this || group.Leader == null || group.Leader.vitals != null && group.Leader.vitals.IsDead)
				{
					group.Leader = null;
					foreach (MobileNPCGroupMember candidate in group.Members)
					{
						if (candidate != null && !candidate.leftGroup && (candidate.vitals == null || !candidate.vitals.IsDead))
						{
							group.Leader = candidate;
							break;
						}
					}
				}
				if (group.Members.Count == 0)
				{
					groups.Remove(group.Id);
					Debug.Log("[MobileNPCGroupMember] Group " + group.Id + " dissolved because it has no living members.");
				}
			}
			GroupId = 0;
		}

		private void ClearSquadRole()
		{
			squadRole = SquadRole.None;
			squadRoleUntil = 0f;
			pendingAdvanceAt = 0f;
			assignedCoverer = null;
		}

		/// <summary>Shares construction progress so a successor leader does not restart the pattern.</summary>
		public void RecordSettlementPiecePlacedServer(bool isCentralStorage)
		{
			if (!Provider.isServer || !initialized || leftGroup || !groups.TryGetValue(GroupId, out GroupRecord group))
				return;
			group.SettlementPiecesPlaced = Mathf.Min(5, group.SettlementPiecesPlaced + 1);
			if (isCentralStorage)
				group.SettlementCentralStoragePlaced = true;
			else
				group.SettlementPerimeterPiecesPlaced = Mathf.Min(4, group.SettlementPerimeterPiecesPlaced + 1);
		}

		/// <summary>Recover group construction progress from an actual saved
		/// living member. Native structures are NEVER created by a snapshot:
		/// scene reconciliation must still find the real built pieces.</summary>
		public void RestoreConstructionProgressServer(int pieces, int perimeter, bool storagePlaced)
		{
			if (!Provider.isServer || !initialized || leftGroup || GroupId <= 0
				|| !groups.TryGetValue(GroupId, out GroupRecord record) || !record.HasSettlement)
				return;
			int count = Mathf.Clamp(pieces, 0, 5);
			int sides = Mathf.Clamp(perimeter, 0, 4);
			record.SettlementPiecesPlaced = Mathf.Max(record.SettlementPiecesPlaced, count);
			record.SettlementPerimeterPiecesPlaced = Mathf.Max(record.SettlementPerimeterPiecesPlaced, sides);
			record.SettlementCentralStoragePlaced |= storagePlaced;
		}

		/// <summary>Restores a saved group identity and shared settlement anchor after snapshot loading.</summary>
		public bool SetGroupAnchorServer(int savedGroupId, Vector3 savedAnchor, bool hasSettlement)
		{
			if (!Provider.isServer || !initialized || leftGroup || savedGroupId <= 0
				|| float.IsNaN(savedAnchor.x) || float.IsInfinity(savedAnchor.x)
				|| float.IsNaN(savedAnchor.y) || float.IsInfinity(savedAnchor.y)
				|| float.IsNaN(savedAnchor.z) || float.IsInfinity(savedAnchor.z))
				return false;

			MobileNPCHumanoidDisposition.Role role = disposition != null
				? disposition.CurrentRole : MobileNPCHumanoidDisposition.Role.Neutral;
			MobileNPCFaction.Id faction = GetCurrentFaction(role);

			// Snapshot group IDs belong to the save file, not this runtime session. Map them
			// to fresh runtime IDs so auto-created groups cannot accidentally collide.
			if (!restoredGroupIdMap.TryGetValue(savedGroupId, out int runtimeGroupId))
			{
				runtimeGroupId = nextGroupId++;
				while (groups.ContainsKey(runtimeGroupId))
					runtimeGroupId = nextGroupId++;
				restoredGroupIdMap.Add(savedGroupId, runtimeGroupId);
			}

			if (!groups.TryGetValue(runtimeGroupId, out GroupRecord target))
			{
				target = new GroupRecord
				{
					Id = runtimeGroupId,
					Anchor = savedAnchor,
					Role = role,
					Faction = faction,
					HasSettlement = hasSettlement
				};
				groups.Add(runtimeGroupId, target);
			}
			else
			{
				PruneDeadMembers(target);
				if (target.Role != role || target.Faction != faction || target.Members.Count >= MaximumGroupSize)
					return false;
			}

			if (GroupId != runtimeGroupId)
				RemoveFromCurrentGroupServer(false);

			GroupId = target.Id;
			GroupAnchor = target.Anchor;
			target.Members.Add(this);
			if (target.Leader == null)
				target.Leader = this;
			if (motor != null)
				motor.TrySetHomePositionServer(target.Anchor);
			nextGroupId = Mathf.Max(nextGroupId, runtimeGroupId + 1);

			// A later record can be the first member encountered with settlement data.
			// Apply the saved anchor through the shared setter so every member is synchronized.
			if (hasSettlement)
				SetGroupAnchorServer(savedAnchor);
			return true;
		}

		/// <summary>Clears volatile group state between loaded maps.</summary>
		public static void ResetAllServerGroups()
		{
			if (!Provider.isServer)
				return;
			groups.Clear();
			restoredGroupIdMap.Clear();
			nextGroupId = 1;
		}
	}
}
