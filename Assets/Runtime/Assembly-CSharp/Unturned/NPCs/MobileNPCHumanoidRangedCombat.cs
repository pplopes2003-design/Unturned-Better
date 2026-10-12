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
		private ItemGunAsset selectedGunAsset;
		private uint selectedGunInstanceID;
		private ushort selectedGunID;
		private int pendingReloadRounds;
		private ushort pendingReloadMagazineID;
		private Item pendingReloadMagazine;
		private int roundsRemaining;
		private float nextShotTime;
		private float reloadFinishedAt;
		private bool reloadPending;
		private int shotsRemainingInBurst;
		private bool waitingForBurstPause;
		// Reused hit buffer avoids allocating and sorting a new RaycastAll array for every shot.
		private readonly RaycastHit[] shotHits = new RaycastHit[64];

		/// <summary>Raised on the server when a shot is committed. Hook prefab audio/animation here.</summary>
		public event System.Action ShotFired;
		/// <summary>Raised on the server when an empty magazine starts reloading.</summary>
		public event System.Action ReloadStarted;
		/// <summary>Raised on the server when the reload finishes.</summary>
		public event System.Action ReloadFinished;

		public int RoundsRemaining => roundsRemaining;
		public ItemGunAsset SelectedGunAsset => selectedGunAsset;
		public bool IsReloading => Provider.isServer && reloadPending;

		private void Awake()
		{
			brain = GetComponent<MobileNPCBrainDriver>();
			aim = GetComponent<MobileNPCHumanoidAim>();
			perception = GetComponent<MobileNPCHumanoidPerception>();
			inventory = GetComponent<MobileNPCInventory>();
			roundsRemaining = 0;
			pendingReloadRounds = 0;
			selectedGunID = 0;
			selectedGunInstanceID = 0;
			shotsRemainingInBurst = RollBurstSize();
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
				bool ammoStateUpdated = inventory.TryUpdateGunAmmoServer(selectedGunInstanceID,
					(byte)Mathf.Clamp(loadedRounds, 0, 255), pendingReloadMagazineID);
				reloadPending = false;
				roundsRemaining = ammoStateUpdated ? loadedRounds : 0;
				if (!ammoStateUpdated)
				{
					Debug.LogWarning("Mobile NPC reload aborted because the stored gun state could not be updated; returning the reserved magazine.", this);
					TryReturnPendingMagazine();
				}
				else
				{
					// The attached magazine is now represented by the gun's stored state.
					inventory.ReleaseReservedSlotServer();
					pendingReloadMagazine = null;
				}
				pendingReloadRounds = 0;
				pendingReloadMagazineID = 0;
				shotsRemainingInBurst = RollBurstSize();
				waitingForBurstPause = false;
				ReloadFinished?.Invoke();
				RefreshSelectedGun();
			}

			if (selectedGunAsset == null)
			{
				return;
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

			Transform targetTransform = brain.Target;
			if (targetTransform == null)
			{
				return;
			}

			Player targetPlayer = ResolveTargetPlayer(targetTransform);
			MobileNPCHumanoidDisposition targetNPC = targetTransform.GetComponent<MobileNPCHumanoidDisposition>();
			if (targetNPC == null) targetNPC = targetTransform.GetComponentInParent<MobileNPCHumanoidDisposition>();
			MobileNPCHumanoidDisposition ownDisposition = GetComponent<MobileNPCHumanoidDisposition>();
			MobileNPCHumanoidVitals targetVitals = targetNPC != null ? targetNPC.GetComponent<MobileNPCHumanoidVitals>() : null;
			if (targetPlayer != null)
			{
				if (targetPlayer.life == null || targetPlayer.life.isDead || perception.VisiblePlayer != targetPlayer)
				{
					return;
				}
			}
			else if (targetNPC == null || targetVitals == null || targetVitals.IsDead
				|| perception.VisibleNPC != targetNPC
				|| !MobileNPCHumanoidDisposition.AreHostile(ownDisposition, targetNPC))
			{
				return;
			}

			Vector3 muzzle = transform.position + Vector3.up * muzzleHeight;
			Vector3 targetPoint = targetTransform.position + Vector3.up * targetHeight;
			Vector3 toTarget = targetPoint - muzzle;
			float weaponRange = selectedGunAsset != null && selectedGunAsset.range > 0f
				? Mathf.Min(effectiveRange, selectedGunAsset.range) : effectiveRange;
			if (toTarget.sqrMagnitude > weaponRange * weaponRange || toTarget.sqrMagnitude < 0.01f)
			{
				return;
			}

			float weaponSpreadDegrees = selectedGunAsset != null ? selectedGunAsset.baseSpreadAngleRadians * Mathf.Rad2Deg : 0f;
			if (!aim.TryGetShotDirection(muzzle, IsMoving(), skill, weaponSpreadDegrees, out Vector3 shotDirection))
			{
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
				nextShotTime = Time.time + Mathf.Max(0.05f, GetSecondsPerShot() * intervalScale);
			}

			// Find the nearest external collider without allocating a per-shot array or sorting all hits.
			int hitCount = Physics.RaycastNonAlloc(muzzle, shotDirection, shotHits, weaponRange, hitMask, QueryTriggerInteraction.Ignore);
			RaycastHit nearestHit = default(RaycastHit);
			float nearestHitDistance = float.PositiveInfinity;
			for (int i = 0; i < hitCount; ++i)
			{
				Transform hitTransform = shotHits[i].transform;
				if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform))
				{
					continue;
				}
				if (shotHits[i].distance < nearestHitDistance)
				{
					nearestHitDistance = shotHits[i].distance;
					nearestHit = shotHits[i];
				}
			}

			// A full non-alloc buffer may have omitted closer colliders. Fall back to the
			// allocating query only in this uncommon saturation case to preserve hit correctness.
			if (hitCount == shotHits.Length)
			{
				RaycastHit[] overflowHits = Physics.RaycastAll(muzzle, shotDirection, weaponRange, hitMask, QueryTriggerInteraction.Ignore);
				nearestHitDistance = float.PositiveInfinity;
				for (int i = 0; i < overflowHits.Length; ++i)
				{
					Transform hitTransform = overflowHits[i].transform;
					if (hitTransform == null || hitTransform == transform || hitTransform.IsChildOf(transform)) continue;
					if (overflowHits[i].distance < nearestHitDistance)
					{
						nearestHitDistance = overflowHits[i].distance;
						nearestHit = overflowHits[i];
					}
				}
			}

			if (!float.IsPositiveInfinity(nearestHitDistance))
			{
				RaycastHit hit = nearestHit;

				Player hitPlayer = hit.transform.GetComponent<Player>();
				if (hitPlayer == null)
				{
					hitPlayer = hit.transform.GetComponentInParent<Player>();
				}

				if (hitPlayer != null && hitPlayer == targetPlayer && hitPlayer.life != null && !hitPlayer.life.isDead)
				{
					DamagePlayerParameters parameters = new DamagePlayerParameters(hitPlayer);
					parameters.cause = EDeathCause.GUN;
					parameters.limb = ELimb.SPINE;
					parameters.killer = Provider.server;
					parameters.direction = shotDirection;
					parameters.damage = GetWeaponDamage();
					parameters.respectArmor = true;
					EPlayerKill kill;
					DamageTool.damagePlayer(parameters, out kill);
				}
				else
				{
					MobileNPCHumanoidDisposition hitNPC = hit.transform.GetComponent<MobileNPCHumanoidDisposition>();
					if (hitNPC == null) hitNPC = hit.transform.GetComponentInParent<MobileNPCHumanoidDisposition>();
					MobileNPCHumanoidVitals hitVitals = hitNPC != null ? hitNPC.GetComponent<MobileNPCHumanoidVitals>() : null;
					if (hitNPC == targetNPC && hitVitals != null
						&& MobileNPCHumanoidDisposition.AreHostile(ownDisposition, hitNPC))
					{
						float hitDamage = GetWeaponDamage();
						hitVitals.ApplyDamage(hitDamage, shotDirection * hitDamage);
					}
				}
			}

			// Gunfire is a world stimulus: vanilla zombies investigate the shot position
			// regardless of which mobile faction fired it.
			ZombieManager.alertZombiesToNoise(transform.position, Mathf.Max(20f, weaponRange));

			if (ammoCost > 0 && roundsRemaining < ammoCost)
			{
				BeginReload();
			}
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
				roundsRemaining = 0;
				return;
			}

			if (selectedGunInstanceID == gunInstanceID && selectedGunAsset == gunAsset) return;

			selectedGunInstanceID = gunInstanceID;
			selectedGunID = gun.id;
			selectedGunAsset = gunAsset;
			roundsRemaining = 0;

			// Vanilla gun state: magazine ID at bytes 8-9, loaded ammo at byte 10.
			// The detached magazine stores rounds in Item.amount; its asset defines capacity.
			if (gun.state == null || gun.state.Length < 11) return;
			ushort loadedMagazineID = System.BitConverter.ToUInt16(gun.state, 8);
			ItemMagazineAsset loadedMagazine = Assets.find(EAssetType.ITEM, loadedMagazineID) as ItemMagazineAsset;
			if (loadedMagazine == null || loadedMagazine.MaxAmountAsByte == 0
				|| loadedMagazine.calibers == null || gunAsset.magazineCalibers == null
				|| gunAsset.magazineCalibers.Length == 0
				|| !loadedMagazine.CalibersContainAnyOfIds(gunAsset.magazineCalibers)) return;
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
				|| !inventory.TryFindCompatibleMagazine(selectedGunAsset, out int magazineSlot, out ItemMagazineAsset magazineAsset)
				|| !inventory.TryTakeItemServer(magazineSlot, out Item consumedMagazine))
			{
				roundsRemaining = 0;
				// Do not poll for ammunition every frame when no compatible magazine exists
				// or the vanilla gun state cannot represent its loaded magazine.
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
			pendingReloadRounds = Mathf.Min(availableRounds, magazineAsset.MaxAmountAsByte);
			pendingReloadMagazineID = consumedMagazine.id;
			pendingReloadMagazine = consumedMagazine;
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
			// A cancelled reload must not silently destroy the magazine removed from storage.
			TryReturnPendingMagazine();
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