////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Text;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Small keyboard-driven interaction flow for single-player mobile NPCs.
	/// Uses the native custom-message surface rather than creating a parallel UI system.
	/// While trading, hold the item you want to offer, then select a stock slot with 1-9.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCInteractionController : MonoBehaviour
	{
		private enum MenuState
		{
			Closed,
			Main,
			TradeStock,
			ConfirmPurchase,
			ConfirmAlliance
		}

		private static MobileNPCInteractionController instance;
		private Player activePlayer;
		private MobileNPCHumanoidDisposition activeNPC;
		private MenuState state;
		private int pendingStockSlot = -1;
		private ushort pendingCurrencyItemId;

		private void Awake()
		{
			instance = this;
		}

		private void OnDestroy()
		{
			if (instance == this)
				instance = null;
			Clear();
		}

		public static void Open(Player player, MobileNPCHumanoidDisposition npc)
		{
			if (instance == null || player == null || npc == null)
			{
				if (player != null)
					PlayerUI.message(EPlayerMessage.NPC_CUSTOM, "A interface de interação dos sobreviventes não está disponível.");
				return;
			}

			instance.activePlayer = player;
			instance.activeNPC = npc;
			instance.state = MenuState.Main;
			instance.ShowMainMenu();
		}

		private void Update()
		{
			if (state == MenuState.Closed)
				return;

			if (!Provider.isServer || activePlayer == null || activeNPC == null
				|| !activePlayer.life.IsAlive || activeNPC.GetComponent<MobileNPCHumanoidVitals>() is MobileNPCHumanoidVitals vitals && vitals.IsDead
				|| (activePlayer.transform.position - activeNPC.transform.position).sqrMagnitude > 16f)
			{
				Clear();
				return;
			}

			if (Input.GetKeyDown(KeyCode.Escape))
			{
				Clear();
				PlayerUI.message(EPlayerMessage.NPC_CUSTOM, "Conversa encerrada.");
				return;
			}

			if (state == MenuState.ConfirmPurchase)
			{
				if (Input.GetKeyDown(KeyCode.Keypad1))
					ConfirmPurchase();
				else if (Input.GetKeyDown(KeyCode.Keypad2))
				{
					state = MenuState.TradeStock;
					OpenTradeMenu();
				}
				return;
			}

			if (state == MenuState.ConfirmAlliance)
			{
				if (Input.GetKeyDown(KeyCode.Keypad1))
					ConfirmAllianceOffer();
				else if (Input.GetKeyDown(KeyCode.Keypad2))
					ShowMainMenu();
				return;
			}

			if (Input.GetKeyDown(KeyCode.Keypad1))
			{
				if (state == MenuState.Main)
					OpenTradeMenu();
				else if (state == MenuState.TradeStock)
					SelectStockSlot(0);
			}
			else if (Input.GetKeyDown(KeyCode.Keypad2))
			{
				if (state == MenuState.Main)
					TryOfferAlliance();
				else if (state == MenuState.TradeStock)
					SelectStockSlot(1);
			}
			else if (Input.GetKeyDown(KeyCode.Keypad3))
			{
				if (state == MenuState.Main)
					ShowConversation();
				else if (state == MenuState.TradeStock)
					SelectStockSlot(2);
			}
			else if (Input.GetKeyDown(KeyCode.Keypad4))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(3);
			}
			else if (Input.GetKeyDown(KeyCode.Keypad5))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(4);
			}
			else if (Input.GetKeyDown(KeyCode.Keypad6))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(5);
			}
			else if (Input.GetKeyDown(KeyCode.Keypad7))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(6);
			}
			else if (Input.GetKeyDown(KeyCode.Keypad8))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(7);
			}
			else if (Input.GetKeyDown(KeyCode.Keypad9))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(8);
			}
		}

		private void ShowMainMenu(string notice = null)
		{
			if (activeNPC == null)
				return;

			if (activeNPC.CurrentRole == MobileNPCHumanoidDisposition.Role.Hostile)
			{
				PlayerUI.message(EPlayerMessage.NPC_CUSTOM, "Hostil: não aceita conversa. Abaixe-se ou afaste-se.");
				Clear();
				return;
			}

			string greeting = activeNPC.CurrentRole == MobileNPCHumanoidDisposition.Role.Friendly
				? "Mercador: tenho suprimentos para trocar."
				: "Sobrevivente: ainda não somos aliados; posso ouvir uma proposta.";
			PlayerUI.message(EPlayerMessage.NPC_CUSTOM,
				(string.IsNullOrEmpty(notice) ? "" : notice + "\n")
				+ greeting + "\nTeclado numérico:\n1. Comércio (1 item segurado por um lote do estoque)\n2. Propor aliança (entregue 1 item segurado)\n3. Conversar\nESC. Fechar");
		}

		private void OpenTradeMenu(string notice = null)
		{
			state = MenuState.Main;
			if (activeNPC == null || activeNPC.CurrentRole != MobileNPCHumanoidDisposition.Role.Friendly)
			{
				PlayerUI.message(EPlayerMessage.NPC_CUSTOM, "Este sobrevivente ainda não é um mercador aliado. Proponha uma aliança primeiro.");
				return;
			}

			MobileNPCInventory inventory = activeNPC.GetComponent<MobileNPCInventory>();
			if (inventory == null || inventory.UsedSlots == 0)
			{
				PlayerUI.message(EPlayerMessage.NPC_CUSTOM,
					(string.IsNullOrEmpty(notice) ? "" : notice + "\n")
					+ "Mercador: meu estoque está vazio. Volte quando eu tiver encontrado suprimentos.");
				return;
			}

			ItemAsset heldAsset = activePlayer != null && activePlayer.equipment != null
				? activePlayer.equipment.asset as ItemAsset : null;
			StringBuilder message = new StringBuilder();
			if (!string.IsNullOrEmpty(notice))
				message.Append(notice).Append("\n");
			message.Append("Mercador — estoque (1 item segurado por um lote completo do estoque).\n");
			if (heldAsset == null)
				message.Append("AVISO: segure o item que deseja oferecer antes de escolher.\n");
			else
				message.Append("Oferta atual: ").Append(heldAsset.itemName).Append(" x1.\n");

			int shown = Mathf.Min(9, inventory.UsedSlots);
			for (int i = 0; i < shown; ++i)
			{
				if (!inventory.TryGetItemSnapshot(i, out Item item) || item == null)
					continue;
				ItemAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemAsset;
				message.Append(i + 1).Append(". ")
					.Append(asset != null ? asset.itemName : "Item " + item.id)
					.Append(" (x").Append(item.amount).Append(")\n");
			}
			message.Append("Use o teclado numérico (1-9) para escolher o item. ESC para fechar.");
			state = MenuState.TradeStock;
			PlayerUI.message(EPlayerMessage.NPC_CUSTOM, message.ToString());
		}

		private void SelectStockSlot(int stockSlot)
		{
			if (activeNPC == null || activePlayer == null)
			{
				Clear();
				return;
			}

			MobileNPCInventory inventory = activeNPC.GetComponent<MobileNPCInventory>();
			ItemAsset heldAsset = activePlayer.equipment != null ? activePlayer.equipment.asset as ItemAsset : null;
			if (heldAsset == null || heldAsset.id == 0)
			{
				PlayerUI.message(EPlayerMessage.NPC_CUSTOM, "Segure o item que deseja oferecer como pagamento e tente novamente.");
				return;
			}
			if (inventory == null || !inventory.TryGetItemSnapshot(stockSlot, out Item stock) || stock == null)
			{
				OpenTradeMenu("Esse item não está mais no estoque. Escolha outro.");
				return;
			}
			if (stock.id == heldAsset.id)
			{
				PlayerUI.message(EPlayerMessage.NPC_CUSTOM, "Escolha um item diferente do que está oferecendo.");
				return;
			}

			ItemAsset stockAsset = Assets.find(EAssetType.ITEM, stock.id) as ItemAsset;
			pendingStockSlot = stockSlot;
			pendingCurrencyItemId = heldAsset.id;
			state = MenuState.ConfirmPurchase;
			PlayerUI.message(EPlayerMessage.NPC_CUSTOM,
				"Confirmar troca?\nVocê entrega: 1 " + heldAsset.itemName
				+ "\nVocê recebe: " + stock.amount + "x " + (stockAsset != null ? stockAsset.itemName : "Item " + stock.id)
				+ "\nTeclado numérico: 1. Confirmar  |  2. Cancelar");
		}

		private void ConfirmPurchase()
		{
			if (activeNPC == null || activePlayer == null || pendingStockSlot < 0)
			{
				Clear();
				return;
			}

			ItemAsset heldAsset = activePlayer.equipment != null ? activePlayer.equipment.asset as ItemAsset : null;
			if (heldAsset == null || heldAsset.id != pendingCurrencyItemId)
			{
				pendingStockSlot = -1;
				pendingCurrencyItemId = 0;
				state = MenuState.TradeStock;
				OpenTradeMenu("Troca cancelada: o item de pagamento mudou. Segure o item e tente novamente.");
				return;
			}

			MobileNPCInventory inventory = activeNPC.GetComponent<MobileNPCInventory>();
			if (MobileNPCTradeService.TryPurchaseSlotServer(activePlayer, inventory, activeNPC,
				pendingStockSlot, pendingCurrencyItemId, 1, 4f, out string error))
			{
				pendingStockSlot = -1;
				pendingCurrencyItemId = 0;
				state = MenuState.TradeStock;
				OpenTradeMenu("Troca concluída. Você entregou 1 " + heldAsset.itemName + ".");
			}
			else
			{
				pendingStockSlot = -1;
				pendingCurrencyItemId = 0;
				state = MenuState.TradeStock;
				OpenTradeMenu("Troca não concluída: " + error);
			}
		}

		private void TryOfferAlliance()
		{
			if (activeNPC == null || activePlayer == null)
			{
				Clear();
				return;
			}

			ItemAsset heldAsset = activePlayer.equipment != null ? activePlayer.equipment.asset as ItemAsset : null;
			if (heldAsset == null || heldAsset.id == 0)
			{
				PlayerUI.message(EPlayerMessage.NPC_CUSTOM, "Para propor aliança, segure o item que deseja oferecer como pagamento.");
				return;
			}

			pendingCurrencyItemId = heldAsset.id;
			state = MenuState.ConfirmAlliance;
			PlayerUI.message(EPlayerMessage.NPC_CUSTOM,
				"Proposta de aliança: entregar 1 " + heldAsset.itemName
				+ " para que o sobrevivente se una à facção dos sobreviventes e lute contra saqueadores hostis.\n"
				+ "A aliança ativa escolta fora de combate e defesa contra saqueadores e zumbis visíveis.\nTeclado numérico: 1. Confirmar oferta  |  2. Cancelar");
		}

		private void ConfirmAllianceOffer()
		{
			if (activeNPC == null || activePlayer == null || pendingCurrencyItemId == 0)
			{
				Clear();
				return;
			}

			ItemAsset heldAsset = activePlayer.equipment != null ? activePlayer.equipment.asset as ItemAsset : null;
			if (heldAsset == null || heldAsset.id != pendingCurrencyItemId)
			{
				pendingCurrencyItemId = 0;
				ShowMainMenu("Oferta cancelada: o item segurado mudou. Segure o item e faça a proposta novamente.");
				return;
			}

			if (MobileNPCAllianceService.TryOfferAllianceServer(activePlayer, activeNPC, pendingCurrencyItemId, 1, 4f, out string error))
			{
				MobileNPCCompanionContract contract = activeNPC.GetComponent<MobileNPCCompanionContract>();
				bool escortAssigned = contract != null && contract.AssignServer(activePlayer);
				PlayerUI.message(EPlayerMessage.NPC_CUSTOM,
					escortAssigned
						? "Aliança aceita. Agora pertenço aos sobreviventes, vou acompanhar você quando estiver fora de combate e enfrentarei saqueadores hostis que detectar."
						: "Aliança aceita. Agora pertenço aos sobreviventes e enfrentarei saqueadores hostis que detectar, mas não consegui iniciar a escolta.");
				Clear();
			}
			else
			{
				pendingCurrencyItemId = 0;
				ShowMainMenu("Aliança recusada: " + error);
			}
		}

		private void ShowConversation()
		{
			if (activeNPC == null)
				return;
			PlayerUI.message(EPlayerMessage.NPC_CUSTOM,
				activeNPC.CurrentRole == MobileNPCHumanoidDisposition.Role.Friendly
					? "Mercador: procuro comida, água, munição e materiais. Posso trocar suprimentos e aceitar uma oferta de aliança."
					: "Sobrevivente: não quero briga. Se trouxer um suprimento, podemos firmar uma aliança; eu acompanho você fora de combate e enfrento saqueadores e zumbis que detectar.");
			state = MenuState.Main;
		}

		private void Clear()
		{
			activePlayer = null;
			activeNPC = null;
			pendingStockSlot = -1;
			pendingCurrencyItemId = 0;
			state = MenuState.Closed;
		}
	}
}