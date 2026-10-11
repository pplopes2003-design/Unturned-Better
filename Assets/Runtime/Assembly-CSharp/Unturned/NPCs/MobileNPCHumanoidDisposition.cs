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

		public Role CurrentRole => role;
		public bool CanEngagePlayers => role == Role.Hostile;

		public void SetRoleServer(Role newRole)
		{
			if (!Provider.isServer)
			{
				return;
			}

			role = newRole;
			if (!CanEngagePlayers)
			{
				MobileNPCBrainDriver brain = GetComponent<MobileNPCBrainDriver>();
				brain?.ResetServer();
			}
		}
	}
}