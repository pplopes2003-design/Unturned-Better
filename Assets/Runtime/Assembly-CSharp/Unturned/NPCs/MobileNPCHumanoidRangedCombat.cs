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

		public int RoundsRemaining => roundsRemaining;
		public bool IsReloading => Provider.isServer && Time.time < reloadFinishedAt;

		private void Awake()
		{
			brain = GetComponent<MobileNPCBrainDriver>();
			aim = GetComponent<MobileNPCHumanoidAim>();
			roundsRemaining = Mathf.Max(1, magazineCapacity);
		}

		private void Update()
		{
			if (!Provider.isServer || brain == null || aim == null
				|| brain.CurrentState != MobileNPCBrain.State.Pursuing)
			{
				return;
			}

			if (Time.time < reloadFinishedAt || Time.time < nextShotTime)
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

			nextShotTime = Time.time + Mathf.Max(0.05f, secondsPerShot);
			if (roundsRemaining <= 0)
			{
				reloadFinishedAt = Time.time + Mathf.Max(0.1f, reloadDuration);
				roundsRemaining = Mathf.Max(1, magazineCapacity);
				return;
			}

			roundsRemaining--;
			if (Physics.Raycast(muzzle, shotDirection, out RaycastHit hit, effectiveRange, hitMask, QueryTriggerInteraction.Ignore))
			{
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
			}

			if (roundsRemaining == 0)
			{
				reloadFinishedAt = Time.time + Mathf.Max(0.1f, reloadDuration);
				roundsRemaining = Mathf.Max(1, magazineCapacity);
			}
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