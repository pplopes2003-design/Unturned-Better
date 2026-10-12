////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Local single-player snapshot persistence for mobile NPCs. Stores living NPC transforms,
	/// role/faction and simulated inventory in a map-keyed JSON file. This does not replace
	/// vanilla world persistence for structures and does not provide multiplayer replication.
	/// </summary>
	internal static class MobileNPCPersistence
	{
		private const int CurrentVersion = 2;
		private const float SaveInterval = 30f;
		private const float DeferredRestoreInterval = 3f;
		private static float nextSaveTime;
		private static float nextDeferredRestoreTime;
		private static bool restoredForCurrentMap;
		private static bool preserveExistingSnapshot;
		private static string currentMapKey;
		private static readonly List<NPCRecord> FailedRestoreRecords = new List<NPCRecord>();

		[Serializable]
		private sealed class SaveFile
		{
			public int version = CurrentVersion;
			public string mapKey;
			public List<NPCRecord> npcs = new List<NPCRecord>();
		}

		[Serializable]
		private sealed class NPCRecord
		{
			public float px, py, pz;
			public float rx, ry, rz, rw;
			public int role;
			public int faction;
			public int groupId;
			public float health;
			public bool hasSettlement;
			public float ax, ay, az;
			public List<ItemRecord> items = new List<ItemRecord>();
		}

		[Serializable]
		private sealed class ItemRecord
		{
			public int id;
			public int amount;
			public int quality;
			public byte[] state;
		}

		/// <summary>
		/// Flushes the outgoing map snapshot before bootstrap teardown clears runtime state.
		/// Uses the remembered map key because Level.info may already refer to the incoming map.
		/// </summary>
		public static void PrepareForMapTransitionServer()
		{
			if (!Provider.isServer || !restoredForCurrentMap || string.IsNullOrEmpty(currentMapKey))
				return;

			// This callback currently runs from the post-load bootstrap. If the old scene
			// has already destroyed its NPC objects, do not overwrite the outgoing snapshot
			// with an accidental empty list; retain the last known-good save instead.
			MobileNPCRegistry.CopyActiveDrivers(ActiveDriverScratch);
			if (ActiveDriverScratch.Count == 0 && FailedRestoreRecords.Count == 0)
				return;

			SaveServer(currentMapKey);
		}

		public static void ResetForLoadedMapServer()
		{
			if (!Provider.isServer)
				return;

			currentMapKey = null;
			restoredForCurrentMap = false;
			preserveExistingSnapshot = false;
			FailedRestoreRecords.Clear();
			nextSaveTime = 0f;
			nextDeferredRestoreTime = 0f;
		}

		public static void TickServer(GameObject prefab, int maximumRestoredNPCs)
		{
			if (!Provider.isServer || !Level.isLoaded || prefab == null)
				return;

			string mapKey = GetCurrentMapKey();
			if (!string.Equals(currentMapKey, mapKey, StringComparison.Ordinal))
			{
				currentMapKey = mapKey;
				restoredForCurrentMap = false;
				preserveExistingSnapshot = false;
				FailedRestoreRecords.Clear();
				nextSaveTime = Time.time + SaveInterval;
				nextDeferredRestoreTime = Time.time + DeferredRestoreInterval;
			}

			if (!restoredForCurrentMap)
			{
				RestoreServer(prefab, mapKey, Mathf.Clamp(maximumRestoredNPCs, 1, 40));
				restoredForCurrentMap = true;
				nextSaveTime = Time.time + SaveInterval;
				return;
			}

			// Resume deferred snapshot records as population slots become available. One
			// attempt per tick keeps restoration bounded and prevents a burst of instantiation.
			if (FailedRestoreRecords.Count > 0 && Time.time >= nextDeferredRestoreTime)
			{
				nextDeferredRestoreTime = Time.time + DeferredRestoreInterval;
				RestorePendingRecordsServer(prefab, Mathf.Clamp(maximumRestoredNPCs, 1, 40));
			}

			if (Time.time >= nextSaveTime)
			{
				nextSaveTime = Time.time + SaveInterval;
				SaveServer(mapKey);
			}
		}

		public static void SaveOnApplicationQuitServer()
		{
			if (Provider.isServer && Level.isLoaded && restoredForCurrentMap && !string.IsNullOrEmpty(currentMapKey))
				SaveServer(currentMapKey);
		}

		private static void RestoreServer(GameObject prefab, string mapKey, int maximumRestoredNPCs)
		{
			string path = GetSavePath(mapKey);
			FailedRestoreRecords.Clear();
			if (!File.Exists(path) && !File.Exists(path + ".bak"))
				return;

			try
			{
				SaveFile save = LoadSaveFile(path, mapKey);
				if (save == null || save.npcs == null)
				{
					// Do not let the next periodic save replace an unreadable snapshot with
					// an empty file. Keep the source files untouched for manual recovery.
					preserveExistingSnapshot = true;
					Debug.LogWarning("[MobileNPCPersistence] No valid primary or backup snapshot was found for " + path
						+ "; preserving existing files instead of overwriting them.");
					return;
				}
				preserveExistingSnapshot = false;

				MobileNPCRegistry.CopyActiveDrivers(ActiveDriverScratch);
				int activeAtStart = MobileNPCRegistry.CountLivingDrivers();
				int restored = 0;
				// Count already-active mobile NPCs as part of the same population cap. This
				// protects against restoration running after another bootstrap has spawned NPCs.
				// Keep all source records pending until each one has been restored successfully.
				// If a later record throws unexpectedly, periodic saves will not erase the tail.
				FailedRestoreRecords.AddRange(save.npcs);
				for (int i = 0; i < save.npcs.Count; ++i)
				{
					NPCRecord record = save.npcs[i];
					if (record == null || !IsFinite(record.px) || !IsFinite(record.py) || !IsFinite(record.pz))
					{
						FailedRestoreRecords.Remove(record);
						continue;
					}

					if (activeAtStart + restored >= maximumRestoredNPCs)
						continue;

					if (TryRestoreRecordServer(prefab, record, out string error))
					{
						FailedRestoreRecords.Remove(record);
						restored++;
					}
					else
					{
						Debug.LogWarning("[MobileNPCPersistence] Could not restore NPC at ("
							+ record.px + ", " + record.py + ", " + record.pz
							+ "); keeping its snapshot for a later attempt: " + error);
					}
				}

				Debug.Log("[MobileNPCPersistence] Restored " + restored + " of " + save.npcs.Count
					+ " saved mobile NPCs for map '" + mapKey + "'.");
			}
			catch (Exception exception)
			{
				preserveExistingSnapshot = File.Exists(path) || File.Exists(path + ".bak");
				Debug.LogError("[MobileNPCPersistence] Failed to read snapshot '" + path + "': " + exception
					+ (preserveExistingSnapshot ? " Existing snapshot files will be preserved." : string.Empty));
			}
		}

		private static void RestorePendingRecordsServer(GameObject prefab, int maximumAlive)
		{
			MobileNPCRegistry.CopyActiveDrivers(ActiveDriverScratch);
			if (MobileNPCRegistry.CountLivingDrivers() >= maximumAlive || FailedRestoreRecords.Count == 0)
				return;

			// Process exactly one pending record per interval. Keeping this bounded avoids
			// a burst of instantiation and makes the intended one-at-a-time behavior explicit.
			NPCRecord record = FailedRestoreRecords[0];
			if (record == null || !IsFinite(record.px) || !IsFinite(record.py) || !IsFinite(record.pz))
			{
				FailedRestoreRecords.RemoveAt(0);
				return;
			}

			if (TryRestoreRecordServer(prefab, record, out string error))
			{
				FailedRestoreRecords.RemoveAt(0);
				Debug.Log("[MobileNPCPersistence] Restored one deferred NPC snapshot record; "
					+ FailedRestoreRecords.Count + " record(s) remain pending.");
			}
			else
			{
				// Rotate a failed record to the end of the queue. One permanently blocked
				// location must not starve every later snapshot record indefinitely.
				FailedRestoreRecords.RemoveAt(0);
				FailedRestoreRecords.Add(record);
				if (!string.IsNullOrEmpty(error))
					Debug.LogWarning("[MobileNPCPersistence] Deferred NPC restore will be retried later: " + error);
			}
		}

		private static bool TryRestoreRecordServer(GameObject prefab, NPCRecord record, out string error)
		{
			error = null;
			Vector3 position = new Vector3(record.px, record.py, record.pz);
			Quaternion rotation = new Quaternion(record.rx, record.ry, record.rz, record.rw);
			float rotationMagnitudeSquared = rotation.x * rotation.x + rotation.y * rotation.y
				+ rotation.z * rotation.z + rotation.w * rotation.w;
			if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w)
				|| !IsFinite(rotationMagnitudeSquared) || rotationMagnitudeSquared < 0.001f)
			{
				rotation = Quaternion.identity;
			}
			else
			{
				// Snapshot data may contain a non-unit quaternion; normalize it before spawning.
				rotation = Quaternion.Normalize(rotation);
			}

			MobileNPCHumanoidDisposition.Role role = (MobileNPCHumanoidDisposition.Role)record.role;
			if (role != MobileNPCHumanoidDisposition.Role.Hostile
				&& role != MobileNPCHumanoidDisposition.Role.Friendly
				&& role != MobileNPCHumanoidDisposition.Role.Neutral)
				role = MobileNPCHumanoidDisposition.Role.Hostile;

			if (!MobileNPCSpawnService.TrySpawnServer(prefab, position, rotation, role,
				out GameObject instance, out error))
				return false;

			try
			{
				MobileNPCFaction faction = instance.GetComponent<MobileNPCFaction>();
				if (faction != null && record.faction >= (int)MobileNPCFaction.Id.Unaffiliated
					&& record.faction <= (int)MobileNPCFaction.Id.Traders
					&& faction.CurrentFaction != (MobileNPCFaction.Id)record.faction)
					faction.SetFactionServer((MobileNPCFaction.Id)record.faction);
	
				MobileNPCHumanoidVitals restoredVitals = instance.GetComponent<MobileNPCHumanoidVitals>();
				if (restoredVitals != null && IsFinite(record.health) && record.health > 0f)
				{
					float damageToRestore = Mathf.Max(0f, restoredVitals.MaximumHealth - record.health);
					if (damageToRestore > 0f)
						restoredVitals.ApplyDamage(damageToRestore);
				}
	
				MobileNPCGroupMember groupMember = instance.GetComponent<MobileNPCGroupMember>();
				if (groupMember != null && IsFinite(record.ax) && IsFinite(record.ay) && IsFinite(record.az))
				{
					Vector3 savedAnchor = new Vector3(record.ax, record.ay, record.az);
					if (record.groupId > 0)
						groupMember.SetGroupAnchorServer(record.groupId, savedAnchor, record.hasSettlement);
					else if (record.hasSettlement)
						groupMember.SetGroupAnchorServer(savedAnchor);
				}
	
				MobileNPCInventory inventory = instance.GetComponent<MobileNPCInventory>();
				if (inventory != null && record.items != null)
				{
					for (int itemIndex = 0; itemIndex < record.items.Count; ++itemIndex)
					{
						ItemRecord item = record.items[itemIndex];
						if (item == null || item.id <= 0 || item.id > ushort.MaxValue
							|| item.amount <= 0 || item.amount > byte.MaxValue
							|| item.quality < 0 || item.quality > byte.MaxValue)
							continue;
	
						// Do not restore corrupt or obsolete IDs as unusable simulated items.
						// Valid items that exceed capacity still trigger rollback below.
						if (!(Assets.find(EAssetType.ITEM, (ushort)item.id) is ItemAsset))
							continue;

						if (!inventory.TryStoreTransferredItem(new Item((ushort)item.id, (byte)item.amount,
							(byte)item.quality, item.state != null ? (byte[])item.state.Clone() : new byte[0])))
							throw new InvalidOperationException("Saved inventory exceeds capacity or contains an item that cannot be restored.");
					}
				}
			}
			catch (Exception exception)
			{
				// Do not leave a half-restored NPC alive: the snapshot record will be retried.
				// Destroying the instance also lets its group/lifecycle components clean up.
				if (instance != null)
					MobileNPCSpawnService.TryDespawnServer(instance, out _);
				error = "Snapshot metadata could not be applied: " + exception.Message;
				Debug.LogWarning("[MobileNPCPersistence] Rolled back a partially restored NPC: " + exception.Message);
				return false;
			}
			return true;
		}

		private static void SaveServer(string mapKey)
		{
			if (preserveExistingSnapshot)
			{
				Debug.LogWarning("[MobileNPCPersistence] Skipping save for '" + mapKey
					+ "' because an existing snapshot could not be loaded safely.");
				return;
			}

			MobileNPCRegistry.CopyActiveDrivers(ActiveDriverScratch);
			SaveFile save = new SaveFile { mapKey = mapKey };
			for (int i = 0; i < ActiveDriverScratch.Count; ++i)
			{
				MobileNPCBrainDriver driver = ActiveDriverScratch[i];
				if (driver == null || driver.gameObject == null)
					continue;

				MobileNPCHumanoidVitals vitals = driver.GetComponent<MobileNPCHumanoidVitals>();
				if (vitals != null && vitals.IsDead)
					continue;

				MobileNPCHumanoidDisposition disposition = driver.GetComponent<MobileNPCHumanoidDisposition>();
				MobileNPCFaction faction = driver.GetComponent<MobileNPCFaction>();
				NPCRecord record = new NPCRecord
				{
					px = driver.transform.position.x,
					py = driver.transform.position.y,
					pz = driver.transform.position.z,
					rx = driver.transform.rotation.x,
					ry = driver.transform.rotation.y,
					rz = driver.transform.rotation.z,
					rw = driver.transform.rotation.w,
					role = disposition != null ? (int)disposition.CurrentRole : (int)MobileNPCHumanoidDisposition.Role.Hostile,
					faction = faction != null ? (int)faction.CurrentFaction : (int)MobileNPCFaction.Id.Unaffiliated,
					health = vitals != null ? vitals.CurrentHealth : 100f
				};

				MobileNPCGroupMember groupMember = driver.GetComponent<MobileNPCGroupMember>();
				if (groupMember != null)
				{
					record.groupId = groupMember.GroupId;
					Vector3 anchor = groupMember.GroupAnchor;
					record.hasSettlement = groupMember.HasSettlement;
					record.ax = anchor.x;
					record.ay = anchor.y;
					record.az = anchor.z;
				}

				MobileNPCInventory inventory = driver.GetComponent<MobileNPCInventory>();
				if (inventory != null)
				{
					for (int slot = 0; slot < inventory.UsedSlots; ++slot)
					{
						if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
							continue;
						record.items.Add(new ItemRecord
						{
							id = item.id,
							amount = item.amount,
							quality = item.quality,
							state = item.state != null ? (byte[])item.state.Clone() : new byte[0]
						});
					}
				}

				save.npcs.Add(record);
			}

			save.npcs.AddRange(FailedRestoreRecords);

			string path = GetSavePath(mapKey);
			string temporaryPath = path + ".tmp";
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(temporaryPath, JsonUtility.ToJson(save, true));
				if (!File.Exists(path))
				{
					File.Move(temporaryPath, path);
				}
				else
				{
					string backupPath = path + ".bak";
					try
					{
						File.Replace(temporaryPath, path, backupPath);
					}
					catch
					{
						// Compatibility fallback for filesystems without atomic replace support.
						File.Copy(path, backupPath, true);
						File.Delete(path);
						File.Move(temporaryPath, path);
					}
				}
			}
			catch (Exception exception)
			{
				Debug.LogError("[MobileNPCPersistence] Failed to save snapshot '" + path + "': " + exception);
				try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
			}
		}

		private static SaveFile LoadSaveFile(string path, string mapKey)
		{
			string[] candidates = { path, path + ".bak" };
			for (int i = 0; i < candidates.Length; ++i)
			{
				if (!File.Exists(candidates[i]))
					continue;

				try
				{
					SaveFile save = JsonUtility.FromJson<SaveFile>(File.ReadAllText(candidates[i]));
					if (save != null && save.version >= 1 && save.version <= CurrentVersion && save.npcs != null
						&& string.Equals(save.mapKey, mapKey, StringComparison.Ordinal))
					{
						if (i > 0)
						{
							Debug.LogWarning("[MobileNPCPersistence] Recovering NPCs from backup snapshot " + candidates[i]);
							// Repair the primary before the next periodic save, otherwise a later
							// atomic replace could overwrite the good backup with the corrupt file.
							try { File.Copy(candidates[i], path, true); }
							catch (Exception repairException)
							{
								Debug.LogWarning("[MobileNPCPersistence] Could not repair the primary snapshot: " + repairException.Message);
							}
						}
						return save;
					}
				}
				catch (Exception exception)
				{
					Debug.LogWarning("[MobileNPCPersistence] Snapshot could not be read from " + candidates[i] + ": " + exception.Message);
				}
			}
			return null;
		}

		private static readonly List<MobileNPCBrainDriver> ActiveDriverScratch = new List<MobileNPCBrainDriver>(32);

		private static string GetCurrentMapKey()
		{
			string name = Level.info != null ? Level.info.name : "UnknownMap";
			if (string.IsNullOrEmpty(name))
				name = "UnknownMap";

			// Unturned assigns a stable server ID per selected single-player character.
			// Include it so two different characters playing the same map never share NPC state.
			string server = string.IsNullOrEmpty(Provider.serverID) ? "UnknownWorld" : Provider.serverID;
			string key = server + "_" + name;
			char[] invalid = Path.GetInvalidFileNameChars();
			for (int i = 0; i < invalid.Length; ++i)
				key = key.Replace(invalid[i], '_');
			return key;
		}

		private static string GetSavePath(string mapKey)
		{
			return Path.Combine(Application.persistentDataPath, "MobileNPCs", mapKey + ".json");
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}
	}
}