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
		private const int CurrentVersion = 1;
		private const float SaveInterval = 30f;
		private static float nextSaveTime;
		private static bool restoredForCurrentMap;
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

		public static void ResetForLoadedMapServer()
		{
			if (!Provider.isServer)
				return;

			currentMapKey = null;
			restoredForCurrentMap = false;
			FailedRestoreRecords.Clear();
			nextSaveTime = 0f;
		}

		public static void TickServer(GameObject prefab)
		{
			if (!Provider.isServer || !Level.isLoaded || prefab == null)
				return;

			string mapKey = GetCurrentMapKey();
			if (!string.Equals(currentMapKey, mapKey, StringComparison.Ordinal))
			{
				currentMapKey = mapKey;
				restoredForCurrentMap = false;
				FailedRestoreRecords.Clear();
				nextSaveTime = Time.time + SaveInterval;
			}

			if (!restoredForCurrentMap)
			{
				RestoreServer(prefab, mapKey);
				restoredForCurrentMap = true;
				nextSaveTime = Time.time + SaveInterval;
				return;
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

		private static void RestoreServer(GameObject prefab, string mapKey)
		{
			string path = GetSavePath(mapKey);
			FailedRestoreRecords.Clear();
			if (!File.Exists(path))
				return;

			try
			{
				SaveFile save = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
				if (save == null || save.version != CurrentVersion || save.npcs == null
					|| !string.Equals(save.mapKey, mapKey, StringComparison.Ordinal))
				{
					Debug.LogWarning("[MobileNPCPersistence] Snapshot version or map key did not match; ignoring " + path);
					return;
				}

				int restored = 0;
				for (int i = 0; i < save.npcs.Count; ++i)
				{
					NPCRecord record = save.npcs[i];
					if (record == null || !IsFinite(record.px) || !IsFinite(record.py) || !IsFinite(record.pz))
						continue;

					Vector3 position = new Vector3(record.px, record.py, record.pz);
					Quaternion rotation = new Quaternion(record.rx, record.ry, record.rz, record.rw);
					if (!IsFinite(rotation.x) || !IsFinite(rotation.y) || !IsFinite(rotation.z) || !IsFinite(rotation.w)
						|| rotation.sqrMagnitude < 0.001f)
						rotation = Quaternion.identity;

					MobileNPCHumanoidDisposition.Role role = (MobileNPCHumanoidDisposition.Role)record.role;
					if (role != MobileNPCHumanoidDisposition.Role.Hostile
						&& role != MobileNPCHumanoidDisposition.Role.Friendly
						&& role != MobileNPCHumanoidDisposition.Role.Neutral)
						role = MobileNPCHumanoidDisposition.Role.Hostile;

					if (!MobileNPCSpawnService.TrySpawnServer(prefab, position, rotation, role, out GameObject instance, out string error))
					{
						// Keep failed records in subsequent snapshots instead of silently deleting them
						// when a later periodic save captures only successfully restored NPCs.
						FailedRestoreRecords.Add(record);
						Debug.LogWarning("[MobileNPCPersistence] Could not restore NPC at " + position + "; keeping its snapshot for a later load: " + error);
						continue;
					}

					MobileNPCFaction faction = instance.GetComponent<MobileNPCFaction>();
					if (faction != null && record.faction >= (int)MobileNPCFaction.Id.Unaffiliated
						&& record.faction <= (int)MobileNPCFaction.Id.Traders
						&& faction.CurrentFaction != (MobileNPCFaction.Id)record.faction)
						faction.SetFactionServer((MobileNPCFaction.Id)record.faction);

					MobileNPCGroupMember groupMember = instance.GetComponent<MobileNPCGroupMember>();
					if (record.hasSettlement && groupMember != null
						&& IsFinite(record.ax) && IsFinite(record.ay) && IsFinite(record.az))
					{
						groupMember.SetGroupAnchorServer(new Vector3(record.ax, record.ay, record.az));
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

							inventory.TryStoreTransferredItem(new Item((ushort)item.id, (byte)item.amount,
								(byte)item.quality, item.state != null ? (byte[])item.state.Clone() : new byte[0]));
						}
					}
					restored++;
				}

				Debug.Log("[MobileNPCPersistence] Restored " + restored + " of " + save.npcs.Count
					+ " saved mobile NPCs for map '" + mapKey + "'.");
			}
			catch (Exception exception)
			{
				Debug.LogError("[MobileNPCPersistence] Failed to read snapshot '" + path + "': " + exception);
			}
		}

		private static void SaveServer(string mapKey)
		{
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
					faction = faction != null ? (int)faction.CurrentFaction : (int)MobileNPCFaction.Id.Unaffiliated
				};

				MobileNPCGroupMember groupMember = driver.GetComponent<MobileNPCGroupMember>();
				if (groupMember != null)
				{
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
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				File.Move(temporaryPath, path);
			}
			catch (Exception exception)
			{
				Debug.LogError("[MobileNPCPersistence] Failed to save snapshot '" + path + "': " + exception);
				try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
			}
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