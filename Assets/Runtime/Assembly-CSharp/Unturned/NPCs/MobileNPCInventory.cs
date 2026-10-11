////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-local storage model for mobile NPCs. This is deliberately not connected
	/// to world-item pickup yet: callers may store an item only after another
	/// server-authoritative operation has transferred ownership and removed it from
	/// the world. It must never be used as a preview/cache of an item still on the ground.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCInventory : MonoBehaviour
	{
		[SerializeField, Min(1)] private int capacity = 12;
		private readonly List<Item> slots = new List<Item>();

		public int Capacity => Mathf.Max(1, capacity);
		public int UsedSlots => slots.Count;
		public int FreeSlots => Mathf.Max(0, Capacity - slots.Count);
		public bool HasSpace => slots.Count < Capacity;

		/// <summary>
		/// Stores an owned item snapshot. The caller must already have exclusive
		/// ownership of this item; this method does not remove any world drop.
		/// </summary>
		public bool TryStoreTransferredItem(Item transferredItem)
		{
			if (!Provider.isServer || transferredItem == null || transferredItem.id == 0
				|| transferredItem.amount == 0 || !HasSpace)
			{
				return false;
			}

			byte[] stateCopy = transferredItem.state != null
				? (byte[])transferredItem.state.Clone()
				: new byte[0];
			slots.Add(new Item(transferredItem.id, transferredItem.amount,
				transferredItem.quality, stateCopy));
			return true;
		}

		/// <summary>
		/// Transfers a nearby world drop by its server instance ID. The item must still
		/// exist in the authoritative item region and be within the requested distance.
		/// </summary>
		public bool TryPickupWorldItemServer(uint instanceID, Vector3 npcPosition, float maximumDistance)
		{
			return Provider.isServer && HasSpace
				&& ItemManager.TryTransferWorldItemToMobileNPCServer(instanceID, npcPosition, maximumDistance, this);
		}

		/// <summary>Counts stored items by vanilla item category.</summary>
		public int CountItemType(EItemType type)
		{
			if (!Provider.isServer) return 0;
			int count = 0;
			for (int i = 0; i < slots.Count; ++i)
			{
				Item stored = slots[i];
				if (stored == null) continue;
				ItemAsset asset = Assets.find(EAssetType.ITEM, stored.id) as ItemAsset;
				if (asset != null && asset.type == type) count++;
			}
			return count;
		}

		/// <summary>Returns whether an item with this legacy ID is already stored.</summary>
		public bool ContainsItemID(ushort itemID)
		{
			if (!Provider.isServer || itemID == 0) return false;
			for (int i = 0; i < slots.Count; ++i)
			{
				if (slots[i] != null && slots[i].id == itemID) return true;
			}
			return false;
		}

		/// <summary>Returns a defensive copy so callers cannot mutate stored state.</summary>
		public bool TryGetItemSnapshot(int slot, out Item snapshot)
		{
			snapshot = null;
			if (!Provider.isServer || slot < 0 || slot >= slots.Count)
			{
				return false;
			}

			Item stored = slots[slot];
			byte[] stateCopy = stored.state != null ? (byte[])stored.state.Clone() : new byte[0];
			snapshot = new Item(stored.id, stored.amount, stored.quality, stateCopy);
			return true;
		}

		/// <summary>Removes and returns one stored slot to a server-side owner.</summary>
		public bool TryTakeItemServer(int slot, out Item transferredItem)
		{
			transferredItem = null;
			if (!Provider.isServer || slot < 0 || slot >= slots.Count)
			{
				return false;
			}

			Item stored = slots[slot];
			byte[] stateCopy = stored.state != null ? (byte[])stored.state.Clone() : new byte[0];
			transferredItem = new Item(stored.id, stored.amount, stored.quality, stateCopy);
			slots.RemoveAt(slot);
			return true;
		}

		/// <summary>Clears storage during explicit server-side teardown.</summary>
		public void ClearServer()
		{
			if (Provider.isServer)
			{
				slots.Clear();
			}
		}
	}
}
