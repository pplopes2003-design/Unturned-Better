////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Opt-in relationship profile for mobile humanoid NPCs.
	/// Hostile NPCs may acquire and attack players. Friendly and neutral NPCs never
	/// acquire players as combat targets in this prototype. Existing NPCs are untouched
	/// unless this component is added to their prefab.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCHumanoidDisposition : MonoBehaviour
	{
		public enum Role
		{
			Hostile,
			Friendly,
			Neutral
		}

		[SerializeField] private Role role = Role.Hostile;
		[SerializeField] private string displayName;

		private static readonly string[] GivenNames =
		{
			"Alex", "Ana", "Bruno", "Camila", "Daniel", "Elisa", "Felipe", "Gabriela",
			"Helena", "Igor", "Julia", "Lucas", "Marina", "Nicolas", "Rafael", "Sofia"
		};
		private static readonly string[] FamilyNames =
		{
			"Alves", "Barros", "Costa", "Dias", "Ferreira", "Lima", "Mendes", "Nogueira",
			"Oliveira", "Pereira", "Ramos", "Rocha", "Santos", "Silva", "Souza", "Teixeira"
		};

		public Role CurrentRole => role;
		public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Sobrevivente" : displayName;

		private void Awake()
		{
			if (string.IsNullOrWhiteSpace(displayName))
			{
				displayName = GivenNames[Random.Range(0, GivenNames.Length)] + " "
					+ FamilyNames[Random.Range(0, FamilyNames.Length)];
			}
		}
		public bool CanEngagePlayers => role == Role.Hostile;
		public bool CanEngageMobileNPCs => role != Role.Neutral;

		/// <summary>
		/// Hostile and Friendly are opposing factions. Members of the same faction and
		/// Neutral agents do not acquire one another as combat targets.
		/// </summary>
		public bool IsHostileTo(MobileNPCHumanoidDisposition other)
		{
			if (other == null || other == this)
				return false;

			MobileNPCFaction ownFaction = GetComponent<MobileNPCFaction>();
			MobileNPCFaction otherFaction = other.GetComponent<MobileNPCFaction>();
			if (ownFaction != null && otherFaction != null
				&& ownFaction.CurrentFaction != MobileNPCFaction.Id.Unaffiliated
				&& otherFaction.CurrentFaction != MobileNPCFaction.Id.Unaffiliated)
			{
				return ownFaction.IsHostileTo(otherFaction);
			}

			if (role == Role.Neutral || other.role == Role.Neutral)
				return false;

			return role != other.role;
		}

		public static bool AreHostile(MobileNPCHumanoidDisposition first, MobileNPCHumanoidDisposition second)
		{
			return first != null && first.IsHostileTo(second);
		}

		public void SetRoleServer(Role newRole)
		{
			if (!Provider.isServer)
			{
				return;
			}

			role = newRole;
			MobileNPCFaction faction = GetComponent<MobileNPCFaction>();
			faction?.SynchronizeDefaultFactionFromRole();

			MobileNPCGroupMember groupMember = GetComponent<MobileNPCGroupMember>();
			groupMember?.RefreshGroupMembershipServer();

			if (!CanEngagePlayers)
			{
				MobileNPCBrainDriver brain = GetComponent<MobileNPCBrainDriver>();
				brain?.ResetServer();
			}
		}
	}
}