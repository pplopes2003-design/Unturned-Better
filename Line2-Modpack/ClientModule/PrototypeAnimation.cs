using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Uses only animation clips actually present on the native NPC_Client
    /// visual. Never invents animation names, never drives gameplay or damage.
    /// </summary>
    internal sealed class PrototypeAnimation : MonoBehaviour
    {
        private Animation _legacy;
        private string _idle;
        private string _walk;
        private string _run;
        private string _sneak;
        private string _crouchIdle;
        private string _medical;
        private string _melee;
        private float _medicalUntil;
        private bool _medicalClipMissingLogged;
        private float _meleeUntil;
        private string _current;

        internal void Initialize(GameObject visual)
        {
            if (visual == null) return;
            _legacy = visual.GetComponentInChildren<Animation>(true);
            if (_legacy == null)
            {
                ProbeLogger.Log("NPC_PROTOTYPE_ANIM: no Animation in native visual; retaining existing pose.");
                return;
            }
            foreach (AnimationState clip in _legacy)
            {
                if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                string name = clip.name;
                if (_idle == null && name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                    _idle = name;
                if (_walk == null &&
                    (name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("move", StringComparison.OrdinalIgnoreCase) >= 0))
                    _walk = name;
                if (_run == null &&
                    (name.IndexOf("sprint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0))
                    _run = name;
                if (_sneak == null &&
                    (name.IndexOf("crouch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("sneak", StringComparison.OrdinalIgnoreCase) >= 0))
                    _sneak = name;
                if (_crouchIdle == null &&
                    (name.IndexOf("Idle_Crouch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("Crouch_Idle", StringComparison.OrdinalIgnoreCase) >= 0))
                    _crouchIdle = name;
                if (_medical == null &&
                    (name.IndexOf("Bandage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("Medical", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("Heal", StringComparison.OrdinalIgnoreCase) >= 0))
                    _medical = name;
                if (_melee == null &&
                    (name.IndexOf("punch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("melee", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0))
                    _melee = name;
            }
            _legacy.enabled = true;
            SetGait(Line2MobileNPCLocomotionPolicy.Gait.Hold, false);
            ProbeLogger.Log("NPC_LOCOMOTION_CLIPS: run=" + (_run ?? "absent") +
                " sneak=" + (_sneak ?? "absent") + " walk=" + (_walk ?? "absent"));

        }

        internal void PlayMeleeSwing()
        {
            if (_legacy == null || !_legacy.enabled || _melee == null) return;
            AnimationState attack = _legacy[_melee];
            if (attack == null) return;
            attack.wrapMode = WrapMode.Once;
            _legacy.CrossFade(_melee, 0.07f);
            _current = _melee;
            _meleeUntil = Time.time + Mathf.Clamp(attack.length, 0.15f, 1.2f);
        }

        internal void PlayMedicalTreatment(float duration)
        {
            if (_legacy == null || !_legacy.enabled) return;
            if (_medical == null)
            {
                if (!_medicalClipMissingLogged)
                {
                    ProbeLogger.Log("NPC_MEDICAL_ANIMATION_UNAVAILABLE: native visual has no medical clip");
                    _medicalClipMissingLogged = true;
                }
                return;
            }
            AnimationState action = _legacy[_medical];
            if (action == null) return;
            action.wrapMode = WrapMode.Once;
            _legacy.CrossFade(_medical, 0.12f);
            _current = _medical;
            _medicalUntil = Time.time + Mathf.Min(Mathf.Max(duration, 0.1f),
                Mathf.Max(0.2f, action.length));
        }

        internal void SetMoving(bool moving)
        {
            SetGait(Line2MobileNPCLocomotionPolicy.Gait.Walk, moving, false);
        }

        internal void SetGait(Line2MobileNPCLocomotionPolicy.Gait gait, bool moving)
        {
            SetGait(gait, moving, gait == Line2MobileNPCLocomotionPolicy.Gait.Sneak);
        }

        internal void SetGait(Line2MobileNPCLocomotionPolicy.Gait gait,
            bool moving, bool physicallyCrouched)
        {
            if (_legacy == null || !_legacy.enabled) return;
            if (Time.time < _meleeUntil || Time.time < _medicalUntil) return;
            string requested;
            if (physicallyCrouched)
                requested = moving ? (_sneak ?? _walk ?? _idle)
                    : (_crouchIdle ?? _idle ?? _walk);
            else if (!moving)
                requested = _idle ?? _walk;
            else if (gait == Line2MobileNPCLocomotionPolicy.Gait.Sprint)
                requested = _run ?? _walk ?? _idle;
            else
                requested = _walk ?? _idle;
            if (requested == null || requested == _current) return;
            AnimationState state = _legacy[requested];
            if (state == null) return;
            state.wrapMode = WrapMode.Loop;
            _legacy.CrossFade(requested, 0.12f);
            _current = requested;
        }

    }
}
