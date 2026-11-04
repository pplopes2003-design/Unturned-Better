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


		/// <summary>Server chat message for a fatal mobile-NPC attack.
		/// Native death attribution accepts SteamID rather than an NPC entity.</summary>
		public void NotifyFatalPlayerAttackServer(Player victim, EDeathCause cause)
		{
			if (!Provider.isServer || victim == null || victim.life == null
				|| !victim.life.isDead || victim.channel == null
				|| victim.channel.owner == null)
				return;
			string reason;
			switch (cause)
			{
				case EDeathCause.GUN: reason = "tiro"; break;
				case EDeathCause.MELEE: reason = "arma corpo a corpo"; break;
				case EDeathCause.PUNCH: reason = "soco"; break;
				default: return;
			}
			ChatManager.say(victim.channel.owner.playerID.steamID,
				"Voce foi morto por " + DisplayName + " (" + reason + ").",
				Color.red);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.Log("[MobileNPCKillFeed] killer=" + DisplayName + " victim="
				+ victim.name + " cause=" + cause, this);
#endif
		}

		/// <summary>
		/// Preserve an individual's server-owned name across save/restart.
		/// This does not change role, faction, aggression, or player identity.
		/// </summary>
		internal void RestoreDisplayNameServer(string savedName)
		{
			if (!Provider.isServer || string.IsNullOrWhiteSpace(savedName))
				return;
			savedName = savedName.Trim();
			if (savedName.Length > 40)
				savedName = savedName.Substring(0, 40);
			for (int i = 0; i < savedName.Length; ++i)
				if (char.IsControl(savedName[i]))
					return;
			displayName = savedName;
		}

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
		// Pacifist by default does not mean helpless after a validated attack.
		// Only THIS individual remembers its actual attacker for a short time.
		private Player recentPlayerAggressor;
		private float selfDefenseExpiresAt;
		public bool CanEngagePlayers => role == Role.Hostile;
		public bool CanEngagePlayer(Player candidate)
		{
			return candidate != null && candidate.life != null
				&& !candidate.life.isDead
				&& (role == Role.Hostile
					|| (Provider.isServer && candidate == recentPlayerAggressor
						&& Time.time < selfDefenseExpiresAt));
		}
		public void RecordValidatedAttackServer(Transform attacker)
		{
			if (!Provider.isServer || attacker == null)
				return;
			Player attackingPlayer = attacker.GetComponent<Player>();
			if (attackingPlayer == null)
				attackingPlayer = attacker.GetComponentInParent<Player>();
			if (attackingPlayer == null || attackingPlayer.life == null
				|| attackingPlayer.life.isDead)
				return;
			recentPlayerAggressor = attackingPlayer;
			selfDefenseExpiresAt = Time.time + 32f;
		}

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
			recentPlayerAggressor = null;
			selfDefenseExpiresAt = 0f;
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