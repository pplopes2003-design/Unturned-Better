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

		public bool IsAttackReady => Provider.isServer && Time.time >= nextAttackTime;

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

			Player targetPlayer = ResolveTargetPlayer();
			if (targetPlayer == null || targetPlayer.life == null || targetPlayer.life.isDead)
			{
				return;
			}

			Vector3 origin = transform.position + Vector3.up * lineOfSightHeight;
			Vector3 targetPoint = targetPlayer.transform.position + Vector3.up * lineOfSightHeight;
			Vector3 offset = targetPoint - origin;
			if (offset.sqrMagnitude > attackRange * attackRange || offset.sqrMagnitude < 0.0001f)
			{
				return;
			}

			if (Physics.Raycast(origin, offset.normalized, out RaycastHit hit, attackRange, lineOfSightMask, QueryTriggerInteraction.Ignore)
				&& hit.transform != targetPlayer.transform && !hit.transform.IsChildOf(targetPlayer.transform))
			{
				return;
			}

			TryAttack(targetPlayer, offset.normalized);
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
			if (!Provider.isServer || targetPlayer == null || targetPlayer.life == null || targetPlayer.life.isDead
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
