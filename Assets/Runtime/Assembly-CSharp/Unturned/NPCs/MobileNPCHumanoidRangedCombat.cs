////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-authoritative baseline hitscan ranged combat for mobile humanoid NPCs.
	/// This is deliberately independent of UseableGun: that class is tightly coupled to a
	/// real Player's equipment, animator, inventory state, and owner-client networking.
	/// A prefab can opt in by adding this component alongside MobileNPCHumanoidAim.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	[RequireComponent(typeof(MobileNPCHumanoidAim))]
	[RequireComponent(typeof(MobileNPCHumanoidPerception))]
	public sealed class MobileNPCHumanoidRangedCombat : MonoBehaviour
	{
		[SerializeField, Min(1f)] private float effectiveRange = 45f;
		[SerializeField, Min(0.1f)] private float damage = 18f;
		[SerializeField, Min(0.05f)] private float secondsPerShot = 0.22f;
		[SerializeField, Range(0f, 0.35f)] private float shotIntervalJitter = 0.12f;
		[SerializeField, Min(1)] private int minimumBurstSize = 2;
		[SerializeField, Min(1)] private int maximumBurstSize = 5;
		[SerializeField, Min(0f)] private float minimumBurstPause = 0.45f;
		[SerializeField, Min(0f)] private float maximumBurstPause = 1.15f;
		[SerializeField, Min(0.1f)] private float reloadDuration = 2.1f;
		[SerializeField, Min(1)] private int magazineCapacity = 24;
		[SerializeField, Min(0.1f)] private float muzzleHeight = 1.45f;
		[SerializeField, Min(0.1f)] private float targetHeight = 1.0f;
		[SerializeField, Min(0f)] private float skill = 0.5f;
		[SerializeField] private LayerMask hitMask = ~0;

		private MobileNPCBrainDriver brain;
		private MobileNPCHumanoidAim aim;
		private MobileNPCHumanoidPerception perception;
		private int roundsRemaining;
		private float nextShotTime;
		private float reloadFinishedAt;
		private bool reloadPending;
		private int shotsRemainingInBurst;
		private bool waitingForBurstPause;

		/// <summary>Raised on the server when a shot is committed. Hook prefab audio/animation here.</summary>
		public event System.Action ShotFired;
		/// <summary>Raised on the server when an empty magazine starts reloading.</summary>
		public event System.Action ReloadStarted;
		/// <summary>Raised on the server when the reload finishes.</summary>
		public event System.Action ReloadFinished;

		public int RoundsRemaining => roundsRemaining;
		public bool IsReloading => Provider.isServer && reloadPending;

		private void Awake()
		{
			brain = GetComponent<MobileNPCBrainDriver>();
			aim = GetComponent<MobileNPCHumanoidAim>();
			perception = GetComponent<MobileNPCHumanoidPerception>();
			roundsRemaining = Mathf.Max(1, magazineCapacity);
			shotsRemainingInBurst = RollBurstSize();
		}

		private void Update()
		{
			if (!Provider.isServer || brain == null || aim == null)
			{
				return;
			}

			if (reloadPending)
			{
				if (Time.time < reloadFinishedAt)
				{
					return;
				}

				reloadPending = false;
				roundsRemaining = Mathf.Max(1, magazineCapacity);
				shotsRemainingInBurst = RollBurstSize();
				waitingForBurstPause = false;
				ReloadFinished?.Invoke();
			}

			if (brain.CurrentState != MobileNPCBrain.State.Pursuing || Time.time < nextShotTime)
			{
				return;
			}

			if (waitingForBurstPause)
			{
				waitingForBurstPause = false;
				shotsRemainingInBurst = RollBurstSize();
			}

			Player target = ResolveTargetPlayer(brain.Target);
			if (target == null || target.life == null || target.life.isDead
				|| perception != null && perception.VisiblePlayer != target)
			{
				return;
			}

			Vector3 muzzle = transform.position + Vector3.up * muzzleHeight;
			Vector3 targetPoint = target.transform.position + Vector3.up * targetHeight;
			Vector3 toTarget = targetPoint - muzzle;
			if (toTarget.sqrMagnitude > effectiveRange * effectiveRange || toTarget.sqrMagnitude < 0.01f)
			{
				return;
			}

			if (!aim.TryGetShotDirection(muzzle, IsMoving(), skill, out Vector3 shotDirection))
			{
				return;
			}

			if (roundsRemaining <= 0)
			{
				BeginReload();
				return;
			}

			roundsRemaining--;
			ShotFired?.Invoke();

			shotsRemainingInBurst = Mathf.Max(0, shotsRemainingInBurst - 1);
			if (shotsRemainingInBurst == 0)
			{
				waitingForBurstPause = true;
				nextShotTime = Time.time + Random.Range(
					Mathf.Min(minimumBurstPause, maximumBurstPause),
					Mathf.Max(minimumBurstPause, maximumBurstPause));
			}
			else
			{
				float intervalScale = Random.Range(1f - shotIntervalJitter, 1f + shotIntervalJitter);
				nextShotTime = Time.time + Mathf.Max(0.05f, secondsPerShot * intervalScale);
			}

			// Ignore this NPC's own colliders, then process the first external obstruction/hit.
			RaycastHit[] hits = Physics.RaycastAll(muzzle, shotDirection, effectiveRange, hitMask, QueryTriggerInteraction.Ignore);
			System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
			for (int i = 0; i < hits.Length; ++i)
			{
				RaycastHit hit = hits[i];
				if (hit.transform == transform || hit.transform.IsChildOf(transform))
				{
					continue;
				}

				Player hitPlayer = hit.transform.GetComponent<Player>();
				if (hitPlayer == null)
				{
					hitPlayer = hit.transform.GetComponentInParent<Player>();
				}

				if (hitPlayer != null && hitPlayer.life != null && !hitPlayer.life.isDead)
				{
					DamagePlayerParameters parameters = new DamagePlayerParameters(hitPlayer);
					parameters.cause = EDeathCause.GUN;
					parameters.limb = ELimb.SPINE;
					parameters.killer = Provider.server;
					parameters.direction = shotDirection;
					parameters.damage = damage;
					parameters.respectArmor = true;
					EPlayerKill kill;
					DamageTool.damagePlayer(parameters, out kill);
				}

				break;
			}

			if (roundsRemaining == 0)
			{
				BeginReload();
			}
		}

		private int RollBurstSize()
		{
			int minimum = Mathf.Max(1, Mathf.Min(minimumBurstSize, maximumBurstSize));
			int maximum = Mathf.Max(minimum, Mathf.Max(minimumBurstSize, maximumBurstSize));
			return Random.Range(minimum, maximum + 1);
		}

		private void BeginReload()
		{
			if (reloadPending)
			{
				return;
			}

			reloadPending = true;
			reloadFinishedAt = Time.time + Mathf.Max(0.1f, reloadDuration);
			ReloadStarted?.Invoke();
		}

		private bool IsMoving()
		{
			MobileNPCHumanoidMotor motor = GetComponent<MobileNPCHumanoidMotor>();
			return motor != null && motor.IsMoving;
		}

		private static Player ResolveTargetPlayer(Transform target)
		{
			if (target == null)
			{
				return null;
			}

			Player player = target.GetComponent<Player>();
			return player != null ? player : target.GetComponentInParent<Player>();
		}
	}
}