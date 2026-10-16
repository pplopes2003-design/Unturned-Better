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
			public bool HasSettlement;
			public int SettlementPiecesPlaced;
			public int SettlementPerimeterPiecesPlaced;
			public bool SettlementCentralStoragePlaced;
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
			if (!Provider.isServer || CurrentSquadRole != SquadRole.Advancing
				|| pendingAdvanceAt <= 0f || Time.time < pendingAdvanceAt)
				return;
			pendingAdvanceAt = 0f;
			if (vitals != null && vitals.IsDead)
				return;
			MobileNPCBrainDriver brain = GetComponent<MobileNPCBrainDriver>();
			if (brain != null)
				brain.TryStartSquadAdvance(pendingAdvanceClue);
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
				&& ownWeapon.RoundsRemaining > 0 && !ownWeapon.IsReloading;
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

				// One shooter can cover one cautious advancement. This stage
				// assigns roles only; genuine suppression fire requires its
				// own line-of-fire, ammunition and ally safety implementation.
				if (!canCover || ally.CurrentSquadRole != SquadRole.None
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
			const float sameSectorSqr = 22f * 22f;
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
				group.WorkSectorRevisitAfter[i] = Time.time
					+ (visited ? 70f : blocked ? 40f : 0f);
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

		private void OnDied()
		{
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
