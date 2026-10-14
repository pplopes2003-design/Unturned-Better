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
		private Vector2 menuScroll;

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
					Debug.LogWarning("[MobileNPCInteractionController] Interaction requested before the mobile NPC controller was installed.");
				return;
			}

			instance.Clear();
			instance.activePlayer = player;
			instance.activeNPC = npc;
			instance.state = MenuState.Main;
			instance.ShowMainMenu();
			Debug.Log("[MobileNPCInteractionController] Dialogue opened for "
				+ npc.DisplayName + " (single-player local interaction).");
		}

		private void Update()
		{
			if (state == MenuState.Closed)
				return;

			if (!IsSessionValid())
			{
				Clear();
				return;
			}

			if (Input.GetKeyDown(KeyCode.Escape))
			{
				Clear();
				return;
			}

			for (int number = 1; number <= 9; ++number)
			{
				if (IsNumberKeyDown(number))
				{
					ChooseOption(number);
					break;
				}
			}
		}

		private bool IsSessionValid()
		{
			if (!Provider.isServer || activePlayer == null || activeNPC == null
				|| activePlayer.life == null || !activePlayer.life.IsAlive
				|| !activeNPC.gameObject.activeInHierarchy
				|| (activePlayer.transform.position - activeNPC.transform.position).sqrMagnitude > 16f)
				return false;
			MobileNPCHumanoidVitals vitals = activeNPC.GetComponent<MobileNPCHumanoidVitals>();
			return vitals == null || !vitals.IsDead;
		}

		private void ChooseOption(int number)
		{
			if (state == MenuState.Closed || !IsSessionValid())
				return;
			if (state == MenuState.ChooseTradePayment)
			{
				if (number == 1 || number == 2 || number == 3)
				{
					tradeUsesCash = number == 1;
					tradeUsesExperience = number == 3;
					OpenTradeMenu(number == 1 ? "Pagamento selecionado: notas e moedas canadenses."
						: number == 2 ? "Pagamento selecionado: troca de item."
						: "Pagamento selecionado: pontos de experiência (XP).");
				}
				return;
			}
			if (state == MenuState.ConfirmPurchase)
			{
				if (number == 1)
					ConfirmPurchase();
				else if (number == 2)
				{
					pendingStockSlot = -1;
					pendingCurrencyItemId = 0;
					pendingExperienceCost = 0;
					OpenTradeMenu();
				}
				return;
			}
			if (state == MenuState.ConfirmAlliance)
			{
				if (number == 1)
					ConfirmAllianceOffer();
				else if (number == 2)
					ShowMainMenu();
				return;
			}
			if (state == MenuState.TradeStock)
			{
				if (number >= 1 && number <= 9)
					SelectStockSlot(number - 1);
				return;
			}
			if (state == MenuState.Main)
			{
				if (activeNPC == null || activeNPC.CurrentRole == MobileNPCHumanoidDisposition.Role.Hostile)
					return;
				switch (number)
				{
					case 1: ShowTradePaymentMenu(); break;
					case 2: TryOfferAlliance(); break;
					case 3: ShowConversation(); break;
				}
			}
		}

#if !DEDICATED_SERVER
		// A persistent screen panel, not NPC_CUSTOM (which belongs to native NPC UI).
		// No native HUD visibility, focus or cursor state is modified by this flow.
		private void OnGUI()
		{
			if (state == MenuState.Closed || !IsSessionValid())
				return;

			// The Unturned HUD and other Unity IMGUI handlers can render later
			// than a default-depth panel. Negative IMGUI depth keeps this
			// local-only dialog above standard overlapping GUI windows.
			// Numeric keys and Escape remain usable with a locked FPS cursor.
			GUI.depth = -10000;
			float width = Mathf.Min(540f, Screen.width - 24f);
			if (width <= 120f)
				return;
			float height = Mathf.Min(420f, Screen.height - 24f);
			Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 18f, width, height);
			GUI.Box(panel, GUIContent.none);

			GUIStyle messageStyle = new GUIStyle(GUI.skin.label);
			messageStyle.wordWrap = true;
			messageStyle.richText = false;
			messageStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 52f), 12, 20);
			messageStyle.normal.textColor = Color.white;

			Rect textArea = new Rect(panel.x + 16f, panel.y + 14f, panel.width - 32f, panel.height - 77f);
			string message = lastPrompt ?? "";
			float textHeight = Mathf.Max(textArea.height, messageStyle.CalcHeight(new GUIContent(message), textArea.width - 18f) + 8f);
			menuScroll = GUI.BeginScrollView(textArea, menuScroll,
				new Rect(0f, 0f, textArea.width - 18f, textHeight));
			GUI.Label(new Rect(0f, 0f, textArea.width - 18f, textHeight), message, messageStyle);
			GUI.EndScrollView();

			float buttonY = panel.yMax - 50f;
			int count = state == MenuState.TradeStock ? 9 : state == MenuState.Main || state == MenuState.ChooseTradePayment ? 3 : 2;
			// Reserve space for Close so stock slots 7-9 are never covered.
			// Previously nine 54px buttons extended underneath the Close button.
			float optionsWidth = Mathf.Max(0f, panel.width - 116f);
			float buttonWidth = Mathf.Min(54f, (optionsWidth - 4f * (count - 1)) / count);
			for (int option = 1; option <= count; ++option)
			{
				if (GUI.Button(new Rect(panel.x + 16f + (option - 1) * (buttonWidth + 4f),
					buttonY, buttonWidth, 32f), option.ToString()))
					ChooseOption(option);
			}
			if (GUI.Button(new Rect(panel.xMax - 82f, buttonY, 66f, 32f), "Fechar"))
				Clear();
		}
#endif

		private void ShowMainMenu(string notice = null)
		{
			if (activeNPC == null)
				return;
			state = MenuState.Main;

			if (activeNPC.CurrentRole == MobileNPCHumanoidDisposition.Role.Hostile)
			{
				// Keep the refusal visible rather than immediately destroying the UI.
				ShowPrompt(activeNPC.DisplayName + ": não aceito conversa. Afaste-se. ESC para fechar.");
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
				ShowMainMenu(
					escortAssigned
						? "Aliança aceita. Vou acompanhar você fora de combate e enfrentar saqueadores hostis."
						: "Aliança aceita, mas não consegui iniciar a escolta.");
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
			menuScroll = Vector2.zero;
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
			menuScroll = Vector2.zero;
		}
	}
}