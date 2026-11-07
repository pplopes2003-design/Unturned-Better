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
            }
            _legacy.enabled = true;
            SetMoving(false);
        }

        internal void SetMoving(bool moving)
        {
            if (_legacy == null || !_legacy.enabled) return;
            string requested = moving ? (_walk ?? _idle) : (_idle ?? _walk);
            if (requested == null || requested == _current) return;
            AnimationState state = _legacy[requested];
            if (state == null) return;
            state.wrapMode = WrapMode.Loop;
            _legacy.CrossFade(requested, 0.12f);
            _current = requested;
        }
    }
}
