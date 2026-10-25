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
        private MobileNPCHumanoidAim humanoidAim;
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
        // ItemAsset often names its hold clip "Idle", which is also the
        // NPC_Client locomotion clip. Never borrow/play the locomotion state
        // as an equipped-item pose. Register each item state under an alias.
        private string[] equippedItemStateNames;
        private string equippedWeaponHoldClip;
        private string currentWeaponActionClip;
        private float weaponActionUntil;
        private float suppressWeaponPoseUntil;
        private bool weaponPosePlaying;
        private ushort boundWeaponAssetID;
        private Transform boundWeaponModel;
        private Transform boundWeaponBarrel;
        private float nextMuzzleDiagnosticAt;
        private Transform weaponLeftShoulder;
        private Transform weaponRightShoulder;
        private Transform weaponSpineHook;
        private bool missingNativeWeaponAnimationLogged;
        // Native ItemAsset.Equip can successfully Play yet leave the arms in
        // the ordinary unarmed stance on the stripped NPC_Client visual.
        // Prove actual shoulder-to-hand elevation in WORLD space first.
        // If the native clip has no effective shoulder motion, provide a
        // small bounded anatomical fallback instead of a floating gun.
        private Transform weaponLeftHand;
        private Transform weaponRightHand;
        private Quaternion weaponLeftShoulderNeutral;
        private Quaternion weaponRightShoulderNeutral;
        private bool weaponVisualActive;
        private float weaponBoundAt;
        private float gunPoseFallbackBlend;
        private float gunPoseCheckAt;
        private float gunFallbackShotAt;
        private float gunFallbackReloadUntil;
        private bool weaponPoseFallbackReported;
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
            humanoidAim = GetComponent<MobileNPCHumanoidAim>();
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
                weaponLeftHand = weaponLeftShoulder != null
                    ? weaponLeftShoulder.Find("Left_Arm/Left_Hand") : null;
                weaponRightHand = weaponRightShoulder != null
                    ? weaponRightShoulder.Find("Right_Arm/Right_Hand") : null;
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
            if (vitals != null && vitals.IsDead)
                return;

            // Normal native Animation evaluation has already finished. An
            // ineffective Equip state cannot be detected merely with IsPlaying.
            UpdateActualGunShoulderPose();

            if (hitReactionSpine == null || legacyAnimation == null
                || Time.time >= proceduralHitUntil)
                return;
            // Add the short flinch after the standard clip was evaluated.
            float duration = Mathf.Max(0.05f,
                proceduralHitUntil - proceduralHitStartedAt);
            float phase = Mathf.Clamp01((Time.time - proceduralHitStartedAt) / duration);
            float envelope = Mathf.Sin(phase * Mathf.PI);
            hitReactionSpine.localRotation *= Quaternion.Euler(
                -proceduralHitMagnitude * envelope, 0f,
                proceduralHitMagnitude * envelope * 0.35f);
        }

        /// <summary>
        /// When the native item overlay ACTUALLY raises both hands, leave it
        /// alone. Only an ineffective/missing overlay triggers this fallback.
        /// The shot's authoritative direction, enemy visibility and damage
        /// remain entirely outside this presentation-only component.
        /// </summary>
        private void UpdateActualGunShoulderPose()
        {
            if (!weaponVisualActive
                || weaponLeftShoulder == null || weaponRightShoulder == null
                || weaponLeftHand == null || weaponRightHand == null
                || (motor != null && (motor.IsClimbingLadder || motor.IsJumping))
                || Time.time < suppressWeaponPoseUntil
                || Time.time < weaponBoundAt + 0.35f)
            {
                gunPoseFallbackBlend = 0f;
                return;
            }

            // A real rifle stance projects both hands ahead of their
            // shoulders. When the Equip/Idle animation remains in low-arm
            // locomotion, the weapon model follows the right hand downward.
            bool nativePoseRaised =
                IsHandExtendedForWeapon(weaponRightShoulder, weaponRightHand)
                && IsHandExtendedForWeapon(weaponLeftShoulder, weaponLeftHand);

            // A fallback held last frame is itself forward! Do not use that
            // procedural output as proof a native clip started working.
            // An extended arm does not mean the actual gun is pointing where
            // the survivor faces. The rig may raise both hands yet leave the
            // native Barrel sideways. Compare the *weapon* as well as the hands.
            Vector3 intendedForward = transform.forward;
            intendedForward.y = 0f;
            if (intendedForward.sqrMagnitude < 0.01f)
                intendedForward = Vector3.forward;
            intendedForward.Normalize();
            bool nativeBarrelAligned = boundWeaponBarrel != null
                && Vector3.Dot(boundWeaponBarrel.forward, intendedForward) > 0.70f;
            bool nativePoseAvailable = gunPoseFallbackBlend <= 0.001f
                && nativePoseRaised && nativeBarrelAligned && legacyAnimation != null
                && IsOwnedWeaponClip(equippedWeaponHoldClip)
                && weaponPosePlaying;

            if (nativePoseAvailable)
            {
                gunPoseFallbackBlend = 0f;
                return;
            }

            gunPoseFallbackBlend = Mathf.MoveTowards(gunPoseFallbackBlend,
                1f, Time.deltaTime * 5f);
            if (!weaponPoseFallbackReported
                && Time.time >= gunPoseCheckAt)
            {
                gunPoseCheckAt = Time.time + 8f;
                weaponPoseFallbackReported = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning("[MobileNPCWeaponPose] Native rifle stance "
                    + "was not visually effective; using verified shoulder "
                    + "bones as procedural fallback npc=" + name
                    + " item=" + boundWeaponAssetID
                    + " nativeClip=" + (equippedWeaponHoldClip ?? "(none)")
                    + ".", this);
#endif
            }

            // Reset to the actual NPC_Client shoulder neutral before each
            // procedural evaluation: otherwise repeated LateUpdate calls
            // would cumulatively twist the arms, even without animation.
            weaponRightShoulder.localRotation = weaponRightShoulderNeutral;
            weaponLeftShoulder.localRotation = weaponLeftShoulderNeutral;

            Vector3 bodyForward = transform.forward;
            bodyForward.y = 0f;
            if (bodyForward.sqrMagnitude < 0.05f)
                bodyForward = Vector3.forward;
            bodyForward.Normalize();
            Vector3 right = transform.right;
            right.y = 0f;
            right.Normalize();

            // Use the existing server-observed virtual crosshair for VERTICAL
            // presentation only. Never rotate arms toward an unseen enemy
            // or override a native weapon animation that already works.
            // Clamp elevation so the stripped NPC_Client rig cannot twist
            // shoulder bones toward unreachable world-space directions.
            Vector3 aimForward = bodyForward;
            if (brain != null && humanoidAim != null && brain.Target != null
                && humanoidAim.TryGetCurrentAimPointServer(brain.Target, out Vector3 observedPoint))
            {
                Vector3 fromShoulders = observedPoint - (weaponRightShoulder.position
                    + weaponLeftShoulder.position) * 0.5f;
                Vector3 planarAim = new Vector3(fromShoulders.x, 0f, fromShoulders.z);
                float horizontal = planarAim.magnitude;
                if (horizontal > 0.15f)
                {
                    // Aim the upper body toward the SAME observed point as the
                    // server's virtual crosshair, including horizontal bearing.
                    // Do not change the actual server shot origin or ballistics.
                    // Limit shoulder twist while the motor turns the hips.
                    planarAim /= horizontal;
                    float signedYaw = Vector3.SignedAngle(bodyForward, planarAim, Vector3.up);
                    Vector3 shoulderForward = Quaternion.AngleAxis(
                        Mathf.Clamp(signedYaw, -50f, 50f), Vector3.up) * bodyForward;
                    float verticalSlope = Mathf.Tan(Mathf.Clamp(
                        Mathf.Atan2(fromShoulders.y, horizontal) * Mathf.Rad2Deg,
                        -35f, 35f) * Mathf.Deg2Rad);
                    aimForward = (shoulderForward + Vector3.up * verticalSlope).normalized;
                }
            }

            // Position the right weapon hand forward and slightly inward.
            // The left hand supports the rifle from the other side. This
            // is bounded upper-arm presentation, NOT collision-free IK.
            float reloadLowering = Time.time < gunFallbackReloadUntil ? 0.38f : 0f;
            float recoil = Mathf.Clamp01(1f - (Time.time - gunFallbackShotAt) / 0.13f);
            Vector3 rightDirection = (aimForward * (0.94f - reloadLowering)
                - right * 0.12f
                + Vector3.down * (0.10f + reloadLowering * 0.5f + recoil * 0.08f)).normalized;
            Vector3 leftDirection = (aimForward * (0.88f - reloadLowering)
                + right * 0.16f
                + Vector3.down * (0.13f + reloadLowering * 0.6f)).normalized;
            AimShoulderToward(weaponRightShoulder, weaponRightHand,
                rightDirection, gunPoseFallbackBlend);
            AimShoulderToward(weaponLeftShoulder, weaponLeftHand,
                leftDirection, gunPoseFallbackBlend);

            // The vanilla hand-hook rotation alone does not prove that an NPC
            // rig's weapon barrel faces its observed aim direction. The Test4
            // log recorded barrelForwardDotBody around -0.10 with a live gun.
            // Correct only the VISUAL model after the procedural arm pose;
            // leave the authoritative ballistic origin and hit validation intact.
            if (boundWeaponModel != null && boundWeaponBarrel != null
                && gunPoseFallbackBlend > 0.01f)
            {
                // Start from the same native third-person attachment every
                // frame; never compound yesterday's procedural correction.
                if (boundWeaponModel.parent != null
                    && boundWeaponModel.parent.name == "Right_Hook")
                {
                    boundWeaponModel.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    Vector3 barrelForward = boundWeaponBarrel.forward;
                    if (barrelForward.sqrMagnitude > 0.5f)
                    {
                        Quaternion alignment = Quaternion.FromToRotation(
                            barrelForward.normalized, aimForward);
                        boundWeaponModel.rotation = Quaternion.Slerp(
                            boundWeaponModel.rotation,
                            alignment * boundWeaponModel.rotation,
                            gunPoseFallbackBlend);
                    }
                }
            }
        }

        private bool IsHandExtendedForWeapon(Transform shoulder, Transform hand)
        {
            Vector3 arm = hand.position - shoulder.position;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (arm.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.1f)
                return false;
            arm.Normalize();
            forward.Normalize();
            return Vector3.Dot(arm, forward) >= 0.33f
                && Vector3.Dot(arm, Vector3.up) > -0.47f;
        }

        private static void AimShoulderToward(Transform shoulder,
            Transform hand, Vector3 direction, float weight)
        {
            Vector3 currentArm = hand.position - shoulder.position;
            if (currentArm.sqrMagnitude < 0.01f || direction.sqrMagnitude < 0.01f)
                return;
            Quaternion correction = Quaternion.FromToRotation(
                currentArm.normalized, direction.normalized);
            Quaternion desired = correction * shoulder.rotation;
            shoulder.rotation = Quaternion.Slerp(shoulder.rotation,
                desired, Mathf.Clamp01(weight) * 0.92f);
        }

        private void HandleShotFired()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Evidence only: a held model pivot or wrist is NOT a verified barrel tip.
            // Compare actual simulated shot origin with visual anchors at a bounded rate.
            // Never silently move ballistic rays onto arbitrary item pivots.
            if (Time.time >= nextMuzzleDiagnosticAt && rangedCombat != null
                && boundWeaponModel != null && weaponRightHand != null)
            {
                nextMuzzleDiagnosticAt = Time.time + 10f;
                Vector3 shotOrigin = rangedCombat.ShotOriginServer;
                Debug.Log("[MobileNPCMuzzleAudit] npc=" + name
                    + " gun=" + boundWeaponAssetID
                    + " ballisticOrigin=" + shotOrigin
                    + " modelPivot=" + boundWeaponModel.position
                    + " rightHand=" + weaponRightHand.position
                    + " nativeBarrel=" + (boundWeaponBarrel != null
                        ? boundWeaponBarrel.position.ToString() : "(missing)")
                    + " offsetToBarrel=" + (boundWeaponBarrel != null
                        ? Vector3.Distance(shotOrigin, boundWeaponBarrel.position).ToString("F2") + "m"
                        : "(unavailable)")
                    + " barrelForwardDotBody=" + (boundWeaponBarrel != null
                        ? Vector3.Dot(boundWeaponBarrel.forward, transform.forward).ToString("F2")
                        : "(unavailable)")
                    + " modelForwardDotBody=" + Vector3.Dot(boundWeaponModel.forward,
                        transform.forward).ToString("F2")
                    + " offsetToModel=" + Vector3.Distance(shotOrigin, boundWeaponModel.position).ToString("F2")
                    + "m. Barrel marks muzzle effects, not automatic permission to relocate ballistic origin.", this);
            }
