using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Owns only the physical NPC CharacterController posture; never changes
    /// the decision brain, the anatomical bone hitboxes or vehicle seats.
    /// Native NPC animation is driven independently by PrototypeAnimation.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcStanceController : MonoBehaviour
    {
        private const float CrouchedHeight = 1.24f;
        private const float StandProbeMargin = 0.04f;
        private const float MinimumCommitment = 0.75f;
        private CharacterController _motor;
        private float _standingHeight;
        private Vector3 _standingCenter;
        private float _nextChangeAt;
        private float _nextBlockedLogAt;
        private bool _crouched;

        internal bool IsCrouched { get { return _crouched; } }

        internal void Initialize(CharacterController motor)
        {
            _motor = motor;
            if (_motor == null) return;
            _standingHeight = motor.height;
            _standingCenter = motor.center;
        }

        internal void Apply(bool desiredCrouch)
        {
            if (_motor == null || !_motor.enabled || !Provider.isServer ||
                Dedicator.IsDedicatedServer || !Level.isLoaded ||
                Time.time < _nextChangeAt || _crouched == desiredCrouch)
                return;

            if (!desiredCrouch && !CanStand())
            {
                if (Time.time >= _nextBlockedLogAt)
                {
                    ProbeLogger.Log("NPC_STANCE_STAND_BLOCKED: actor=" + name +
                        " (overhead collision, remaining crouched)");
                    _nextBlockedLogAt = Time.time + 5f;
                }
                return;
            }

            float height = desiredCrouch
                ? Mathf.Min(_standingHeight, CrouchedHeight) : _standingHeight;
            // Keep capsule feet at the same world position when changing height.
            float bottom = _standingCenter.y - _standingHeight * 0.5f;
            Vector3 center = _standingCenter;
            center.y = bottom + height * 0.5f;
            _motor.height = height;
            _motor.center = center;
            _crouched = desiredCrouch;
            _nextChangeAt = Time.time + MinimumCommitment;
            ProbeLogger.Log("NPC_STANCE_CHANGED: actor=" + name +
                " crouched=" + _crouched + " height=" + height.ToString("F2"));
        }

        private bool CanStand()
        {
            // Do not stand through ceilings. Native BLOCK_COLLISION covers
            // the physical world without including the actor's own
            // anatomical Entity-layer trigger hitboxes.
            float radius = Mathf.Max(0.05f, _motor.radius - 0.025f);
            Vector3 center = transform.TransformPoint(_standingCenter);
            float halfSegment = Mathf.Max(0f, _standingHeight * 0.5f - radius);
            Vector3 bottom = center - Vector3.up * halfSegment;
            Vector3 top = center + Vector3.up * halfSegment;
            return !Physics.CheckCapsule(bottom, top, radius,
                RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
        }
    }
}
