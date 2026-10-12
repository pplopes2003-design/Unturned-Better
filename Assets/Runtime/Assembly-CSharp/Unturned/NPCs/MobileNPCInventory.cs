////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-local storage model for mobile NPCs. Items enter storage only after a
	/// server-authoritative world transfer; combat may consume stored resources, but this
	/// is not a vanilla Player inventory and does not replicate or persist by itself.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MobileNPCInventory : MonoBehaviour
	{
		[SerializeField, Min(1)] private int capacity = 12;
		private readonly List<Item> slots = new List<Item>();
		// Runtime-only identity keeps duplicate gun assets independently addressable.
		private readonly List<uint> slotInstanceIDs = new List<uint>();
		private uint nextSlotInstanceID = 1;

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
			slotInstanceIDs.Add(AllocateSlotInstanceID());
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

		/// <summary>Chooses the best stored vanilla gun using its range, fire delay and condition.</summary>
		public bool TryGetBestGun(out Item gunSnapshot, out ItemGunAsset gunAsset)
		{
			return TryGetBestGun(out uint ignoredInstanceID, out gunSnapshot, out gunAsset);
		}

		/// <summary>Chooses the best gun and returns its stable runtime slot identity.</summary>
		public bool TryGetBestGun(out uint instanceID, out Item gunSnapshot, out ItemGunAsset gunAsset)
		{
			instanceID = 0;
			gunSnapshot = null;
			gunAsset = null;
			if (!Provider.isServer) return false;
			float bestScore = float.MinValue;
			for (int index = 0; index < slots.Count; ++index)
			{
				Item stored = slots[index];
				if (stored == null) continue;
				ItemGunAsset candidate = Assets.find(EAssetType.ITEM, stored.id) as ItemGunAsset;
				if (candidate == null) continue;
				float score = Mathf.Clamp(candidate.range, 0f, 300f) * 0.6f
					+ (255f - candidate.firerate) * 0.08f
					+ Mathf.Clamp(stored.quality, 0, 100) * 0.05f;
				if (score <= bestScore) continue;
				bestScore = score;
				byte[] stateCopy = stored.state != null ? (byte[])stored.state.Clone() : new byte[0];
				gunSnapshot = new Item(stored.id, stored.amount, stored.quality, stateCopy);
				gunAsset = candidate;
				instanceID = slotInstanceIDs[index];
			}
			return gunSnapshot != null && gunAsset != null && instanceID != 0;
		}

		/// <summary>Finds the first stored magazine whose calibers intersect the gun's accepted magazine calibers.</summary>
		public bool TryFindCompatibleMagazine(ItemGunAsset gunAsset, out int slotIndex, out ItemMagazineAsset magazineAsset)
		{
			slotIndex = -1;
			magazineAsset = null;
			if (!Provider.isServer || gunAsset == null || gunAsset.magazineCalibers == null
				|| gunAsset.magazineCalibers.Length == 0) return false;

			for (int index = 0; index < slots.Count; ++index)
			{
				Item stored = slots[index];
				if (stored == null) continue;
				ItemMagazineAsset candidate = Assets.find(EAssetType.ITEM, stored.id) as ItemMagazineAsset;
				if (candidate == null || stored.amount == 0 || candidate.MaxAmountAsByte == 0
					|| candidate.calibers == null || candidate.calibers.Length == 0) continue;
				if (!candidate.CalibersContainAnyOfIds(gunAsset.magazineCalibers)) continue;

				slotIndex = index;
				magazineAsset = candidate;
				return true;
			}
			return false;
		}

		/// <summary>Checks whether the selected gun has a writable vanilla state.</summary>
		public bool CanUpdateGunAmmoServer(ushort gunID)
		{
			if (!Provider.isServer || gunID == 0) return false;
			for (int index = 0; index < slots.Count; ++index)
				if (slots[index] != null && slots[index].id == gunID)
					return CanUpdateGunAmmoServer(slotInstanceIDs[index]);
			return false;
		}

		public bool CanUpdateGunAmmoServer(uint instanceID)
		{
			if (!Provider.isServer || instanceID == 0) return false;
			int index = FindSlotByInstanceID(instanceID);
			if (index < 0) return false;
			Item stored = slots[index];
			return stored != null && stored.state != null && stored.state.Length >= 11
				&& Assets.find(EAssetType.ITEM, stored.id) is ItemGunAsset;
		}

		/// <summary>Persists ammo state into the exact stored gun instance.</summary>
		public bool TryUpdateGunAmmoServer(ushort gunID, byte roundsRemaining, ushort? loadedMagazineID = null)
		{
			if (!Provider.isServer || gunID == 0) return false;
			for (int index = 0; index < slots.Count; ++index)
				if (slots[index] != null && slots[index].id == gunID)
					return TryUpdateGunAmmoServer(slotInstanceIDs[index], roundsRemaining, loadedMagazineID);
			return false;
		}

		public bool TryUpdateGunAmmoServer(uint instanceID, byte roundsRemaining, ushort? loadedMagazineID = null)
		{
			if (!Provider.isServer || instanceID == 0) return false;
			int index = FindSlotByInstanceID(instanceID);
			if (index < 0) return false;
			Item stored = slots[index];
			if (stored == null || !(Assets.find(EAssetType.ITEM, stored.id) is ItemGunAsset)
				|| stored.state == null || stored.state.Length < 11) return false;
			byte[] stateCopy = (byte[])stored.state.Clone();
			if (loadedMagazineID.HasValue)
			{
				byte[] magazineBytes = System.BitConverter.GetBytes(loadedMagazineID.Value);
				stateCopy[8] = magazineBytes[0];
				stateCopy[9] = magazineBytes[1];
			}
			stateCopy[10] = roundsRemaining;
			slots[index] = new Item(stored.id, stored.amount, stored.quality, stateCopy);
			return true;
		}

		public bool ContainsGunInstanceServer(uint instanceID)
		{
			return Provider.isServer && instanceID != 0 && FindSlotByInstanceID(instanceID) >= 0;
		}

		private int FindSlotByInstanceID(uint instanceID)
		{
			for (int index = 0; index < slotInstanceIDs.Count; ++index)
				if (slotInstanceIDs[index] == instanceID) return index;
			return -1;
		}

		private uint AllocateSlotInstanceID()
		{
			for (int attempt = 0; attempt <= slotInstanceIDs.Count + 1; ++attempt)
			{
				uint candidate = nextSlotInstanceID++;
				if (nextSlotInstanceID == 0) nextSlotInstanceID = 1;
				if (candidate == 0) continue;
				bool used = false;
				for (int i = 0; i < slotInstanceIDs.Count; ++i)
					if (slotInstanceIDs[i] == candidate) { used = true; break; }
				if (!used) return candidate;
			}
			return 0;
		}

		public bool TryConsumeFirstItemOfType(EItemType type, out Item consumedItem)
		{
			consumedItem = null;
			if (!Provider.isServer) return false;

			for (int i = 0; i < slots.Count; ++i)
			{
				Item stored = slots[i];
				if (stored == null) continue;
				ItemAsset asset = Assets.find(EAssetType.ITEM, stored.id) as ItemAsset;
				if (asset == null || asset.type != type) continue;

				byte[] stateCopy = stored.state != null ? (byte[])stored.state.Clone() : new byte[0];
				consumedItem = new Item(stored.id, stored.amount, stored.quality, stateCopy);
				slots.RemoveAt(i);
				slotInstanceIDs.RemoveAt(i);
				return true;
			}
			return false;
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
			slotInstanceIDs.RemoveAt(slot);
			return true;
		}

		/// <summary>Clears storage during explicit server-side teardown.</summary>
		public void ClearServer()
		{
			if (Provider.isServer)
			{
				slots.Clear();
				slotInstanceIDs.Clear();
			}
		}
	}
}
