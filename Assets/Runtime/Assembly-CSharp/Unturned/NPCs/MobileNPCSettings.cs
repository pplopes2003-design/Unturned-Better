////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Persistent single-player mobile NPC options. These settings intentionally affect
	/// local population creation and behavior only; multiplayer synchronization is not implied.
	/// </summary>
	internal static class MobileNPCSettings
	{
		private const string EnabledKey = "MobileNPCs.Enabled";
		private const string PopulationKey = "MobileNPCs.MaximumAlive";
		private const string DifficultyKey = "MobileNPCs.Difficulty";
		private const string AirdropKey = "MobileNPCs.AirdropContest";
		private const string RoleKey = "MobileNPCs.DefaultRole";

		public enum Difficulty
		{
			Easy = 0,
			Normal = 1,
			Hard = 2
		}

		public static bool Enabled
		{
			get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
			set { PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0); Save(); }
		}

		public static int MaximumAlive
		{
			get => Mathf.Clamp(PlayerPrefs.GetInt(PopulationKey, 20), 1, 40);
			set { PlayerPrefs.SetInt(PopulationKey, Mathf.Clamp(value, 1, 40)); Save(); }
		}

		public static Difficulty CurrentDifficulty
		{
			get => (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt(DifficultyKey, (int)Difficulty.Normal), 0, 2);
			set { PlayerPrefs.SetInt(DifficultyKey, Mathf.Clamp((int)value, 0, 2)); Save(); }
		}

		public static bool AirdropContestEnabled
		{
			get => PlayerPrefs.GetInt(AirdropKey, 1) != 0;
			set { PlayerPrefs.SetInt(AirdropKey, value ? 1 : 0); Save(); }
		}

		public static MobileNPCHumanoidDisposition.Role DefaultRole
		{
			get => (MobileNPCHumanoidDisposition.Role)Mathf.Clamp(PlayerPrefs.GetInt(RoleKey, (int)MobileNPCHumanoidDisposition.Role.Hostile), 0, 2);
			set { PlayerPrefs.SetInt(RoleKey, Mathf.Clamp((int)value, 0, 2)); Save(); }
		}

		public static float ReactionTimeMultiplier
		{
			get
			{
				switch (CurrentDifficulty)
				{
					case Difficulty.Easy: return 1.45f;
					case Difficulty.Hard: return 0.75f;
					default: return 1f;
				}
			}
		}

		public static float AimSpreadMultiplier
		{
			get
			{
				switch (CurrentDifficulty)
				{
					case Difficulty.Easy: return 1.45f;
					case Difficulty.Hard: return 0.72f;
					default: return 1f;
				}
			}
		}

		private static void Save()
		{
			PlayerPrefs.Save();
		}
	}
}
