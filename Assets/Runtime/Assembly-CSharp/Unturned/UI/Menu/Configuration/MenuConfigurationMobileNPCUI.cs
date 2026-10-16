////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>Small settings page for the experimental single-player mobile NPC system.</summary>
	public sealed class MenuConfigurationMobileNPCUI
	{
		private static SleekFullscreenBox container;
		private static SleekButtonIcon populationLabel;
		private static SleekButtonIcon difficultyButton;
		private static SleekButtonIcon enabledButton;
		private static SleekButtonIcon airdropButton;
		private static SleekButtonIcon roleButton;
		private static SleekButtonIcon mixButton;
		private static SleekButtonIcon gameModeButton;
		private static SleekButtonIcon eraseHistoryButton;
		private static float eraseConfirmationDeadline;
		private static string eraseStatus;
		private static bool constructed;

		public static void open()
		{
			EnsureConstructed();
			eraseConfirmationDeadline = 0f;
			RefreshLabels();
			container.AnimateIntoView();
		}

		public static void close()
		{
			eraseConfirmationDeadline = 0f;
			if (container != null)
				container.AnimateOutOfView(0, -1);
		}

		private static void EnsureConstructed()
		{
			if (constructed)
				return;
			constructed = true;

			container = new SleekFullscreenBox();
			container.PositionOffset_X = 10;
			container.PositionOffset_Y = 10;
			container.PositionScale_Y = -1;
			container.SizeOffset_X = -20;
			container.SizeOffset_Y = -20;
			container.SizeScale_X = 1;
			container.SizeScale_Y = 1;
			MenuUI.container.AddChild(container);

			populationLabel = CreateButton(-130, -195, 260, 40);
			populationLabel.onClickedButton += onClickedPopulationLabel;
			SleekButtonIcon populationDown = CreateButton(-230, -195, 80, 40);
			populationDown.text = "-";
			populationDown.onClickedButton += onClickedPopulationDown;
			SleekButtonIcon populationUp = CreateButton(150, -195, 80, 40);
			populationUp.text = "+";
			populationUp.onClickedButton += onClickedPopulationUp;

			difficultyButton = CreateButton(-150, -140, 300, 40);
			difficultyButton.onClickedButton += onClickedDifficulty;
			enabledButton = CreateButton(-150, -85, 300, 40);
			enabledButton.onClickedButton += onClickedEnabled;
			airdropButton = CreateButton(-150, -30, 300, 40);
			airdropButton.onClickedButton += onClickedAirdrop;
			roleButton = CreateButton(-150, 25, 300, 40);
			roleButton.onClickedButton += onClickedRole;
			mixButton = CreateButton(-170, 80, 340, 40);
			mixButton.onClickedButton += onClickedMix;
			gameModeButton = CreateButton(-170, 135, 340, 40);
			gameModeButton.onClickedButton += onClickedGameMode;
			eraseHistoryButton = CreateButton(-215, 190, 430, 40);
			eraseHistoryButton.onClickedButton += onClickedEraseHistory;

			SleekButtonIcon back = CreateButton(-100, 245, 200, 40);
			back.text = "Back";
			back.onClickedButton += onClickedBack;
			RefreshLabels();
		}

		private static SleekButtonIcon CreateButton(int x, int y, int width, int height)
		{
			// This page is part of the legacy Sleek menu hierarchy. Using a Glazier
			// button here can produce a button whose uGUI text component is absent.
			SleekButtonIcon button = new SleekButtonIcon(null);
			button.PositionOffset_X = x;
			button.PositionOffset_Y = y;
			button.PositionScale_X = 0.5f;
			button.PositionScale_Y = 0.5f;
			button.SizeOffset_X = width;
			button.SizeOffset_Y = height;
			container.AddChild(button);
			return button;
		}

		private static void RefreshLabels()
		{
			if (populationLabel == null)
				return;
			populationLabel.text = "Maximum living NPCs: " + MobileNPCSettings.MaximumAlive;
			difficultyButton.text = "Difficulty: " + MobileNPCSettings.CurrentDifficulty;
			enabledButton.text = "NPC spawning/restoring: " + (MobileNPCSettings.Enabled ? "ON" : "OFF");
			airdropButton.text = "Airdrop contest: " + (MobileNPCSettings.AirdropContestEnabled ? "ON" : "OFF");
			roleButton.text = "Manual test-spawn role: " + MobileNPCSettings.DefaultRole;
			mixButton.text = "World population mix: " + MixLabel(MobileNPCSettings.CurrentPopulationMix);
			gameModeButton.text = "Agressividade: " + (MobileNPCSettings.CombatMode ? "Combate" : "Normal");
			eraseHistoryButton.text = Time.realtimeSinceStartup < eraseConfirmationDeadline
				? "CONFIRMAR: apagar historico de NPCs (todos os mapas)"
				: !string.IsNullOrEmpty(eraseStatus) ? eraseStatus
				: "Apagar historico de NPCs (todos os mapas)";
		}

		private static void onClickedPopulationDown(ISleekElement button)
		{
			MobileNPCSettings.MaximumAlive = Mathf.Max(1, MobileNPCSettings.MaximumAlive - 1);
			RefreshLabels();
		}

		private static void onClickedPopulationUp(ISleekElement button)
		{
			MobileNPCSettings.MaximumAlive = Mathf.Min(40, MobileNPCSettings.MaximumAlive + 1);
			RefreshLabels();
		}

		private static void onClickedPopulationLabel(ISleekElement button)
		{
			// The center label is intentionally inert; use the adjacent +/- controls.
		}

		private static void onClickedDifficulty(ISleekElement button)
		{
			MobileNPCSettings.CurrentDifficulty = (MobileNPCSettings.Difficulty)(((int)MobileNPCSettings.CurrentDifficulty + 1) % 3);
			RefreshLabels();
		}

		private static void onClickedEnabled(ISleekElement button)
		{
			MobileNPCSettings.Enabled = !MobileNPCSettings.Enabled;
			RefreshLabels();
		}

		private static void onClickedAirdrop(ISleekElement button)
		{
			MobileNPCSettings.AirdropContestEnabled = !MobileNPCSettings.AirdropContestEnabled;
			RefreshLabels();
		}

		private static void onClickedRole(ISleekElement button)
		{
			MobileNPCSettings.DefaultRole = (MobileNPCHumanoidDisposition.Role)(((int)MobileNPCSettings.DefaultRole + 1) % 3);
			RefreshLabels();
		}

		private static string MixLabel(MobileNPCSettings.PopulationMix mix)
		{
			switch (mix)
			{
				case MobileNPCSettings.PopulationMix.HostileHeavy: return "Hostile-heavy (70/20/10)";
				case MobileNPCSettings.PopulationMix.Peaceful: return "Peaceful (20/50/30)";
				default: return "Balanced (50/30/20)";
			}
		}

		private static void onClickedMix(ISleekElement button)
		{
			MobileNPCSettings.CurrentPopulationMix = (MobileNPCSettings.PopulationMix)(((int)MobileNPCSettings.CurrentPopulationMix + 1) % 3);
			RefreshLabels();
		}

		private static void onClickedGameMode(ISleekElement button)
		{
			MobileNPCSettings.CurrentGameMode = MobileNPCSettings.CombatMode
				? MobileNPCSettings.GameMode.Normal : MobileNPCSettings.GameMode.Combat;
			eraseConfirmationDeadline = 0f;
			eraseStatus = null;
			RefreshLabels();
		}

		private static void onClickedEraseHistory(ISleekElement button)
		{
			// Two distinct clicks no more than eight seconds apart. No erase in
			// the running world and no broad Application.persistentDataPath wipe.
			if (Time.realtimeSinceStartup >= eraseConfirmationDeadline)
			{
				eraseConfirmationDeadline = Time.realtimeSinceStartup + 8f;
				eraseStatus = null;
				RefreshLabels();
				return;
			}
			eraseConfirmationDeadline = 0f;
			if (MobileNPCPersistence.TryEraseNPCSnapshotsFromMainMenu(
				out int removed, out string error))
				eraseStatus = "Historico apagado (" + removed + " arquivos NPC)";
			else
			{
				eraseStatus = "Falha ao apagar: veja Player.log";
				Debug.LogWarning("[MobileNPCMenu] Could not reset NPC-only snapshots: " + error);
			}
			RefreshLabels();
		}

		private static void onClickedBack(ISleekElement button)
		{
			close();
			MenuConfigurationUI.open();
		}
	}
}
