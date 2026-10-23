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
		[SerializeField, Min(0.1f)] private float attackRange = 1.75f;
		[SerializeField, Min(0.1f)] private float attackDamage = 12.0f; // Vanilla unarmed PlayerEquipment: 15 * SPINE(0.8).
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
		// Exact unarmed zombie limb multipliers from PlayerEquipment:
		// 15 base, 0.3/0.3 limbs, 0.6 spine, 1.1 skull.
		private static readonly ZombieDamageMultiplier NativeZombieFist =
			new ZombieDamageMultiplier(15, 0.3f, 0.3f, 0.6f, 1.1f);

		public bool IsAttackReady => Provider.isServer && Time.time >= nextAttackTime;
		public float ZombieMeleeReachServer => Provider.isServer
			? GetMeleeReachServer(false) : attackRange;
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
			if (brainDriver == null || !brainDriver.CanExecuteMeleeAttackServer)
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
			float currentReach = GetMeleeReachServer(targetPlayer != null);
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

			// All melee targets must be in front of the actor, not only zombies.
			// Moving backwards, sideways, or exiting a crouch must never grant
			// a behind-the-back hit against a player or another NPC.
			// Motor steering turns toward the presently visible selected enemy.
			Vector3 towardVictim = targetTransform.position - transform.position;
			towardVictim.y = 0f;
			Vector3 forward = transform.forward;
			forward.y = 0f;
			if (towardVictim.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.01f
				|| Vector3.Dot(forward.normalized, towardVictim.normalized) < 0.72f)
				return;

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
		private float GetMeleeReachServer(bool againstPlayer)
		{
			if (inventory != null && inventory.TryGetBestMeleeServer(againstPlayer, out Item held,
				out ItemMeleeAsset melee))
				return Mathf.Clamp(melee.range, 0.8f, 3.0f);
			return attackRange;
		}

		private float ResolveAttackDamageServer(bool playerTarget,
			out ItemMeleeAsset selected, out Item heldItem)
		{
			selected = null;
			heldItem = null;
			if (inventory == null
				|| !inventory.TryGetBestMeleeServer(playerTarget, out Item item, out ItemMeleeAsset melee))
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
			// Vary recovery slightly between committed hits instead of a
			// metronomic loop, while retaining the authored animation-safe
			// minimum cadence. This changes timing only, not damage or reach.
			float recoveryVariation = Random.Range(0.94f, 1.12f);
			nextAttackTime = Time.time + Mathf.Max(0.35f, attackCooldown * recoveryVariation);
			LastAttackUsedMeleeWeapon = weapon != null;
			LastAttackWeaponID = heldItem != null ? heldItem.id : (ushort)0;
			// Visual/audio subscribers are optional. One broken animation must
			// never abort validated melee HP damage or make an NPC immortal.
			System.Action listeners = AttackStarted;
			if (listeners != null)
			{
				foreach (System.Delegate observer in listeners.GetInvocationList())
				{
					try { ((System.Action)observer)(); }
					catch (System.Exception ex)
					{
						Debug.LogError("[MobileNPCHumanoidCombat] Optional attack presentation "
							+ "failed; melee damage remains authoritative: " + ex, this);
					}
				}
			}
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

			if ((targetNPC.transform.position - transform.position).sqrMagnitude > GetMeleeReachServer(false) * GetMeleeReachServer(false))
			{
				return false;
			}

			float resolvedDamage = ResolveAttackDamageServer(false,
				out ItemMeleeAsset usedWeapon, out Item usedItem);
			if (resolvedDamage <= 0f)
				return false;
			CommitStrikeServer(usedWeapon, usedItem);
			// Apply one health debit and one physical region injury, just like
			// verified player punches. The real first-hit collider from the
			// preceding ray is preserved when called from this behavior.
			Vector3 direction = attackDirection.sqrMagnitude > 0.0001f
				? attackDirection.normalized : transform.forward;
			Vector3 strikePoint = hitTransform != null ? hitTransform.position
				: targetNPC.transform.position + Vector3.up * 1.05f;
			bool landed = targetVitals.ApplyValidatedWeaponImpactServer(
				resolvedDamage, strikePoint, direction, transform, false, hitTransform);
			if (landed)
				WearWeaponOnHitServer(usedWeapon, usedItem);
			return landed;
		}

		/// <summary>Damages a vanilla zombie using the game's own server-side zombie damage pipeline.</summary>
		public bool TryAttack(Zombie targetZombie, Vector3 attackDirection)
		{
			if (!Provider.isServer || targetZombie == null || targetZombie.isDead || Time.time < nextAttackTime
				|| (targetZombie.transform.position - transform.position).sqrMagnitude > GetMeleeReachServer(false) * GetMeleeReachServer(false))
				return false;

			Vector3 facing = targetZombie.transform.position - transform.position;
			facing.y = 0f;
			if (facing.sqrMagnitude < 0.01f
				|| Vector3.Dot(transform.forward, facing.normalized) < 0.72f)
				return false; // also protect externally triggered melee calls

			float resolvedDamage = ResolveAttackDamageServer(false,
				out ItemMeleeAsset usedWeapon, out Item usedItem);
			if (resolvedDamage <= 0f)
				return false;

			// Back-of-head opening is one surprise hit, not unlimited
			// bonus damage after the zombie has turned and acquired us.
			// Reuse the NATIVE SKULL/SPINE multiplier of the owned melee
			// weapon. Avoid guessed multipliers or an automatic kill.
			bool surpriseFromBehind = !targetZombie.isHunting
				&& Vector3.Dot(targetZombie.transform.forward, facing.normalized) > 0.6f;
			if (surpriseFromBehind)
			{
				IDamageMultiplier limbProfile = usedWeapon != null
					? (IDamageMultiplier)usedWeapon.zombieOrPlayerDamageMultiplier
					: NativeZombieFist;
				float skull = limbProfile.multiply(ELimb.SKULL);
				float spine = limbProfile.multiply(ELimb.SPINE);
				if (skull > spine && spine > 0f)
					resolvedDamage *= skull / spine;
			}
			CommitStrikeServer(usedWeapon, usedItem);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (surpriseFromBehind)
				Debug.Log("[MobileNPCMelee] Rear zombie head-opening npc=" + name
					+ " zombie=" + targetZombie.name + " damage="
					+ resolvedDamage.ToString("F1") + ".", this);
#endif
			ushort damage = (ushort)Mathf.Clamp(Mathf.RoundToInt(resolvedDamage), 1, ushort.MaxValue);
			targetZombie.askDamage(damage, attackDirection.sqrMagnitude > 0.0001f
				? attackDirection.normalized * damage : transform.forward * damage,
				out _, out _, trackKill: false, dropLoot: true);
			// Getting hit from behind is the end of the surprise opening.
			// The vanilla zombie alert decides whether/how to turn and hunt.
			if (surpriseFromBehind && !targetZombie.isDead)
			{
				MobileNPCHumanoidVitals attacker = GetComponent<MobileNPCHumanoidVitals>();
				if (attacker != null)
					targetZombie.alert(attacker);
			}
			WearWeaponOnHitServer(usedWeapon, usedItem);
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
			if (offset.sqrMagnitude > GetMeleeReachServer(true) * GetMeleeReachServer(true))
			{
				return false;
			}

			float resolvedDamage = ResolveAttackDamageServer(true,
				out ItemMeleeAsset usedWeapon, out Item usedItem);
			if (resolvedDamage <= 0f)
				return false;
			CommitStrikeServer(usedWeapon, usedItem);
			DamagePlayerParameters parameters = new DamagePlayerParameters(targetPlayer);
			parameters.cause = usedWeapon != null ? EDeathCause.MELEE : EDeathCause.PUNCH;
			parameters.limb = ELimb.SPINE;
			parameters.killer = Provider.server;
			parameters.direction = attackDirection.sqrMagnitude > 0.0001f ? attackDirection.normalized : transform.forward;
			parameters.damage = resolvedDamage;
			parameters.respectArmor = true;
			EPlayerKill kill;
			DamageTool.damagePlayer(parameters, out kill);
			WearWeaponOnHitServer(usedWeapon, usedItem);
			return true;
		}
	}
}
