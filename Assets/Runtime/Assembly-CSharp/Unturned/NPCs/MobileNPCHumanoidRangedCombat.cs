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
	public sealed class MobileNPCHumanoidRangedCombat : MonoBehaviour
	{
		[SerializeField, Min(1f)] private float effectiveRange = 45f;
		[SerializeField, Min(0.1f)] private float damage = 18f;
		[SerializeField, Min(0.05f)] private float secondsPerShot = 0.22f;
		[SerializeField, Min(0.1f)] private float reloadDuration = 2.1f;
		[SerializeField, Min(1)] private int magazineCapacity = 24;
		[SerializeField, Min(0.1f)] private float muzzleHeight = 1.45f;
		[SerializeField, Min(0.1f)] private float targetHeight = 1.0f;
		[SerializeField, Min(0f)] private float skill = 0.5f;
		[SerializeField] private LayerMask hitMask = ~0;

		private MobileNPCBrainDriver brain;
		private MobileNPCHumanoidAim aim;
		private int roundsRemaining;
		private float nextShotTime;
		private float reloadFinishedAt;
		private bool reloadPending;

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
			roundsRemaining = Mathf.Max(1, magazineCapacity);
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
				ReloadFinished?.Invoke();
			}

			if (brain.CurrentState != MobileNPCBrain.State.Pursuing || Time.time < nextShotTime)
			{
				return;
			}

			Player target = ResolveTargetPlayer(brain.Target);
			if (target == null || target.life == null || target.life.isDead)
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

			nextShotTime = Time.time + Mathf.Max(0.05f, secondsPerShot);
			roundsRemaining--;
			ShotFired?.Invoke();

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