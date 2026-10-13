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

            // The checked-in NPC_Client rig has Idle_Climb and Move_Climb,
            // but no verified authored Jump/Vault clip. Only attempt a real
            // clip if that runtime prefab actually supplies one. Never repeat
            // or restart it every frame and never emit ground footsteps midair.
            if (motor.IsJumping)
            {
                nextFootstepTime = 0f;
                if (!jumpPresentationAttempted)
                {
                    jumpPresentationAttempted = true;
                    TryPlayActionClip("Jump", "Leap");
                }
                return;
            }
            jumpPresentationAttempted = false;

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
            if (legacyAnimation == null)
            {
                return;
            }

            // Prefer the two confirmed native kick/strike clips. Search every legacy
            // Animation component under the visual rig because some NPC_Client prefab
            // variants split gestures and locomotion across different child objects.
            string[] candidates = Random.value < 0.5f
                ? new[] { "Idle_Kick_Left", "Idle_Kick_Right" }
                : new[] { "Idle_Kick_Right", "Idle_Kick_Left" };
            Animation clipOwner = null;
            string clipName = null;
            AnimationState state = null;
            Animation[] animations = GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < animations.Length && state == null; ++i)
            {
                Animation candidate = animations[i];
                if (candidate == null || candidate == legacyAnimation && candidate[candidates[0]] == null
                    && candidate[candidates[1]] == null)
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

            if (state == null || clipOwner == null || string.IsNullOrEmpty(clipName))
            {
                Debug.LogWarning("[MobileNPCVisualAnimation] Neither native strike clip (Idle_Kick_Left/Right) "
                    + "is available on the NPC_Client animation hierarchy.", this);
                return;
            }

            state.wrapMode = WrapMode.Once;
            attackAnimationUntil = Time.time + Mathf.Max(0.2f, state.length);
            currentClip = clipName;
            clipOwner.enabled = true;
            clipOwner.CrossFade(clipName, Mathf.Min(crossFadeDuration, 0.08f));
        }

        private void HandleDamageReceived(Transform attacker, float amount)
        {
            if (vitals == null || vitals.IsDead || amount <= 0f)
                return;
            // Only use an actual hit-reaction clip installed on this prefab.
            TryPlayActionClip("Hit", "Flinch", "Damage", "Hurt");
        }

        private void HandleShotFired()
        {
            // Only play clips verified present on this runtime Animation rig.
            // Shoot audio and weapon model are owned by MobileNPCRangedPresentation.
            TryPlayActionClip("Shoot", "Fire", "Recoil");
        }

        private void HandleReloadStarted()
        {
            TryPlayActionClip("Reload");
        }

        private void TryPlayActionClip(params string[] fragments)
        {
            if (vitals != null && vitals.IsDead)
                return;

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
                    return;
                }
            }
            // No fabricated animation when the actual NPC_Client rig lacks one.
        }

        private void HandleDied()
        {
            // Death presentation releases the authored bone rigidbodies. Stop this
            // locomotion writer so it cannot cross-fade Idle_Stand over the ragdoll.
            enabled = false;
        }

        private void OnDestroy()
        {
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
