using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Drives prototype movement using paths provided by the native Steam
    /// ASPFP Seeker (via Line2NativeAStarRouteProvider).
    /// CharacterController remains the ONLY owner of physical movement.
    /// When a path is pending, stop rather than charge into closed doors.
    /// Uses low-cost local steering only if ASPFP is actually unavailable
    /// or has explicitly failed for the current request.
    /// </summary>
    internal sealed class PrototypeAStarFollower : MonoBehaviour
    {
        private Line2NativeAStarRouteProvider _provider;
        private PrototypeObstacleNavigator _fallback;
        private readonly List<Vector3> _path = new List<Vector3>(96);
        private Vector3 _pathGoal;
        private int _waypoint;
        private float _nextRepathAt;
        private float _routeReceivedAt;
        private float _failedUntil;
        private float _nextDiagnosticsAt;
        private string _status = "uninitialized";

        internal string Status { get { return _status; } }
        internal bool UsingNativePath { get { return _path.Count > 0; } }

        internal void Initialize(Line2NativeAStarRouteProvider provider,
                                 PrototypeObstacleNavigator fallback)
        {
            _provider = provider;
            _fallback = fallback;
            _path.Clear();
            _waypoint = 0;
            _status = provider == null ? "missing-native-provider" : "waiting-for-native-Astar";
            _nextRepathAt = Time.time;
        }

        internal Vector3 GetHeading(Vector3 goal)
        {
            Vector3 current = transform.position;
            Vector3 toward = goal - current;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.8f * 0.8f)
                return Vector3.zero;

            if (_provider == null || !_provider.IsAvailable)
            {
                _status = _provider == null ? "provider-missing"
                    : _provider.DiagnosticStatus;
                return _fallback != null ? _fallback.GetHeading(toward, goal)
                    : Vector3.zero;
            }

            // The path contains the world-space start and end, so consume
            // waypoint 1 first. Do not re-request on every animated frame.
            bool stale = (goal - _pathGoal).sqrMagnitude > 2.5f * 2.5f
                || Mathf.Abs(goal.y - _pathGoal.y) > 0.8f
                || Time.time - _routeReceivedAt > 5.0f;
            if (stale)
            {
                _path.Clear();
                _waypoint = 0;
            }

            if (_path.Count == 0)
            {
                if (Time.time < _failedUntil)
                    return _fallback != null ? _fallback.GetHeading(toward, goal)
                        : Vector3.zero;

                if (Time.time < _nextRepathAt)
                    return Vector3.zero;

                bool ready = _provider.TryGetRoute(current, goal, _path);
                if (!ready)
                {
                    _status = _provider.DiagnosticStatus;
                    if (_provider.LastPathFailed)
                    {
                        _failedUntil = Time.time + 2.0f;
                        _nextRepathAt = Time.time + 2.0f;
                        return _fallback != null
                            ? _fallback.GetHeading(toward, goal) : Vector3.zero;
                    }
                    // Wait for the actual A* route; never treat a pending
                    // path as proof the door or wall is traversable.
                    return Vector3.zero;
                }
                if (_path.Count < 2 || _path.Count > 128)
                {
                    _path.Clear();
                    _status = "invalid-native-route";
                    _nextRepathAt = Time.time + 2f;
                    return Vector3.zero;
                }
                _pathGoal = goal;
                _waypoint = 1;
                _routeReceivedAt = Time.time;
                _nextRepathAt = Time.time + 1.0f;
                _status = "native-Astar-route-active";
                LogOnce();
            }

            // Native waypoints may have vertical offsets from graph snapping,
            // but the humanoid CharacterController handles gravity itself.
            while (_waypoint < _path.Count)
            {
                Vector3 p = _path[_waypoint];
                Vector3 delta = p - current;
                if (Mathf.Abs(delta.y) < 1.0f &&
                    new Vector2(delta.x, delta.z).sqrMagnitude < 0.9f * 0.9f)
                {
                    ++_waypoint;
                    continue;
                }
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.1f)
                    return Vector3.zero;
                return delta.normalized;
            }

            _path.Clear();
            _status = "native-route-arrived";
            return Vector3.zero;
        }

        private void LogOnce()
        {
            if (Time.time < _nextDiagnosticsAt) return;
            ProbeLogger.Log("NPC_NATIVE_ASTAR_ROUTE_OK: waypoints=" + _path.Count +
                " provider=" + (_provider != null ? _provider.ObservedBackendName : "none"));
            _nextDiagnosticsAt = Time.time + 15f;
        }

        private void OnDisable()
        {
            _path.Clear();
        }
    }
}
