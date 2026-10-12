////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-local escort contract granted after a successful alliance offer.
	/// The NPC follows the player when out of combat; normal perception and combat
	/// continue to take priority when an opposing mobile NPC is seen.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	public sealed class MobileNPCCompanionContract : MonoBehaviour
	{
		[SerializeField, Min(1f)] private float followStartDistance = 4.0f;
		[SerializeField, Min(0.1f)] private float updateInterval = 0.35f;
		[SerializeField, Min(0.5f)] private float followDistance = 2.0f;

		private MobileNPCBrainDriver driver;
		private Transform playerTarget;
		private float nextUpdateTime;

		public bool IsContractActive => Provider.isServer && playerTarget != null;

		private void Awake()
		{
			driver = GetComponent<MobileNPCBrainDriver>();
		}

		public bool AssignServer(Player player)
		{
			if (!Provider.isServer || player == null || player.life == null || player.life.isDead
				|| GetComponent<MobileNPCHumanoidVitals>() is MobileNPCHumanoidVitals vitals && vitals.IsDead)
				return false;

			playerTarget = player.transform;
			nextUpdateTime = 0f;
			Debug.Log("[MobileNPCCompanionContract] " + name + " accepted an escort contract.");
			return true;
		}

		private void Update()
		{
			if (!Provider.isServer || playerTarget == null || driver == null)
				return;

			Player player = playerTarget.GetComponent<Player>();
			if (player == null || player.life == null || player.life.isDead || !player.gameObject.activeInHierarchy)
			{
				playerTarget = null;
				return;
			}

			if (Time.time < nextUpdateTime)
				return;
			nextUpdateTime = Time.time + Mathf.Max(0.1f, updateInterval);

			// Never overwrite a combat destination. Visible hostile NPCs take priority,
			// while the next tick resumes following once combat perception clears.
			if (driver.Target != null || driver.CurrentState == MobileNPCBrain.State.Pursuing)
				return;

			Vector3 offset = playerTarget.position - transform.position;
			offset.y = 0f;
			if (offset.sqrMagnitude < followStartDistance * followStartDistance)
				return;

			Vector3 behindPlayer = playerTarget.position - playerTarget.forward * Mathf.Max(0.5f, followDistance);
			if (MobileNPCSpawnService.TryFindSafeGroundPosition(behindPlayer, 12f, 8f, ~0, 38f,
				out Vector3 safePoint, out _))
				driver.SetFollowDestinationServer(safePoint);
		}
	}
}