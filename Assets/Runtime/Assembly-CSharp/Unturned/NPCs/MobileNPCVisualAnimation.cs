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

        private MobileNPCHumanoidMotor motor;
        private MobileNPCBrainDriver brain;
        private Animation legacyAnimation;
        private string idleClip;
        private string walkClip;
        private string runClip;
        private string currentClip;

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
            }

            PlayIfAvailable(desired);
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
