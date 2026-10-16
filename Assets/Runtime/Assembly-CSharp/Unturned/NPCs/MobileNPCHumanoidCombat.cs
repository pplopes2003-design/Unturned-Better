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
		private MobileNPCInventory inventory;
		private MobileNPCHumanoidRangedCombat rangedCombat;
		public bool LastAttackUsedMeleeWeapon { get; private set; }
		public ushort LastAttackWeaponID { get; private set; }
		private float nextAttackTime;
		private float nextCombatCheckTime;
		private readonly RaycastHit[] lineOfSightHits = new RaycastHit[16];

		public bool IsAttackReady => Provider.isServer && Time.time >= nextAttackTime;
		public event System.Action AttackStarted;

		private void Awake()
		{
			brainDriver = GetComponent<MobileNPCBrainDriver>();
			inventory = GetComponent<MobileNPCInventory>();
			rangedCombat = GetComponent<MobileNPCHumanoidRangedCombat>();
		}

		private void Update()
		{
			MobileNPCHumanoidMotor motor = GetComponent<MobileNPCHumanoidMotor>();
			if (motor != null && (motor.IsClimbingLadder || motor.IsVaultTraversing))
				return; // both hands occupied by physical climb or parapet clamber
			if (!Provider.isServer || brainDriver == null || brainDriver.CurrentState != MobileNPCBrain.State.Pursuing
				|| brainDriver.IsAmbushPreparing)
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
				targetNPC = targetTransform.GetComponentInParent<MobileNPCHumanoidDisposition>();
			Zombie targetZombie = targetTransform.GetComponent<Zombie>();
			if (targetZombie == null)
				targetZombie = targetTransform.GetComponentInParent<Zombie>();

			MobileNPCHumanoidVitals targetVitals = targetNPC != null ? targetNPC.GetComponent<MobileNPCHumanoidVitals>() : null;
			float currentReach = GetMeleeReachServer();
			// A wounded survivor should keep retreating, not turn a retreat
			// into a pursuit. But if a zombie has reached striking distance,
			// allow a verified short defensive melee hit while escaping.
			// Never override player/faction targeting or target visibility.
			if (brainDriver.CurrentGoal == MobileNPCTactics.Goal.Retreat
				&& (targetZombie == null || targetZombie.isDead
					|| (targetZombie.transform.position - transform.position).sqrMagnitude
						> currentReach * currentReach))
				return;

			if (targetPlayer != null)
			{
				MobileNPCHumanoidDisposition own =
					GetComponent<MobileNPCHumanoidDisposition>();
				if (own == null || !own.CanEngagePlayer(targetPlayer)
					|| targetPlayer.life == null || targetPlayer.life.isDead)
					return;
			}
			else if (targetNPC != null)
			{
				if (targetVitals == null || targetVitals.IsDead
					|| !MobileNPCHumanoidDisposition.AreHostile(GetComponent<MobileNPCHumanoidDisposition>(), targetNPC))
					return;
			}
			else if (targetZombie == null || targetZombie.isDead)
			{
				return;
			}

			Vector3 origin = transform.position + Vector3.up * lineOfSightHeight;
			Vector3 targetPoint = targetTransform.position + Vector3.up * lineOfSightHeight;
			Vector3 offset = targetPoint - origin;
			if (offset.sqrMagnitude > currentReach * currentReach || offset.sqrMagnitude < 0.0001f)
			{
				return;
			}

			int hitCount = Physics.RaycastNonAlloc(origin, offset.normalized, lineOfSightHits,
				currentReach, lineOfSightMask, QueryTriggerInteraction.Ignore);
			float nearestDistance = float.PositiveInfinity;
			Transform nearestTransform = null;
			if (hitCount == lineOfSightHits.Length)
			{
				// A saturated NonAlloc buffer is not guaranteed to contain the nearest
				// collider, so use the allocating fallback only for this rare case.
				RaycastHit[] overflowHits = Physics.RaycastAll(origin, offset.normalized,
					currentReach, lineOfSightMask, QueryTriggerInteraction.Ignore);
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

			// Normally the nearest body collider must confirm the target. Some zombie
			// prefabs expose a non-standard collider hierarchy, so permit an unobstructed
			// linecast fallback for zombies only; players and NPCs remain fail-closed.
			bool unobstructedZombieFallback = false;
			if (nearestTransform == null)
			{
				unobstructedZombieFallback = targetZombie != null
					&& !Physics.Linecast(origin, targetPoint, lineOfSightMask, QueryTriggerInteraction.Ignore);
				if (!unobstructedZombieFallback)
					return;
			}

			if (targetPlayer != null)
			{
				Player hitPlayer = nearestTransform.GetComponent<Player>();
				if (hitPlayer == null) hitPlayer = nearestTransform.GetComponentInParent<Player>();
				if (hitPlayer != targetPlayer) return;
			}
			else if (targetNPC != null)
			{
				MobileNPCHumanoidDisposition hitNPC = nearestTransform.GetComponent<MobileNPCHumanoidDisposition>();
				if (hitNPC == null) hitNPC = nearestTransform.GetComponentInParent<MobileNPCHumanoidDisposition>();
				if (hitNPC != targetNPC) return;
			}
			else if (!unobstructedZombieFallback)
			{
				Zombie hitZombie = nearestTransform.GetComponent<Zombie>();
				if (hitZombie == null) hitZombie = nearestTransform.GetComponentInParent<Zombie>();
				if (hitZombie != targetZombie) return;
			}

			// Avoid an unrealistic simultaneous full-auto burst and heavy melee
			// hit except when an enemy is physically crowding the NPC.
			if (rangedCombat != null && rangedCombat.SelectedGunAsset != null
				&& rangedCombat.RoundsRemaining > 0 && !rangedCombat.IsReloading
				&& offset.sqrMagnitude > 1.15f * 1.15f
				&& !brainDriver.HoldRangedFireForCloseAssault)
				return;

			if (targetPlayer != null)
				TryAttack(targetPlayer, offset.normalized);
			else if (targetNPC != null)
				TryAttack(targetNPC, offset.normalized, nearestTransform);
			else
				TryAttack(targetZombie, offset.normalized);
		}

		/// <summary>Attacks come from actually owned weapons or natural fists.
		/// Never instantiate imaginary equipment. The vanilla melee range is
		/// capped for collision safety against thin walls and doors.</summary>
		private float GetMeleeReachServer()
		{
			if (inventory != null && inventory.TryGetBestMeleeServer(out Item held,
				out ItemMeleeAsset melee))
				return Mathf.Clamp(melee.range, 1.2f, 3.0f);
			return attackRange;
		}

		private float ResolveAttackDamageServer(bool playerTarget,
			out ItemMeleeAsset selected, out Item heldItem)
		{
			selected = null;
			heldItem = null;
			if (inventory == null
				|| !inventory.TryGetBestMeleeServer(out Item item, out ItemMeleeAsset melee))
				return attackDamage;
			IDamageMultiplier multiplier = playerTarget
				? (IDamageMultiplier)melee.playerDamageMultiplier
				: melee.zombieOrPlayerDamageMultiplier;
			if (multiplier == null)
				return attackDamage;
			float damage = multiplier.multiply(ELimb.SPINE);
			if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
				return attackDamage;
			float condition = item.quality / 100f;
			damage *= condition < 0.5f ? 0.5f + condition : 1f;
			selected = melee;
			heldItem = item;
			return damage;
		}

		private void CommitStrikeServer(ItemMeleeAsset weapon, Item heldItem)
		{
			nextAttackTime = Time.time + Mathf.Max(0.35f, attackCooldown);
			LastAttackUsedMeleeWeapon = weapon != null;
			LastAttackWeaponID = heldItem != null ? heldItem.id : (ushort)0;
			AttackStarted?.Invoke();
		}

		private void WearWeaponOnHitServer(ItemMeleeAsset weapon, Item heldItem)
		{
			if (weapon != null && heldItem != null && inventory != null)
				inventory.TryWearOwnedMeleeServer(heldItem.id);
		}

		/// <summary>Damages an opposing mobile NPC using its server-authoritative vitals.</summary>
		public bool TryAttack(MobileNPCHumanoidDisposition targetNPC, Vector3 attackDirection, Transform hitTransform = null)
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
			// Apply one health debit and one physical region injury, just like
			// verified player punches. The real first-hit collider from the
			// preceding ray is preserved when called from this behavior.
			Vector3 direction = attackDirection.sqrMagnitude > 0.0001f
				? attackDirection.normalized : transform.forward;
			Vector3 strikePoint = hitTransform != null ? hitTransform.position
				: targetNPC.transform.position + Vector3.up * 1.05f;
			return targetVitals.ApplyValidatedWeaponImpactServer(
				attackDamage, strikePoint, direction, transform, false, hitTransform);
		}

		/// <summary>Damages a vanilla zombie using the game's own server-side zombie damage pipeline.</summary>
		public bool TryAttack(Zombie targetZombie, Vector3 attackDirection)
		{
			if (!Provider.isServer || targetZombie == null || targetZombie.isDead || Time.time < nextAttackTime
				|| (targetZombie.transform.position - transform.position).sqrMagnitude > attackRange * attackRange)
				return false;

			nextAttackTime = Time.time + attackCooldown;
			AttackStarted?.Invoke();
			ushort damage = (ushort)Mathf.Clamp(Mathf.RoundToInt(attackDamage), 1, ushort.MaxValue);
			targetZombie.askDamage(damage, attackDirection.sqrMagnitude > 0.0001f
				? attackDirection.normalized * damage : transform.forward * damage,
				out _, out _, trackKill: false, dropLoot: true);
			return true;
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
			if (!Provider.isServer || ownDisposition == null || !ownDisposition.CanEngagePlayer(targetPlayer)
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
