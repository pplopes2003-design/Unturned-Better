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
		private int reservedSlots;

		public int Capacity => Mathf.Max(1, capacity);
		public int UsedSlots => slots.Count;
		public int FreeSlots => Mathf.Max(0, Capacity - slots.Count - reservedSlots);
		public bool HasSpace => slots.Count + reservedSlots < Capacity;

		/// <summary>
		/// Stores an owned item snapshot. The caller must already have exclusive
		/// ownership of this item; this method does not remove any world drop.
		/// </summary>
		public bool TryStoreTransferredItem(Item transferredItem)
		{
			if (!Provider.isServer || transferredItem == null || transferredItem.id == 0
				|| !CanStoreItemAmount(transferredItem) || !HasSpace)
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

		private static bool CanStoreItemAmount(Item item)
		{
			if (item == null || item.id == 0) return false;
			if (item.amount > 0) return true;
			ItemAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemAsset;
			return asset is ItemMagazineAsset && !asset.ShouldDeleteAtZeroAmount;
		}

		/// <summary>Reserves one inventory slot for a transaction that may need rollback.</summary>
		public bool TryReserveSlotServer()
		{
			if (!Provider.isServer || !HasSpace) return false;
			reservedSlots++;
			return true;
		}

		/// <summary>Consumes a reserved slot to restore the exact item held by a pending transaction.</summary>
		public bool TryStoreReservedItemServer(Item transferredItem)
		{
			if (!Provider.isServer || reservedSlots <= 0 || slots.Count >= Capacity
				|| !CanStoreItemAmount(transferredItem))
				return false;
			byte[] stateCopy = transferredItem.state != null
				? (byte[])transferredItem.state.Clone()
				: new byte[0];
			slots.Add(new Item(transferredItem.id, transferredItem.amount, transferredItem.quality, stateCopy));
			slotInstanceIDs.Add(AllocateSlotInstanceID());
			reservedSlots--;
			return true;
		}

		public void ReleaseReservedSlotServer()
		{
			if (Provider.isServer && reservedSlots > 0)
				reservedSlots--;
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
			return stored != null && stored.state != null && stored.state.Length >= 18
				&& Assets.find(EAssetType.ITEM, stored.id) is ItemGunAsset;
		}

		/// <summary>Persists ammo state into the exact stored gun instance.</summary>
		public bool TryUpdateGunAmmoServer(ushort gunID, byte roundsRemaining, ushort? loadedMagazineID = null, byte? loadedMagazineQuality = null)
		{
			if (!Provider.isServer || gunID == 0) return false;
			for (int index = 0; index < slots.Count; ++index)
				if (slots[index] != null && slots[index].id == gunID)
					return TryUpdateGunAmmoServer(slotInstanceIDs[index], roundsRemaining, loadedMagazineID, loadedMagazineQuality);
			return false;
		}

		public bool TryUpdateGunAmmoServer(uint instanceID, byte roundsRemaining, ushort? loadedMagazineID = null, byte? loadedMagazineQuality = null)
		{
			if (!Provider.isServer || instanceID == 0) return false;
			int index = FindSlotByInstanceID(instanceID);
			if (index < 0) return false;
			Item stored = slots[index];
			if (stored == null || !(Assets.find(EAssetType.ITEM, stored.id) is ItemGunAsset)
				|| stored.state == null || stored.state.Length < 18) return false;
			byte[] stateCopy = (byte[])stored.state.Clone();
			if (loadedMagazineID.HasValue)
			{
				byte[] magazineBytes = System.BitConverter.GetBytes(loadedMagazineID.Value);
				stateCopy[8] = magazineBytes[0];
				stateCopy[9] = magazineBytes[1];
				if (loadedMagazineQuality.HasValue) stateCopy[17] = loadedMagazineQuality.Value;
			}
			stateCopy[10] = roundsRemaining;
			slots[index] = new Item(stored.id, stored.amount, stored.quality, stateCopy);
			return true;
		}

		public bool TryGetGunSnapshotServer(uint instanceID, out Item snapshot)
		{
			snapshot = null;
			if (!Provider.isServer || instanceID == 0) return false;
			int index = FindSlotByInstanceID(instanceID);
			if (index < 0) return false;
			Item stored = slots[index];
			if (stored == null || !(Assets.find(EAssetType.ITEM, stored.id) is ItemGunAsset)
				|| stored.state == null || stored.state.Length < 18) return false;
			byte[] stateCopy = (byte[])stored.state.Clone();
			snapshot = new Item(stored.id, stored.amount, stored.quality, stateCopy);
			return true;
		}

		public bool ContainsGunInstanceServer(uint instanceID)
		{
			return Provider.isServer && instanceID != 0 && FindSlotByInstanceID(instanceID) >= 0;
		}

		/// <summary>
		/// Crafts one vanilla recipe that produces a placeable barricade/storage/structure.
		/// This intentionally supports only simple recipes whose inputs are represented as
		/// discrete item slots; recipes requiring tools, skills, workstations, target items,
		/// NPC quest conditions, or special operations are left untouched.
		/// </summary>
		public bool TryCraftBuildableForSettlementServer(Items sharedStorage = null)
		{
			if (!Provider.isServer || !Level.isLoaded)
				return false;

			List<ItemAsset> itemAssets = new List<ItemAsset>();
			Assets.find(itemAssets);
			for (int assetIndex = 0; assetIndex < itemAssets.Count; ++assetIndex)
			{
				ItemAsset sourceAsset = itemAssets[assetIndex];
				if (sourceAsset == null || sourceAsset.blueprints == null || sourceAsset.blueprints.Count == 0)
					continue;

				for (int blueprintIndex = 0; blueprintIndex < sourceAsset.blueprints.Count; ++blueprintIndex)
				{
					Blueprint blueprint = sourceAsset.blueprints[blueprintIndex];
					if (!IsSimpleSettlementBlueprint(blueprint))
						continue;

					ItemAsset outputAsset = blueprint.outputs[0].FindItemAsset();
					if (outputAsset == null || outputAsset.id == 0
						|| !(outputAsset is ItemBarricadeAsset || outputAsset is ItemStructureAsset)
						|| blueprint.outputs[0].amount < 1 || blueprint.outputs[0].amount > byte.MaxValue)
						continue;

					List<int> consumedIndices = new List<int>();
					if (!TryReserveBlueprintInputs(blueprint, consumedIndices))
					{
						consumedIndices.Clear();
						if (sharedStorage == null || !TryStageBlueprintInputsFromStorage(blueprint, sharedStorage)
							|| !TryReserveBlueprintInputs(blueprint, consumedIndices))
							continue;
					}
					if (consumedIndices.Count == 0)
						continue;

					// Ensure crafting cannot consume the ingredients and then fail because the
					// output has nowhere to go. At least one consumed slot can be reused.
					if (slots.Count - consumedIndices.Count >= Capacity)
						continue;

					consumedIndices.Sort();
					for (int i = consumedIndices.Count - 1; i >= 0; --i)
					{
						int slot = consumedIndices[i];
						slots.RemoveAt(slot);
						slotInstanceIDs.RemoveAt(slot);
					}

					Item output = new Item(outputAsset.id, (byte)blueprint.outputs[0].amount, 100, new byte[0]);
					if (!TryStoreTransferredItem(output))
					{
						// The capacity check above should make this unreachable, but fail closed.
						UnturnedLog.warn("[MobileNPCInventory] A settlement crafting output could not be stored.");
						return false;
					}

					Debug.Log("[MobileNPCInventory] NPC crafted " + outputAsset.FriendlyName
						+ " from vanilla blueprint " + blueprint + ".");
					return true;
				}
			}
			return false;
		}

		private static bool IsSimpleSettlementBlueprint(Blueprint blueprint)
		{
			if (blueprint == null || blueprint.outputs == null || blueprint.outputs.Length != 1
				|| blueprint.supplies == null || blueprint.supplies.Length == 0
				|| blueprint.TargetItem != null || blueprint.Operation != EBlueprintOperation.None
				|| blueprint.RequiresSkill || !string.IsNullOrEmpty(blueprint.map)
				|| blueprint.RequiresStaticTags != null && blueprint.RequiresStaticTags.Length > 0
				|| blueprint.GetApplicableRequiredNearbyCraftingTags() != null
				|| blueprint.questConditions != null && blueprint.questConditions.Length > 0)
				return false;

			for (int i = 0; i < blueprint.supplies.Length; ++i)
			{
				BlueprintSupply supply = blueprint.supplies[i];
				if (supply == null || supply.amount < 1 || supply.CountingMethod != ECraftingInputCountingMethod.TotalItems)
					return false;
				ItemAsset inputAsset = supply.FindItemAsset();
				if (inputAsset == null || inputAsset.id == 0)
					return false;
			}
			return true;
		}

		/// <summary>
		/// Moves only the missing discrete recipe ingredients from the group's shared vanilla
		/// storage into this NPC's inventory. Items are removed from storage only after the
		/// NPC inventory accepts an exact snapshot; partial staging is safe and can be reused.
		/// </summary>
		private bool TryStageBlueprintInputsFromStorage(Blueprint blueprint, Items sharedStorage)
		{
			if (!Provider.isServer || blueprint == null || sharedStorage == null || sharedStorage.items == null)
				return false;

			bool movedAny = false;
			for (int supplyIndex = 0; supplyIndex < blueprint.supplies.Length; ++supplyIndex)
			{
				BlueprintSupply supply = blueprint.supplies[supplyIndex];
				ItemAsset inputAsset = supply.FindItemAsset();
				if (inputAsset == null)
					return false;

				int have = 0;
				for (int slot = 0; slot < slots.Count; ++slot)
				{
					Item item = slots[slot];
					if (item != null && item.id == inputAsset.id
						&& (item.amount > 0 || supply.ShouldCountEmptyAsOne))
						have++;
				}
				int missing = Mathf.Max(0, supply.amount - have);
				for (int storageIndex = sharedStorage.items.Count - 1; storageIndex >= 0 && missing > 0; --storageIndex)
				{
					ItemJar jar = sharedStorage.items[storageIndex];
					Item stored = jar != null ? jar.item : null;
					if (stored == null || stored.id != inputAsset.id
						|| (stored.amount == 0 && !supply.ShouldCountEmptyAsOne) || !HasSpace)
						continue;

					Item snapshot = new Item(stored.id, stored.amount, stored.quality,
						stored.state != null ? (byte[])stored.state.Clone() : new byte[0]);
					if (!TryStoreTransferredItem(snapshot))
						continue;

					sharedStorage.removeItem((byte)storageIndex);
					missing--;
					movedAny = true;
				}
				if (missing > 0)
					return false;
			}
			return movedAny;
		}

		private bool TryReserveBlueprintInputs(Blueprint blueprint, List<int> consumedIndices)
		{
			bool[] claimed = new bool[slots.Count];
			for (int supplyIndex = 0; supplyIndex < blueprint.supplies.Length; ++supplyIndex)
			{
				BlueprintSupply supply = blueprint.supplies[supplyIndex];
				ItemAsset inputAsset = supply.FindItemAsset();
				int found = 0;
				for (int slotIndex = 0; slotIndex < slots.Count && found < supply.amount; ++slotIndex)
				{
					Item item = slots[slotIndex];
					if (claimed[slotIndex] || item == null || item.id != inputAsset.id)
						continue;
					if (item.amount == 0 && !supply.ShouldCountEmptyAsOne)
						continue;

					claimed[slotIndex] = true;
					found++;
					if (supply.ShouldConsume)
						consumedIndices.Add(slotIndex);
				}
				if (found < supply.amount)
					return false;
			}
			return true;
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
				// Consume one unit rather than deleting an entire stack of food/water.
				consumedItem = new Item(stored.id, 1, stored.quality, stateCopy);
				if (stored.amount > 1)
				{
					slots[i] = new Item(stored.id, (byte)(stored.amount - 1), stored.quality, stateCopy);
				}
				else
				{
					slots.RemoveAt(i);
					slotInstanceIDs.RemoveAt(i);
				}
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
				reservedSlots = 0;
			}
		}
	}
}
