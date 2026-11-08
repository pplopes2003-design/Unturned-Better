using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Steam-safe visual/audio adapter of Line 1's MobileNPCRangedPresentation.
    /// Never deals damage or changes inventory. Shows only an item actually
    /// owned by the NPC, using public ItemAsset prefabs and verified skeleton
    /// hooks. Firing and reload sounds follow real committed weapon actions.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcWeaponPresentation : MonoBehaviour
    {
        private const float RefreshInterval = 0.55f;
        private const float MissingHookRetrySeconds = 6f;
        private Line2MobileNPCInventory _inventory;
        private Line2NPCRangedExecutor _ranged;
        private Line2NPCMeleeExecutor _melee;
        private Transform _rightHand;
        private Transform _leftHand;
        private Transform _spineHook;
        private Transform _spine;
        private Transform _model;
        private ushort _displayedItem;
        private ushort _lastMeleeItem;
        private float _meleeUntil;
        private float _nextRefresh;
        private float _nextHookRetry;
        private bool _loggedMissingHook;

        private void OnEnable()
        {
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _ranged = GetComponent<Line2NPCRangedExecutor>();
            _melee = GetComponent<Line2NPCMeleeExecutor>();
            if (_ranged != null)
            {
                _ranged.ShotFired += OnShotFired;
                _ranged.ReloadStarted += OnReloadStarted;
            }
            if (_melee != null)
                _melee.AttackCommitted += OnMeleeCommitted;
            _nextRefresh = 0f;
        }

        private void OnDisable()
        {
            if (_ranged != null)
            {
                _ranged.ShotFired -= OnShotFired;
                _ranged.ReloadStarted -= OnReloadStarted;
            }
            if (_melee != null)
                _melee.AttackCommitted -= OnMeleeCommitted;
            ClearVisual();
            _rightHand = null;
            _leftHand = null;
            _spineHook = null;
            _spine = null;
        }

        private void Update()
        {
            if (Dedicator.IsDedicatedServer || !Level.isLoaded ||
                !Provider.isServer || Time.time < _nextRefresh)
                return;
            _nextRefresh = Time.time + RefreshInterval;
            RefreshVisual();
        }

        private void RefreshVisual()
        {
            if (_inventory == null) return;
            ItemAsset desired = null;
            if (Time.time < _meleeUntil)
            {
                if (_lastMeleeItem != 0)
                    desired = Assets.find(EAssetType.ITEM,
                        _lastMeleeItem) as ItemMeleeAsset;
                // An unarmed punch visually holsters the gun for one swing.
            }
            else
            {
                uint gunInstance;
                Item ownedGun;
                ItemGunAsset gun;
                if (_inventory.TryGetBestGun(
                        out gunInstance, out ownedGun, out gun))
                    desired = gun;
                else
                {
                    Item ownedMelee;
                    ItemMeleeAsset melee;
                    if (_inventory.TryGetBestMeleeServer(
                            out ownedMelee, out melee))
                        desired = melee;
                }
            }

            ushort desiredId = desired != null ? desired.id : (ushort)0;
            if (desiredId == _displayedItem && _model != null) return;
            if (desiredId == 0)
            {
                ClearVisual();
                return;
            }
            // A different gun may have been consumed, traded or broken.
            // Never keep depicting the old gun while the new item's visual
            // is unavailable, even if we must retry loading its prefab.
            if (_model != null && desiredId != _displayedItem)
                ClearVisual();
            if (Time.time < _nextHookRetry)
                return;
            if (!FindHandHook())
            {
                _nextHookRetry = Time.time + MissingHookRetrySeconds;
                if (!_loggedMissingHook)
                {
                    _loggedMissingHook = true;
                    ProbeLogger.Log("NPC_WEAPON_VISUAL_NO_HAND_HOOK: actor=" +
                        gameObject.name + " (native skeleton differs)");
                }
                return;
            }

            // Respect the SAME authored equipment parent as native
            // PlayerEquipment. Workshop items may explicitly require
            // left-hand, spine or spine-hook mounting.
            Transform mount = _rightHand;
            switch (desired.EquipableModelParent)
            {
                case EEquipableModelParent.LeftHook:
                    mount = _leftHand;
                    break;
                case EEquipableModelParent.Spine:
                    mount = _spine;
                    break;
                case EEquipableModelParent.SpineHook:
                    mount = _spineHook;
                    break;
            }
            if (mount == null)
            {
                _nextHookRetry = Time.time + MissingHookRetrySeconds;
                if (!_loggedMissingHook)
                {
                    _loggedMissingHook = true;
                    ProbeLogger.Log("NPC_WEAPON_VISUAL_NO_ASSET_HOOK: actor=" +
                        gameObject.name + " asset=" + desired.id);
                }
                return;
            }
            GameObject prefab = desired.equipablePrefab != null
                ? desired.equipablePrefab : desired.item;
            if (prefab == null)
            {
                _nextHookRetry = Time.time + MissingHookRetrySeconds;
                return;
            }

            // Instantiate the item model, NEVER a gameplay actor, projectile
            // or second inventory entry. Physics colliders are disabled so
            // the visual cannot obstruct pathfinding or register bullet hits.
            GameObject created = null;
            try
            {
                created = Instantiate(prefab);
                if (created == null) return;
                Transform visual = created.transform;
                visual.SetParent(mount, false);
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.Euler(0f, 0f, 90f);
                visual.localScale = Vector3.one;
                foreach (Collider c in created.GetComponentsInChildren<Collider>(true))
                    c.enabled = false;
                foreach (Rigidbody rb in created.GetComponentsInChildren<Rigidbody>(true))
                {
                    rb.detectCollisions = false;
                    rb.isKinematic = true;
                }
                Layerer.enemy(visual);
                ClearVisual();
                _model = visual;
                _displayedItem = desiredId;
                created = null;
            }
            catch (Exception error)
            {
                _nextHookRetry = Time.time + MissingHookRetrySeconds;
                ProbeLogger.Log("NPC_WEAPON_VISUAL_REJECTED: " +
                    error.GetType().Name);
            }
            finally
            {
                if (created != null) Destroy(created);
            }
        }

        private bool FindHandHook()
        {
            if (_spine != null) return true;
            if (_spine == null)
            {
                Transform[] all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; ++i)
                    if (all[i] != null && all[i].name == "Spine")
                    {
                        _spine = all[i];
                        break;
                    }
            }
            if (_spine == null) return false;
            // Original NPC_Client prefab's authored skeleton path. Never
            // infer a hand by arbitrary world offsets if a prefab differs.
            _rightHand = _spine.Find(
                "Right_Shoulder/Right_Arm/Right_Hand/Right_Hook");
            _leftHand = _spine.Find(
                "Left_Shoulder/Left_Arm/Left_Hand/Left_Hook");
            _spineHook = _spine.Find("Spine_Hook");
            if (_rightHand == null || _leftHand == null)
            {
                Transform[] descendants =
                    _spine.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < descendants.Length; ++i)
                {
                    if (descendants[i] == null) continue;
                    if (_rightHand == null &&
                        descendants[i].name == "Right_Hook")
                        _rightHand = descendants[i];
                    if (_leftHand == null &&
                        descendants[i].name == "Left_Hook")
                        _leftHand = descendants[i];
                }
            }
            return true;
        }

        private void ClearVisual()
        {
            if (_model != null)
                Destroy(_model.gameObject);
            _model = null;
            _displayedItem = 0;
        }

        private void OnShotFired(ItemGunAsset gun)
        {
            if (gun != null) PlayNativeClip(gun.shoot, 0.65f);
        }

        private void OnReloadStarted(ItemGunAsset gun)
        {
            if (gun != null) PlayNativeClip(gun.reload, 0.70f);
        }

        private void OnMeleeCommitted(ushort itemId)
        {
            _lastMeleeItem = itemId;
            _meleeUntil = Time.time + 1.05f;
            _nextRefresh = 0f;
        }

        private void PlayNativeClip(AudioClip clip, float volume)
        {
            if (clip == null || !Provider.isServer ||
                Dedicator.IsDedicatedServer) return;
            try
            {
                var audio = new OneShotAudioParameters(transform, clip);
                audio.volume = Mathf.Clamp01(volume);
                audio.SetLinearRolloff(1f, 64f);
                audio.Play();
            }
            catch (Exception error)
            {
                ProbeLogger.Log("NPC_WEAPON_AUDIO_REJECTED: " +
                    error.GetType().Name);
            }
        }
    }
}
