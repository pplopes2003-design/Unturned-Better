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
        private Transform _rightShoulder, _leftShoulder, _rightGrip, _leftGrip;
        private Line2NPCCombatPerception _combatPerception;
        private PrototypeNpcVitals _vitals;
        private Animation _weaponAnimation;
        private Transform _barrel;
        private readonly System.Collections.Generic.List<string> _itemStates =
            new System.Collections.Generic.List<string>(8);
        private float _nextPoseDiagnostic;
        private PrototypeActor _actor;
        private string _weaponHoldState;
        private Vector3 _smoothedAimDirection;
        private float _lastPoseBlend;
        private float _medicalPoseUntil;
        private bool _medicalModelHidden;

        private void OnEnable()
        {
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _ranged = GetComponent<Line2NPCRangedExecutor>();
            _melee = GetComponent<Line2NPCMeleeExecutor>();
            _combatPerception = GetComponent<Line2NPCCombatPerception>();
            _vitals = GetComponent<PrototypeNpcVitals>();
            _actor = GetComponent<PrototypeActor>();
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
            _medicalPoseUntil = 0f;
            _medicalModelHidden = false;
            ClearVisual();
            _rightHand = null;
            _leftHand = null;
            _spineHook = null;
            _spine = null;
            _rightShoulder = _leftShoulder = _rightGrip = _leftGrip = null;
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
                if (desired is ItemGunAsset)
                    BindNativeRiflePose(desired);
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
            UnbindNativeRiflePose();
            if (_model != null)
                Destroy(_model.gameObject);
            _model = null;
            _displayedItem = 0;
        }

        // Port Line 1's native item animation and bounded aiming behavior.
        // Presentation only; never modifies damage, movement or line of sight.
        private void BindNativeRiflePose(ItemAsset asset)
        {
            if (_spine == null || asset == null) return;
            _rightShoulder = _spine.Find("Right_Shoulder");
            _leftShoulder = _spine.Find("Left_Shoulder");
            _rightGrip = _spine.Find("Right_Shoulder/Right_Arm/Right_Hand");
            _leftGrip = _spine.Find("Left_Shoulder/Left_Arm/Left_Hand");
            _weaponAnimation = GetComponentInChildren<Animation>(true);
            _barrel = null;
            if (_model != null)
            {
                foreach (Transform part in _model.GetComponentsInChildren<Transform>(true))
                    if (part != null && part.name == "Barrel")
                    { _barrel = part; break; }
            }
            if (_weaponAnimation == null || _rightShoulder == null ||
                _leftShoulder == null) return;
            AnimationClip[] clips = null;
            try
            {
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance;
                var member = asset.GetType().GetProperty("animations", flags);
                if (member != null)
                    clips = member.GetValue(asset, null) as AnimationClip[];
                if (clips == null)
                {
                    var field = asset.GetType().GetField("animations", flags);
                    if (field != null) clips = field.GetValue(asset) as AnimationClip[];
                }
            }
            catch (Exception error)
            {
                ProbeLogger.Log("NPC_NATIVE_ITEM_ANIM_UNAVAILABLE: " +
                    error.GetType().Name);
            }
            if (clips == null) return;
            string selected = null;
            int priority = 0;
            for (int i = 0; i < clips.Length; ++i)
            {
                AnimationClip clip = clips[i];
                if (clip == null || !clip.legacy || string.IsNullOrEmpty(clip.name))
                    continue;
                string stateName = "Line2_Item_" + asset.id + "_" + i;
                if (_weaponAnimation[stateName] != null)
                    _weaponAnimation.RemoveClip(stateName);
                _weaponAnimation.AddClip(clip, stateName);
                _itemStates.Add(stateName);
                AnimationState state = _weaponAnimation[stateName];
                if (state == null) continue;
                state.AddMixingTransform(_rightShoulder, true);
                state.AddMixingTransform(_leftShoulder, true);
                state.layer = 1;
                state.blendMode = AnimationBlendMode.Blend;
                // Only truly sustained poses can loop. "Aim" and "Equip"
                // are transitions on native weapons: looping them repeatedly
                // snaps the hands back to the start of the animation.
                int score = string.Equals(clip.name, "Aim_Idle", StringComparison.OrdinalIgnoreCase)
                    ? 3 : string.Equals(clip.name, "Idle", StringComparison.OrdinalIgnoreCase)
                    ? 2 : 0;
                if (score > priority) { selected = stateName; priority = score; }
            }
            if (selected != null)
            {
                _weaponHoldState = selected;
                _lastPoseBlend = 1f;
                _weaponAnimation[selected].wrapMode = WrapMode.Loop;
                _weaponAnimation.Blend(selected, 1f, 0.15f);
                ProbeLogger.Log("NPC_NATIVE_WEAPON_POSE: actor=" + gameObject.name +
                    " item=" + asset.id + " clip=" + selected);
            }
        }

        private void UnbindNativeRiflePose()
        {
            if (_weaponAnimation != null)
                foreach (string name in _itemStates)
                {
                    if (_weaponAnimation[name] == null) continue;
                    _weaponAnimation.Stop(name);
                    _weaponAnimation.RemoveClip(name);
                }
            _itemStates.Clear();
            _weaponHoldState = null;
            _smoothedAimDirection = Vector3.zero;
            _barrel = null;
        }

        private static void RotateShoulderToward(Transform shoulder,
            Transform grip, Vector3 direction)
        {
            if (shoulder == null || grip == null ||
                direction.sqrMagnitude < 0.5f) return;
            Vector3 current = grip.position - shoulder.position;
            if (current.sqrMagnitude < 0.03f) return;
            Quaternion requested =
                Quaternion.FromToRotation(current.normalized, direction.normalized)
                * shoulder.rotation;
            shoulder.rotation = Quaternion.RotateTowards(
                shoulder.rotation, requested, 320f * Time.deltaTime);
        }

        internal void BeginMedicalPose(float duration)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                _vitals == null || _vitals.IsDead)
                return;
            _medicalPoseUntil = Time.time + Mathf.Max(0.1f, duration);
            if (_model != null)
            {
                _model.gameObject.SetActive(false);
                _medicalModelHidden = true;
            }
            ProbeLogger.Log("NPC_MEDICAL_POSE_BEGIN: actor=" + gameObject.name);
        }

        internal void CancelMedicalPose()
        {
            _medicalPoseUntil = 0f;
            if (_model != null && _medicalModelHidden)
                _model.gameObject.SetActive(true);
            _medicalModelHidden = false;
        }

        private void LateUpdate()
        {
            if (_model != null && _medicalModelHidden &&
                Time.time >= _medicalPoseUntil)
            {
                _model.gameObject.SetActive(true);
                _medicalModelHidden = false;
            }
            if (_model == null || _rightShoulder == null ||
                _leftShoulder == null || _rightGrip == null ||
                _leftGrip == null || _vitals == null || _vitals.IsDead ||
                Time.time < _meleeUntil ||
                !Provider.isServer || Dedicator.IsDedicatedServer)
                return;
            if (Time.time < _medicalPoseUntil)
            {
                // Line 1's verified fallback for NPC_Client visuals with no
                // authentic Use/Aid clip: move REAL arm bones toward the
                // abdomen after regular animation evaluation. Presentation
                // only; the medical executor owns inventory and health.
                Vector3 care = (_leftShoulder.position +
                    _rightShoulder.position) * 0.5f +
                    transform.forward * 0.32f + Vector3.down * 0.34f;
                RotateShoulderToward(_rightShoulder, _rightGrip,
                    care - _rightShoulder.position);
                RotateShoulderToward(_leftShoulder, _leftGrip,
                    care - _leftShoulder.position);
                return;
            }
            // Player third-person equipment is parented to real hand hooks
            // and blends an upper-body item pose over locomotion. Match that
            // division of responsibility: walking holds the gun, sprinting
            // lowers it, and crouching retains a supported aiming pose.
            bool sprinting = _actor != null && _actor.IsMoving &&
                _actor.CurrentGait == Line2MobileNPCLocomotionPolicy.Gait.Sprint;
            if (_weaponAnimation != null && _weaponHoldState != null)
            {
                AnimationState held = _weaponAnimation[_weaponHoldState];
                if (held != null)
                {
                    float desiredWeight = sprinting ? 0.18f : 1f;
                    float nextWeight = Mathf.MoveTowards(_lastPoseBlend,
                        desiredWeight, 4f * Time.deltaTime);
                    if (Mathf.Abs(nextWeight - _lastPoseBlend) > 0.001f)
                    {
                        _lastPoseBlend = nextWeight;
                        held.weight = nextWeight;
                    }
                }
            }
            Vector3 facing = transform.forward;
            Transform opponent = _combatPerception != null
                ? _combatPerception.VisibleTarget : null;
            if (opponent != null && _combatPerception.Kind !=
                    Line2NPCCombatPerception.TargetKind.None &&
                _combatPerception.HasVerifiedSight(opponent,
                    _combatPerception.Kind,
                    _actor != null ? Mathf.Max(45f,
                        _actor.EffectiveVisualRange) : 45f))
            {
                Vector3 toOpponent = opponent.position + Vector3.up * 1.15f
                    - (_rightShoulder.position + _leftShoulder.position) * 0.5f;
                if (toOpponent.sqrMagnitude > 0.2f)
                    facing = toOpponent.normalized;
            }
            // A live, verified target may change elevation or bearing.
            // Track its current aim vector smoothly rather than snapping
            // a firearm to a newly sampled position every sensory tick.
            if (_smoothedAimDirection.sqrMagnitude < 0.2f)
                _smoothedAimDirection = facing;
            else
                _smoothedAimDirection = Vector3.RotateTowards(
                    _smoothedAimDirection, facing, 260f * Mathf.Deg2Rad *
                    Time.deltaTime, 0f);
            facing = _smoothedAimDirection.normalized;
            // During sprint follow body heading instead of retaining ADS
            // on a target while running; player third-person sprint lowers
            // the rifle as the running clip takes precedence.
            if (sprinting)
                facing = (transform.forward + Vector3.down * 0.42f).normalized;
            Vector3 right = transform.right;
            RotateShoulderToward(_rightShoulder, _rightGrip,
                (facing - right * 0.12f + Vector3.down * 0.08f).normalized);
            RotateShoulderToward(_leftShoulder, _leftGrip,
                (facing + right * 0.15f + Vector3.down * 0.12f).normalized);
            // Line 1's verified muzzle alignment fix. A raised hand does
            // not prove that the actual mounted barrel points forward:
            // the original Steam NPC rig can hold the rifle sideways.
            // Only rotate the right shoulder of a Right_Hook-mounted gun.
            if (_barrel != null && _model.parent == _rightHand)
            {
                Vector3 bore = _barrel.position - _model.position;
                if (bore.sqrMagnitude > 0.04f)
                {
                    float angularError = Vector3.Angle(bore.normalized, facing);
                    if (angularError > 4f && angularError < 165f)
                    {
                        Quaternion desired = Quaternion.FromToRotation(
                            bore.normalized, facing) * _rightShoulder.rotation;
                        _rightShoulder.rotation = Quaternion.RotateTowards(
                            _rightShoulder.rotation, desired,
                            Mathf.Min(angularError, 320f * Time.deltaTime));
                    }
                }
            }
            if (Time.time >= _nextPoseDiagnostic)
            {
                _nextPoseDiagnostic = Time.time + 12f;
                Vector3 bore = _barrel != null
                    ? _barrel.position - _model.position : Vector3.zero;
                float alignment = bore.sqrMagnitude > 0.04f
                    ? Vector3.Dot(bore.normalized, facing.normalized) : -2f;
                ProbeLogger.Log("NPC_WEAPON_POSE_ACTIVE: actor=" + gameObject.name +
                    " targetVisible=" + (opponent != null) +
                    " barrelForwardDot=" + alignment.ToString("F2"));
            }
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
