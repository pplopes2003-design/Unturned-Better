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
        private Line2CoarseRouteBridge _coarseFallback;
        private Line2MobileNPCTravelMemory _travelMemory;
        // Stores positions observed AFTER actual CharacterController motion,
        // never the proposed A* waypoints.
        private readonly List<Vector3> _walked = new List<Vector3>(
            Line2MobileNPCTravelMemory.MaxFootsteps);
        private Vector3 _journeyStart;
        private bool _journeyInvalid;
        private readonly List<Vector3> _path = new List<Vector3>(96);
        private Vector3 _pathGoal;
        private Vector3 _requestedRouteGoal;
        private bool _requestedRoutePending;
        private int _waypoint;
        private float _nextRepathAt;
        private float _routeReceivedAt;
        private Vector3 _lastNativeProgressPosition;
        private float _lastNativeProgressAt;
        private float _nextBlockedLog;
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
            _requestedRoutePending = false;
            _waypoint = 0;
            _status = provider == null ? "missing-native-provider" : "waiting-for-native-Astar";
            _nextRepathAt = Time.time;
        }

        internal void AttachTravelMemory(Line2MobileNPCTravelMemory travelMemory)
        {
            _travelMemory = travelMemory;
        }

        internal void AttachCoarseFallback(Line2CoarseRouteBridge fallback)
        {
            _coarseFallback = fallback;
        }

        private Vector3 UseCoarseOrSteer(Vector3 toward, Vector3 goal)
        {
            if (_coarseFallback != null)
            {
                bool waiting;
                Vector3 heading = _coarseFallback.GetHeading(goal, out waiting);
                if (heading.sqrMagnitude > 0.01f)
                {
                    _status = "line1-physical-coarse-route";
                    return heading;
                }
                if (waiting)
                {
                    _status = "line1-coarse-route-searching";
                    return Vector3.zero;
                }
                // The original Line1 planner physically enumerated all
                // nearby accessible ground and found NO connected corridor.
                // Never substitute blind forward steering at that point:
                // that is exactly how NPCs repeatedly pressed against the
                // same house wall rather than choosing a new destination.
                if (_coarseFallback.Status == "no-connected-route")
                {
                    _status = "line1-no-connected-route";
                    return Vector3.zero;
                }
            }
            _status = "short-range-collision-steering";
            return _fallback != null
                ? _fallback.GetHeading(toward, goal) : Vector3.zero;
        }

        private void StartJourney(Vector3 current)
        {
            _walked.Clear();
            _walked.Add(current);
            _journeyStart = current;
            _journeyInvalid = false;
        }

        private void ObserveActualMovement(Vector3 current)
        {
            if (_journeyInvalid || _walked.Count == 0)
                return;
            float distance = Vector3.Distance(current, _walked[_walked.Count - 1]);
            if (distance < 0.95f)
                return;
            // No synthetic interpolation across a teleport or giant frame.
            if (distance > 2.6f ||
                _walked.Count >= Line2MobileNPCTravelMemory.MaxFootsteps)
            {
                _journeyInvalid = true;
                return;
            }
            _walked.Add(current);
        }

        private void FinishJourney(Vector3 current)
        {
            ObserveActualMovement(current);
            if (!_journeyInvalid && _travelMemory != null &&
                _walked.Count >= 3 &&
                Vector3.Distance(current, _pathGoal) < 1.7f &&
                Mathf.Abs(current.y - _pathGoal.y) <= 0.9f &&
                _travelMemory.RememberCompletedJourneyServer(
                    _journeyStart, _pathGoal, _walked))
            {
                ProbeLogger.Log("NPC_ROUTE_LEARNED: actor=" + gameObject.name +
                                " physicalFootsteps=" + _walked.Count);
            }
            _walked.Clear();
        }

        internal Vector3 GetHeading(Vector3 goal)
        {
            Vector3 current = transform.position;
            Vector3 toward = goal - current;
            toward.y = 0f;
            // A native path can end at a snapped graph node rather than
            // the precise brain goal. Only real CharacterController
            // proximity on the correct floor can mark arrival.
            if (Line2NpcNativeArrivalPolicy.HasPhysicallyArrived(
                    current, goal))
            {
                if (_path.Count > 0) FinishJourney(current);
                _path.Clear();
                _waypoint = 0;
                _status = "native-physical-goal-arrived";
                return Vector3.zero;
            }

            if (_provider == null || !_provider.IsAvailable)
            {
                _walked.Clear();
                _status = _provider == null ? "provider-missing"
                    : _provider.DiagnosticStatus;
                return UseCoarseOrSteer(toward, goal);
            }

            // The path contains the world-space start and end, so consume
            // waypoint 1 first. Do not re-request on every animated frame.
            // Doorways and building corners may be less than 2.5m apart.
            // Do not keep following a route to the other side of a wall
            // when the brain's observed destination changes materially.
            bool stale = (goal - _pathGoal).sqrMagnitude > 3.0f * 3.0f
                || Mathf.Abs(goal.y - _pathGoal.y) > 0.45f
                // Native paths around houses can take well over five
                // seconds. The former hard 5s timer destroyed a still-valid
                // route at precisely the moment the NPC reached the corner,
                // causing repeated path requests and apparent paralysis.
                // Real movement progress and the tactical watchdog own
                // earlier invalidation of blocked paths.
                || Time.time - _routeReceivedAt > 25.0f;
            if (stale)
            {
                _walked.Clear();
                _path.Clear();
                _waypoint = 0;
            }

            if (_path.Count == 0)
            {
                if (Time.time < _failedUntil)
                    return UseCoarseOrSteer(toward, goal);

                if (Time.time < _nextRepathAt)
                    return Vector3.zero;

                // Commit the pending destination while native Seeker works.
                // A moving player can update the high-level goal every
                // 0.45s. Passing that micro-change into StartPath cancels
                // the ongoing request repeatedly, freezing the NPC.
                // Large changes (>3m) still allow deliberate replanning.
                if (_requestedRoutePending &&
                    (goal - _requestedRouteGoal).sqrMagnitude > 9f)
                    _requestedRoutePending = false;
                Vector3 requestGoal = _requestedRoutePending
                    ? _requestedRouteGoal : goal;
                bool ready = _provider.TryGetRoute(current, requestGoal, _path);
                if (!ready)
                {
                    _status = _provider.DiagnosticStatus;
                    if (_provider.LastPathFailed)
                    {
                        _requestedRoutePending = false;
                        _failedUntil = Time.time + 2.0f;
                        _nextRepathAt = Time.time + 2.0f;
                        return UseCoarseOrSteer(toward, goal);
                    }
                    if (!_requestedRoutePending)
                    {
                        _requestedRouteGoal = requestGoal;
                        _requestedRoutePending = true;
                    }
                    // A pending path never proves a traversable doorway.
                    return Vector3.zero;
                }
                _requestedRoutePending = false;
                if (_path.Count < 2 || _path.Count > 128)
                {
                    _path.Clear();
                    _status = "invalid-native-route";
                    _nextRepathAt = Time.time + 2f;
                    return Vector3.zero;
                }
                _pathGoal = requestGoal;
                StartJourney(current);
                _waypoint = 1;
                _routeReceivedAt = Time.time;
                _lastNativeProgressPosition = current;
                _lastNativeProgressAt = Time.time;
                _nextRepathAt = Time.time + 1.0f;
                _status = "native-Astar-route-active";
                LogOnce();
            }

            ObserveActualMovement(current);
            // A valid ASPFP route can become physically obstructed by
            // doors, vehicles or geometry changes. Never retain a route
            // forever when the CharacterController has not advanced.
            if ((current - _lastNativeProgressPosition).sqrMagnitude > 0.25f)
            {
                _lastNativeProgressPosition = current;
                _lastNativeProgressAt = Time.time;
            }
            else if (Time.time - _lastNativeProgressAt > 4.8f)
            {
                _path.Clear();
                _walked.Clear();
                _waypoint = 0;
                _nextRepathAt = Time.time + 1.2f;
                _status = "native-physical-stall-repath";
                if (Time.time >= _nextBlockedLog)
                {
                    _nextBlockedLog = Time.time + 10f;
                    ProbeLogger.Log("NPC_NATIVE_ASTAR_STALLED: actor=" +
                        gameObject.name + " actual=" + current.ToString("F1") +
                        " goal=" + goal.ToString("F1") +
                        " (repath requested; never teleport)");
                }
                return Vector3.zero;
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
                {
                    // We are horizontally aligned but on a different
                    // floor. Do not mistake this for a reached waypoint or
                    // freeze indefinitely on a zero horizontal heading.
                    // Discard the route and give the native planner another
                    // opportunity to find a connected staircase.
                    _path.Clear();
                    _walked.Clear();
                    _waypoint = 0;
                    _nextRepathAt = Time.time + 1.5f;
                    _status = "native-vertical-waypoint-unreachable";
                    return Vector3.zero;
                }
                // ASPFP owns the route. A short, bounded collision
                // correction may steer around a temporary nearby object,
                // but must not generate an alternative map-wide path.
                Vector3 nativeHeading = delta.normalized;
                if (_fallback != null)
                {
                    Vector3 adjusted = _fallback.GetHeading(
                        nativeHeading, p);
                    if (adjusted.sqrMagnitude > 0.01f &&
                        Vector3.Dot(adjusted, nativeHeading) > 0.05f)
                        return adjusted;
                    if (adjusted.sqrMagnitude <= 0.01f)
                    {
                        _status = "native-route-local-obstacle-blocked";
                        return Vector3.zero;
                    }
                }
                return nativeHeading;
            }

            // Consuming the ASPFP vectorPath proves only that the LAST
            // native graph node was reached. It can still be more than
            // a meter from the actual goal or on a different floor.
            // Reject such a partial route; a fresh request may find a
            // connected alternative, while the journey watchdog remains
            // responsible for a bounded failure after repeated attempts.
            if (!Line2NpcNativeArrivalPolicy.HasPhysicallyArrived(
                    current, goal))
            {
                _path.Clear();
                _walked.Clear();
                _waypoint = 0;
                _nextRepathAt = Time.time + 1.4f;
                _status = "native-endpoint-not-actual-arrival";
                return Vector3.zero;
            }
            FinishJourney(current);
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
            _walked.Clear();
            _path.Clear();
            _requestedRoutePending = false;
        }
    }
}