#endif
            gunFallbackShotAt = Time.time;
            // Preserve the held rifle pose when the particular weapon has
            // no authored firing animation. Sound/model are handled by the
            // existing presentation component, not by this visual adapter.
            if (!TryPlayEquippedWeaponAction("Shoot", "Fire", "Recoil"))
                TryPlayActionClip("Shoot", "Fire", "Recoil");
        }

        private void HandleReloadStarted()
        {
            gunFallbackReloadUntil = Time.time + 0.55f;
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
            if (asset == null)
                return;

            // Called only after the real rifle model has been bound to the
            // hand hook. If the native item has no playable legacy clips,
            // its appearance still needs to raise the arms.
            weaponVisualActive = asset is ItemGunAsset;
            weaponBoundAt = Time.time;
            boundWeaponAssetID = asset.id;
            boundWeaponModel = itemModel;
            // PlayerEquipment's THIRD-PERSON equipment path positions the
            // instantiated native model at its hook origin with a +90 degree
            // local Z rotation. The NPC uses the same native hand hook and
            // item prefabs, so do not leave the model with a prefab-local
            // rotation that turns the muzzle sideways or backwards.
            // Only touch actual hand-hook attachments, never spine gear.
            if (itemModel != null && itemModel.parent != null
                && itemModel.parent.name == "Right_Hook")
            {
                itemModel.localPosition = Vector3.zero;
                itemModel.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (itemModel != null && weaponVisualActive)
                Debug.Log("[MobileNPCWeaponMount] npc=" + name
                    + " item=" + asset.id
                    + " parent=" + (itemModel.parent != null ? itemModel.parent.name : "(none)")
                    + " localEuler=" + itemModel.localEulerAngles
                    + " matchesPlayerRightHook="
                    + (itemModel.parent != null && itemModel.parent.name == "Right_Hook"), this);
#endif
            boundWeaponBarrel = null;
            // Unturned's native gun prefab convention names the muzzle effect
            // attachment "Barrel". Do not mistake the model pivot for it.
            if (itemModel != null)
            {
                Transform[] children = itemModel.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < children.Length; ++i)
                {
                    if (children[i] != null && children[i].name == "Barrel")
                    {
                        boundWeaponBarrel = children[i];
                        break;
                    }
                }
            }
            weaponPoseFallbackReported = false;
            gunPoseFallbackBlend = 0f;
            if (weaponVisualActive && (weaponLeftShoulder == null
                || weaponRightShoulder == null || weaponLeftHand == null
                || weaponRightHand == null))
                Debug.LogError("[MobileNPCWeaponPose] Rifle visual exists but NPC_Client "
                    + "shoulder/hand rig is missing; cannot animate held weapon item="
                    + asset.id + " npc=" + name + ".", this);
            if (weaponLeftShoulder != null)
                weaponLeftShoulderNeutral = weaponLeftShoulder.localRotation;
            if (weaponRightShoulder != null)
                weaponRightShoulderNeutral = weaponRightShoulder.localRotation;
            if (legacyAnimation == null)
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
            equippedItemStateNames = new string[nativeClips.Length];
            boundWeaponAssetID = asset.id;
            for (int i = 0; i < nativeClips.Length; ++i)
            {
                AnimationClip clip = nativeClips[i];
                if (clip == null || string.IsNullOrEmpty(clip.name)
                    || !clip.legacy)
                    continue;
                string itemStateName = "MobileNPC_Item_" + asset.id + "_"
                    + i + "_" + clip.name;
                // Register the original clip under a unique local state
                // name. NPC walk/idle/punch clips remain untouched even
                // when an item also calls its animation Idle or Equip.
                if (legacyAnimation[itemStateName] != null)
                    legacyAnimation.RemoveClip(itemStateName);
                legacyAnimation.AddClip(clip, itemStateName);
                equippedItemStateNames[i] = itemStateName;
                AnimationState state = legacyAnimation[itemStateName];
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
            // The player's firearm has a forward aiming state separate
            // from Equip: holding its Equip final frame can leave the
            // character's arms down despite a loaded rifle. Prefer a real
            // authored Aim pose (if present), then Idle, then Equip.
            if (weaponVisualActive && IsOwnedWeaponClip("Aim"))
                equippedWeaponHoldClip = "Aim";
            else if (weaponVisualActive && IsOwnedWeaponClip("Aim_Idle"))
                equippedWeaponHoldClip = "Aim_Idle";
            else if (IsOwnedWeaponClip("Idle"))
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

        private string GetOwnedWeaponStateName(string originalClipName)
        {
            if (equippedItemClips == null || equippedItemStateNames == null
                || legacyAnimation == null || string.IsNullOrEmpty(originalClipName))
                return null;
            for (int i = 0; i < equippedItemClips.Length; ++i)
            {
                AnimationClip clip = equippedItemClips[i];
                string stateName = equippedItemStateNames[i];
                if (clip != null && clip.legacy
                    && clip.name == originalClipName
                    && !string.IsNullOrEmpty(stateName)
                    && legacyAnimation[stateName] != null)
                    return stateName;
            }
            return null;
        }

        private bool IsOwnedWeaponClip(string clipName)
        {
            return GetOwnedWeaponStateName(clipName) != null;
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
            string holdStateName = GetOwnedWeaponStateName(equippedWeaponHoldClip);
            if (string.IsNullOrEmpty(holdStateName))
                return;
            if (weaponPosePlaying && legacyAnimation.IsPlaying(holdStateName))
                return;
            currentWeaponActionClip = null;
            AnimationState state = legacyAnimation[holdStateName];
            state.layer = 1;
            state.wrapMode = equippedWeaponHoldClip == "Idle"
                || equippedWeaponHoldClip == "Aim"
                || equippedWeaponHoldClip == "Aim_Idle"
                ? WrapMode.Loop : WrapMode.ClampForever;
            state.enabled = true;
            legacyAnimation.enabled = true;
            legacyAnimation.CrossFade(holdStateName, 0.12f);
            if (!legacyAnimation.IsPlaying(holdStateName))
                legacyAnimation.Play(holdStateName);
            weaponPosePlaying = legacyAnimation.IsPlaying(holdStateName);
        }

        private void SuspendWeaponPose()
        {
            if (legacyAnimation == null || equippedItemClips == null)
                return;
            for (int i = 0; i < equippedItemClips.Length; ++i)
            {
                string stateName = equippedItemStateNames != null
                    && i < equippedItemStateNames.Length
                    ? equippedItemStateNames[i] : null;
                if (!string.IsNullOrEmpty(stateName)
                    && legacyAnimation[stateName] != null)
                    legacyAnimation.Stop(stateName);
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
                string actionStateName = GetOwnedWeaponStateName(clipName);
                AnimationState state = actionStateName != null
                    ? legacyAnimation[actionStateName] : null;
                if (state == null)
                    continue;
                state.wrapMode = WrapMode.Once;
                state.layer = 1;
                legacyAnimation.enabled = true;
                legacyAnimation.CrossFade(actionStateName, Mathf.Min(crossFadeDuration, 0.08f));
                if (!legacyAnimation.IsPlaying(actionStateName))
                    legacyAnimation.Play(actionStateName);
                if (!legacyAnimation.IsPlaying(actionStateName))
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
                    string stateName = equippedItemStateNames != null
                        && i < equippedItemStateNames.Length
                        ? equippedItemStateNames[i] : null;
                    if (!string.IsNullOrEmpty(stateName)
                        && legacyAnimation[stateName] != null)
                        legacyAnimation.RemoveClip(stateName);
                }
            }
            equippedItemClips = null;
            equippedItemStateNames = null;
            weaponVisualActive = false;
            boundWeaponModel = null;
            boundWeaponBarrel = null;
            gunPoseFallbackBlend = 0f;
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
