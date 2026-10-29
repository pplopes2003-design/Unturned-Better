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
		private float nextMedicalCraftAttemptTime;

		// CombatOnly grants authentic armor/clothes on top of a firearm,
		// three spare magazines and finite medical supplies. Keep its
		// storage model separate from the saved Normal-mode capacity.
		public int Capacity => MobileNPCSettings.CombatOnly
			? Mathf.Max(18, capacity) : Mathf.Max(1, capacity);
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
			return TryPickupWorldItemServer(instanceID, npcPosition, maximumDistance, out _);
		}

		/// <summary>For the Dev Build log: expose rejection reason without changing the transactional storage contract.</summary>
		public bool TryPickupWorldItemServer(uint instanceID, Vector3 npcPosition,
			float maximumDistance, out string failureReason)
		{
			failureReason = null;
			if (!Provider.isServer || !HasSpace)
			{
				failureReason = "NPC storage full or not server";
				return false;
			}
			return ItemManager.TryTransferWorldItemToMobileNPCServer(
				instanceID, npcPosition, maximumDistance, this, out failureReason);
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

		/// <summary>Raid stockpile is private, finite and actually carried.
		/// A charge is NOT considered deployable until there is a native
		/// charge-placement/detonator implementation. Do not count a held
		/// C4-looking object as an already functional breach charge.</summary>
		public bool HasUsableRaidExplosiveServer()
		{
			if (!Provider.isServer) return false;
			for (int i = 0; i < slots.Count; ++i)
			{
				Item held = slots[i];
				if (held == null || held.id == 0 || held.amount == 0) continue;
				ItemAsset asset = Assets.find(EAssetType.ITEM, held.id) as ItemAsset;
				if (MobileNPCRaidSupplyPolicy.IsBreachThrowable(asset))
					return true;
				if (!MobileNPCRaidSupplyPolicy.IsRaidLauncher(asset)
					|| held.state == null || held.state.Length < 18) continue;
				ItemGunAsset gun = (ItemGunAsset)asset;
				ushort magId = System.BitConverter.ToUInt16(held.state,
					GunStateIndices.MAGAZINE_ID);
				ItemMagazineAsset mag = Assets.find(EAssetType.ITEM, magId) as ItemMagazineAsset;
				if (mag != null && mag.CalibersContainAnyOfIds(gun.magazineCalibers)
					&& held.state[GunStateIndices.AMMO] >= gun.ammoPerShot)
					return true;
			}
			return false;
		}

		public bool TryGetLoadedRaidLauncherServer(out uint instanceID, out Item gun,
			out ItemGunAsset asset, out ItemMagazineAsset magazine)
		{
			instanceID = 0; gun = null; asset = null; magazine = null;
			if (!Provider.isServer) return false;
			for (int i = 0; i < slots.Count; ++i)
			{
				Item held = slots[i];
				if (held == null || held.state == null || held.state.Length < 18) continue;
				ItemGunAsset candidate = Assets.find(EAssetType.ITEM, held.id) as ItemGunAsset;
				if (!MobileNPCRaidSupplyPolicy.IsRaidLauncher(candidate)) continue;
				ushort magId = System.BitConverter.ToUInt16(held.state,
					GunStateIndices.MAGAZINE_ID);
				ItemMagazineAsset loaded = Assets.find(EAssetType.ITEM, magId) as ItemMagazineAsset;
				if (loaded == null || !loaded.CalibersContainAnyOfIds(candidate.magazineCalibers)
					|| held.state[GunStateIndices.AMMO] < candidate.ammoPerShot)
					continue;
				instanceID = slotInstanceIDs[i];
				gun = new Item(held.id, held.amount, held.quality,
					(byte[])held.state.Clone());
				asset = candidate;
				magazine = loaded;
				return true;
			}
			return false;
		}

		/// <summary>Native C4 is only operational when an actual matching
		/// charge and a vanilla detonator are carried by the SAME survivor.</summary>
		public bool HasUsableRaidChargeServer()
		{
			if (!Provider.isServer) return false;
			bool hasCharge = false, hasDetonator = false;
			for (int i = 0; i < slots.Count; ++i)
			{
				Item held = slots[i];
				if (held == null || held.id == 0 || held.amount == 0) continue;
				ItemAsset asset = Assets.find(EAssetType.ITEM, held.id) as ItemAsset;
				if (asset is ItemDetonatorAsset) hasDetonator = true;
				if (asset is ItemChargeAsset charge
					&& charge.build == EBuild.CHARGE
					&& charge.barricade != null
					&& charge.barricade.GetComponentInChildren<InteractableCharge>(true) != null
					&& (charge.structureDamage > 0f || charge.barricadeDamage > 0f))
					hasCharge = true;
			}
			return hasCharge && hasDetonator;
		}

		public bool HasRaidLauncherServer()
		{
			if (!Provider.isServer) return false;
			for (int i = 0; i < slots.Count; ++i)
			{
				Item held = slots[i];
				if (held != null && MobileNPCRaidSupplyPolicy.IsRaidLauncher(
					Assets.find(EAssetType.ITEM, held.id) as ItemAsset))
					return true;
			}
			return false;
		}

		public bool IsCompatibleRaidMagazineServer(ItemMagazineAsset magazine)
		{
			if (!Provider.isServer || magazine == null || magazine.calibers == null)
				return false;
			for (int i = 0; i < slots.Count; ++i)
			{
				Item held = slots[i];
				ItemGunAsset launcher = held != null
					? Assets.find(EAssetType.ITEM, held.id) as ItemGunAsset : null;
				if (MobileNPCRaidSupplyPolicy.IsRaidLauncher(launcher)
					&& magazine.CalibersContainAnyOfIds(launcher.magazineCalibers))
					return true;
			}
			return false;
		}

		/// <summary>Swap one real, compatible spare rocket magazine into an
		/// owned launcher without inventing ammunition. Rolls back the debited
		/// magazine when native gun state refuses the exchange.</summary>
		public bool TryReloadOwnedRaidLauncherServer()
		{
			if (!Provider.isServer) return false;
			for (int gunSlot = 0; gunSlot < slots.Count; ++gunSlot)
			{
				Item held = slots[gunSlot];
				ItemGunAsset launcher = held != null
					? Assets.find(EAssetType.ITEM, held.id) as ItemGunAsset : null;
				if (!MobileNPCRaidSupplyPolicy.IsRaidLauncher(launcher)
					|| held.state == null || held.state.Length < 18
					|| held.state[GunStateIndices.AMMO] >= launcher.ammoPerShot)
					continue;
				ushort previousMagazineId = System.BitConverter.ToUInt16(
					held.state, GunStateIndices.MAGAZINE_ID);
				byte oldAmmo = held.state[GunStateIndices.AMMO];
				byte oldQuality = held.state[17];
				for (int magazineSlot = 0; magazineSlot < slots.Count; ++magazineSlot)
				{
					Item magazineItem = slots[magazineSlot];
					ItemMagazineAsset mag = magazineItem != null
						? Assets.find(EAssetType.ITEM, magazineItem.id) as ItemMagazineAsset : null;
					if (mag == null || magazineItem.amount < launcher.ammoPerShot
						|| !mag.CalibersContainAnyOfIds(launcher.magazineCalibers))
						continue;
					uint gunInstance = slotInstanceIDs[gunSlot];
					if (!TryTakeItemServer(magazineSlot, out Item replacement))
						return false;
					if (!TryUpdateGunAmmoServer(gunInstance, replacement.amount,
						replacement.id, replacement.quality))
					{
						if (!TryStoreTransferredItem(replacement))
							UnturnedLog.warn("[MobileNPCInventory] Lost reserve launcher magazine during rejected swap.");
						return false;
					}
					// Return native old magazine only when it has remaining
					// rounds; never give a new full one to the shooter.
					if (oldAmmo > 0 && previousMagazineId != 0)
					{
						ItemMagazineAsset oldAsset =
							Assets.find(EAssetType.ITEM, previousMagazineId) as ItemMagazineAsset;
						if (oldAsset != null
							&& !TryStoreTransferredItem(new Item(previousMagazineId,
								oldAmmo, oldQuality, new byte[0])))
							UnturnedLog.warn("[MobileNPCInventory] Could not store exchanged partial launcher magazine.");
					}
					return true;
				}
			}
			return false;
		}

		private float nextRaidCraftAt;
		/// <summary>Craft ONE actual vanilla explosive throwable using a simple
		/// authored blueprint and owned ingredients. Recipes requiring skill,
		/// workstations, quests, target items or special operations remain off-limits.
		/// No homemade recipes or ingredient lists are invented here.</summary>
		public bool TryCraftRaidExplosiveServer(Items verifiedSharedStorage = null)
		{
			if (!Provider.isServer || !Level.isLoaded || !MobileNPCSettings.CombatMode
				|| Time.time < nextRaidCraftAt || HasUsableRaidExplosiveServer())
				return false;
			nextRaidCraftAt = Time.time + UnityEngine.Random.Range(35f, 55f);
			List<ItemAsset> catalog = new List<ItemAsset>();
			Assets.find(catalog);
			for (int i = 0; i < catalog.Count; ++i)
			{
				ItemAsset source = catalog[i];
				if (source == null || source.blueprints == null) continue;
				for (int j = 0; j < source.blueprints.Count; ++j)
				{
					Blueprint native = source.blueprints[j];
					if (!IsSimpleSettlementBlueprint(native)) continue;
					ItemAsset output = native.outputs[0].FindItemAsset();
					if (!MobileNPCRaidSupplyPolicy.IsBreachThrowable(output)
						&& !MobileNPCRaidSupplyPolicy.IsRaidCharge(output))
						continue;
					// Borrow exact authored inputs only from a verified native group
					// storage that the caller physically approaches; no phantom items.
					if (verifiedSharedStorage != null)
						TryStageBlueprintInputsFromStorage(native, verifiedSharedStorage);
					if (TryCraftOneSimpleOutputServer(native, output))
						return true;
				}
			}
			return false;
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
				// The mobile gun combat path simulates bullet weapons; rocket
				// launchers require a genuine vanilla projectile/prefab service.
				if (candidate == null || candidate.projectile != null) continue;
				float score = Mathf.Clamp(candidate.range, 0f, 300f) * 0.6f
					+ (255f - candidate.firerate) * 0.08f
					+ Mathf.Clamp(stored.quality, 0, 100) * 0.05f;
				// An equipped but empty rifle is not automatically preferable to
				// another real firearm with a loaded, valid magazine. This is
				// inventory awareness, NOT fabricated ammunition or a gun buff.
				if (stored.state != null && stored.state.Length >= 18)
				{
					ushort magazineId = System.BitConverter.ToUInt16(stored.state, GunStateIndices.MAGAZINE_ID);
					ItemMagazineAsset magazine = Assets.find(EAssetType.ITEM, magazineId) as ItemMagazineAsset;
					if (magazine != null && candidate.magazineCalibers != null
						&& candidate.magazineCalibers.Length > 0
						&& magazine.CalibersContainAnyOfIds(candidate.magazineCalibers)
						&& stored.state[GunStateIndices.AMMO] >= Mathf.Max(1, candidate.ammoPerShot))
						// A usable loaded sidearm beats an impressive but empty rifle.
						score += 175f;
					ushort sightId = System.BitConverter.ToUInt16(stored.state, GunStateIndices.SIGHT_ID);
					ItemSightAsset scope = Assets.find(EAssetType.ITEM, sightId) as ItemSightAsset;
					if (scope != null && scope.zoom >= 2.5f)
						score += Mathf.Min(10f, scope.zoom * 1.5f);
				}
				if (score <= bestScore) continue;
				bestScore = score;
				byte[] stateCopy = stored.state != null ? (byte[])stored.state.Clone() : new byte[0];
				gunSnapshot = new Item(stored.id, stored.amount, stored.quality, stateCopy);
				gunAsset = candidate;
				instanceID = slotInstanceIDs[index];
			}
			return gunSnapshot != null && gunAsset != null && instanceID != 0;
		}

		/// <summary>
		/// Real owned melee weapon selection: native per-target damage,
		/// range and item quality, not an invented NPC-only weapon table.
		/// Zero-damage tools are not treated as weapons.
		/// </summary>
		public bool TryGetBestMeleeServer(out Item weapon, out ItemMeleeAsset asset)
		{
			return TryGetBestMeleeServer(false, out weapon, out asset);
		}

		public bool TryGetBestMeleeServer(bool againstPlayer,
			out Item weapon, out ItemMeleeAsset asset)
		{
			weapon = null;
			asset = null;
			if (!Provider.isServer)
				return false;
			float best = 0f;
			for (int i = 0; i < slots.Count; ++i)
			{
				Item candidate = slots[i];
				if (candidate == null)
					continue;
				ItemMeleeAsset melee = Assets.find(EAssetType.ITEM, candidate.id) as ItemMeleeAsset;
				if (melee == null || melee.isPro || melee.isRepair
					|| melee.playerDamageMultiplier == null
					|| melee.zombieOrPlayerDamageMultiplier == null)
					continue;
				float quality = candidate.quality / 100f;
				float qualityFactor = quality < 0.5f ? 0.5f + quality : 1f;
				float playerDamage = melee.playerDamageMultiplier.multiply(ELimb.SPINE);
				float zombieDamage = melee.zombieOrPlayerDamageMultiplier.multiply(ELimb.SPINE);
				// Do not pick a zombie-only weapon to extend a fist's reach
				// against a player, or the reverse. Match vanilla multipliers.
				float strength = (againstPlayer ? playerDamage : zombieDamage) * qualityFactor;
				if (strength <= 0f || float.IsNaN(strength) || float.IsInfinity(strength))
					continue;
				float score = strength + Mathf.Clamp(melee.range, 0.8f, 3.2f) * 2f;
				if (score <= best)
					continue;
				best = score;
				weapon = new Item(candidate.id, candidate.amount, candidate.quality,
					candidate.state != null ? (byte[])candidate.state.Clone() : new byte[0]);
				asset = melee;
			}
			return weapon != null;
		}

		/// <summary>
		/// Apply the same configured durability loss as Unturned's UseableMelee
		/// to one matching actual owned weapon when a server melee hit lands.
		/// </summary>
		public bool TryWearOwnedMeleeServer(ushort itemID)
		{
			if (!Provider.isServer || itemID == 0
				|| !Provider.modeConfigData.Items.ShouldWeaponTakeDamage)
				return false;
			for (int i = 0; i < slots.Count; ++i)
			{
				Item held = slots[i];
				if (held == null || held.id != itemID)
					continue;
				ItemMeleeAsset melee = Assets.find(EAssetType.ITEM, held.id) as ItemMeleeAsset;
				if (melee == null || Random.value >= melee.durability || held.quality == 0)
					return false;
				byte worn = (byte)Mathf.Max(0, held.quality - melee.wear);
				slots[i] = new Item(held.id, held.amount, worn,
					held.state != null ? (byte[])held.state.Clone() : new byte[0]);
				return true;
			}
			return false;
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
		public bool TryCraftBuildableForSettlementServer(Items sharedStorage = null, bool storageOnly = false)
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
					ItemBarricadeAsset outputBarricade = outputAsset as ItemBarricadeAsset;
					bool outputIsStorage = outputBarricade != null && outputBarricade.build == EBuild.STORAGE;
					if (outputAsset == null || outputAsset.id == 0
						|| !(outputAsset is ItemBarricadeAsset || outputAsset is ItemStructureAsset)
						|| (storageOnly && !outputIsStorage) || (!storageOnly && outputIsStorage)
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

		/// <summary>
		/// Craft a wound dressing ONLY using real, currently available Unturned
		/// item blueprints and exact owned materials. Does not consume arbitrary
		/// clothing, award phantom rags, or bypass crafting requirements.
		/// A bounded two-step clothing -> intermediate -> dressing chain is allowed
		/// only when both exact native recipes are safe and directly connected.
		/// </summary>
		public bool TryCraftNativeWoundDressingServer()
		{
			if (!Provider.isServer || !Level.isLoaded || Time.time < nextMedicalCraftAttemptTime)
				return false;
			// Scan recipe catalog infrequently; do not repeat per wounded NPC
			// per-frame, particularly in a 20+ NPC combat.
			nextMedicalCraftAttemptTime = Time.time + 18f;

			List<ItemAsset> assets = new List<ItemAsset>();
			Assets.find(assets);
			// Direct recipes first (e.g. a supply item into a rag).
			for (int i = 0; i < assets.Count; ++i)
			{
				ItemAsset source = assets[i];
				if (source == null || source.blueprints == null)
					continue;
				for (int b = 0; b < source.blueprints.Count; ++b)
				{
					Blueprint recipe = source.blueprints[b];
					if (!IsSimpleSettlementBlueprint(recipe) || recipe.outputs[0] == null)
						continue;
					ItemAsset output = recipe.outputs[0].FindItemAsset();
					if (output == null || !IsNativeWoundDressing(output.id))
						continue;
					if (TryCraftOneSimpleOutputServer(recipe, output))
						return true;
				}
			}

			// Two native recipes: worn clothing -> intermediate input used by
			// a real medical blueprint. This never invents a "tear shirt" recipe.
			for (int i = 0; i < assets.Count; ++i)
			{
				ItemAsset medicineSource = assets[i];
				if (medicineSource == null || medicineSource.blueprints == null)
					continue;
				for (int m = 0; m < medicineSource.blueprints.Count; ++m)
				{
					Blueprint medicine = medicineSource.blueprints[m];
					if (!IsSimpleSettlementBlueprint(medicine) || medicine.outputs[0] == null
						|| medicine.supplies.Length != 1 || medicine.supplies[0].amount != 1
						|| !medicine.supplies[0].ShouldConsume)
						continue;
					ItemAsset dressing = medicine.outputs[0].FindItemAsset();
					ItemAsset intermediate = medicine.supplies[0].FindItemAsset();
					if (dressing == null || !IsNativeWoundDressing(dressing.id) || intermediate == null)
						continue;
					for (int j = 0; j < assets.Count; ++j)
					{
						ItemAsset garmentSource = assets[j];
						if (garmentSource == null || garmentSource.blueprints == null)
							continue;
						for (int g = 0; g < garmentSource.blueprints.Count; ++g)
						{
							Blueprint first = garmentSource.blueprints[g];
							if (!IsSimpleSettlementBlueprint(first) || first.outputs[0] == null
								|| first.outputs[0].FindItemAsset() != intermediate
								|| !ConsumesGarment(first))
								continue;
							if (TryCraftOneSimpleOutputServer(first, intermediate))
							{
								// The intermediate is a real owned item after step one. A
								// failure here keeps it instead of fabricating a dressing.
								return TryCraftOneSimpleOutputServer(medicine, dressing);
							}
						}
					}
				}
			}
			return false;
		}

		private static bool IsNativeWoundDressing(ushort id)
		{
			return id == 393 || id == 95 || id == 394;
		}

		private static bool ConsumesGarment(Blueprint recipe)
		{
			for (int i = 0; i < recipe.supplies.Length; ++i)
			{
				BlueprintSupply required = recipe.supplies[i];
				ItemAsset item = required.FindItemAsset();
				if (!required.ShouldConsume || item == null)
					continue;
				if (item.type == EItemType.SHIRT || item.type == EItemType.PANTS
					|| item.type == EItemType.VEST || item.type == EItemType.HAT
					|| item.type == EItemType.MASK)
					return true;
			}
			return false;
		}

		private bool TryCraftOneSimpleOutputServer(Blueprint recipe, ItemAsset outputAsset)
		{
			if (!Provider.isServer || outputAsset == null
				|| !IsSimpleSettlementBlueprint(recipe) || recipe.outputs[0] == null
				|| recipe.outputs[0].FindItemAsset() != outputAsset
				|| recipe.outputs[0].amount < 1 || recipe.outputs[0].amount > byte.MaxValue)
				return false;

			List<int> inputs = new List<int>();
			if (!TryReserveBlueprintInputs(recipe, inputs) || inputs.Count == 0
				|| slots.Count - inputs.Count + reservedSlots >= Capacity)
				return false;

			inputs.Sort();
			for (int i = inputs.Count - 1; i >= 0; --i)
			{
				slots.RemoveAt(inputs[i]);
				slotInstanceIDs.RemoveAt(inputs[i]);
			}
			Item output = new Item(outputAsset.id,
				(byte)recipe.outputs[0].amount, 100, new byte[0]);
			if (!TryStoreTransferredItem(output))
			{
				UnturnedLog.warn("[MobileNPCInventory] Medical crafting output could not be stored; inspect inventory transaction.");
				return false;
			}
			Debug.Log("[MobileNPCInventory] Crafted real native wound dressing/item "
				+ outputAsset.id + " from validated recipe for " + name + ".", this);
			return true;
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

		/// <summary>
		/// Consume exactly ONE genuine owned native explosive throwable.
		/// This mirrors the one-use inventory debit of vanilla UseableThrowable,
		/// and must be called only immediately before deploying a prefab.
		/// Never use a fabricated grenade or consume flash/sticky/impact items
		/// whose behavior is not implemented in the first NPC throw milestone.
		/// </summary>
		public bool TryConsumeSimpleGrenadeServer(ushort expectedID, out ItemThrowableAsset asset)
		{
			asset = null;
			if (!Provider.isServer || expectedID == 0)
				return false;
			for (int i = 0; i < slots.Count; i++)
			{
				Item item = slots[i];
				if (item == null || item.id != expectedID || item.amount == 0)
					continue;
				ItemThrowableAsset candidate =
					Assets.find(EAssetType.ITEM, item.id) as ItemThrowableAsset;
				if (candidate == null || !candidate.isExplosive
					|| candidate.isSticky || candidate.explodeOnImpact
					|| candidate.throwable == null || candidate.range <= 0f
					|| candidate.fuseLength < 0.5f)
					continue;
				if (item.amount > 1)
					slots[i] = new Item(item.id, (byte)(item.amount - 1),
						item.quality, item.state != null ? (byte[])item.state.Clone() : new byte[0]);
				else
				{
					slots.RemoveAt(i);
					slotInstanceIDs.RemoveAt(i);
				}
				asset = candidate;
				return true;
			}
			return false;
		}

		/// <summary>Consume precisely one unit of an owned specified item.
		/// Shared by injury care and ordinary HP healing. Never remove a
		/// whole stack or consume an arbitrary medical-category item.</summary>
		public bool TryConsumeOneItemByIdServer(ushort itemID, out Item consumedItem)
		{
			consumedItem = null;
			if (!Provider.isServer || itemID == 0)
				return false;
			for (int i = 0; i < slots.Count; ++i)
			{
				Item stored = slots[i];
				if (stored == null || stored.id != itemID || stored.amount == 0)
					continue;
				byte[] stateCopy = stored.state != null
					? (byte[])stored.state.Clone() : new byte[0];
				consumedItem = new Item(stored.id, 1, stored.quality, stateCopy);
				if (stored.amount > 1)
					slots[i] = new Item(stored.id, (byte)(stored.amount - 1),
						stored.quality, stateCopy);
				else
				{
					slots.RemoveAt(i);
					slotInstanceIDs.RemoveAt(i);
				}
				return true;
			}
			return false;
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

		/// <summary>
		/// Spend up to requestedUnits of actual fuel from ONE owned native fuel
		/// canister. The remaining state is saved against the same slot ID.
		/// Only called after an eligible physically nearby generator and its
		/// remaining capacity have been authoritatively checked on the server.
		/// </summary>
		public bool TrySpendGeneratorFuelServer(ushort requestedUnits, out ushort spentUnits)
		{
			spentUnits = 0;
			if (!Provider.isServer || requestedUnits == 0)
				return false;
			for (int i = 0; i < slots.Count; i++)
			{
				Item item = slots[i];
				ItemFuelAsset asset = item != null ? item.GetAsset<ItemFuelAsset>() : null;
				if (item == null || item.amount != 1 || asset == null || asset.fuel == 0
					|| item.state == null || item.state.Length < 2)
					continue;
				ushort available = System.BitConverter.ToUInt16(item.state, 0);
				if (available == 0 || available > asset.fuel)
					continue;
				ushort amount = (ushort)Mathf.Min((int)requestedUnits, (int)available);
				if (amount == 0)
					continue;
				ushort remaining = (ushort)(available - amount);
				if (remaining == 0 && asset.shouldDeleteAfterFillingTarget)
				{
					slots.RemoveAt(i);
					slotInstanceIDs.RemoveAt(i);
				}
				else
				{
					byte[] newState = (byte[])item.state.Clone();
					byte[] fuelBytes = System.BitConverter.GetBytes(remaining);
					newState[0] = fuelBytes[0];
					newState[1] = fuelBytes[1];
					slots[i] = new Item(item.id, item.amount, item.quality, newState);
				}
				spentUnits = amount;
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

		/// <summary>Server-authoritative clothing wear for the exact equipped
		/// inventory item. Mirrors native player armor quality loss: one point
		/// per protected verified hit, only if configured by Unturned.
		/// Item stays in inventory and retains identity/state for saves/loot.
		/// </summary>
		public bool TryDegradeWornClothingQualityServer(int slot, ushort expectedID)
		{
			if (!Provider.isServer || slot < 0 || slot >= slots.Count
				|| expectedID == 0 || !Provider.modeConfigData.Items.ShouldClothingTakeDamage)
				return false;
			Item stored = slots[slot];
			if (stored == null || stored.id != expectedID || stored.quality == 0)
				return false;
			ItemClothingAsset clothing = Assets.find(EAssetType.ITEM, stored.id) as ItemClothingAsset;
			if (clothing == null)
				return false;
			slots[slot] = new Item(stored.id, stored.amount,
				(byte)(stored.quality - 1),
				stored.state != null ? (byte[])stored.state.Clone() : new byte[0]);
			return true;
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
