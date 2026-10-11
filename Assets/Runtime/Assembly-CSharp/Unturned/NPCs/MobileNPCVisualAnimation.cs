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
        private Animation legacyAnimation;
        private string idleClip;
        private string walkClip;
        private string runClip;
        private string currentClip;
        private float nextFootstepTime;

        private void Awake()
        {
            motor = GetComponent<MobileNPCHumanoidMotor>();
            brain = GetComponent<MobileNPCBrainDriver>();
            legacyAnimation = GetComponentInChildren<Animation>(true);
            ResolveClips();
        }

        private void Start()
        {
            PlayIfAvailable(idleClip);
        }

        private void Update()
        {
            if (legacyAnimation == null || motor == null)
            {
                return;
            }

            string desired = idleClip;
            if (motor.IsMoving)
            {
                bool pursuing = brain != null && brain.CurrentState == MobileNPCBrain.State.Pursuing;
                desired = pursuing && !string.IsNullOrEmpty(runClip) ? runClip : walkClip;
                if (string.IsNullOrEmpty(desired))
                {
                    desired = runClip;
                }
                PlayFootstepIfDue(pursuing);
            }
            else
            {
                nextFootstepTime = 0f;
            }

            PlayIfAvailable(desired);
        }

        private void PlayFootstepIfDue(bool isRunning)
        {
#if !DEDICATED_SERVER
            if (Time.time < nextFootstepTime)
            {
                return;
            }

            float interval = isRunning ? runningFootstepInterval : walkingFootstepInterval;
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
            parameters.volume = 0.125f * audioDef.volumeMultiplier;
            parameters.RandomizePitch(audioDef.minPitch, audioDef.maxPitch);
            parameters.SetLinearRolloff(1f, 32f);
            parameters.Play();
#endif
        }

        private void ResolveClips()
        {
            if (legacyAnimation == null)
            {
                return;
            }

            string firstClip = null;
            foreach (AnimationState state in legacyAnimation)
            {
                if (state == null || string.IsNullOrEmpty(state.name))
                {
                    continue;
                }

                if (firstClip == null)
                {
                    firstClip = state.name;
                }

                string name = state.name.ToLowerInvariant();
                if (idleClip == null && (name.Contains("idle") || name.Contains("stand")))
                {
                    idleClip = state.name;
                }
                if (walkClip == null && (name.Contains("walk") || name.Contains("move")))
                {
                    walkClip = state.name;
                }
                if (runClip == null && (name.Contains("run") || name.Contains("sprint")))
                {
                    runClip = state.name;
                }
            }

            if (idleClip == null)
            {
                idleClip = firstClip;
            }

            if (walkClip == null)
            {
                walkClip = runClip;
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
