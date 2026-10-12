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
		private static ISleekButton populationLabel;
		private static ISleekButton difficultyButton;
		private static ISleekButton enabledButton;
		private static ISleekButton airdropButton;
		private static ISleekButton roleButton;
		private static ISleekButton mixButton;
		private static bool constructed;

		public static void open()
		{
			EnsureConstructed();
			RefreshLabels();
			container.AnimateIntoView();
		}

		public static void close()
		{
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

			populationLabel = CreateButton(-130, -100, 260, 40);
			populationLabel.OnClicked += onClickedPopulationLabel;
			CreateButton(-230, -100, 80, 40).OnClicked += onClickedPopulationDown;
			CreateButton(150, -100, 80, 40).OnClicked += onClickedPopulationUp;

			difficultyButton = CreateButton(-150, -45, 300, 40);
			difficultyButton.OnClicked += onClickedDifficulty;
			enabledButton = CreateButton(-150, 10, 300, 40);
			enabledButton.OnClicked += onClickedEnabled;
			airdropButton = CreateButton(-150, 65, 300, 40);
			airdropButton.OnClicked += onClickedAirdrop;
			roleButton = CreateButton(-150, 120, 300, 40);
			roleButton.OnClicked += onClickedRole;
			mixButton = CreateButton(-170, 175, 340, 40);
			mixButton.OnClicked += onClickedMix;

			ISleekButton back = CreateButton(-100, 230, 200, 40);
			back.Text = "Back";
			back.OnClicked += onClickedBack;
			RefreshLabels();
		}

		private static ISleekButton CreateButton(int x, int y, int width, int height)
		{
			ISleekButton button = Glazier.Get().CreateButton();
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
			populationLabel.Text = "Maximum living NPCs: " + MobileNPCSettings.MaximumAlive;
			difficultyButton.Text = "Difficulty: " + MobileNPCSettings.CurrentDifficulty;
			enabledButton.Text = "NPC spawning/restoring: " + (MobileNPCSettings.Enabled ? "ON" : "OFF");
			airdropButton.Text = "Airdrop contest: " + (MobileNPCSettings.AirdropContestEnabled ? "ON" : "OFF");
			roleButton.Text = "Manual test-spawn role: " + MobileNPCSettings.DefaultRole;
			mixButton.Text = "World population mix: " + MixLabel(MobileNPCSettings.CurrentPopulationMix);
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

		private static void onClickedBack(ISleekElement button)
		{
			close();
			MenuConfigurationUI.open();
		}
	}
}
