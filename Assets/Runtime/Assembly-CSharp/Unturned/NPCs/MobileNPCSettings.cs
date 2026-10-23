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
		private const string MixKey = "MobileNPCs.PopulationMix";
		private const string GameModeKey = "MobileNPCs.GameMode";

		public enum PopulationMix
		{
			Balanced = 0,
			HostileHeavy = 1,
			Peaceful = 2
		}

		// Different from accuracy/reaction-time difficulty: this changes goals,
		// scarcity pressure and risk appetite, not eyesight or aimbot skill.
		public enum GameMode { Normal = 0, Combat = 1, CombatOnly = 2 }

		private static GameMode currentGameMode = (GameMode)Mathf.Clamp(
			PlayerPrefs.GetInt(GameModeKey, (int)GameMode.Normal), 0, 2);

		public static GameMode CurrentGameMode
		{
			get => currentGameMode;
			set
			{
				currentGameMode = (GameMode)Mathf.Clamp((int)value, 0, 2);
				PlayerPrefs.SetInt(GameModeKey, (int)currentGameMode);
				Save();
			}
		}
		// Old Combat continues to exist unchanged. CombatOnly opts into the
		// faster all-hostile experience; both modes suspend hunger/thirst.
		public static bool CombatMode => currentGameMode != GameMode.Normal;
		public static bool CombatOnly => currentGameMode == GameMode.CombatOnly;

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
			get => Difficulty.Hard;
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

		public static PopulationMix CurrentPopulationMix
		{
			get => (PopulationMix)Mathf.Clamp(PlayerPrefs.GetInt(MixKey, (int)PopulationMix.Balanced), 0, 2);
			set { PlayerPrefs.SetInt(MixKey, Mathf.Clamp((int)value, 0, 2)); Save(); }
		}

		/// <summary>Weighted role selection for automatic world population, independent of the manual test-spawn role.</summary>
		public static MobileNPCHumanoidDisposition.Role SelectPopulationRole()
		{
			if (CombatOnly)
				return MobileNPCHumanoidDisposition.Role.Hostile;
			int roll = Random.Range(0, 100);
			// Combat focuses on a contested world, not every NPC attacking the
			// player. Survivors still need opposing factions to defend each other.
			if (CombatMode)
			{
				if (roll < 66) return MobileNPCHumanoidDisposition.Role.Hostile;
				if (roll < 87) return MobileNPCHumanoidDisposition.Role.Friendly;
				return MobileNPCHumanoidDisposition.Role.Neutral;
			}
			switch (CurrentPopulationMix)
			{
				case PopulationMix.HostileHeavy:
					if (roll < 70) return MobileNPCHumanoidDisposition.Role.Hostile;
					if (roll < 90) return MobileNPCHumanoidDisposition.Role.Friendly;
					return MobileNPCHumanoidDisposition.Role.Neutral;
				case PopulationMix.Peaceful:
					if (roll < 20) return MobileNPCHumanoidDisposition.Role.Hostile;
					if (roll < 70) return MobileNPCHumanoidDisposition.Role.Friendly;
					return MobileNPCHumanoidDisposition.Role.Neutral;
				default:
					if (roll < 50) return MobileNPCHumanoidDisposition.Role.Hostile;
					if (roll < 80) return MobileNPCHumanoidDisposition.Role.Friendly;
					return MobileNPCHumanoidDisposition.Role.Neutral;
			}
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
