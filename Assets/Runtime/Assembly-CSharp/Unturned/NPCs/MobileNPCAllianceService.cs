////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-authoritative alliance offer backend. The caller chooses the exact item
	/// and quantity requested in dialogue. Accepted offers turn a non-hostile survivor
	/// into a Survivors-faction ally, so it can engage hostile mobile NPC factions.
	/// Player-facing choice UI and zombie-target combat are separate integration steps.
	/// </summary>
	public static class MobileNPCAllianceService
	{
		private sealed class PaymentSlice
		{
			public ushort Id;
			public byte Amount;
			public byte Quality;
			public byte[] State;
		}

		public static bool TryOfferAllianceServer(Player player, MobileNPCHumanoidDisposition npc,
			ushort paymentItemId, int paymentAmount, float maximumDistance, out string error)
		{
			error = null;
			if (!Provider.isServer)
			{
				error = "A aliança só pode ser concluída pelo servidor.";
				return false;
			}

			if (player == null || player.life == null || player.life.isDead || player.inventory == null
				|| npc == null || npc.CurrentRole == MobileNPCHumanoidDisposition.Role.Hostile
				|| npc.GetComponent<MobileNPCHumanoidVitals>() is MobileNPCHumanoidVitals vitals && vitals.IsDead
				|| paymentItemId == 0 || paymentAmount < 1 || paymentAmount > 255
				|| float.IsNaN(maximumDistance) || float.IsInfinity(maximumDistance) || maximumDistance <= 0f)
			{
				error = "A proposta, o NPC, o item de pagamento ou a distância são inválidos.";
				return false;
			}

			Vector3 offset = player.transform.position - npc.transform.position;
			if (offset.sqrMagnitude > maximumDistance * maximumDistance)
			{
				error = "Chegue mais perto para negociar a aliança.";
				return false;
			}

			List<PaymentSlice> payment = new List<PaymentSlice>();
			if (!CollectPayment(player, paymentItemId, paymentAmount, payment))
			{
				error = "Você não possui a quantidade de itens exigida para firmar a aliança.";
				return false;
			}

			if (!RemovePayment(player, paymentItemId, paymentAmount, payment))
			{
				RefundPayment(player, payment);
				error = "Não foi possível concluir o pagamento; os itens foram devolvidos.";
				return false;
			}

			// Apply the role first (which refreshes group membership), then override its
			// default trader faction with Survivors so the ally can oppose Raiders.
			npc.SetRoleServer(MobileNPCHumanoidDisposition.Role.Friendly);
			MobileNPCFaction faction = npc.GetComponent<MobileNPCFaction>();
			if (faction != null)
				faction.SetFactionServer(MobileNPCFaction.Id.Survivors);

			MobileNPCBrainDriver brain = npc.GetComponent<MobileNPCBrainDriver>();
			brain?.ResetServer();
			return true;
		}

		private static bool CollectPayment(Player player, ushort itemId, int amount, List<PaymentSlice> payment)
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
					if (jar != null && jar.item != null && jar.item.id == itemId)
						available += jar.item.amount;
				}
			}
			return available >= amount;
		}

		private static bool RemovePayment(Player player, ushort itemId, int amount, List<PaymentSlice> payment)
		{
			int remaining = amount;
			Items[] pages = player.inventory.items;
			for (int page = pages.Length - 1; page >= 0 && remaining > 0; --page)
			{
				Items items = pages[page];
				if (items == null) continue;
				for (int index = items.getItemCount() - 1; index >= 0 && remaining > 0; --index)
				{
					ItemJar jar = items.getItem((byte)index);
					if (jar == null || jar.item == null || jar.item.id != itemId) continue;

					Item item = jar.item;
					byte taken = (byte)Mathf.Min(remaining, item.amount);
					payment.Add(new PaymentSlice
					{
						Id = item.id,
						Amount = taken,
						Quality = item.quality,
						State = item.state != null ? (byte[])item.state.Clone() : new byte[0]
					});
					if (taken >= item.amount)
						player.inventory.removeItem((byte)page, (byte)index);
					else
						player.inventory.updateAmount((byte)page, (byte)index, (byte)(item.amount - taken));
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
				Item item = new Item(slice.Id, slice.Amount, slice.Quality,
					slice.State != null ? (byte[])slice.State.Clone() : new byte[0]);
				if (!player.inventory.tryAddItem(item, false))
					player.inventory.forceAddItem(item, false);
			}
		}
	}
}