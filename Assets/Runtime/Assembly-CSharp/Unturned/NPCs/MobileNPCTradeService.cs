////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-authoritative barter primitive for friendly mobile NPCs. A caller supplies
	/// the currency item ID and price, allowing maps to define their own economy. UI and
	/// multiplayer replication are deliberately outside this single-player foundation.
	/// </summary>
	public static class MobileNPCTradeService
	{
		private sealed class PaymentSlice
		{
			public ushort Id;
			public byte Amount;
			public byte Quality;
			public byte[] State;
			public int Page;
			public int Index;
		}

		/// <summary>
		/// Buys one complete NPC inventory slot. The player must be alive and within
		/// interaction distance; the trader must be Friendly. Failed deliveries refund
		/// the exact currency slices and return the stock item to the trader inventory.
		/// </summary>
		public static bool TryPurchaseSlotServer(Player player, MobileNPCInventory traderInventory,
			MobileNPCHumanoidDisposition disposition, int stockSlot, ushort currencyItemId,
			int currencyCost, float maximumDistance, out string error)
		{
			error = null;
			if (!Provider.isServer)
			{
				error = "Trading is server-authoritative.";
				return false;
			}

			if (player == null || player.life == null || player.life.isDead || player.inventory == null
				|| traderInventory == null || disposition == null || disposition.CurrentRole != MobileNPCHumanoidDisposition.Role.Friendly
				|| currencyItemId == 0 || currencyCost <= 0 || currencyCost > 100000
				|| float.IsNaN(maximumDistance) || float.IsInfinity(maximumDistance) || maximumDistance <= 0f)
			{
				error = "The player, trader, currency, price or interaction range is invalid.";
				return false;
			}

			Vector3 offset = player.transform.position - traderInventory.transform.position;
			if (offset.sqrMagnitude > maximumDistance * maximumDistance)
			{
				error = "Move closer to the trader before exchanging items.";
				return false;
			}

			if (!traderInventory.TryGetItemSnapshot(stockSlot, out Item stockSnapshot) || stockSnapshot == null)
			{
				error = "That stock slot is no longer available.";
				return false;
			}

			List<PaymentSlice> payment = new List<PaymentSlice>();
			if (!TryCollectPayment(player, currencyItemId, currencyCost, payment))
			{
				error = "You do not have enough of the requested currency.";
				return false;
			}

			if (!RemovePayment(player, currencyItemId, currencyCost, payment))
			{
				RefundPayment(player, payment);
				error = "The payment could not be collected; payment was refunded.";
				return false;
			}

			if (!traderInventory.TryTakeItemServer(stockSlot, out Item stockItem) || stockItem == null)
			{
				RefundPayment(player, payment);
				error = "The stock changed during the exchange; payment was refunded.";
				return false;
			}

			if (!player.inventory.tryAddItem(stockItem, false))
			{
				traderInventory.TryStoreTransferredItem(stockItem);
				RefundPayment(player, payment);
				error = "There is not enough inventory space; payment was refunded.";
				return false;
			}

			return true;
		}

		/// <summary>
		/// Purchases one trader stock slot with the game's native experience points.
		/// Unturned has no universal vanilla cash wallet, so XP is exposed as a separate
		/// payment option rather than pretending a currency item or balance exists.
		/// </summary>
		public static bool TryPurchaseSlotWithExperienceServer(Player player, MobileNPCInventory traderInventory,
			MobileNPCHumanoidDisposition disposition, int stockSlot, uint experienceCost,
			float maximumDistance, out string error)
		{
			error = null;
			if (!Provider.isServer)
			{
				error = "A compra só pode ser concluída pelo servidor.";
				return false;
			}

			if (player == null || player.life == null || player.life.isDead || player.inventory == null
				|| player.skills == null || traderInventory == null || disposition == null
				|| disposition.CurrentRole != MobileNPCHumanoidDisposition.Role.Friendly
				|| experienceCost == 0 || experienceCost > 1000000
				|| float.IsNaN(maximumDistance) || float.IsInfinity(maximumDistance) || maximumDistance <= 0f)
			{
				error = "O jogador, mercador, preço ou alcance da compra é inválido.";
				return false;
			}

			Vector3 offset = player.transform.position - traderInventory.transform.position;
			if (offset.sqrMagnitude > maximumDistance * maximumDistance)
			{
				error = "Chegue mais perto do mercador antes de comprar.";
				return false;
			}

			if (player.skills.experience < experienceCost)
			{
				error = "Você não tem XP suficiente para essa compra.";
				return false;
			}

			if (!traderInventory.TryGetItemSnapshot(stockSlot, out Item snapshot) || snapshot == null)
			{
				error = "Esse item não está mais no estoque.";
				return false;
			}

			if (!traderInventory.TryTakeItemServer(stockSlot, out Item stockItem) || stockItem == null)
			{
				error = "O estoque mudou durante a compra.";
				return false;
			}

			player.skills.ServerModifyExperience(-((int)experienceCost));
			if (!player.inventory.tryAddItem(stockItem, false))
			{
				traderInventory.TryStoreTransferredItem(stockItem);
				player.skills.ServerModifyExperience((int)experienceCost);
				error = "Não há espaço no inventário; o XP foi devolvido.";
				return false;
			}

			return true;
		}

		// Vanilla Canadian cash item IDs: $5/$10/$20/$50/$100 notes and $1/$2 coins.
		// Currency is item-based; it does not depend on a server-side wallet.
		private static readonly ushort[] CashItemIds = { 1055, 1054, 1053, 1052, 1051, 1057, 1056 };
		private static readonly uint[] CashValues = { 100, 50, 20, 10, 5, 2, 1 };

		public static bool TryPurchaseSlotWithCashServer(Player player, MobileNPCInventory traderInventory,
			MobileNPCHumanoidDisposition disposition, int stockSlot, uint cashCost,
			float maximumDistance, out string error)
		{
			error = null;
			if (!Provider.isServer || player == null || player.life == null || player.life.isDead
				|| player.inventory == null || traderInventory == null || disposition == null
				|| disposition.CurrentRole != MobileNPCHumanoidDisposition.Role.Friendly
				|| cashCost == 0 || cashCost > 1000000
				|| float.IsNaN(maximumDistance) || float.IsInfinity(maximumDistance) || maximumDistance <= 0f)
			{
				error = "O jogador, mercador, preço ou alcance da compra é inválido.";
				return false;
			}

			Vector3 offset = player.transform.position - traderInventory.transform.position;
			if (offset.sqrMagnitude > maximumDistance * maximumDistance)
			{
				error = "Chegue mais perto do mercador antes de comprar.";
				return false;
			}
			if (!traderInventory.TryGetItemSnapshot(stockSlot, out Item stockSnapshot) || stockSnapshot == null)
			{
				error = "Esse item não está mais no estoque.";
				return false;
			}

			uint availableValue = 0;
			Items[] pages = player.inventory.items;
			for (int page = 0; page < pages.Length; ++page)
			{
				Items items = pages[page];
				if (items == null) continue;
				for (byte index = 0; index < items.getItemCount(); ++index)
				{
					ItemJar jar = items.getItem(index);
					if (jar == null || jar.item == null) continue;
					uint unitValue = GetCashItemValue(jar.item.id);
					availableValue += unitValue * jar.item.amount;
				}
			}
			if (availableValue < cashCost)
			{
				error = "Você não tem notas/moedas suficientes. Saldo em dinheiro: $" + availableValue + " CAD.";
				return false;
			}

			List<PaymentSlice> plannedPayment = new List<PaymentSlice>();
			uint remaining = cashCost;
			// Choose high denominations first. Any overpayment is returned as change.
			for (int denomination = 0; denomination < CashItemIds.Length && remaining > 0; ++denomination)
			{
				ushort itemId = CashItemIds[denomination];
				uint unitValue = CashValues[denomination];
				for (int page = pages.Length - 1; page >= 0 && remaining > 0; --page)
				{
					Items items = pages[page];
					if (items == null) continue;
					for (int index = items.getItemCount() - 1; index >= 0 && remaining > 0; --index)
					{
						ItemJar jar = items.getItem((byte)index);
						if (jar == null || jar.item == null || jar.item.id != itemId) continue;
						uint neededUnits = (remaining + unitValue - 1) / unitValue;
					byte taken = (byte)Mathf.Min(jar.item.amount, neededUnits);
					if (taken == 0) continue;
					plannedPayment.Add(new PaymentSlice
					{
						Id = jar.item.id, Amount = taken, Quality = jar.item.quality,
						State = jar.item.state != null ? (byte[])jar.item.state.Clone() : new byte[0],
						Page = page, Index = index
					});
					uint covered = taken * unitValue;
					remaining = covered >= remaining ? 0 : remaining - covered;
					}
				}
			}

			if (remaining > 0)
			{
				error = "Não foi possível separar o dinheiro para esta compra.";
				return false;
			}

			// Remove slots in descending page/index order so deleting a whole stack cannot
			// shift the location of a payment slice that has not yet been processed.
			plannedPayment.Sort((a, b) => a.Page != b.Page ? b.Page.CompareTo(a.Page) : b.Index.CompareTo(a.Index));
			List<PaymentSlice> removedPayment = new List<PaymentSlice>();
			for (int i = 0; i < plannedPayment.Count; ++i)
			{
				PaymentSlice slice = plannedPayment[i];
				Items items = player.inventory.items[slice.Page];
				if (items == null || slice.Index >= items.getItemCount())
				{
					RefundPayment(player, removedPayment);
					error = "O inventário mudou durante o pagamento; o dinheiro foi devolvido.";
					return false;
				}
				ItemJar jar = items.getItem((byte)slice.Index);
				if (jar == null || jar.item == null || jar.item.id != slice.Id || jar.item.amount < slice.Amount)
				{
					RefundPayment(player, removedPayment);
					error = "O inventário mudou durante o pagamento; o dinheiro foi devolvido.";
					return false;
				}
				removedPayment.Add(new PaymentSlice { Id = slice.Id, Amount = slice.Amount, Quality = slice.Quality, State = (byte[])slice.State.Clone() });
				if (jar.item.amount == slice.Amount)
					player.inventory.removeItem((byte)slice.Page, (byte)slice.Index);
				else
					player.inventory.updateAmount((byte)slice.Page, (byte)slice.Index, (byte)(jar.item.amount - slice.Amount));
			}

			if (!traderInventory.TryTakeItemServer(stockSlot, out Item stockItem) || stockItem == null)
			{
				RefundPayment(player, removedPayment);
				error = "O estoque mudou durante a compra; o dinheiro foi devolvido.";
				return false;
			}
			if (!player.inventory.tryAddItem(stockItem, false))
			{
				traderInventory.TryStoreTransferredItem(stockItem);
				RefundPayment(player, removedPayment);
				error = "Não há espaço no inventário; o dinheiro foi devolvido.";
				return false;
			}

			uint paidValue = 0;
			for (int i = 0; i < removedPayment.Count; ++i)
				paidValue += GetCashItemValue(removedPayment[i].Id) * removedPayment[i].Amount;
			uint change = paidValue - cashCost;
			while (change > 0)
			{
				bool granted = false;
				for (int i = 0; i < CashValues.Length; ++i)
				{
					if (CashValues[i] > change) continue;
					byte amount = (byte)Mathf.Min(255, change / CashValues[i]);
					ItemTool.tryForceGiveItem(player, CashItemIds[i], amount);
					change -= amount * CashValues[i];
					granted = true;
					break;
				}
				if (!granted) break;
			}
			return true;
		}

		public static uint GetCashItemValue(ushort itemId)
		{
			for (int i = 0; i < CashItemIds.Length; ++i)
				if (CashItemIds[i] == itemId)
					return CashValues[i];
			return 0;
		}

		private static bool TryCollectPayment(Player player, ushort currencyItemId, int currencyCost,
			List<PaymentSlice> payment)
		{
			int available = 0;
			Items[] pages = player.inventory.items;
			for (int page = 0; page < pages.Length; ++page)
			{
				Items items = pages[page];
				if (items == null) continue;
				for (byte index = 0; index < items.getItemCount(); ++index)
				{
					ItemJar jar = items.getItem(index);
					if (jar == null || jar.item == null || jar.item.id != currencyItemId) continue;
					available += jar.item.amount;
				}
			}
			return available >= currencyCost;
		}

		private static bool RemovePayment(Player player, ushort currencyItemId, int currencyCost,
			List<PaymentSlice> payment)
		{
			int remaining = currencyCost;
			Items[] pages = player.inventory.items;
			// Walk backwards so removing a whole stack never shifts an unvisited index.
			for (int page = pages.Length - 1; page >= 0 && remaining > 0; --page)
			{
				Items items = pages[page];
				if (items == null) continue;
				for (int index = items.getItemCount() - 1; index >= 0 && remaining > 0; --index)
				{
					ItemJar jar = items.getItem((byte)index);
					if (jar == null || jar.item == null || jar.item.id != currencyItemId) continue;

					Item stored = jar.item;
					byte taken = (byte)Mathf.Min(remaining, stored.amount);
					payment.Add(new PaymentSlice
					{
						Id = stored.id,
						Amount = taken,
						Quality = stored.quality,
						State = stored.state != null ? (byte[])stored.state.Clone() : new byte[0]
					});

					if (taken >= stored.amount)
						player.inventory.removeItem((byte)page, (byte)index);
					else
						player.inventory.updateAmount((byte)page, (byte)index, (byte)(stored.amount - taken));

					remaining -= taken;
				}
			}

			return remaining == 0;
		}

		private static void RefundPayment(Player player, List<PaymentSlice> payment)
		{
			for (int i = 0; i < payment.Count; ++i)
			{
				PaymentSlice slice = payment[i];
				Item refund = new Item(slice.Id, slice.Amount, slice.Quality,
					slice.State != null ? (byte[])slice.State.Clone() : new byte[0]);
				if (!player.inventory.tryAddItem(refund, false))
				{
					// The consumed currency freed at least as much inventory space as it used.
					// forceAddItem is the vanilla fallback if unusual item dimensions prevent refund placement.
					player.inventory.forceAddItem(refund, false);
				}
			}
		}
	}
}