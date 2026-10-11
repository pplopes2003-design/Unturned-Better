////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Explicit server-side spawn entry point for opt-in mobile humanoid prefabs.
	/// This is not a network spawn: client visibility must not be assumed until the
	/// entity has been integrated with the game's generated replication protocol.
	/// Legacy/static NPC prefabs are never loaded or modified by this service.
	/// </summary>
	public static class MobileNPCSpawnService
	{
		public static bool TrySpawnServer(GameObject prefab, Vector3 position, Quaternion rotation,
			MobileNPCHumanoidDisposition.Role role, out GameObject instance, out string error)
		{
			instance = null;
			error = null;

			if (!Provider.isServer)
			{
				error = "Mobile humanoid NPCs can only be spawned by the server.";
				return false;
			}

			if (prefab == null)
			{
				error = "A mobile humanoid prefab is required.";
				return false;
			}

			if (!Level.checkSafeIncludingClipVolumes(position))
			{
				error = "The requested NPC position is outside safe level bounds or inside a player clip volume.";
				return false;
			}

			// Validate the prefab before instantiating it, so invalid assets do not leave
			// partially configured objects in the scene.
			if (prefab.GetComponent<CharacterController>() == null
				|| prefab.GetComponent<MobileNPCBrainDriver>() == null
				|| prefab.GetComponent<MobileNPCHumanoidMotor>() == null
				|| prefab.GetComponent<MobileNPCHumanoidPerception>() == null
				|| prefab.GetComponent<MobileNPCHumanoidDisposition>() == null)
			{
				error = "Prefab must include CharacterController, MobileNPCBrainDriver, MobileNPCHumanoidMotor, MobileNPCHumanoidPerception and MobileNPCHumanoidDisposition on its root.";
				return false;
			}

			GameObject spawned = Object.Instantiate(prefab, position, rotation);
			MobileNPCHumanoidDisposition disposition = spawned.GetComponent<MobileNPCHumanoidDisposition>();
			MobileNPCHumanoidMotor motor = spawned.GetComponent<MobileNPCHumanoidMotor>();
			MobileNPCHumanoidVitals vitals = spawned.GetComponent<MobileNPCHumanoidVitals>();
			disposition.SetRoleServer(role);
			motor.InitializeServer();
			// Initialize health synchronously so callers never receive a spawned NPC with
			// uninitialized server vitals while waiting for Unity's Start callback.
			if (vitals != null)
			{
				vitals.InitializeServer();
			}

			if (!motor.IsInitialized)
			{
				Object.Destroy(spawned);
				error = "The mobile humanoid motor failed to initialize on the server.";
				return false;
			}

			instance = spawned;
			return true;
		}

		/// <summary>
		/// Destroys a mobile NPC owned by the server. This is local server lifecycle
		/// cleanup only; it does not broadcast a network despawn to clients.
		/// </summary>
		public static bool TryDespawnServer(GameObject instance, out string error)
		{
			error = null;

			if (!Provider.isServer)
			{
				error = "Mobile humanoid NPCs can only be despawned by the server.";
				return false;
			}

			if (instance == null)
			{
				error = "A mobile humanoid NPC instance is required.";
				return false;
			}

			// Require the mobile-NPC component signature rather than allowing this
			// helper to destroy an arbitrary GameObject or a legacy static NPC.
			if (instance.GetComponent<MobileNPCBrainDriver>() == null
				|| instance.GetComponent<MobileNPCHumanoidMotor>() == null
				|| instance.GetComponent<MobileNPCHumanoidDisposition>() == null)
			{
				error = "The instance is not a recognized mobile humanoid NPC.";
				return false;
			}

			Object.Destroy(instance);
			return true;
		}
	}
}
