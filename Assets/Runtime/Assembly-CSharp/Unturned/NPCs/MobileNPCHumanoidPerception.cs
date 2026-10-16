////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-authoritative perception for mobile humanoids.
	/// Prioritizes visible players, then hostile mobile NPCs, then zombies using range,
	/// field-of-view, and unobstructed line-of-sight checks.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	public sealed class MobileNPCHumanoidPerception : MonoBehaviour
	{
		[SerializeField, Min(1f)] private float playerSightRange = 20f;
		[SerializeField, Min(1f)] private float npcSightRange = 32f;
		[SerializeField, Range(1f, 360f)] private float fieldOfView = 120f;
		[SerializeField, Min(0.02f)] private float scanInterval = 0.2f;
		[SerializeField, Min(0.1f)] private float eyeHeight = 1.55f;
		[SerializeField, Min(0.1f)] private float targetHeight = 1.0f;
		[SerializeField] private LayerMask obstructionMask = ~0;

		private MobileNPCBrainDriver brain;
		private MobileNPCHumanoidMotor motor;
		private MobileNPCHumanoidDisposition disposition;
		private float nextScanTime;
		private Player visiblePlayer;
		private MobileNPCHumanoidDisposition visibleNPC;
		private Zombie visibleZombie;
		private readonly RaycastHit[] sightHits = new RaycastHit[16];
		// Shared scratch storage avoids FindObjectsOfType allocations for every NPC scan.
		// Unity gameplay updates run on the main thread, so this list is not concurrently accessed.
		private static readonly List<MobileNPCBrainDriver> candidateDrivers = new List<MobileNPCBrainDriver>(32);

		public Player VisiblePlayer => Provider.isServer ? visiblePlayer : null;
		public MobileNPCHumanoidDisposition VisibleNPC => Provider.isServer ? visibleNPC : null;
		public Zombie VisibleZombie => Provider.isServer ? visibleZombie : null;

		private void Awake()
		{
			brain = GetComponent<MobileNPCBrainDriver>();
			motor = GetComponent<MobileNPCHumanoidMotor>();
			disposition = GetComponent<MobileNPCHumanoidDisposition>();
		}

		private void Update()
		{
			if (!Provider.isServer || brain == null || Time.time < nextScanTime)
			{
				return;
			}

			nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
			if (disposition == null)
			{
				visiblePlayer = null;
				visibleNPC = null;
				visibleZombie = null;
				brain.ResetServer();
				if (motor != null)
				{
					motor.SetTargetVisibleFromServer(false);
				}
				return;
			}

			ClearDeadSelectedTarget();
			// All roles may LOOK at players; only an intrinsically hostile NPC
			// or a recently attacked survivor may SELECT one for combat.
			Player playerCandidate = FindVisiblePlayer();
			MobileNPCHumanoidDisposition npcCandidate = disposition.CanEngageMobileNPCs ? FindVisibleNPC() : null;
			Zombie zombieCandidate = FindVisibleZombie();

			// A critically wounded survivor should break contact with nearby zombies instead
			// of continuing to trade melee hits until death. Only initiate this once per
			// retreat so repeated perception scans do not keep resetting its escape route.
			MobileNPCHumanoidVitals ownVitals = GetComponent<MobileNPCHumanoidVitals>();
			if (playerCandidate == null && npcCandidate == null && zombieCandidate != null
				&& ownVitals != null && ownVitals.HealthFraction <= 0.22f
				&& brain.CurrentGoal != MobileNPCTactics.Goal.Retreat)
			{
				brain.ReportIncomingFire(zombieCandidate.transform.position, zombieCandidate.transform);
			}

			// Players have priority over hostile mobile NPCs. A previously selected
			// player also retains target priority during the brain's short occlusion
			// memory; do not replace that target with a nearer NPC while the player is
			// behind cover. Visibility remains false during occlusion, so aiming/fire
			// integrations must still require fresh line-of-sight evidence.
			if (playerCandidate != null && disposition.CanEngagePlayer(playerCandidate))
			{
				visiblePlayer = playerCandidate;
				visibleNPC = null;
				visibleZombie = null;
				brain.ReportVisibleTarget(playerCandidate.transform);
			}
			else if (HasRememberedPlayerTarget())
			{
				visiblePlayer = null;
				visibleNPC = null;
				visibleZombie = null;
			}
			else if (npcCandidate != null)
			{
				visiblePlayer = null;
				visibleNPC = npcCandidate;
				visibleZombie = null;
				brain.ReportVisibleTarget(npcCandidate.transform);
				MobileNPCGroupMember groupContact = GetComponent<MobileNPCGroupMember>();
				if (groupContact != null)
					groupContact.BroadcastLocalThreatServer(npcCandidate.transform.position);
			}
			else if (zombieCandidate != null)
			{
				visiblePlayer = null;
				visibleNPC = null;
				visibleZombie = zombieCandidate;
				brain.ReportVisibleTarget(zombieCandidate.transform);
				MobileNPCGroupMember zombieGroupContact = GetComponent<MobileNPCGroupMember>();
				if (zombieGroupContact != null)
					zombieGroupContact.BroadcastLocalThreatServer(zombieCandidate.transform.position);
			}
			else if (playerCandidate != null)
			{
				// A nonthreatening player may be seen and recognized without
				// being targeted, stalked, chased or triggering stealth.
				// Actual verified aggression changes CanEngagePlayer separately.
				visiblePlayer = playerCandidate;
				visibleNPC = null;
				visibleZombie = null;
				brain.ReportNonThreateningPlayerSightServer();
			}
			else
			{
				visiblePlayer = null;
				visibleNPC = null;
				visibleZombie = null;
			}

			if (motor != null)
			{
				// Visibility used for combat gait must refer to a legally
				// engageable target, not a friendly player simply nearby.
				motor.SetTargetVisibleFromServer(
					visiblePlayer != null && disposition.CanEngagePlayer(visiblePlayer)
					|| visibleNPC != null || visibleZombie != null);
			}
		}

		private void ClearDeadSelectedTarget()
		{
			if (brain == null || brain.CurrentState != MobileNPCBrain.State.Pursuing || brain.Target == null)
				return;

			Transform selected = brain.Target;
			Player selectedPlayer = selected.GetComponent<Player>();
			if (selectedPlayer == null)
				selectedPlayer = selected.GetComponentInParent<Player>();
			if (selectedPlayer != null)
			{
				if (selectedPlayer.life == null || selectedPlayer.life.isDead
					|| disposition == null || !disposition.CanEngagePlayer(selectedPlayer))
					brain.ResetServer();
				return;
			}

			MobileNPCHumanoidDisposition selectedNPC = selected.GetComponent<MobileNPCHumanoidDisposition>();
			if (selectedNPC == null)
				selectedNPC = selected.GetComponentInParent<MobileNPCHumanoidDisposition>();
			if (selectedNPC != null)
			{
				MobileNPCHumanoidVitals vitals = selectedNPC.GetComponent<MobileNPCHumanoidVitals>();
				if (vitals == null || vitals.IsDead || !selectedNPC.gameObject.activeInHierarchy)
					brain.ResetServer();
				return;
			}

			Zombie selectedZombie = selected.GetComponent<Zombie>();
			if (selectedZombie == null)
				selectedZombie = selected.GetComponentInParent<Zombie>();
			if (selectedZombie != null && (selectedZombie.isDead || !selectedZombie.gameObject.activeInHierarchy))
				brain.ResetServer();
		}

		private bool HasRememberedPlayerTarget()
		{
			if (brain == null || brain.CurrentState != MobileNPCBrain.State.Pursuing || brain.Target == null)
			{
				return false;
			}

			Player selectedPlayer = brain.Target.GetComponent<Player>();
			if (selectedPlayer == null)
			{
				selectedPlayer = brain.Target.GetComponentInParent<Player>();
			}
			return selectedPlayer != null && selectedPlayer.life != null && !selectedPlayer.life.isDead
				&& selectedPlayer.gameObject.activeInHierarchy;
		}

		private MobileNPCHumanoidDisposition FindVisibleNPC()
		{
			MobileNPCRegistry.CopyActiveDrivers(candidateDrivers);
			MobileNPCHumanoidDisposition best = null;
			float bestDistanceSquared = npcSightRange * npcSightRange;
			Vector3 eye = transform.position + Vector3.up * eyeHeight;

			for (int i = 0; i < candidateDrivers.Count; ++i)
			{
				MobileNPCBrainDriver candidateDriver = candidateDrivers[i];
				MobileNPCHumanoidDisposition candidate = candidateDriver != null ? candidateDriver.GetComponent<MobileNPCHumanoidDisposition>() : null;
				if (candidate == null || candidate == disposition || !candidate.isActiveAndEnabled
					|| !candidate.gameObject.activeInHierarchy || !disposition.IsHostileTo(candidate))
				{
					continue;
				}

				MobileNPCHumanoidVitals vitals = candidate.GetComponent<MobileNPCHumanoidVitals>();
				if (vitals == null || vitals.IsDead)
				{
					continue;
				}

				Vector3 target = candidate.transform.position + Vector3.up * targetHeight;
				Vector3 offset = target - eye;
				float distanceSquared = offset.sqrMagnitude;
				if (distanceSquared < 0.01f || distanceSquared > bestDistanceSquared)
				{
					continue;
				}

				Vector3 direction = offset.normalized;
				if (Vector3.Angle(transform.forward, direction) > fieldOfView * 0.5f)
				{
					continue;
				}

				Transform nearestTransform = FindNearestSightTransform(eye, direction, Mathf.Sqrt(distanceSquared));

				// Fail closed when the ray did not confirm a collider before the target.
				// Treating an empty result as visible can make agents shoot through walls
				// when the prefab's obstruction mask is misconfigured.
				if (nearestTransform == null)
				{
					continue;
				}

				if (nearestTransform != null)
				{
					MobileNPCHumanoidDisposition hitNPC = nearestTransform.GetComponent<MobileNPCHumanoidDisposition>();
					if (hitNPC == null)
					{
						hitNPC = nearestTransform.GetComponentInParent<MobileNPCHumanoidDisposition>();
					}
					if (hitNPC != candidate)
					{
						continue;
					}
				}

				best = candidate;
				bestDistanceSquared = distanceSquared;
			}

			return best;
		}

		private Zombie FindVisibleZombie()
		{
			List<Zombie> zombies = ZombieManager.AllZombies;
			if (zombies == null)
				return null;

			Zombie best = null;
			float bestDistanceSquared = npcSightRange * npcSightRange;
			Vector3 eye = transform.position + Vector3.up * eyeHeight;
			for (int i = 0; i < zombies.Count; ++i)
			{
				Zombie candidate = zombies[i];
				if (candidate == null || candidate.isDead || !candidate.gameObject.activeInHierarchy)
					continue;

				Vector3 target = candidate.transform.position + Vector3.up * targetHeight;
				Vector3 offset = target - eye;
				float distanceSquared = offset.sqrMagnitude;
				if (distanceSquared < 0.01f || distanceSquared > bestDistanceSquared)
					continue;

				Vector3 direction = offset.normalized;
				// Close threats are allowed to trigger a defensive response from any side.
				// This prevents a zombie reaching the NPC's back from being ignored solely
				// because the NPC happened to be roaming in the opposite direction.
				if (Vector3.Angle(transform.forward, direction) > fieldOfView * 0.5f
					&& distanceSquared > 64f)
					continue;

				float distance = Mathf.Sqrt(distanceSquared);
				Transform nearestTransform = FindNearestSightTransform(eye, direction, distance);
				if (nearestTransform == null)
				{
					// Some zombie prefabs expose their damage collider differently from the
					// sight ray. Accept only a truly unobstructed ray as a fallback; a wall
					// hit still blocks acquisition.
					if (Physics.Linecast(eye, target, obstructionMask, QueryTriggerInteraction.Ignore))
						continue;
				}
				else
				{
					Zombie hitZombie = nearestTransform.GetComponent<Zombie>();
					if (hitZombie == null)
						hitZombie = nearestTransform.GetComponentInParent<Zombie>();
					if (hitZombie != candidate)
						continue;
				}

				best = candidate;
				bestDistanceSquared = distanceSquared;
			}
			return best;
		}

		private Player FindVisiblePlayer()
		{
			Player best = null;
			float bestDistanceSquared = playerSightRange * playerSightRange;
			Vector3 eye = transform.position + Vector3.up * eyeHeight;

			// Iterate the authoritative connected-player collection instead of allocating
			// a fresh scene-wide Player array for every NPC scan.
			if (Provider.clients == null)
			{
				return null;
			}

			for (int i = 0; i < Provider.clients.Count; ++i)
			{
				SteamPlayer steamPlayer = Provider.clients[i];
				Player candidate = steamPlayer != null ? steamPlayer.player : null;
				if (candidate == null || candidate == GetComponent<Player>()
					|| candidate.life == null || candidate.life.isDead
					|| candidate.gameObject == null || !candidate.gameObject.activeInHierarchy)
				{
					continue;
				}

				Vector3 target = candidate.transform.position + Vector3.up * targetHeight;
				Vector3 offset = target - eye;
				float distanceSquared = offset.sqrMagnitude;
				if (distanceSquared < 0.01f || distanceSquared > bestDistanceSquared)
				{
					continue;
				}

				Vector3 direction = offset.normalized;
				if (Vector3.Angle(transform.forward, direction) > fieldOfView * 0.5f)
				{
					continue;
				}

				Transform nearestTransform = FindNearestSightTransform(eye, direction, Mathf.Sqrt(distanceSquared));

				// Require positive line-of-sight evidence instead of accepting a target
				// when the ray query returns no colliders.
				if (nearestTransform == null)
				{
					continue;
				}

				if (nearestTransform != null)
				{
					Player hitPlayer = nearestTransform.GetComponent<Player>();
					if (hitPlayer == null)
					{
						hitPlayer = nearestTransform.GetComponentInParent<Player>();
					}

					if (hitPlayer != candidate)
					{
						continue;
					}
				}

				best = candidate;
				bestDistanceSquared = distanceSquared;
			}

			return best;
		}
		/// <summary>
		/// Returns the nearest collider along the sight ray, excluding this NPC's own rig.
		/// The allocating fallback is used only when the reusable hit buffer saturates,
		/// because a full NonAlloc buffer does not guarantee it contains the nearest hit.
		/// </summary>
		private Transform FindNearestSightTransform(Vector3 origin, Vector3 direction, float distance)
		{
			int hitCount = Physics.RaycastNonAlloc(origin, direction, sightHits, distance,
				obstructionMask, QueryTriggerInteraction.Ignore);
			float nearestDistance = float.PositiveInfinity;
			Transform nearestTransform = null;

			if (hitCount == sightHits.Length)
			{
				RaycastHit[] overflowHits = Physics.RaycastAll(origin, direction, distance,
					obstructionMask, QueryTriggerInteraction.Ignore);
				for (int i = 0; i < overflowHits.Length; ++i)
				{
					Transform hitTransform = overflowHits[i].transform;
					if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
					{
						continue;
					}
					if (overflowHits[i].distance < nearestDistance)
					{
						nearestDistance = overflowHits[i].distance;
						nearestTransform = hitTransform;
					}
				}
				return nearestTransform;
			}

			for (int i = 0; i < hitCount; ++i)
			{
				Transform hitTransform = sightHits[i].transform;
				if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
				{
					continue;
				}
				if (sightHits[i].distance < nearestDistance)
				{
					nearestDistance = sightHits[i].distance;
					nearestTransform = hitTransform;
				}
			}
			return nearestTransform;
		}

	}
}