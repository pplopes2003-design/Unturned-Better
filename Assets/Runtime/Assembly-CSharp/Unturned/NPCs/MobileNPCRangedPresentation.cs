////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Reuses the selected vanilla gun's shoot/reload clips for mobile NPC combat.
    /// This is presentation-only: damage, ammo and shot timing remain server-authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCHumanoidRangedCombat))]
    public sealed class MobileNPCRangedPresentation : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float shotVolume = 0.65f;
        [SerializeField, Range(0f, 1f)] private float reloadVolume = 0.7f;
        [SerializeField, Min(1f)] private float minimumRolloffDistance = 1f;
        [SerializeField, Min(2f)] private float maximumRolloffDistance = 64f;

        private MobileNPCHumanoidRangedCombat combat;

        private void Awake()
        {
            combat = GetComponent<MobileNPCHumanoidRangedCombat>();
        }

        private void OnEnable()
        {
            if (combat == null)
            {
                combat = GetComponent<MobileNPCHumanoidRangedCombat>();
            }

            if (combat == null) return;
            combat.ShotFired += HandleShotFired;
            combat.ReloadStarted += HandleReloadStarted;
        }

        private void OnDisable()
        {
            if (combat == null) return;
            combat.ShotFired -= HandleShotFired;
            combat.ReloadStarted -= HandleReloadStarted;
        }

        private void HandleShotFired()
        {
#if !DEDICATED_SERVER
            ItemGunAsset gun = combat != null ? combat.SelectedGunAsset : null;
            PlayClip(gun != null ? gun.shoot : null, shotVolume);
#endif
        }

        private void HandleReloadStarted()
        {
#if !DEDICATED_SERVER
            ItemGunAsset gun = combat != null ? combat.SelectedGunAsset : null;
            PlayClip(gun != null ? gun.reload : null, reloadVolume);
#endif
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (clip == null || !Provider.isServer)
            {
                return;
            }

            OneShotAudioParameters parameters = new OneShotAudioParameters(transform, clip);
            parameters.volume = Mathf.Clamp01(volume);
            parameters.SetLinearRolloff(Mathf.Max(0.1f, minimumRolloffDistance),
                Mathf.Max(minimumRolloffDistance + 0.1f, maximumRolloffDistance));
            parameters.Play();
        }
    }
}
