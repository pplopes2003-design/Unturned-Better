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