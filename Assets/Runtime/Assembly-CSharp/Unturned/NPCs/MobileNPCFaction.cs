////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Lightweight faction identity and relationship rules for the mobile NPC system.
	/// This component is opt-in and does not change legacy NPCs or vanilla zombies.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCFaction : MonoBehaviour
	{
		public enum Id
		{
			Unaffiliated = 0,
			Survivors = 1,
			Raiders = 2,
			Military = 3,
			Scavengers = 4,
			Traders = 5
		}

		private bool usesRoleDefaultFaction;

		private void Awake()
		{
			if (faction != Id.Unaffiliated)
				return;

			usesRoleDefaultFaction = true;
			SynchronizeDefaultFactionFromRole();
		}

		/// <summary>Updates only automatically assigned identities; explicit faction choices are preserved.</summary>
		internal void SynchronizeDefaultFactionFromRole()
		{
			if (!usesRoleDefaultFaction && faction != Id.Unaffiliated)
				return;

			MobileNPCHumanoidDisposition disposition = GetComponent<MobileNPCHumanoidDisposition>();
			if (disposition == null)
				return;

			usesRoleDefaultFaction = true;
			switch (disposition.CurrentRole)
			{
				case MobileNPCHumanoidDisposition.Role.Hostile:
					faction = Id.Raiders;
					break;
				case MobileNPCHumanoidDisposition.Role.Friendly:
					faction = Id.Traders;
					break;
				default:
					faction = Id.Scavengers;
					break;
			}
		}

		public enum Relationship
		{
			Allied,
			Neutral,
			Hostile
		}

		[SerializeField] private Id faction = Id.Unaffiliated;
		public Id CurrentFaction => faction;

		public void SetFactionServer(Id value)
		{
			if (!Provider.isServer)
				return;

			if ((int)value < (int)Id.Unaffiliated || (int)value > (int)Id.Traders)
				return;

			faction = value;
			usesRoleDefaultFaction = false;
			MobileNPCBrainDriver brain = GetComponent<MobileNPCBrainDriver>();
			if (brain != null)
				brain.ResetServer();

			MobileNPCGroupMember groupMember = GetComponent<MobileNPCGroupMember>();
			groupMember?.RefreshGroupMembershipServer();
		}

		public static Relationship GetRelationship(Id first, Id second)
		{
			if (first == second)
				return Relationship.Allied;

			if (first == Id.Unaffiliated || second == Id.Unaffiliated)
				return Relationship.Neutral;

			// Traders avoid combat unless a hostile raider faction targets them.
			if ((first == Id.Traders && second == Id.Raiders)
				|| (first == Id.Raiders && second == Id.Traders))
				return Relationship.Hostile;

			// Raiders are hostile to organized survivors and military patrols.
			if ((first == Id.Raiders && (second == Id.Survivors || second == Id.Military))
				|| (second == Id.Raiders && (first == Id.Survivors || first == Id.Military)))
				return Relationship.Hostile;

			// Military and survivor groups distrust one another by default but do not
			// automatically open fire; maps can override this through future diplomacy rules.
			if ((first == Id.Military && second == Id.Survivors)
				|| (second == Id.Military && first == Id.Survivors))
				return Relationship.Neutral;

			// Scavengers do not start faction wars; they may still defend themselves
			// through the individual disposition/combat layer.
			return Relationship.Neutral;
		}

		public bool IsHostileTo(MobileNPCFaction other)
		{
			return other != null && other != this
				&& GetRelationship(faction, other.faction) == Relationship.Hostile;
		}
	}
}