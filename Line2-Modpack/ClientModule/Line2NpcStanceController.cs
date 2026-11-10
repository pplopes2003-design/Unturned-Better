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
            // Port the Line 1 motor's proven clearance approach: inspect
            // only the NEW head space. Testing the entire standing capsule
            // also overlaps floor contact and can trap NPCs in crouch.
            float radius = Mathf.Max(0.05f, _motor.radius * 0.9f);
            float currentTop = transform.position.y + _motor.center.y +
                _motor.height * 0.5f;
            float desiredTop = transform.position.y + _standingCenter.y +
                _standingHeight * 0.5f;
            Vector3 lowerHead = new Vector3(transform.position.x,
                Mathf.Max(currentTop + radius * 0.2f, desiredTop - radius),
                transform.position.z);
            Vector3 upperHead = new Vector3(transform.position.x,
                Mathf.Max(lowerHead.y, desiredTop - radius * 0.2f),
                transform.position.z);
            Collider[] hits = Physics.OverlapCapsule(
                lowerHead, upperHead, radius, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit != null && hit.transform != transform &&
                    !hit.transform.IsChildOf(transform))
                    return false;
            }
            return true;
        }

    }
}
