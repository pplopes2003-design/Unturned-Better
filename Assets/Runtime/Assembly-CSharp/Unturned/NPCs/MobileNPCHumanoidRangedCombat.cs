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
	[RequireComponent(typeof(MobileNPCInventory))]
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
		[SerializeField, Min(0.1f)] private float muzzleHeight = 1.45f;
		[SerializeField, Min(0.1f)] private float targetHeight = 1.0f;
		[SerializeField, Min(0f)] private float skill = 0.5f;
		[SerializeField] private LayerMask hitMask = ~0;

		private MobileNPCBrainDriver brain;
		private MobileNPCHumanoidAim aim;
		private MobileNPCHumanoidPerception perception;
		private MobileNPCInventory inventory;
		private MobileNPCGroupMember groupMember;
		private ItemGunAsset selectedGunAsset;
		private bool selectedGunHasCompatibleMagazine;
		private uint selectedGunInstanceID;
		private ushort selectedGunID;
		private int pendingReloadRounds;
		private ushort pendingReloadMagazineID;
		private Item pendingReloadMagazine;
		private Item pendingReplacedMagazine;
		private int roundsRemaining;
		private float selectedOpticZoom = 1f;
		private float selectedBulletGravityMultiplier = 1f;
		private bool selectedGunSoundSuppressed;
		private float nextShotTime;
		private float reloadFinishedAt;
		private bool reloadPending;
		private int shotsRemainingInBurst;
		private bool waitingForBurstPause;
		// Reused hit buffer avoids allocating and sorting a new RaycastAll array for every shot.
		private readonly RaycastHit[] shotHits = new RaycastHit[64];
		private const float NativeBallisticDelta = 0.02f; // UseableGun.BALLISTICS_DELTA_TIME
		private const int MaxInFlightBullets = 6;
		private const int MaxStepChecksPerActorFrame = 5;
		private const int MaxSharedBulletChecksPerFrame = 100;
		private static int sharedBallisticFrame = -1;
		private static int sharedBallisticChecks;
		private readonly PendingBullet[] pendingBullets = new PendingBullet[MaxInFlightBullets];

		private struct PendingBullet
		{
			public bool Active;
			public Vector3 Position;
			public Vector3 Velocity;
			public Vector3 InitialDirection;
			public float NextStepTime;
			public float DistanceTravelled;
			public float MaximumRange;
			public float GravityMultiplier;
			public float ActualDamage;
			public float IntendedDistance;
			public int RemainingSteps;
			public int Profile;
			public ushort GunId;
			public Player TargetPlayer;
			public MobileNPCHumanoidDisposition TargetNPC;
			public Zombie TargetZombie;
			public MobileNPCHumanoidDisposition OwnDisposition;
			public MobileNPCAdaptiveMemory.ExperienceKind Context;
		}
		

		/// <summary>Raised on the server when a shot is committed. Hook prefab audio/animation here.</summary>
		public event System.Action ShotFired;
		/// <summary>Raised on the server when an empty magazine starts reloading.</summary>
		public event System.Action ReloadStarted;
		/// <summary>Raised on the server when the reload finishes.</summary>
		public event System.Action ReloadFinished;

		public int RoundsRemaining => roundsRemaining;
		public ItemGunAsset SelectedGunAsset => selectedGunAsset;
		public bool IsReloading => Provider.isServer && reloadPending;
		/// <summary>Only while a real visible living target can be engaged
		/// with owned loaded ammunition; defensive orders still supersede.</summary>
		public bool CanHoldDeliberateFiringPositionServer(float distance)
		{
			if (!Provider.isServer || selectedGunAsset == null
				|| selectedGunAsset.projectile != null || reloadPending
				|| !selectedGunHasCompatibleMagazine || roundsRemaining < Mathf.Max(1, selectedGunAsset.ammoPerShot))
				return false;
			float range = selectedOpticZoom >= 2.5f
				? Mathf.Min(115f, Mathf.Min(selectedGunAsset.range,
					effectiveRange * Mathf.Min(2.55f, selectedOpticZoom * 0.65f)))
				: Mathf.Min(effectiveRange, selectedGunAsset.range);
			return distance >= (selectedOpticZoom >= 2.5f ? 19f : 11f)
				&& distance <= range * 0.86f;
		}
		/// <summary>Only an actually mounted sight is an optical advantage.</summary>
		public float OpticZoom => Provider.isServer && selectedGunAsset != null
			&& !reloadPending && roundsRemaining > 0 ? selectedOpticZoom : 1f;
		public float GetScopedObservationRange(float nakedEyeRange)
		{
			return Provider.isServer && OpticZoom >= 2.5f
				&& selectedGunAsset != null && selectedGunAsset.projectile == null
				? Mathf.Min(115f, Mathf.Min(selectedGunAsset.range,
					nakedEyeRange + 12f * Mathf.Min(OpticZoom, 8f)))
				: nakedEyeRange;
		}

		private void Awake()
		{
			brain = GetComponent<MobileNPCBrainDriver>();
			aim = GetComponent<MobileNPCHumanoidAim>();
			perception = GetComponent<MobileNPCHumanoidPerception>();
			inventory = GetComponent<MobileNPCInventory>();
			groupMember = GetComponent<MobileNPCGroupMember>();
			roundsRemaining = 0;
			pendingReloadRounds = 0;
			selectedGunID = 0;
			selectedGunInstanceID = 0;
			selectedGunHasCompatibleMagazine = false;
			shotsRemainingInBurst = RollBurstSize();

			// Add tactical movement only when the ranged-combat component is present.
			// This keeps unarmed/melee NPC prefabs unaffected.
			if (GetComponent<MobileNPCTacticalCombatBehavior>() == null)
				gameObject.AddComponent<MobileNPCTacticalCombatBehavior>();
		}

		private void Update()
		{
			if (!Provider.isServer || brain == null || aim == null || inventory == null)
			{
				return;
			}

			if (!reloadPending && pendingReloadMagazine != null)
				TryReturnPendingMagazine();

			if (!reloadPending)
			{
				RefreshSelectedGun();
			}

			if (reloadPending)
			{
				if (Time.time < reloadFinishedAt) return;

				int loadedRounds = Mathf.Max(0, pendingReloadRounds);
				byte magazineQuality = pendingReloadMagazine != null ? pendingReloadMagazine.quality : (byte)100;
				bool ammoStateUpdated = inventory.TryUpdateGunAmmoServer(selectedGunInstanceID,
					(byte)Mathf.Clamp(loadedRounds, 0, 255), pendingReloadMagazineID, magazineQuality);
				reloadPending = false;
				roundsRemaining = ammoStateUpdated ? loadedRounds : 0;
				selectedGunHasCompatibleMagazine = ammoStateUpdated;
				if (!ammoStateUpdated)
				{
					Debug.LogWarning("Mobile NPC reload aborted because the stored gun state could not be updated; returning the reserved magazine.", this);
					pendingReplacedMagazine = null;
					TryReturnPendingMagazine();
				}
				else
				{
					// Swapping magazines returns the old magazine with its remaining rounds and durability.
					pendingReloadMagazine = pendingReplacedMagazine;
					pendingReplacedMagazine = null;
					if (pendingReloadMagazine != null)
						TryReturnPendingMagazine();
					else
						inventory.ReleaseReservedSlotServer();
				}
				pendingReloadRounds = 0;
				pendingReloadMagazineID = 0;
				shotsRemainingInBurst = RollBurstSize();
				waitingForBurstPause = false;
				ReloadFinished?.Invoke();
				RefreshSelectedGun();
			}

			if (selectedGunAsset == null || selectedGunAsset.projectile != null)
			{
				// Actual launched rocket/grenade prefab weapons need a separate
				// native projectile service, never emulate them as rifle hitscan.
				return;
			}

			// A-Life need -> acquisition -> use: a safe NPC may reload BEFORE
			// battle, using exactly the same owned-magazine transaction as the
			// existing combat reload. Do not create ammunition or pick a second
			// weapon silently while a reservation is being committed.
			if (!reloadPending && !selectedGunAsset.infiniteAmmo
				&& brain.Target == null
				&& brain.CurrentState == MobileNPCBrain.State.Idle
				&& brain.CurrentGoal == MobileNPCTactics.Goal.None
				&& selectedGunInstanceID != 0
				&& roundsRemaining <= Mathf.Max(8, selectedGunAsset.ammoPerShot * 2)
				&& inventory.TryFindCompatibleMagazine(selectedGunAsset,
					out int idleMagazineSlot, out ItemMagazineAsset idleMagazineAsset)
			// Do not discard a partly full magazine for an even emptier one.
			&& inventory.TryGetItemSnapshot(idleMagazineSlot, out Item spareMagazine)
			&& spareMagazine != null && spareMagazine.amount > roundsRemaining)
			{
				MobileNPCHumanoidMotor idleMotor = GetComponent<MobileNPCHumanoidMotor>();
				if (idleMotor == null || (!idleMotor.IsClimbingLadder
					&& !idleMotor.IsVaultTraversing))
				{
					BeginReload();
					if (reloadPending)
						return;
				}
			}

			MobileNPCHumanoidMotor motor = GetComponent<MobileNPCHumanoidMotor>();
			if (motor != null && (motor.IsClimbingLadder || motor.IsVaultTraversing))
				return; // hands occupied by ladder or parapet; no shooting mid-action

			if (brain.CurrentState != MobileNPCBrain.State.Pursuing
				|| brain.IsAmbushPreparing
				|| brain.HoldRangedFireForCloseAssault
				|| brain.CurrentGoal == MobileNPCTactics.Goal.Retreat
				|| Time.time < nextShotTime)
			{
				return;
			}

			if (waitingForBurstPause)
			{
				waitingForBurstPause = false;
				shotsRemainingInBurst = RollBurstSize();
			}

			Transform targetTransform = brain.Target;
			if (targetTransform == null)
			{
				return;
			}

			Player targetPlayer = ResolveTargetPlayer(targetTransform);
			MobileNPCHumanoidDisposition targetNPC = targetTransform.GetComponent<MobileNPCHumanoidDisposition>();
			if (targetNPC == null) targetNPC = targetTransform.GetComponentInParent<MobileNPCHumanoidDisposition>();
			Zombie targetZombie = targetTransform.GetComponent<Zombie>();
			if (targetZombie == null) targetZombie = targetTransform.GetComponentInParent<Zombie>();
			MobileNPCHumanoidDisposition ownDisposition = GetComponent<MobileNPCHumanoidDisposition>();
			MobileNPCHumanoidVitals targetVitals = targetNPC != null ? targetNPC.GetComponent<MobileNPCHumanoidVitals>() : null;
			if (targetPlayer != null)
			{
				if (ownDisposition == null || !ownDisposition.CanEngagePlayer(targetPlayer)
					|| targetPlayer.life == null || targetPlayer.life.isDead
					|| perception.VisiblePlayer != targetPlayer)
					return;
			}
			else if (targetNPC != null)
			{
				if (targetVitals == null || targetVitals.IsDead || perception.VisibleNPC != targetNPC
					|| !MobileNPCHumanoidDisposition.AreHostile(ownDisposition, targetNPC))
					return;
			}
			else if (targetZombie == null || targetZombie.isDead || perception.VisibleZombie != targetZombie)
			{
				return;
			}

			MobileNPCAdaptiveMemory adaptiveMemory = GetComponent<MobileNPCAdaptiveMemory>();
			MobileNPCAdaptiveMemory.ExperienceKind shotContext = targetPlayer != null
				? MobileNPCAdaptiveMemory.ExperienceKind.FireAtPlayer
				: targetZombie != null ? MobileNPCAdaptiveMemory.ExperienceKind.FireAtZombie
				: MobileNPCAdaptiveMemory.ExperienceKind.FireAtHostileNPC;
			// Local contextual experience can modestly change the fire cadence, never
			// grant perfect aim, unlimited ammo, target visibility or permission to shoot.
			float learnedCadence = adaptiveMemory != null
				? adaptiveMemory.GetShotCadenceMultiplier(shotContext, transform.position) : 1f;
			Vector3 muzzle = transform.position + Vector3.up * muzzleHeight;
			Vector3 targetPoint = targetTransform.position + Vector3.up * targetHeight;
			Vector3 toTarget = targetPoint - muzzle;
			// Scoped distance is bounded by the real weapon and a hard cap,
			// never the map size. With no scope retain ordinary short/medium range.
			float scopedRange = selectedOpticZoom >= 2.5f
				? Mathf.Min(115f, Mathf.Max(effectiveRange, effectiveRange * Mathf.Min(2.55f, selectedOpticZoom * 0.65f)))
				: effectiveRange;
			float weaponRange = selectedGunAsset.range > 0f
				? Mathf.Min(scopedRange, selectedGunAsset.range) : effectiveRange;
			bool nativeBallistics = Provider.modeConfigData.Gameplay.Ballistics;
			if (nativeBallistics && (selectedGunAsset.muzzleVelocity <= 0.1f
				|| selectedGunAsset.ballisticSteps <= 0))
			{
				// With ballistics enabled, invalid projectile data must NOT
				// silently switch this NPC to perfect long-range hitscan.
				nextShotTime = Time.time + 1f;
				return;
			}
			if (nativeBallistics)
				weaponRange = Mathf.Min(weaponRange, selectedGunAsset.muzzleVelocity
					* NativeBallisticDelta * selectedGunAsset.ballisticSteps);
			if (toTarget.sqrMagnitude > weaponRange * weaponRange || toTarget.sqrMagnitude < 0.01f)
				return;

			float weaponSpreadDegrees = selectedGunAsset.baseSpreadAngleRadians * Mathf.Rad2Deg;
			// A human estimates lead/drop from previously SEEN target motion.
			// The authored gun and attachment physics will actually carry the shot.
			float bulletSpeed = nativeBallistics ? selectedGunAsset.muzzleVelocity : 10000f;
			if (!aim.TryGetBallisticShotDirection(muzzle, IsMoving(), skill,
				weaponSpreadDegrees, bulletSpeed, selectedBulletGravityMultiplier,
				selectedOpticZoom, selectedGunID, out Vector3 shotDirection,
				out int selectedBallisticProfile))
				return;
			int availableBulletSlot = nativeBallistics ? FindFreeBulletSlot() : -1;
			if (nativeBallistics && availableBulletSlot < 0)
			{
				// Too many live rounds: wait, don't fabricate a direct-hit shortcut.
				nextShotTime = Time.time + 0.1f;
				return;
			}

			// A verified line of sight to the enemy is not permission to
			// fire through a friend. Test the ACTUAL randomized projectile
			// direction against current living squad positions before
			// reserving ammo or raising ShotFired. No suppression bullets
			// or target/aim information is provided by group membership.
			if (groupMember == null)
				groupMember = GetComponent<MobileNPCGroupMember>();
			if (groupMember != null
				&& !groupMember.IsSquadFireLaneSafeServer(muzzle, shotDirection, weaponRange))
			{
				nextShotTime = Time.time + 0.22f;
				return;
			}

			if (!selectedGunHasCompatibleMagazine)
			{
				BeginReload();
				return;
			}

			int ammoCost = selectedGunAsset != null ? selectedGunAsset.ammoPerShot : 1;
			if (ammoCost > 0 && roundsRemaining < ammoCost)
			{
				BeginReload();
				return;
			}

			if (!selectedGunAsset.infiniteAmmo && ammoCost > 0)
			{
				int nextRounds = roundsRemaining - ammoCost;
				if (!inventory.TryUpdateGunAmmoServer(selectedGunInstanceID, (byte)Mathf.Clamp(nextRounds, 0, 255)))
				{
					// Fail closed: do not commit a shot if its ammunition state cannot be saved.
					nextShotTime = Time.time + 1.0f;
					return;
				}
				roundsRemaining = nextRounds;
			}
			ShotFired?.Invoke();
			// NPC shots must be audible stimuli to other mobile NPCs, not only
			// vanilla zombies. This uses the same server registry path as player gunfire.
			// The source is this exact real shot. Use the same sound radius
			// authored on the native weapon, not bullet travel distance.
			// This notification is only for the separate mobile-NPC ears.
			if (!selectedGunSoundSuppressed && selectedGunAsset.alertRadius > 0f)
				MobileNPCRegistry.NotifyGunfire(transform.position,
					selectedGunAsset.alertRadius, null, transform);

			shotsRemainingInBurst = Mathf.Max(0, shotsRemainingInBurst - 1);
			if (shotsRemainingInBurst == 0)
			{
				waitingForBurstPause = true;
				nextShotTime = Time.time + Random.Range(
					Mathf.Min(minimumBurstPause, maximumBurstPause),
					Mathf.Max(minimumBurstPause, maximumBurstPause)) * learnedCadence;
			}
			else
			{
				float intervalScale = Random.Range(1f - shotIntervalJitter, 1f + shotIntervalJitter);
				nextShotTime = Time.time + Mathf.Max(0.05f, GetSecondsPerShot() * intervalScale * learnedCadence);
			}

			PendingBullet committed = new PendingBullet
			{
				Active = nativeBallistics,
				Position = muzzle,
				Velocity = shotDirection * bulletSpeed,
				InitialDirection = shotDirection,
				NextStepTime = Time.time + NativeBallisticDelta,
				MaximumRange = weaponRange,
				GravityMultiplier = selectedBulletGravityMultiplier,
				ActualDamage = GetWeaponDamage(),
				IntendedDistance = toTarget.magnitude,
				RemainingSteps = nativeBallistics ? selectedGunAsset.ballisticSteps : 1,
				Profile = selectedBallisticProfile,
				GunId = selectedGunID,
				TargetPlayer = targetPlayer,
				TargetNPC = targetNPC,
				TargetZombie = targetZombie,
				OwnDisposition = ownDisposition,
				Context = shotContext
			};
			if (nativeBallistics)
				pendingBullets[availableBulletSlot] = committed;
			else
			{
				// Respect the original game's optional non-ballistic gameplay mode.
				if (TryGetNearestExternalHit(muzzle, shotDirection, weaponRange, out RaycastHit hit))
					CompleteCommittedShot(committed, ApplyShotImpact(hit, committed), true);
				else
					CompleteCommittedShot(committed, false, true);
			}

			// Vanilla Player guns use AlertTool.alert() for an unsuppressed
			// committed shot. Mobile NPCs do not run UseableGun, so call that
			// SAME native entry point exactly once with this weapon's
			// genuine alert radius; do not bypass its navigation/aggro rules.
			if (!selectedGunSoundSuppressed && selectedGunAsset.alertRadius > 0f)
				AlertTool.alert(transform.position, selectedGunAsset.alertRadius);

			if (ammoCost > 0 && roundsRemaining < ammoCost)
			{
				BeginReload();
			}
		}

		private void FixedUpdate()
		{
			// Match the native 50Hz ballistic simulation where feasible. Physics
			// contacts are checked during fixed ticks, not at arbitrary render FPS.
			if (Provider.isServer)
				TickInFlightBulletsServer();
		}

		private int FindFreeBulletSlot()
		{
			for (int i = 0; i < pendingBullets.Length; ++i)
				if (!pendingBullets[i].Active) return i;
			return -1;
		}

		private bool TryGetNearestExternalHit(Vector3 origin, Vector3 direction,
			float distance, out RaycastHit hit)
		{
			hit = default(RaycastHit);
			int count = Physics.RaycastNonAlloc(origin, direction, shotHits,
				distance, hitMask, QueryTriggerInteraction.Ignore);
			float nearest = float.PositiveInfinity;
			for (int i = 0; i < count; ++i)
			{
				Transform t = shotHits[i].transform;
				if (t == null || t == transform || t.IsChildOf(transform)) continue;
				if (shotHits[i].distance < nearest)
				{
					nearest = shotHits[i].distance;
					hit = shotHits[i];
				}
			}
			// A full NonAlloc buffer may omit the nearest collider.
			if (count == shotHits.Length)
			{
				RaycastHit[] overflow = Physics.RaycastAll(origin, direction,
					distance, hitMask, QueryTriggerInteraction.Ignore);
				nearest = float.PositiveInfinity;
				for (int i = 0; i < overflow.Length; ++i)
				{
					Transform t = overflow[i].transform;
					if (t == null || t == transform || t.IsChildOf(transform)) continue;
					if (overflow[i].distance < nearest)
					{
						nearest = overflow[i].distance;
						hit = overflow[i];
					}
				}
			}
			return !float.IsPositiveInfinity(nearest);
		}

		private void TickInFlightBulletsServer()
		{
			if (sharedBallisticFrame != Time.frameCount)
			{
				sharedBallisticFrame = Time.frameCount;
				sharedBallisticChecks = 0;
			}
			int checks = 0;
			for (int i = 0; i < pendingBullets.Length; ++i)
			{
				PendingBullet bullet = pendingBullets[i];
				if (!bullet.Active) continue;
				while (bullet.Active && Time.time >= bullet.NextStepTime
					&& checks < MaxStepChecksPerActorFrame
					&& sharedBallisticChecks < MaxSharedBulletChecksPerFrame)
				{
					++checks;
					++sharedBallisticChecks;
					float speed = bullet.Velocity.magnitude;
					float stepLength = Mathf.Min(speed * NativeBallisticDelta,
						bullet.MaximumRange - bullet.DistanceTravelled);
					if (stepLength <= 0.001f || bullet.RemainingSteps <= 0)
					{
						CompleteCommittedShot(bullet, false, false);
						bullet.Active = false;
						break;
					}
					Vector3 direction = bullet.Velocity / Mathf.Max(speed, 0.001f);
					if (TryGetNearestExternalHit(bullet.Position, direction, stepLength,
						out RaycastHit hit))
					{
						bool contacted = ApplyShotImpact(hit, bullet);
						// A bullet hitting an intervening wall is not evidence
						// that drop/lead compensation was wrong. Keep the miss for
						// tactical cadence, but exclude it from ballistic training.
						CompleteCommittedShot(bullet, contacted, false, contacted);
						bullet.Active = false;
						break;
					}
					// Same semi-implicit native 50Hz update as UseableGun:
					// position advances, then vertical velocity takes gravity.
					bullet.Position += bullet.Velocity * NativeBallisticDelta;
					bullet.Velocity.y += Physics.gravity.y
						* bullet.GravityMultiplier * NativeBallisticDelta;
					bullet.DistanceTravelled += stepLength;
					bullet.RemainingSteps--;
					bullet.NextStepTime += NativeBallisticDelta;
					if (bullet.RemainingSteps <= 0 ||
						bullet.DistanceTravelled >= bullet.MaximumRange - 0.01f)
					{
						CompleteCommittedShot(bullet, false, false);
						bullet.Active = false;
					}
				}
				pendingBullets[i] = bullet;
		}
		}

		private bool ApplyShotImpact(RaycastHit hit, PendingBullet bullet)
		{
			if (hit.transform == null) return false;
			Player hitPlayer = hit.transform.GetComponent<Player>();
			if (hitPlayer == null)
				hitPlayer = hit.transform.GetComponentInParent<Player>();
			if (hitPlayer != null && hitPlayer == bullet.TargetPlayer
				&& hitPlayer.life != null && !hitPlayer.life.isDead)
			{
				DamagePlayerParameters parameters = new DamagePlayerParameters(hitPlayer);
				parameters.cause = EDeathCause.GUN;
				parameters.limb = ELimb.SPINE;
				parameters.killer = Provider.server;
				parameters.direction = bullet.Velocity.normalized;
				parameters.damage = bullet.ActualDamage;
				parameters.respectArmor = true;
				EPlayerKill kill;
				DamageTool.damagePlayer(parameters, out kill);
				return true;
			}
			if (bullet.TargetNPC != null)
			{
				MobileNPCHumanoidDisposition hitNPC =
					hit.transform.GetComponentInParent<MobileNPCHumanoidDisposition>();
				MobileNPCHumanoidVitals vitals =
					hitNPC != null ? hitNPC.GetComponent<MobileNPCHumanoidVitals>() : null;
				if (hitNPC == bullet.TargetNPC && vitals != null && !vitals.IsDead
					&& MobileNPCHumanoidDisposition.AreHostile(bullet.OwnDisposition, hitNPC))
					return vitals.ApplyValidatedWeaponImpactServer(
						bullet.ActualDamage, hit.point, bullet.Velocity.normalized,
						transform, true, hit.transform);
			}
			if (bullet.TargetZombie != null)
			{
				Zombie hitZombie = hit.transform.GetComponentInParent<Zombie>();
				if (hitZombie == bullet.TargetZombie && !hitZombie.isDead)
				{
					int impact = Mathf.Clamp(Mathf.RoundToInt(bullet.ActualDamage), 1, ushort.MaxValue);
					hitZombie.askDamage((ushort)impact,
						bullet.Velocity.normalized * impact,
						out _, out _, trackKill: false, dropLoot: true);
					return true;
				}
			}
			return false;
		}

		private void CompleteCommittedShot(PendingBullet bullet, bool contacted,
			bool instantaneous, bool validBallisticFeedback = true)
		{
			// Do not grade a miss after the victim disappeared or moved out
			// of sight, as the shooter has no trustworthy visual feedback.
			bool observed = instantaneous
				|| perception != null && (bullet.TargetPlayer != null
					&& perception.VisiblePlayer == bullet.TargetPlayer
					|| bullet.TargetNPC != null && perception.VisibleNPC == bullet.TargetNPC
					|| bullet.TargetZombie != null && perception.VisibleZombie == bullet.TargetZombie);
			if (!contacted && !observed) return;
			MobileNPCAdaptiveMemory memory = GetComponent<MobileNPCAdaptiveMemory>();
			if (memory != null)
				memory.RecordOwnOutcomeServer(bullet.Context, transform.position, contacted);
			if (aim != null && bullet.Profile >= 0 && observed
				&& validBallisticFeedback)
				aim.RecordBallisticOutcomeServer(bullet.GunId, bullet.IntendedDistance,
					bullet.Profile, contacted);
		}

		private void RefreshSelectedGun()
		{
			// Keep the current weapon while it has rounds or a compatible reload available;
			// otherwise a newly looted gun could silently discard a magazine mid-reload.
			if (inventory != null && selectedGunInstanceID != 0 && selectedGunAsset != null
				&& inventory.ContainsGunInstanceServer(selectedGunInstanceID)
				&& (roundsRemaining > 0 || inventory.TryFindCompatibleMagazine(selectedGunAsset, out int ignoredSlot, out ItemMagazineAsset ignoredMagazine)))
			{
				return;
			}

			if (inventory == null || !inventory.TryGetBestGun(out uint gunInstanceID, out Item gun, out ItemGunAsset gunAsset))
			{
				selectedGunID = 0;
				selectedGunInstanceID = 0;
				selectedGunAsset = null;
				selectedGunHasCompatibleMagazine = false;
				selectedOpticZoom = 1f;
				selectedBulletGravityMultiplier = 1f;
				selectedGunSoundSuppressed = false;
				roundsRemaining = 0;
				return;
			}

			if (selectedGunInstanceID == gunInstanceID && selectedGunAsset == gunAsset) return;

			selectedGunInstanceID = gunInstanceID;
			selectedGunID = gun.id;
			selectedGunAsset = gunAsset;
			selectedGunHasCompatibleMagazine = false;
			selectedOpticZoom = 1f;
			selectedBulletGravityMultiplier = gunAsset.bulletGravityMultiplier;
			selectedGunSoundSuppressed = false;
			roundsRemaining = 0;
			// Native gun state owns actual attachments. A scope from nowhere
			// must never grant longer-range vision or gun accuracy.
			if (gun.state != null && gun.state.Length >= 18)
			{
				ushort sightId = System.BitConverter.ToUInt16(gun.state, GunStateIndices.SIGHT_ID);
				ItemSightAsset sight = Assets.find(EAssetType.ITEM, sightId) as ItemSightAsset;
				if (sight != null)
				{
					selectedOpticZoom = Mathf.Clamp(sight.zoom, 1f, 12f);
					selectedBulletGravityMultiplier *= sight.BallisticGravityMultiplier;
				}
				ushort barrelId = System.BitConverter.ToUInt16(gun.state, GunStateIndices.BARREL_ID);
				ItemBarrelAsset barrel = Assets.find(EAssetType.ITEM, barrelId) as ItemBarrelAsset;
				if (barrel != null)
				{
					selectedBulletGravityMultiplier *= barrel.BallisticGravityMultiplier;
					// Same vanilla decision as UseableGun: a working, actually
					// equipped suppressor reduces audible zombie/NPC gun alerts.
					selectedGunSoundSuppressed = barrel.isSilenced
						&& gun.state[16] > 0;
				}
				ushort tacticalId = System.BitConverter.ToUInt16(gun.state, GunStateIndices.TACTICAL_ID);
				ItemTacticalAsset tactical = Assets.find(EAssetType.ITEM, tacticalId) as ItemTacticalAsset;
				if (tactical != null)
					selectedBulletGravityMultiplier *= tactical.BallisticGravityMultiplier;
				ushort gripId = System.BitConverter.ToUInt16(gun.state, GunStateIndices.GRIP_ID);
				ItemGripAsset grip = Assets.find(EAssetType.ITEM, gripId) as ItemGripAsset;
				if (grip != null)
					selectedBulletGravityMultiplier *= grip.BallisticGravityMultiplier;
				ushort magazineId = System.BitConverter.ToUInt16(gun.state, GunStateIndices.MAGAZINE_ID);
				ItemMagazineAsset magazine = Assets.find(EAssetType.ITEM, magazineId) as ItemMagazineAsset;
				if (magazine != null)
					selectedBulletGravityMultiplier *= magazine.BallisticGravityMultiplier;
			}
			selectedBulletGravityMultiplier = Mathf.Clamp(selectedBulletGravityMultiplier, 0.05f, 5f);

			// Vanilla gun state: magazine ID at bytes 8-9, loaded ammo at byte 10.
			// The detached magazine stores rounds in Item.amount; its asset defines capacity.
			if (gun.state == null || gun.state.Length < 11) return;
			ushort loadedMagazineID = System.BitConverter.ToUInt16(gun.state, 8);
			ItemMagazineAsset loadedMagazine = Assets.find(EAssetType.ITEM, loadedMagazineID) as ItemMagazineAsset;
			if (loadedMagazine == null || loadedMagazine.MaxAmountAsByte == 0
				|| loadedMagazine.calibers == null || gunAsset.magazineCalibers == null
				|| gunAsset.magazineCalibers.Length == 0
				|| !loadedMagazine.CalibersContainAnyOfIds(gunAsset.magazineCalibers)) return;
			selectedGunHasCompatibleMagazine = true;
			roundsRemaining = Mathf.Min(gun.state[10], loadedMagazine.MaxAmountAsByte);
			if (roundsRemaining != gun.state[10])
				inventory.TryUpdateGunAmmoServer(selectedGunInstanceID, (byte)roundsRemaining, loadedMagazineID);
		}

		private float GetSecondsPerShot()
		{
			if (selectedGunAsset != null)
			{
				float roundsPerSecond = selectedGunAsset.CalculateRoundsPerSecond();
				if (roundsPerSecond > 0f && !float.IsNaN(roundsPerSecond) && !float.IsInfinity(roundsPerSecond))
				{
					return 1f / roundsPerSecond;
				}
			}
			return secondsPerShot;
		}

		private float GetWeaponDamage()
		{
			if (selectedGunAsset != null && selectedGunAsset.playerDamageMultiplier.damage > 0f)
			{
				return selectedGunAsset.playerDamageMultiplier.damage;
			}
			return damage;
		}

		private int RollBurstSize()
		{
			int minimum = Mathf.Max(1, Mathf.Min(minimumBurstSize, maximumBurstSize));
			int maximum = Mathf.Max(minimum, Mathf.Max(minimumBurstSize, maximumBurstSize));
			return Random.Range(minimum, maximum + 1);
		}

		private void BeginReload()
		{
			if (reloadPending || inventory == null || selectedGunAsset == null)
			{
				return;
			}

			if (!inventory.CanUpdateGunAmmoServer(selectedGunInstanceID)
				|| !inventory.TryGetGunSnapshotServer(selectedGunInstanceID, out Item gunSnapshot))
			{
				roundsRemaining = 0;
				nextShotTime = Time.time + 1.0f;
				return;
			}

			if (!inventory.TryFindCompatibleMagazine(selectedGunAsset, out int magazineSlot, out ItemMagazineAsset magazineAsset)
				|| !inventory.TryTakeItemServer(magazineSlot, out Item consumedMagazine))
			{
				roundsRemaining = 0;
				nextShotTime = Time.time + 1.0f;
				return;
			}

			// Vanilla stores detached magazine rounds in Item.amount; MaxAmountAsByte is capacity.
			int availableRounds = consumedMagazine.amount;
			if (availableRounds <= 0 || magazineAsset.MaxAmountAsByte == 0)
			{
				inventory.TryStoreTransferredItem(consumedMagazine);
				roundsRemaining = 0;
				nextShotTime = Time.time + 1.0f;
				return;
			}
			if (!inventory.TryReserveSlotServer())
			{
				inventory.TryStoreTransferredItem(consumedMagazine);
				roundsRemaining = 0;
				nextShotTime = Time.time + 1.0f;
				return;
			}

			// Capture the magazine that is currently attached so its remaining rounds and
			// durability can be returned when the new magazine is committed.
			ushort oldMagazineID = System.BitConverter.ToUInt16(gunSnapshot.state, 8);
			byte oldMagazineRounds = gunSnapshot.state[10];
			ItemMagazineAsset oldMagazineAsset = Assets.find(EAssetType.ITEM, oldMagazineID) as ItemMagazineAsset;
			Item oldMagazine = null;
			if (oldMagazineAsset != null && (oldMagazineRounds > 0
				|| (!selectedGunAsset.shouldDeleteEmptyMagazines && !oldMagazineAsset.ShouldDeleteAtZeroAmount)))
				oldMagazine = new Item(oldMagazineID, (byte)Mathf.Min(oldMagazineRounds, oldMagazineAsset.MaxAmountAsByte), gunSnapshot.state[17]);

			pendingReloadRounds = Mathf.Min(availableRounds, magazineAsset.MaxAmountAsByte);
			pendingReloadMagazineID = consumedMagazine.id;
			pendingReloadMagazine = consumedMagazine;
			pendingReplacedMagazine = oldMagazine;
			reloadPending = true;
			float weaponReloadDuration = selectedGunAsset.reloadTime > 0f ? selectedGunAsset.reloadTime : reloadDuration;
			reloadFinishedAt = Time.time + Mathf.Max(0.1f, weaponReloadDuration);
			ReloadStarted?.Invoke();
		}

		private void TryReturnPendingMagazine()
		{
			if (pendingReloadMagazine == null || inventory == null || !Provider.isServer) return;
			if (inventory.TryStoreReservedItemServer(pendingReloadMagazine))
			{
				pendingReloadMagazine = null;
			}
		}

		private void OnDisable()
		{
			if (!Provider.isServer) return;
			// Never retain an active muzzle-to-target projectile across object
			// disable, map reload or respawn of a completely different survivor.
			for (int i = 0; i < pendingBullets.Length; ++i)
				pendingBullets[i].Active = false;
			// A cancelled reload must not silently destroy the magazine removed from storage.
			TryReturnPendingMagazine();
			pendingReplacedMagazine = null;
			reloadPending = false;
			pendingReloadRounds = 0;
			pendingReloadMagazineID = 0;
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