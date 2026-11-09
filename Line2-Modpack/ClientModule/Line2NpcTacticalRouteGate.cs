using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Serializes tactical candidate preflights through a DEDICATED native
    /// Steam ASPFP Seeker. Does not move the NPC or create a navigation mesh.
    /// Never steals the movement follower's pending A* request.
    /// </summary>
    internal sealed class Line2NpcTacticalRouteGate : MonoBehaviour
    {
        private Line2NativeAStarRouteProvider _provider;
        private readonly List<Vector3> _route = new List<Vector3>(128);
        private Vector3 _candidate;
        private Vector3 _start;
        private bool _active;
        private float _began;
        private float _retryAfter;
        private Vector3 _lastSuccess;
        private float _successUntil;
        private Vector3 _lastFailure;
        private float _failureUntil;
        private float _nextLog;
        internal string Status { get; private set; }

        internal void Initialize(Line2NativeAStarRouteProvider provider)
        {
            _provider = provider;
            Status = "tactical-native-Astar-waiting";
        }

        internal bool IsVerified(Vector3 point)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || _provider == null ||
                !Level.checkSafeIncludingClipVolumes(point))
                return false;

            Vector3 current = transform.position;
            if (Vector3.Distance(current, point) < 1.7f &&
                Mathf.Abs(current.y - point.y) < 0.85f)
                return true;

            if (Time.time < _successUntil &&
                (point - _lastSuccess).sqrMagnitude < 0.5f &&
                (current - _start).sqrMagnitude < 4f)
                return true;

            if (Time.time < _failureUntil &&
                (point - _lastFailure).sqrMagnitude < 0.5f)
                return false;

            // One asynchronous request per actor. Do NOT cancel a pending
            // request every time the cover scorer tests its six candidates.
            if (_active)
            {
                if ((point - _candidate).sqrMagnitude > 0.5f)
                    return false;
                if ((current - _start).sqrMagnitude > 4f ||
                    Time.time - _began > 5f)
                {
                    Fail("stale-or-timeout");
                    return false;
                }
            }
            else
            {
                if (Time.time < _retryAfter || !_provider.IsAvailable)
                {
                    Status = _provider.IsAvailable
                        ? "candidate-backoff" : _provider.DiagnosticStatus;
                    return false;
                }
                _active = true;
                _candidate = point;
                _start = current;
                _began = Time.time;
                _route.Clear();
            }

            bool ready = _provider.TryGetRoute(_start, _candidate, _route);
            if (ready)
            {
                bool valid = _route.Count >= 2 && _route.Count <= 128 &&
                    Vector3.Distance(_route[0], _start) <= 1.5f &&
                    Vector3.Distance(_route[_route.Count - 1], point) <= 2f &&
                    Mathf.Abs(_route[_route.Count - 1].y - point.y) <= 0.85f;
                _route.Clear();
                _active = false;
                if (valid)
                {
                    _lastSuccess = point;
                    _successUntil = Time.time + 4f;
                    _retryAfter = 0f;
                    Status = "tactical-native-Astar-connected";
                    Log("NPC_TACTICAL_ROUTE_VERIFIED", point);
                    return true;
                }
                Fail("partial-or-wrong-floor");
                return false;
            }
            if (_provider.LastPathFailed)
                Fail(_provider.DiagnosticStatus);
            else
                Status = "tactical-native-Astar-pending";
            return false;
        }

        private void Fail(string reason)
        {
            _active = false;
            _route.Clear();
            _lastFailure = _candidate;
            _failureUntil = Time.time + 2.7f;
            _retryAfter = Time.time + 0.5f;
            Status = "tactical-route-rejected:" + reason;
            Log("NPC_TACTICAL_ROUTE_REJECTED", _candidate);
        }

        private void Log(string tag, Vector3 point)
        {
            if (Time.time < _nextLog) return;
            _nextLog = Time.time + 5f;
            ProbeLogger.Log(tag + ": actor=" + gameObject.name +
                " destination=" + point.ToString("F1") +
                " backend=" + (_provider != null
                    ? _provider.ObservedBackendName : "missing"));
        }

        private void OnDisable()
        {
            _active = false;
            _route.Clear();
            _successUntil = 0f;
            _failureUntil = 0f;
        }
    }
}
