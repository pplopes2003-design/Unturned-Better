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
			ChooseTradePayment,
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
		private bool tradeUsesExperience;
		private bool tradeUsesCash;
		private uint pendingExperienceCost;
		private string lastPrompt;
		private float nextPromptRefreshTime;

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
				ShowPrompt( "Conversa encerrada.");
				return;
			}

			if (state != MenuState.Closed && !string.IsNullOrEmpty(lastPrompt) && Time.time >= nextPromptRefreshTime)
			{
				PlayerUI.message(EPlayerMessage.NPC_CUSTOM, lastPrompt);
				nextPromptRefreshTime = Time.time + 1.25f;
			}

			if (state == MenuState.ChooseTradePayment)
			{
				if (IsNumberKeyDown(1))
				{
					tradeUsesCash = true;
					tradeUsesExperience = false;
					OpenTradeMenu("Pagamento selecionado: notas e moedas canadenses.");
				}
				else if (IsNumberKeyDown(2))
				{
					tradeUsesCash = false;
					tradeUsesExperience = false;
					OpenTradeMenu("Pagamento selecionado: troca de item.");
				}
				else if (IsNumberKeyDown(3))
				{
					tradeUsesCash = false;
					tradeUsesExperience = true;
					OpenTradeMenu("Pagamento selecionado: pontos de experiência (XP).");
				}
				return;
			}

			if (state == MenuState.ConfirmPurchase)
			{
				if (IsNumberKeyDown(1))
					ConfirmPurchase();
				else if (IsNumberKeyDown(2))
				{
					pendingStockSlot = -1;
					pendingCurrencyItemId = 0;
					pendingExperienceCost = 0;
					state = MenuState.TradeStock;
					OpenTradeMenu();
				}
				return;
			}

			if (state == MenuState.ConfirmAlliance)
			{
				if (IsNumberKeyDown(1))
					ConfirmAllianceOffer();
				else if (IsNumberKeyDown(2))
					ShowMainMenu();
				return;
			}

			if (IsNumberKeyDown(1))
			{
				if (state == MenuState.Main)
					ShowTradePaymentMenu();
				else if (state == MenuState.TradeStock)
					SelectStockSlot(0);
			}
			else if (IsNumberKeyDown(2))
			{
				if (state == MenuState.Main)
					TryOfferAlliance();
				else if (state == MenuState.TradeStock)
					SelectStockSlot(1);
			}
			else if (IsNumberKeyDown(3))
			{
				if (state == MenuState.Main)
					ShowConversation();
				else if (state == MenuState.TradeStock)
					SelectStockSlot(2);
			}
			else if (IsNumberKeyDown(4))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(3);
			}
			else if (IsNumberKeyDown(5))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(4);
			}
			else if (IsNumberKeyDown(6))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(5);
			}
			else if (IsNumberKeyDown(7))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(6);
			}
			else if (IsNumberKeyDown(8))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(7);
			}
			else if (IsNumberKeyDown(9))
			{
				if (state == MenuState.TradeStock) SelectStockSlot(8);
			}
		}

		private void ShowMainMenu(string notice = null)
		{
			if (activeNPC == null)
				return;
			state = MenuState.Main;

			if (activeNPC.CurrentRole == MobileNPCHumanoidDisposition.Role.Hostile)
			{
				ShowPrompt(activeNPC.DisplayName + ": não aceito conversa. Abaixe-se ou afaste-se.");
				Clear();
				return;
			}

			string npcName = activeNPC.DisplayName;
			string greeting = activeNPC.CurrentRole == MobileNPCHumanoidDisposition.Role.Friendly
				? npcName + ": sou comerciante e tenho suprimentos para trocar."
				: npcName + ": sou sobrevivente; podemos negociar uma aliança.";
			ShowPrompt(
				(string.IsNullOrEmpty(notice) ? "" : notice + "\n")
				+ greeting + "\nOpções (teclado numérico):\n1. Comércio (notas/moedas, XP ou troca)\n2. Propor aliança (oferecer 1 item)\n3. Conversar\nESC. Fechar");
		}

		private void ShowTradePaymentMenu()
		{
			if (activeNPC == null || activeNPC.CurrentRole != MobileNPCHumanoidDisposition.Role.Friendly)
			{
				ShowPrompt("Este sobrevivente ainda não é comerciante. Proponha uma aliança primeiro.");
				return;
			}

			state = MenuState.ChooseTradePayment;
			ShowPrompt(activeNPC.DisplayName + " — escolha como pagar:\n"
				+ "1. Notas e moedas ($ CAD)\n"
				+ "2. Trocar um item do inventário\n"
				+ "3. Pontos de experiência (XP)\n"
				+ "Use o teclado numérico. ESC para fechar.");
		}

		private void OpenTradeMenu(string notice = null)
		{
			state = MenuState.Main;
			if (activeNPC == null || activeNPC.CurrentRole != MobileNPCHumanoidDisposition.Role.Friendly)
			{
				ShowPrompt( "Este sobrevivente ainda não é um mercador aliado. Proponha uma aliança primeiro.");
				return;
			}

			MobileNPCInventory inventory = activeNPC.GetComponent<MobileNPCInventory>();
			if (inventory == null || inventory.UsedSlots == 0)
			{
				ShowPrompt(
					(string.IsNullOrEmpty(notice) ? "" : notice + "\n")
					+ "Mercador: meu estoque está vazio. Volte quando eu tiver encontrado suprimentos.");
				return;
			}

			ItemAsset heldAsset = activePlayer != null && activePlayer.equipment != null
				? activePlayer.equipment.asset as ItemAsset : null;
			StringBuilder message = new StringBuilder();
			if (!string.IsNullOrEmpty(notice))
				message.Append(notice).Append("\n");
			message.Append(activeNPC.DisplayName).Append(" — estoque.\n");
			if (tradeUsesCash)
				message.Append("Pagamento: notas e moedas ($ CAD); o troco é devolvido.\n");
			else if (tradeUsesExperience)
				message.Append("Pagamento: XP do jogo; preço indicado em cada item.\n");
			else if (heldAsset == null)
				message.Append("AVISO: segure o item que deseja oferecer antes de escolher.\n");
			else
				message.Append("Oferta atual: ").Append(heldAsset.itemName).Append(" x1 por lote.\n");

			int shown = Mathf.Min(9, inventory.UsedSlots);
			for (int i = 0; i < shown; ++i)
			{
				if (!inventory.TryGetItemSnapshot(i, out Item item) || item == null)
					continue;
				ItemAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemAsset;
				message.Append(i + 1).Append(". ")
					.Append(asset != null ? asset.itemName : "Item " + item.id)
					.Append(" (x").Append(item.amount).Append(")");
				if (tradeUsesCash)
					message.Append(" — $").Append(GetCashPrice(item)).Append(" CAD");
				else if (tradeUsesExperience)
					message.Append(" — ").Append(GetExperiencePrice(item)).Append(" XP");
				message.Append("\n");
			}
			message.Append("Use o teclado numérico (1-9) para escolher o item. ESC para fechar.");
			state = MenuState.TradeStock;
			ShowPrompt( message.ToString());
		}

		private void SelectStockSlot(int stockSlot)
		{
			if (activeNPC == null || activePlayer == null)
			{
				Clear();
				return;
			}

			MobileNPCInventory inventory = activeNPC.GetComponent<MobileNPCInventory>();
			if (inventory == null || !inventory.TryGetItemSnapshot(stockSlot, out Item stock) || stock == null)
			{
				OpenTradeMenu("Esse item não está mais no estoque. Escolha outro.");
				return;
			}

			pendingStockSlot = stockSlot;
			if (tradeUsesCash)
			{
				pendingCurrencyItemId = 0;
				pendingExperienceCost = GetCashPrice(stock);
				state = MenuState.ConfirmPurchase;
				ItemAsset cashStockAsset = Assets.find(EAssetType.ITEM, stock.id) as ItemAsset;
				ShowPrompt("Confirmar compra com dinheiro?\nVocê paga: $" + pendingExperienceCost + " CAD"
					+ "\nVocê recebe: " + stock.amount + "x " + (cashStockAsset != null ? cashStockAsset.itemName : "Item " + stock.id)
					+ "\nO sistema selecionará notas/moedas e devolverá o troco.\nTeclado numérico: 1. Confirmar  |  2. Cancelar");
				return;
			}

			if (tradeUsesExperience)
			{
				pendingCurrencyItemId = 0;
				pendingExperienceCost = GetExperiencePrice(stock);
				state = MenuState.ConfirmPurchase;
				ItemAsset experienceStockAsset = Assets.find(EAssetType.ITEM, stock.id) as ItemAsset;
				ShowPrompt("Confirmar compra com XP?\nVocê paga: " + pendingExperienceCost + " XP"
					+ "\nVocê recebe: " + stock.amount + "x " + (experienceStockAsset != null ? experienceStockAsset.itemName : "Item " + stock.id)
					+ "\nTeclado numérico: 1. Confirmar  |  2. Cancelar");
				return;
			}

			ItemAsset heldAsset = activePlayer.equipment != null ? activePlayer.equipment.asset as ItemAsset : null;
			if (heldAsset == null || heldAsset.id == 0)
			{
				ShowPrompt("Segure o item que deseja oferecer como pagamento e tente novamente.");
				return;
			}
			if (stock.id == heldAsset.id)
			{
				ShowPrompt("Escolha um item diferente do que está oferecendo.");
				return;
			}

			ItemAsset stockAsset = Assets.find(EAssetType.ITEM, stock.id) as ItemAsset;
			pendingCurrencyItemId = heldAsset.id;
			pendingExperienceCost = 0;
			state = MenuState.ConfirmPurchase;
			ShowPrompt("Confirmar troca?\nVocê entrega: 1 " + heldAsset.itemName
				+ "\nVocê recebe: " + stock.amount + "x " + (stockAsset != null ? stockAsset.itemName : "Item " + stock.id)
				+ "\nTeclado numérico: 1. Confirmar  |  2. Cancelar");
		}

		private static uint GetCashPrice(Item stock)
		{
			// Prototype pricing per complete stock slot; tune item categories and rarity later.
			return stock == null ? 10u : (uint)Mathf.Clamp(10 + stock.amount * 5, 10, 500);
		}

		private static uint GetExperiencePrice(Item stock)
		{
			return stock == null ? 100u : (uint)Mathf.Clamp(50 + stock.amount * 10, 50, 2600);
		}

		private void ConfirmPurchase()
		{
			if (activeNPC == null || activePlayer == null || pendingStockSlot < 0)
			{
				Clear();
				return;
			}

			if (tradeUsesCash)
			{
				MobileNPCInventory cashInventory = activeNPC.GetComponent<MobileNPCInventory>();
				uint price = pendingExperienceCost;
				bool purchased = MobileNPCTradeService.TryPurchaseSlotWithCashServer(activePlayer,
					cashInventory, activeNPC, pendingStockSlot, price, 4f, out string cashError);
				pendingStockSlot = -1;
				pendingExperienceCost = 0;
				state = MenuState.TradeStock;
				OpenTradeMenu(purchased
					? "Compra concluída por $" + price + " CAD."
					: "Compra não concluída: " + cashError);
				return;
			}

			if (tradeUsesExperience)
			{
				MobileNPCInventory experienceInventory = activeNPC.GetComponent<MobileNPCInventory>();
				uint price = pendingExperienceCost;
				bool purchased = MobileNPCTradeService.TryPurchaseSlotWithExperienceServer(activePlayer,
					experienceInventory, activeNPC, pendingStockSlot, price, 4f, out string experienceError);
				pendingStockSlot = -1;
				pendingExperienceCost = 0;
				state = MenuState.TradeStock;
				OpenTradeMenu(purchased
					? "Compra concluída; foram gastos " + price + " XP."
					: "Compra não concluída: " + experienceError);
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
				ShowPrompt( "Para propor aliança, segure o item que deseja oferecer como pagamento.");
				return;
			}

			pendingCurrencyItemId = heldAsset.id;
			state = MenuState.ConfirmAlliance;
			ShowPrompt(
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
				ShowPrompt(
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
			ShowPrompt(
				(activeNPC.CurrentRole == MobileNPCHumanoidDisposition.Role.Friendly
					? activeNPC.DisplayName + ": procuro comida, água, munição e materiais. Posso trocar suprimentos e aceitar uma oferta de aliança."
					: activeNPC.DisplayName + ": não quero briga. Se trouxer um suprimento, podemos firmar uma aliança; eu acompanho você fora de combate e enfrento saqueadores e zumbis que detectar.")
				+ "\nOpções: 1. Comércio  |  2. Aliança  |  3. Conversar  |  ESC. Fechar");
			state = MenuState.Main;
		}

		private static bool IsNumberKeyDown(int number)
		{
			// Support both the numeric keypad and the number row. This also avoids
			// leaving the player without a usable input path on compact keyboards.
			switch (number)
			{
				case 1: return Input.GetKeyDown(KeyCode.Keypad1) || Input.GetKeyDown(KeyCode.Alpha1);
				case 2: return Input.GetKeyDown(KeyCode.Keypad2) || Input.GetKeyDown(KeyCode.Alpha2);
				case 3: return Input.GetKeyDown(KeyCode.Keypad3) || Input.GetKeyDown(KeyCode.Alpha3);
				case 4: return Input.GetKeyDown(KeyCode.Keypad4) || Input.GetKeyDown(KeyCode.Alpha4);
				case 5: return Input.GetKeyDown(KeyCode.Keypad5) || Input.GetKeyDown(KeyCode.Alpha5);
				case 6: return Input.GetKeyDown(KeyCode.Keypad6) || Input.GetKeyDown(KeyCode.Alpha6);
				case 7: return Input.GetKeyDown(KeyCode.Keypad7) || Input.GetKeyDown(KeyCode.Alpha7);
				case 8: return Input.GetKeyDown(KeyCode.Keypad8) || Input.GetKeyDown(KeyCode.Alpha8);
				case 9: return Input.GetKeyDown(KeyCode.Keypad9) || Input.GetKeyDown(KeyCode.Alpha9);
				default: return false;
			}
		}

		private void ShowPrompt(string message)
		{
			lastPrompt = message;
			nextPromptRefreshTime = Time.time + 1.25f;
			PlayerUI.message(EPlayerMessage.NPC_CUSTOM, message);
		}

		private void Clear()
		{
			activePlayer = null;
			activeNPC = null;
			pendingStockSlot = -1;
			pendingCurrencyItemId = 0;
			pendingExperienceCost = 0;
			lastPrompt = null;
			tradeUsesExperience = false;
			tradeUsesCash = false;
			state = MenuState.Closed;
		}
	}
}