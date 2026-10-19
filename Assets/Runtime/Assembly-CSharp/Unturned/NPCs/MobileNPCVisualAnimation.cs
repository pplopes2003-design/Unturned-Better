////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Best-effort adapter for the legacy Animation clips included by the game's NPC visual prefab.
    /// It never requires an Animator Controller or modifies the source visual asset.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCHumanoidMotor))]
    public sealed class MobileNPCVisualAnimation : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float crossFadeDuration = 0.15f;
        [SerializeField, Min(0.1f)] private float walkingFootstepInterval = 0.48f;
        [SerializeField, Min(0.1f)] private float runningFootstepInterval = 0.32f;
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 1.35f;

        private MobileNPCHumanoidMotor motor;
        private MobileNPCBrainDriver brain;
        private MobileNPCHumanoidCombat combat;
        private MobileNPCHumanoidRangedCombat rangedCombat;
        private MobileNPCHumanoidVitals vitals;
        private Animation legacyAnimation;
        private Animator animatorFallback;
        private string idleClip;
        private string walkClip;
        private string runClip;
        private string currentClip;
        private float nextFootstepTime;
        private float attackAnimationUntil;
        private bool jumpPresentationAttempted;
        private bool missingClimbClipLogged;
        private bool missingCombatStrikeClipLogged;
        // Native item-specific animation overlays. Players obtain these
        // from ItemAsset.animations through PlayerAnimator.AddEquippedItemAnimation.
        // NPCs previously instantiated only the gun mesh, leaving arms at
        // their unarmed locomotion/bind-pose angles.
        private AnimationClip[] equippedItemClips;
        private string equippedWeaponHoldClip;
        private string currentWeaponActionClip;
        private float weaponActionUntil;
        private float suppressWeaponPoseUntil;
        private bool weaponPosePlaying;
        private ushort boundWeaponAssetID;
        private Transform weaponLeftShoulder;
        private Transform weaponRightShoulder;
        private Transform weaponSpineHook;
        private bool missingNativeWeaponAnimationLogged;
        private Transform hitReactionSpine;
        private float proceduralHitUntil;
        private float proceduralHitStartedAt;
        private float proceduralHitMagnitude;
        private static readonly MasterBundleReference<AudioClip> NativePunchAudio =
            new MasterBundleReference<AudioClip>("core.masterbundle", "Sounds/MeleeAttack_02.mp3");

        private void Awake()
        {
            motor = GetComponent<MobileNPCHumanoidMotor>();
            brain = GetComponent<MobileNPCBrainDriver>();
            combat = GetComponent<MobileNPCHumanoidCombat>();
            rangedCombat = GetComponent<MobileNPCHumanoidRangedCombat>();
            if (rangedCombat != null)
            {
                rangedCombat.ShotFired += HandleShotFired;
                rangedCombat.ReloadStarted += HandleReloadStarted;
            }
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            if (combat != null)
            {
                combat.AttackStarted += PlayAttackAnimation;
            }
            if (vitals != null)
            {
                vitals.Died += HandleDied;
                vitals.DamageReceived += HandleDamageReceived;
                if (vitals.IsDead)
                {
                    enabled = false;
                    return;
                }
            }
            legacyAnimation = GetComponentInChildren<Animation>(true);
            if (legacyAnimation != null)
            {
                // NPC_Client's legacy rig is driven by Unity's Animation component.
                // The bootstrap strips gameplay scripts, but must not leave this
                // component disabled or the skinned model remains in bind pose.
                legacyAnimation.enabled = true;
            }
            else
            {
                // Some game builds may ship an Animator-driven variant. Preserve its
                // authored controller rather than guessing parameter/state names.
                animatorFallback = GetComponentInChildren<Animator>(true);
                if (animatorFallback != null)
                {
                    animatorFallback.enabled = true;
                    animatorFallback.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
            }
            ResolveClips();
            Transform visual = transform.Find("HumanoidVisual");
            hitReactionSpine = visual != null
                ? visual.Find("Root/Skeleton/Spine") : null;
            if (hitReactionSpine != null)
            {
                weaponLeftShoulder = hitReactionSpine.Find("Left_Shoulder");
                weaponRightShoulder = hitReactionSpine.Find("Right_Shoulder");
                weaponSpineHook = hitReactionSpine.Find("Spine_Hook");
            }
        }

        private void Start()
        {
            PlayIfAvailable(idleClip);
        }

        private void Update()
        {
            if (motor == null)
            {
                return;
            }

            // Only a real tagged LADDER uses Move_Climb/Idle_Climb. A
            // parapet vault is an ordinary gravity-driven Unturned jump,
            // never a slow clamber or a forced ladder animation.
            if (motor.IsClimbingLadder)
            {
                SuspendWeaponPose();
                nextFootstepTime = 0f;
                // These names are present in Human/Basic/Move and Idle
                // importers. Play only if the specific NPC_Client runtime
                // Animation component really references the clip.
                if (legacyAnimation != null)
                {
                    string clip = legacyAnimation["Move_Climb"] != null
                        ? "Move_Climb"
                        : legacyAnimation["Idle_Climb"] != null
                            ? "Idle_Climb" : null;
                    if (clip != null)
                    {
                        legacyAnimation[clip].wrapMode = WrapMode.Loop;
                        PlayIfAvailable(clip);
                    }
                    else if (!missingClimbClipLogged)
                    {
                        missingClimbClipLogged = true;
                        Debug.LogWarning("[MobileNPCVisualAnimation] Native ladder climbing is active "
                            + "but NPC_Client has no Move_Climb/Idle_Climb animation state.", this);
                    }
                }
                return;
            }

            if (motor.IsJumping)
            {
                SuspendWeaponPose();
                nextFootstepTime = 0f;
                if (!jumpPresentationAttempted)
                {
                    jumpPresentationAttempted = true;
                    TryPlayActionClip("Jump", "Leap");
                }
                // NPC_Client does not guarantee an authored Jump clip. Keep
                // its existing run/walk locomotion pose during the airborne
                // movement rather than forcing Ladder's climb animation.
                if (legacyAnimation != null)
                {
                    bool hasJumpClip = false;
                    foreach (AnimationState clip in legacyAnimation)
                    {
                        if (clip != null && !string.IsNullOrEmpty(clip.name)
                            && (clip.name.IndexOf("Jump", System.StringComparison.OrdinalIgnoreCase) >= 0
                                || clip.name.IndexOf("Leap", System.StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            hasJumpClip = true;
                            break;
                        }
                    }
                    if (!hasJumpClip)
                    {
                        string movingPose = !string.IsNullOrEmpty(runClip)
                            ? runClip : !string.IsNullOrEmpty(walkClip) ? walkClip : idleClip;
                        PlayIfAvailable(movingPose);
                    }
                }
                return;
            }
            jumpPresentationAttempted = false;
            MaintainWeaponPose();

            if (legacyAnimation != null && Time.time < attackAnimationUntil)
            {
                return;
            }

            if (legacyAnimation == null)
            {
                // An Animator controller, when present, owns its own default locomotion
                // state machine. Do not force unverified parameters on the native asset.
                if (animatorFallback != null && !animatorFallback.enabled)
                {
                    animatorFallback.enabled = true;
                }
                if (motor.IsMoving)
                {
                    PlayFootstepIfDue(motor.IsSprinting);
                }
                else
                {
                    nextFootstepTime = 0f;
                }
                return;
            }

            string desired = idleClip;
            if (motor.IsMoving)
            {
                bool running = motor.IsSprinting;
                desired = running && !string.IsNullOrEmpty(runClip) ? runClip : walkClip;
                // An actual crouch animation is optional on NPC_Client; never
                // fabricate a state when the corresponding clip is absent.
                if (motor.IsCrouched)
                {
                    string crouchClip = FindCrouchClip();
                    if (crouchClip != null)
                        desired = crouchClip;
                }
                if (string.IsNullOrEmpty(desired))
                {
                    desired = runClip;
                }
                PlayFootstepIfDue(running);
            }
            else
            {
                nextFootstepTime = 0f;
            }

            PlayIfAvailable(desired);
        }

        private string FindCrouchClip()
        {
            if (legacyAnimation == null)
                return null;
            foreach (AnimationState clip in legacyAnimation)
            {
                if (clip == null || string.IsNullOrEmpty(clip.name))
                    continue;
                bool crouch = clip.name.IndexOf("Crouch", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || clip.name.IndexOf("Sneak", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (crouch)
                {
                    clip.wrapMode = WrapMode.Loop;
                    return clip.name;
                }
            }
            return null;
        }

        private void PlayFootstepIfDue(bool isRunning)
        {
#if !DEDICATED_SERVER
            if (Time.time < nextFootstepTime)
            {
                return;
            }

            // Crouched movement produces fewer/quieter footsteps; actual zombie
            // detection integration will follow after audio source verification.
            float interval = motor != null && motor.IsCrouched
                ? walkingFootstepInterval * 1.8f
                : isRunning ? runningFootstepInterval : walkingFootstepInterval;
            nextFootstepTime = Time.time + Mathf.Max(0.1f, interval) * Random.Range(0.92f, 1.08f);

            Vector3 rayOrigin = transform.position + Vector3.up * 0.2f;
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit ground,
                Mathf.Max(0.1f, groundProbeDistance), ~0, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            string materialName = ground.transform != null && ground.transform.CompareTag("Ground")
                ? PhysicsTool.GetTerrainMaterialName(transform.position)
                : ground.collider != null ? ground.collider.sharedMaterial?.name : null;
            if (string.IsNullOrEmpty(materialName))
            {
                return;
            }

            string key = isRunning ? "FootstepRun" : "FootstepWalk";
            OneShotAudioDefinition audioDef = PhysicMaterialCustomData.GetAudioDef(materialName, key);
            if (audioDef == null)
            {
                return;
            }

            AudioClip clip = audioDef.GetRandomClip();
            if (clip == null)
            {
                return;
            }

            OneShotAudioParameters parameters = new OneShotAudioParameters(transform, clip);
            parameters.volume = (motor != null && motor.IsCrouched ? 0.035f : 0.125f) * audioDef.volumeMultiplier;
            parameters.RandomizePitch(audioDef.minPitch, audioDef.maxPitch);
            parameters.SetLinearRolloff(1f, 32f);
            parameters.Play();
#endif
        }

        private void PlayAttackAnimation()
        {
            // Native weapon overlays use the same shoulder bones as the
            // verified Punch_Left/Right action. Temporarily release them
            // so unarmed/melee swings remain visible and ragdoll unaffected.
            suppressWeaponPoseUntil = Time.time + 1.05f;
            SuspendWeaponPose();
            PlayAuthenticMeleeAudio();
            // The actual damage is server-authoritative in MobileNPCHumanoidCombat.
            // This adapter only animates a committed swing. Weapon vs fists
            // affects the preferred native clip family, never grants a hit.
            // The NPC-only visual prefab explicitly includes actual imported
            // Punch_Left/Punch_Right from Human/Basic/Punch/Animation.fbx.
            // NPC_Client did NOT contain them: Idle_Punch_Left/Right were
            // fabricated clip names, so unarmed strikes could be silent poses.
            // Its genuine weaponless kicks are Idle_Kick_Left/Right.
            bool armed = combat != null && combat.LastAttackUsedMeleeWeapon;
            // The checked-in Human/Basic/Punch FBX has Punch_Left/Right,
            // while Human/Basic/NPC has the kick clips. Both fist attacks
            // and held melee items need an ARM strike first; prioritizing
            // kicks for armed attacks made weapons visibly attack with feet.
            // A weapon-specific swing asset can replace this fallback later.
            string[] candidates = Random.value < 0.5f
                ? new[] { "Punch_Left", "Punch_Right",
                    "Idle_Kick_Left", "Idle_Kick_Right" }
                : new[] { "Punch_Right", "Punch_Left",
                    "Idle_Kick_Right", "Idle_Kick_Left" };

            Animation clipOwner = null;
            string clipName = null;
            AnimationState state = null;
            Animation[] animations = GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < animations.Length && state == null; ++i)
            {
                Animation candidate = animations[i];
                if (candidate == null)
                    continue;
                for (int c = 0; c < candidates.Length; ++c)
                {
                    AnimationState found = candidate[candidates[c]];
                    if (found == null)
                        continue;
                    clipOwner = candidate;
                    clipName = candidates[c];
                    state = found;
                    break;
                }
            }

            if (state == null || clipOwner == null)
            {
                if (!missingCombatStrikeClipLogged)
                {
                    Debug.LogWarning("[MobileNPCVisualAnimation] No native "
                        + (armed ? "weapon swing" : "punch/kick")
                        + " clip bound on the mobile humanoid rig. Actual melee hit "
                        + "still uses physical server verification.", this);
                    missingCombatStrikeClipLogged = true;
                }
                return;
            }

            state.wrapMode = WrapMode.Once;
            attackAnimationUntil = Time.time + Mathf.Max(0.2f, state.length);
            currentClip = clipName;
            clipOwner.enabled = true;
            if (!clipOwner.gameObject.activeInHierarchy)
            {
                Debug.LogError("[MobileNPCVisualAnimation] Committed melee strike "
                    + "has a bound clip but its Animation owner is inactive: "
                    + clipOwner.name + " clip=" + clipName + ".", this);
                return;
            }
            clipOwner.CrossFade(clipName, Mathf.Min(crossFadeDuration, 0.08f));
            // A valid AnimationState is NOT proof that CrossFade accepted
            // the command. Explicitly start a one-shot if no playback began.
            // This is visual-only and never applies a second damage event.
            if (!clipOwner.IsPlaying(clipName))
                clipOwner.Play(clipName);
            if (!clipOwner.IsPlaying(clipName))
                Debug.LogError("[MobileNPCVisualAnimation] Melee clip refused playback: "
                    + clipName + " owner=" + clipOwner.name + ".", this);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCVisualAnimation] Confirmed native melee clip "
                + clipName + " for npc=" + name
                + " weapon=" + (armed ? combat.LastAttackWeaponID.ToString() : "fists")
                + " length=" + state.length.ToString("F2"), this);
#endif
        }

        private void PlayAuthenticMeleeAudio()
        {
            if (Dedicator.IsDedicatedServer)
                return;
            AudioClip clip = null;
            if (combat != null && combat.LastAttackUsedMeleeWeapon
                && combat.LastAttackWeaponID != 0)
            {
                ItemMeleeAsset weapon = Assets.find(EAssetType.ITEM,
                    combat.LastAttackWeaponID) as ItemMeleeAsset;
                if (weapon != null)
                    clip = weapon.use;
            }
            if (clip == null)
                clip = NativePunchAudio.loadAsset();
            if (clip == null)
                return;
            OneShotAudioParameters audio = new OneShotAudioParameters(transform, clip);
            audio.volume = 0.3f;
            audio.SetLinearRolloff(1f, 16f);
            audio.Play();
        }

        private void HandleDamageReceived(Transform attacker, float amount)
        {
            if (vitals == null || vitals.IsDead || amount <= 0f)
                return;
            // This native NPC visual has Punch/Kick/Locomotion but no
            // authored "Hit"/"Flinch" full-body clips. If another prefab
            // provides one prefer it; otherwise make a tiny temporary
            // additive reaction on its REAL animated spine, not a fake clip.
            if (!TryPlayActionClip("Hit", "Flinch", "Damage", "Hurt")
                && hitReactionSpine != null && legacyAnimation != null)
            {
                proceduralHitStartedAt = Time.time;
                proceduralHitUntil = Time.time + 0.23f;
                proceduralHitMagnitude = Mathf.Clamp(amount * 0.14f, 2.5f, 8f);
            }
        }

        private void LateUpdate()
        {
            if (hitReactionSpine == null || legacyAnimation == null
                || vitals != null && vitals.IsDead
                || Time.time >= proceduralHitUntil)
                return;
            // Unity's legacy Animation evaluates the normal clip each frame
            // before LateUpdate. Add the short flinch AFTER that evaluation,
            // so it neither replaces walking nor permanently edits bind pose.
            float duration = Mathf.Max(0.05f,
                proceduralHitUntil - proceduralHitStartedAt);
            float phase = Mathf.Clamp01((Time.time - proceduralHitStartedAt) / duration);
            float envelope = Mathf.Sin(phase * Mathf.PI);
            hitReactionSpine.localRotation *= Quaternion.Euler(
                -proceduralHitMagnitude * envelope, 0f,
                proceduralHitMagnitude * envelope * 0.35f);
        }

        private void HandleShotFired()
        {
            // Preserve the held rifle pose when the particular weapon has
            // no authored firing animation. Sound/model are handled by the
            // existing presentation component, not by this visual adapter.
            if (!TryPlayEquippedWeaponAction("Shoot", "Fire", "Recoil"))
                TryPlayActionClip("Shoot", "Fire", "Recoil");
        }

        private void HandleReloadStarted()
        {
            if (!TryPlayEquippedWeaponAction("Reload"))
                TryPlayActionClip("Reload");
        }

        /// <summary>
        /// Mirrors the real third-person PlayerAnimator item setup:
        /// native ItemAsset animation clips mixed on both shoulders and
        /// (when applicable) the item's model/hook. No Animator Controller,
        /// guessed animation names or player gameplay scripts are created.
        /// Invoked only when a new actual weapon model has been attached.
        /// </summary>
        public void BindEquippedWeaponAnimation(ItemAsset asset, Transform itemModel)
        {
            ClearEquippedWeaponAnimation();
            if (asset == null || legacyAnimation == null)
                return;
            AnimationClip[] nativeClips = asset.animations;
            if (nativeClips == null || nativeClips.Length == 0
                || weaponLeftShoulder == null || weaponRightShoulder == null)
            {
                if (!missingNativeWeaponAnimationLogged)
                {
                    missingNativeWeaponAnimationLogged = true;
                    Debug.LogWarning("[MobileNPCWeaponPose] Native item animation or "
                        + "humanoid shoulder bones unavailable for item " + asset.id + ".", this);
                }
                return;
            }
            equippedItemClips = nativeClips;
            boundWeaponAssetID = asset.id;
            for (int i = 0; i < nativeClips.Length; ++i)
            {
                AnimationClip clip = nativeClips[i];
                if (clip == null || string.IsNullOrEmpty(clip.name)
                    || !clip.legacy)
                    continue;
                // Avoid overwriting NPC_Client's validated locomotion or
                // punch clips. If an asset reuses one of those names the
                // native overlay is not safe on this independent rig.
                if (legacyAnimation[clip.name] != null)
                    continue;
                legacyAnimation.AddClip(clip, clip.name);
                AnimationState state = legacyAnimation[clip.name];
                if (state == null)
                    continue;
                state.AddMixingTransform(weaponLeftShoulder, true);
                state.AddMixingTransform(weaponRightShoulder, true);
                if (itemModel != null && weaponSpineHook != null)
                {
                    state.AddMixingTransform(weaponSpineHook, true);
                    state.AddMixingTransform(itemModel, true);
                }
                state.layer = 1;
                state.blendMode = AnimationBlendMode.Blend;
            }
            // An authored Idle is preferred for an actual sustained stance.
            // If absent, Equip's final native keyframe is a better weapon
            // hold than leaving arms in a low unarmed walking pose.
            if (IsOwnedWeaponClip("Idle"))
                equippedWeaponHoldClip = "Idle";
            else if (IsOwnedWeaponClip("Equip"))
                equippedWeaponHoldClip = "Equip";
            else
            {
                for (int i = 0; i < nativeClips.Length; ++i)
                {
                    if (nativeClips[i] != null
                        && IsOwnedWeaponClip(nativeClips[i].name)
                        && nativeClips[i].name.IndexOf("Aim",
                            System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        equippedWeaponHoldClip = nativeClips[i].name;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(equippedWeaponHoldClip))
            {
                if (!missingNativeWeaponAnimationLogged)
                {
                    missingNativeWeaponAnimationLogged = true;
                    Debug.LogWarning("[MobileNPCWeaponPose] Item " + asset.id
                        + " has no usable Idle/Equip/Aim legacy hold clip.", this);
                }
                return;
            }
            weaponPosePlaying = false;
            weaponActionUntil = 0f;
            MaintainWeaponPose();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCWeaponPose] Native equipped item " + asset.id
                + " hold=" + equippedWeaponHoldClip
                + " clips=" + nativeClips.Length + ".", this);
#endif
        }

        private bool IsOwnedWeaponClip(string clipName)
        {
            if (equippedItemClips == null || legacyAnimation == null
                || string.IsNullOrEmpty(clipName)
                || legacyAnimation[clipName] == null)
                return false;
            for (int i = 0; i < equippedItemClips.Length; ++i)
                if (equippedItemClips[i] != null
                    && equippedItemClips[i].name == clipName
                    && equippedItemClips[i].legacy)
                    return true;
            return false;
        }

        private void MaintainWeaponPose()
        {
            if (legacyAnimation == null
                || string.IsNullOrEmpty(equippedWeaponHoldClip)
                || !IsOwnedWeaponClip(equippedWeaponHoldClip)
                || Time.time < suppressWeaponPoseUntil
                || vitals != null && vitals.IsDead)
                return;
            if (Time.time < weaponActionUntil)
                return;
            if (weaponPosePlaying && legacyAnimation.IsPlaying(equippedWeaponHoldClip))
                return;
            currentWeaponActionClip = null;
            AnimationState state = legacyAnimation[equippedWeaponHoldClip];
            state.layer = 1;
            state.wrapMode = equippedWeaponHoldClip == "Idle"
                ? WrapMode.Loop : WrapMode.ClampForever;
            state.enabled = true;
            legacyAnimation.enabled = true;
            legacyAnimation.CrossFade(equippedWeaponHoldClip, 0.12f);
            if (!legacyAnimation.IsPlaying(equippedWeaponHoldClip))
                legacyAnimation.Play(equippedWeaponHoldClip);
            weaponPosePlaying = legacyAnimation.IsPlaying(equippedWeaponHoldClip);
        }

        private void SuspendWeaponPose()
        {
            if (legacyAnimation == null || equippedItemClips == null)
                return;
            for (int i = 0; i < equippedItemClips.Length; ++i)
            {
                AnimationClip clip = equippedItemClips[i];
                if (clip != null && IsOwnedWeaponClip(clip.name))
                    legacyAnimation.Stop(clip.name);
            }
            currentWeaponActionClip = null;
            weaponActionUntil = 0f;
            weaponPosePlaying = false;
        }

        private bool TryPlayEquippedWeaponAction(params string[] names)
        {
            if (legacyAnimation == null || equippedItemClips == null
                || Time.time < suppressWeaponPoseUntil
                || vitals != null && vitals.IsDead)
                return false;
            for (int n = 0; n < names.Length; ++n)
            {
                string clipName = names[n];
                if (!IsOwnedWeaponClip(clipName))
                    continue;
                AnimationState state = legacyAnimation[clipName];
                if (state == null)
                    continue;
                state.wrapMode = WrapMode.Once;
                state.layer = 1;
                legacyAnimation.enabled = true;
                legacyAnimation.CrossFade(clipName, Mathf.Min(crossFadeDuration, 0.08f));
                if (!legacyAnimation.IsPlaying(clipName))
                    legacyAnimation.Play(clipName);
                if (!legacyAnimation.IsPlaying(clipName))
                    return false;
                weaponPosePlaying = false;
                currentWeaponActionClip = clipName;
                weaponActionUntil = Time.time + Mathf.Max(0.15f, state.length);
                return true;
            }
            return false;
        }

        public void ClearEquippedWeaponAnimation()
        {
            if (equippedItemClips != null && legacyAnimation != null)
            {
                SuspendWeaponPose();
                for (int i = 0; i < equippedItemClips.Length; ++i)
                {
                    AnimationClip clip = equippedItemClips[i];
                    if (clip != null && IsOwnedWeaponClip(clip.name))
                        legacyAnimation.RemoveClip(clip.name);
                }
            }
            equippedItemClips = null;
            equippedWeaponHoldClip = null;
            currentWeaponActionClip = null;
            boundWeaponAssetID = 0;
            weaponPosePlaying = false;
            weaponActionUntil = 0f;
        }

        private bool TryPlayActionClip(params string[] fragments)
        {
            if (vitals != null && vitals.IsDead)
                return false;

            Animation[] animations = GetComponentsInChildren<Animation>(true);
            for (int a = 0; a < animations.Length; ++a)
            {
                Animation animation = animations[a];
                if (animation == null)
                    continue;
                foreach (AnimationState state in animation)
                {
                    if (state == null || string.IsNullOrEmpty(state.name))
                        continue;
                    bool matches = false;
                    for (int i = 0; i < fragments.Length; ++i)
                    {
                        if (state.name.IndexOf(fragments[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matches = true;
                            break;
                        }
                    }
                    if (!matches)
                        continue;
                    state.wrapMode = WrapMode.Once;
                    animation.enabled = true;
                    animation.CrossFade(state.name, Mathf.Min(crossFadeDuration, 0.08f));
                    attackAnimationUntil = Time.time + Mathf.Max(0.15f, state.length);
                    currentClip = state.name;
                    return true;
                }
            }
            // A missing native clip is not a reason to disable melee or
            // render an invisible hit reaction. Caller may use a bounded
            // procedural animation instead.
            return false;
        }

        private void HandleDied()
        {
            ClearEquippedWeaponAnimation();
            // Death presentation releases the authored bone rigidbodies. Stop this
            // locomotion writer so it cannot cross-fade Idle_Stand over the ragdoll.
            enabled = false;
        }

        private void OnDestroy()
        {
            ClearEquippedWeaponAnimation();
            if (combat != null)
            {
                combat.AttackStarted -= PlayAttackAnimation;
            }
            if (vitals != null)
            {
                vitals.Died -= HandleDied;
                vitals.DamageReceived -= HandleDamageReceived;
            }
            if (rangedCombat != null)
            {
                rangedCombat.ShotFired -= HandleShotFired;
                rangedCombat.ReloadStarted -= HandleReloadStarted;
            }
        }

        private void ResolveClips()
        {
            if (legacyAnimation == null)
            {
                Debug.LogWarning("[MobileNPCVisualAnimation] NPC_Client has no legacy Animation component; "
                    + (animatorFallback != null
                        ? "the authored Animator controller will run without custom locomotion mapping."
                        : "no supported animation system was found."), this);
                return;
            }

            // These names are confirmed in the checked-in source asset metadata:
            // Human/Basic/Idle/Animation.fbx -> Idle_Stand
            // Human/Basic/Move/Animation.fbx -> Move_Walk, Move_Run.
            // Do not infer roles from substrings: NPC_Client also contains many
            // Idle_* gestures, which are not suitable locomotion states.
            idleClip = "Idle_Stand";
            walkClip = "Move_Walk";
            runClip = "Move_Run";

            ValidateRequiredClip(idleClip);
            ValidateRequiredClip(walkClip);
            ValidateRequiredClip(runClip);

            // The source importer marks the movement clips as looping, but set the
            // runtime state explicitly as well so prefab/global wrap settings cannot
            // turn walking or running into a one-shot animation.
            SetLoopingState(idleClip);
            SetLoopingState(walkClip);
            SetLoopingState(runClip);

            Debug.Log("[MobileNPCVisualAnimation] Explicit locomotion map: idle=" + idleClip
                + ", walk=" + walkClip + ", run=" + runClip + ".", this);
        }

        private void ValidateRequiredClip(string clipName)
        {
            if (legacyAnimation[clipName] == null)
            {
                Debug.LogError("[MobileNPCVisualAnimation] Required clip '" + clipName
                    + "' is missing from NPC_Client's Animation component. Check the prefab's m_Animations references.",
                    this);
            }
        }

        private void SetLoopingState(string clipName)
        {
            AnimationState state = legacyAnimation != null ? legacyAnimation[clipName] : null;
            if (state != null)
            {
                state.wrapMode = WrapMode.Loop;
            }
        }

        private void PlayIfAvailable(string clipName)
        {
            if (legacyAnimation == null || string.IsNullOrEmpty(clipName) || currentClip == clipName
                || legacyAnimation[clipName] == null)
            {
                return;
            }

            currentClip = clipName;
            if (crossFadeDuration > 0f)
            {
                legacyAnimation.CrossFade(clipName, crossFadeDuration);
            }
            else
            {
                legacyAnimation.Play(clipName);
            }
        }
    }
}
