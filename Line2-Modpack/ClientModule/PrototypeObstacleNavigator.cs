using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Cheap local collision steering for prototype humanoids. Queries only
    /// 3-5 short directions every 0.35s, no navmesh rebuild, no global scan.
    /// This is NOT an indoor pathfinder or a door opening system.
    /// </summary>
    internal sealed class PrototypeObstacleNavigator : MonoBehaviour
    {
        private const float ProbeInterval = 0.35f;
        private const float ForwardRange = 1.75f;
        private const float BodyRadius = 0.28f;
        private const int HitLimit = 8;
        private readonly RaycastHit[] _hits = new RaycastHit[HitLimit];

        private Vector3 _heading;
        private Vector3 _lastGoal;
        private float _nextProbeAt;
        private float _detourUntil;
        private float _stuckAt;
        private Vector3 _lastProgressPosition;
        private int _preferredSide = 1;
        private int _blockedChecks;

        internal void Initialize()
        {
            _lastProgressPosition = transform.position;
            _stuckAt = Time.time + 3f;
            _nextProbeAt = Time.time;
        }

        internal Vector3 GetHeading(Vector3 desired, Vector3 goal)
        {
            desired.y = 0;
            if (desired.sqrMagnitude <= 0.001f)
            {
                _heading = Vector3.zero;
                return _heading;
            }
            desired.Normalize();
            if (Time.time < _nextProbeAt &&
                (_lastGoal - goal).sqrMagnitude < 2.25f)
                return _heading;

            _nextProbeAt = Time.time + ProbeInterval;
            _lastGoal = goal;

            if ((transform.position - _lastProgressPosition).sqrMagnitude > 1.0f)
            {
                _lastProgressPosition = transform.position;
                _stuckAt = Time.time + 3f;
                _blockedChecks = 0;
            }
            else if (Time.time >= _stuckAt)
            {
                // Break out of a corner without switching sides each frame.
                _preferredSide = -_preferredSide;
                _stuckAt = Time.time + 3f;
            }

            // Commit to the detour briefly, otherwise a direct-to-goal
            // probe repeatedly destroys the turn before reaching the corner.
            if (Time.time < _detourUntil && IsOpen(_heading))
                return _heading;

            if (IsOpen(desired))
            {
                _heading = desired;
                _blockedChecks = 0;
                return _heading;
            }

            float[] angles = { 45f, 80f };
            for (int i = 0; i < angles.Length; i++)
            {
                Vector3 right = Quaternion.Euler(0, angles[i] * _preferredSide, 0) * desired;
                if (IsOpen(right))
                {
                    _heading = right;
                    _detourUntil = Time.time + 1.4f;
                    _blockedChecks = 0;
                    return _heading;
                }
                Vector3 left = Quaternion.Euler(0, -angles[i] * _preferredSide, 0) * desired;
                if (IsOpen(left))
                {
                    _heading = left;
                    _preferredSide = -_preferredSide;
                    _detourUntil = Time.time + 1.4f;
                    _blockedChecks = 0;
                    return _heading;
                }
            }

            _blockedChecks++;
            _heading = Vector3.zero;
            if (_blockedChecks >= 5)
            {
                _blockedChecks = 0;
                _preferredSide = -_preferredSide;
                _detourUntil = 0f;
            }
            return _heading;
        }

        private bool IsOpen(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.2f) return false;
            Vector3 origin = transform.position + Vector3.up * 1.0f;
            int found = Physics.SphereCastNonAlloc(origin, BodyRadius, direction,
                _hits, ForwardRange, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = found == HitLimit; // avoid treating truncated buffer as clear
            for (int i = 0; i < found; i++)
            {
                Collider collider = _hits[i].collider;
                _hits[i] = default(RaycastHit);
                if (collider == null || !collider.enabled) continue;
                Transform hit = collider.transform;
                if (hit == transform || hit.IsChildOf(transform)) continue;
                if (_hits[i].distance < 0.01f) continue;
                blocked = true;
            }
            return !blocked;
        }
    }
}
