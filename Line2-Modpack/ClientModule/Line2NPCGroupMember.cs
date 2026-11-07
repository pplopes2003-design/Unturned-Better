using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Small Steam adapter for ORIGINAL Line 1 MobileNPCGroupMember:
    /// max 4 living NPCs per group; same faction, physical 140m join
    /// radius; unregister on death/destroy. This intentionally does NOT
    /// invent settlements, raids, or persistent group identities yet.
    /// </summary>
    internal sealed class Line2NPCGroupMember : MonoBehaviour
    {
        private const int MaxGroupSize = 4;
        private const float JoinRangeSquared = 140f * 140f;
        private static readonly Dictionary<int, List<Line2NPCGroupMember>> Groups =
            new Dictionary<int, List<Line2NPCGroupMember>>();
        private static int nextGroupId = 1;

        /// <summary>
        /// Reserve identities of dead/abandoned groups and queued replacement
        /// tickets after loading a map. Otherwise a newly spawned group might
        /// be assigned the ID of a destroyed historical faction campsite.
        /// </summary>
        internal static void ReserveHistoricalGroupId(int used)
        {
            if (used > 0 && used <= 1000000 && nextGroupId <= used)
                nextGroupId = used + 1;
        }

        internal int GroupId { get; private set; }
        internal Line2NPCFactionPolicy.Id Faction { get; private set; }
        internal bool Joined { get { return GroupId != 0; } }

        internal void Initialize(PrototypeActor actor,
                                 List<PrototypeActor> existing)
        {
            if (actor == null || !Provider.isServer ||
                Dedicator.IsDedicatedServer || Joined)
                return;
            Faction = Line2NPCFactionPolicy.DefaultForRole(actor.Disposition);
            int group = 0;
            float nearest = JoinRangeSquared;
            if (existing != null)
            {
                foreach (PrototypeActor other in existing)
                {
                    if (other == null || other == actor || other.IsDead)
                        continue;
                    Line2NPCGroupMember membership =
                        other.GetComponent<Line2NPCGroupMember>();
                    if (membership == null || !membership.Joined ||
                        membership.Faction != Faction)
                        continue;
                    List<Line2NPCGroupMember> members;
                    if (!Groups.TryGetValue(membership.GroupId, out members) ||
                        members.Count >= MaxGroupSize)
                        continue;
                    float distance =
                        (other.transform.position - transform.position).sqrMagnitude;
                    if (distance < nearest)
                    {
                        nearest = distance;
                        group = membership.GroupId;
                    }
                }
            }
            if (group == 0) group = nextGroupId++;
            if (nextGroupId <= 0) nextGroupId = 1;
            List<Line2NPCGroupMember> roster;
            if (!Groups.TryGetValue(group, out roster))
            {
                roster = new List<Line2NPCGroupMember>(MaxGroupSize);
                Groups.Add(group, roster);
            }
            if (roster.Count >= MaxGroupSize) return;
            GroupId = group;
            roster.Add(this);
        }

        /// <summary>Rejoin a surviving map group using its saved ID. A freshly
        /// spawned actor must not inherit an extinct group's identity unless
        /// this is an actual restore of that same survivor.</summary>
        internal bool TryRestoreMembership(PrototypeActor actor, int savedGroupId)
        {
            if (actor == null || actor.IsDead || !Provider.isServer ||
                Dedicator.IsDedicatedServer || savedGroupId <= 0 ||
                savedGroupId > 1000000) return false;
            Line2NPCFactionPolicy.Id faction =
                Line2NPCFactionPolicy.DefaultForRole(actor.Disposition);
            List<Line2NPCGroupMember> existing;
            if (Groups.TryGetValue(savedGroupId, out existing))
            {
                if (existing.Count >= MaxGroupSize) return false;
                foreach (Line2NPCGroupMember member in existing)
                    if (member == null || member.Faction != faction) return false;
            }
            Leave();
            if (!Groups.TryGetValue(savedGroupId, out existing))
            {
                existing = new List<Line2NPCGroupMember>(MaxGroupSize);
                Groups.Add(savedGroupId, existing);
            }
            Faction = faction;
            GroupId = savedGroupId;
            existing.Add(this);
            if (nextGroupId <= savedGroupId) nextGroupId = savedGroupId + 1;
            return true;
        }

        internal static bool TryGetLivingMember(int groupId, out PrototypeActor member)
        {
            member = null;
            List<Line2NPCGroupMember> group;
            if (groupId <= 0 || !Groups.TryGetValue(groupId, out group)) return false;
            foreach (Line2NPCGroupMember candidate in group)
            {
                if (candidate == null) continue;
                PrototypeActor actor = candidate.GetComponent<PrototypeActor>();
                if (actor == null || actor.IsDead) continue;
                member = actor;
                return true;
            }
            return false;
        }

        /// <summary>Only fresh replacements with living former teammates
        /// can rejoin an existing group. Extinct groups cannot be revived.</summary>
        internal bool TryJoinLivingGroup(PrototypeActor actor, int previousGroupId)
        {
            PrototypeActor living;
            if (actor == null || actor.IsDead ||
                !TryGetLivingMember(previousGroupId, out living) ||
                (living.transform.position - actor.transform.position).sqrMagnitude >
                    JoinRangeSquared)
                return false;
            return TryRestoreMembership(actor, previousGroupId);
        }

        internal void Leave()
        {
            if (!Joined) return;
            List<Line2NPCGroupMember> members;
            if (Groups.TryGetValue(GroupId, out members))
            {
                members.Remove(this);
                if (members.Count == 0) Groups.Remove(GroupId);
            }
            GroupId = 0;
        }

        private void OnDisable() { Leave(); }
        private void OnDestroy() { Leave(); }

        internal static void ResetSession()
        {
            Groups.Clear();
            nextGroupId = 1;
        }
    }
}
