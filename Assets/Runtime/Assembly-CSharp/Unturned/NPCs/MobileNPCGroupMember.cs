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
			public MobileNPCHumanoidDisposition.Role Role;
		}

		private MobileNPCHumanoidVitals vitals;
		private MobileNPCHumanoidMotor motor;
		private MobileNPCHumanoidDisposition disposition;
		private bool initialized;
		private bool leftGroup;

		public int GroupId { get; private set; }
		public Vector3 GroupAnchor { get; private set; }
		public bool CanPlanSettlement => initialized && !leftGroup && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord currentGroup) && currentGroup.Leader == this;
		public bool HasSettlement => initialized && !leftGroup && GroupId > 0
			&& groups.TryGetValue(GroupId, out GroupRecord settlementGroup) && settlementGroup.HasSettlement;

		private void Awake()
		{
			vitals = GetComponent<MobileNPCHumanoidVitals>();
			motor = GetComponent<MobileNPCHumanoidMotor>();
			disposition = GetComponent<MobileNPCHumanoidDisposition>();
		}

		/// <summary>Assigns this newly spawned individual to the nearest living group, or creates one.</summary>
		public void InitializeServer()
		{
			if (!Provider.isServer || initialized || motor == null || vitals != null && vitals.IsDead)
				return;

			initialized = true;
			MobileNPCHumanoidDisposition.Role role = disposition != null
				? disposition.CurrentRole : MobileNPCHumanoidDisposition.Role.Neutral;
			GroupRecord group = FindNearestGroup(transform.position, role);
			bool createdGroup = group == null;
			if (group == null)
			{
				group = new GroupRecord
				{
					Id = nextGroupId++,
					Anchor = motor.HomePosition,
					Role = role
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
			foreach (MobileNPCGroupMember member in group.Members)
			{
				if (member == null || member.vitals != null && member.vitals.IsDead)
					continue;
				member.GroupAnchor = anchor;
				if (member.motor != null)
					member.motor.TrySetHomePositionServer(anchor);
			}
		}

		private static GroupRecord FindNearestGroup(Vector3 position, MobileNPCHumanoidDisposition.Role role)
		{
			GroupRecord nearest = null;
			float bestDistanceSquared = GroupJoinRadius * GroupJoinRadius;
			foreach (GroupRecord group in groups.Values)
			{
				PruneDeadMembers(group);
				if (group.Members.Count == 0 || group.Members.Count >= MaximumGroupSize || group.Role != role)
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
		}

		private void OnDied()
		{
			LeaveGroupServer();
		}

		private void OnDestroy()
		{
			if (vitals != null)
				vitals.Died -= OnDied;
			LeaveGroupServer();
		}

		private void LeaveGroupServer()
		{
			if (!Provider.isServer || leftGroup)
				return;

			leftGroup = true;
			if (groups.TryGetValue(GroupId, out GroupRecord group))
			{
				group.Members.Remove(this);
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

		/// <summary>Clears volatile group state between loaded maps.</summary>
		public static void ResetAllServerGroups()
		{
			if (!Provider.isServer)
				return;
			groups.Clear();
			nextGroupId = 1;
		}
	}
}
