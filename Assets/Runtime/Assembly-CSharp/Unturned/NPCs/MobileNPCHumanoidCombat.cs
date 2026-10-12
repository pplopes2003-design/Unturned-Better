////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
	/// <summary>
	/// Server-authoritative baseline melee combat for a mobile humanoid NPC.
	/// The brain target must already have been validated by server-side perception.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MobileNPCBrainDriver))]
	public sealed class MobileNPCHumanoidCombat : MonoBehaviour
	{
		[SerializeField, Min(0.1f)] private float attackRange = 2.0f;
		[SerializeField, Min(0.1f)] private float attackDamage = 18.0f;
		[SerializeField, Min(0.1f)] private float attackCooldown = 1.4f;
		[SerializeField, Min(0.1f)] private float lineOfSightHeight = 1.1f;
		[SerializeField] private LayerMask lineOfSightMask = ~0;
		[SerializeField, Min(0.02f)] private float combatCheckInterval = 0.1f;

		private MobileNPCBrainDriver brainDriver;
		private float nextAttackTime;
		private float nextCombatCheckTime;
		private readonly RaycastHit[] lineOfSightHits = new RaycastHit[16];

		public bool IsAttackReady => Provider.isServer && Time.time >= nextAttackTime;
		public event System.Action AttackStarted;

		private void Awake()
		{
			brainDriver = GetComponent<MobileNPCBrainDriver>();
		}

		private void Update()
		{
			if (!Provider.isServer || brainDriver == null || brainDriver.CurrentState != MobileNPCBrain.State.Pursuing)
			{
				return;
			}

			if (Time.time < nextCombatCheckTime)
			{
				return;
			}
			nextCombatCheckTime = Time.time + Mathf.Max(0.02f, combatCheckInterval);

			Transform targetTransform = brainDriver.Target;
			if (targetTransform == null)
			{
				return;
			}

			Player targetPlayer = targetTransform.GetComponent<Player>();
			if (targetPlayer == null)
			{
				targetPlayer = targetTransform.GetComponentInParent<Player>();
			}
			MobileNPCHumanoidDisposition targetNPC = targetTransform.GetComponent<MobileNPCHumanoidDisposition>();
			if (targetNPC == null)
			{
				targetNPC = targetTransform.GetComponentInParent<MobileNPCHumanoidDisposition>();
			}

			MobileNPCHumanoidVitals targetVitals = targetNPC != null ? targetNPC.GetComponent<MobileNPCHumanoidVitals>() : null;
			if (targetPlayer != null)
			{
				if (targetPlayer.life == null || targetPlayer.life.isDead)
				{
					return;
				}
			}
			else if (targetNPC == null || targetVitals == null || targetVitals.IsDead
				|| !MobileNPCHumanoidDisposition.AreHostile(GetComponent<MobileNPCHumanoidDisposition>(), targetNPC))
			{
				return;
			}

			Vector3 origin = transform.position + Vector3.up * lineOfSightHeight;
			Vector3 targetPoint = targetTransform.position + Vector3.up * lineOfSightHeight;
			Vector3 offset = targetPoint - origin;
			if (offset.sqrMagnitude > attackRange * attackRange || offset.sqrMagnitude < 0.0001f)
			{
				return;
			}

			int hitCount = Physics.RaycastNonAlloc(origin, offset.normalized, lineOfSightHits,
				attackRange, lineOfSightMask, QueryTriggerInteraction.Ignore);
			float nearestDistance = float.PositiveInfinity;
			Transform nearestTransform = null;
			if (hitCount == lineOfSightHits.Length)
			{
				// A saturated NonAlloc buffer is not guaranteed to contain the nearest
				// collider, so use the allocating fallback only for this rare case.
				RaycastHit[] overflowHits = Physics.RaycastAll(origin, offset.normalized,
					attackRange, lineOfSightMask, QueryTriggerInteraction.Ignore);
				for (int i = 0; i < overflowHits.Length; ++i)
				{
					Transform hitTransform = overflowHits[i].transform;
					if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
						continue;
					if (overflowHits[i].distance < nearestDistance)
					{
						nearestDistance = overflowHits[i].distance;
						nearestTransform = hitTransform;
					}
				}
			}
			else
			{
				for (int i = 0; i < hitCount; ++i)
				{
					Transform hitTransform = lineOfSightHits[i].transform;
					if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
					{
						continue;
					}
					if (lineOfSightHits[i].distance < nearestDistance)
					{
						nearestDistance = lineOfSightHits[i].distance;
						nearestTransform = hitTransform;
					}
				}
			}

			// No collider hit means there is no confirmed line of sight. Never apply melee
			// damage through walls or to a target that has no hittable body collider.
			if (nearestTransform == null)
			{
				return;
			}

			if (targetPlayer != null)
			{
				Player hitPlayer = nearestTransform.GetComponent<Player>();
				if (hitPlayer == null) hitPlayer = nearestTransform.GetComponentInParent<Player>();
				if (hitPlayer != targetPlayer) return;
			}
			else
			{
				MobileNPCHumanoidDisposition hitNPC = nearestTransform.GetComponent<MobileNPCHumanoidDisposition>();
				if (hitNPC == null) hitNPC = nearestTransform.GetComponentInParent<MobileNPCHumanoidDisposition>();
				if (hitNPC != targetNPC) return;
			}

			if (targetPlayer != null)
			{
				TryAttack(targetPlayer, offset.normalized);
			}
			else
			{
				TryAttack(targetNPC, offset.normalized);
			}
		}

		/// <summary>Damages an opposing mobile NPC using its server-authoritative vitals.</summary>
		public bool TryAttack(MobileNPCHumanoidDisposition targetNPC, Vector3 attackDirection)
		{
			MobileNPCHumanoidDisposition ownDisposition = GetComponent<MobileNPCHumanoidDisposition>();
			MobileNPCHumanoidVitals targetVitals = targetNPC != null ? targetNPC.GetComponent<MobileNPCHumanoidVitals>() : null;
			if (!Provider.isServer || targetNPC == null || targetVitals == null || targetVitals.IsDead
				|| !MobileNPCHumanoidDisposition.AreHostile(ownDisposition, targetNPC)
				|| Time.time < nextAttackTime)
			{
				return false;
			}

			if ((targetNPC.transform.position - transform.position).sqrMagnitude > attackRange * attackRange)
			{
				return false;
			}

			nextAttackTime = Time.time + attackCooldown;
			AttackStarted?.Invoke();
			return targetVitals.ApplyDamage(attackDamage, attackDirection.normalized * attackDamage);
		}

		private Player ResolveTargetPlayer()
		{
			Transform target = brainDriver.Target;
			if (target == null)
			{
				return null;
			}

			Player player = target.GetComponent<Player>();
			if (player == null)
			{
				player = target.GetComponentInParent<Player>();
			}

			return player;
		}

		/// <summary>Can also be called by an animation event, but only on the server.</summary>
		public bool TryAttack(Player targetPlayer, Vector3 attackDirection)
		{
			MobileNPCHumanoidDisposition ownDisposition = GetComponent<MobileNPCHumanoidDisposition>();
			if (!Provider.isServer || ownDisposition == null || !ownDisposition.CanEngagePlayers
				|| targetPlayer == null || targetPlayer.life == null || targetPlayer.life.isDead
				|| Time.time < nextAttackTime)
			{
				return false;
			}

			Vector3 offset = targetPlayer.transform.position - transform.position;
			if (offset.sqrMagnitude > attackRange * attackRange)
			{
				return false;
			}

			nextAttackTime = Time.time + attackCooldown;
			AttackStarted?.Invoke();
			DamagePlayerParameters parameters = new DamagePlayerParameters(targetPlayer);
			parameters.cause = EDeathCause.MELEE;
			parameters.limb = ELimb.SPINE;
			parameters.killer = Provider.server;
			parameters.direction = attackDirection.sqrMagnitude > 0.0001f ? attackDirection.normalized : transform.forward;
			parameters.damage = attackDamage;
			parameters.respectArmor = true;
			EPlayerKill kill;
			DamageTool.damagePlayer(parameters, out kill);
			return true;
		}
	}
}
