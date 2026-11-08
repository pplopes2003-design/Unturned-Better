using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Physical sensor for the exact Line 1 incremental horizon planner.
    /// Used ONLY when Steam ASPFP is absent or reports a failed route.
    /// All accepted cells have real footing, capsule clearance and connected
    /// walking edges. Closed locked windows, walls, and floor changes are not
    /// interpreted as traversable. Door traversal requires the native
    /// unlocked-door adapter to validate the precise obstructing collider.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2CoarseRouteBridge : MonoBehaviour
    {
        private const float Radius = 0.32f;
        private const float MaxStepRise = 0.85f;
        private const float WaypointDistance = 0.85f;
        private const float RecheckSeconds = 0.55f;
        private const int MaxOverlap = 32;
        private const int MaxCastHits = 16;
        private readonly Collider[] _overlaps = new Collider[MaxOverlap];
        private readonly RaycastHit[] _castHits = new RaycastHit[MaxCastHits];
        private readonly Line2MobileNPCGlobalRoutePlanner _planner =
            new Line2MobileNPCGlobalRoutePlanner();
        private readonly List<Vector3> _route = new List<Vector3>(160);
        private Line2MobileNPCDoorNavigation _doors;
        private CharacterController _motor;
        private Vector3 _routeGoal;
        private int _waypoint;
        private float _nextProgressCheck;
        private Vector3 _lastProgressPosition;
        private float _nextLogAt;
        private int _failCount;

        internal bool IsPlanning { get { return _planner.IsSearching; } }
        internal string Status { get { return _planner.LastOutcome; } }

        internal void Initialize()
        {
            _motor = GetComponent<CharacterController>();
            _doors = GetComponent<Line2MobileNPCDoorNavigation>();
            _route.Clear();
            _planner.Reset();
            _lastProgressPosition = transform.position;
        }

        private void OnDisable()
        {
            _route.Clear();
            _planner.Reset();
            Array.Clear(_overlaps, 0, _overlaps.Length);
            Array.Clear(_castHits, 0, _castHits.Length);
        }

        private static bool Finite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                     float.IsInfinity(v.x) || float.IsInfinity(v.y) ||
                     float.IsInfinity(v.z));
        }

        /// <summary>Return a physically validated heading when ready. A pending
        /// graph search deliberately returns zero instead of steering into walls.</summary>
        internal Vector3 GetHeading(Vector3 destination, out bool waiting)
        {
            waiting = false;
            if (!Provider.isServer || Dedicator.IsDedicatedServer || !Level.isLoaded ||
                _motor == null || !_motor.enabled || !Finite(destination) ||
                !Level.checkSafeIncludingClipVolumes(destination))
                return Vector3.zero;

            Vector3 here = transform.position;
            if (_route.Count > 0 &&
                ((destination - _routeGoal).sqrMagnitude > 4f ||
                 Mathf.Abs(destination.y - _routeGoal.y) > 0.6f))
                ClearRoute();

            while (_waypoint < _route.Count)
            {
                Vector3 waypoint = _route[_waypoint];
                Vector3 diff = waypoint - here;
                if (Mathf.Abs(diff.y) > 1.15f)
                {
                    ClearRoute();
                    break;
                }
                diff.y = 0f;
                if (diff.sqrMagnitude < WaypointDistance * WaypointDistance)
                {
                    _waypoint++;
                    continue;
                }
                if (Time.time >= _nextProgressCheck)
                {
                    _nextProgressCheck = Time.time + RecheckSeconds;
                    if ((here - _lastProgressPosition).sqrMagnitude < 0.04f)
                    {
                        if (++_failCount > 6)
                        {
                            ClearRoute();
                            waiting = true;
                            return Vector3.zero;
                        }
                    }
                    else _failCount = 0;
                    _lastProgressPosition = here;
                    // Moving world props may invalidate an old corridor.
                    // Never use a route with an unverified next step.
                    Vector3 near = here + diff.normalized *
                        Mathf.Min(1.4f, diff.magnitude);
                    if (!ValidateEdge(here, near))
                    {
                        ClearRoute();
                        waiting = true;
                        return Vector3.zero;
                    }
                }
                return diff.normalized;
            }
            if (_route.Count > 0) ClearRoute();

            if (_planner.Step(here, destination, Time.time,
                Time.frameCount, GetInstanceID(), ResolveFooting,
                ValidateEdge, _route) && _route.Count > 0)
            {
                _routeGoal = destination;
                _waypoint = 0;
                _lastProgressPosition = here;
                _nextProgressCheck = Time.time + RecheckSeconds;
                _failCount = 0;
                if (Time.time >= _nextLogAt)
                {
                    _nextLogAt = Time.time + 14f;
                    ProbeLogger.Log("NPC_COARSE_ROUTE: waypoints=" + _route.Count +
                        " outcome=" + _planner.LastOutcome +
                        " expanded=" + _planner.LastExpanded +
                        " actor=" + gameObject.name);
                }
                waiting = true; // Start walking on following Update.
                return Vector3.zero;
            }
            waiting = _planner.IsSearching;
            return Vector3.zero;
        }

        private void ClearRoute()
        {
            _route.Clear();
            _waypoint = 0;
            _planner.Reset();
            _failCount = 0;
        }

        private bool ResolveFooting(Vector3 estimate, out Vector3 foot)
        {
            foot = Vector3.zero;
            if (!Finite(estimate) || !Level.checkSafeIncludingClipVolumes(estimate))
                return false;
            RaycastHit ground;
            if (!Physics.Raycast(estimate + Vector3.up * 2.1f, Vector3.down,
                out ground, 4.8f, Line2NpcFloorGeometry.SupportMask, QueryTriggerInteraction.Ignore) ||
                ground.normal.y < 0.70f || !Finite(ground.point))
                return false;
            // A same-surface coarse planner must not jump to another storey
            // or silently cross stacked indoor/underground floors.
            if (Mathf.Abs(ground.point.y - transform.position.y) > 2.2f)
                return false;
            foot = ground.point + Vector3.up * 0.07f;
            return Level.checkSafeIncludingClipVolumes(foot);
        }

        private bool ClearCapsule(Vector3 position)
        {
            Vector3 bottom = position + Vector3.up * 0.48f;
            Vector3 top = position + Vector3.up * 1.58f;
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, Radius,
                _overlaps, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (count >= MaxOverlap) return false;
            bool blocked = false;
            for (int i = 0; i < count; ++i)
            {
                Collider col = _overlaps[i];
                _overlaps[i] = null;
                if (col == null || !col.enabled || col.transform == transform ||
                    col.transform.IsChildOf(transform)) continue;
                blocked = true;
            }
            return !blocked || _doors != null &&
                _doors.IsOnlyUnlockedDoorOccupyingNodeServer(
                    bottom, top, Radius, RayMasks.BLOCK_COLLISION);
        }

        private bool ClearEdgeCast(Vector3 a, Vector3 b)
        {
            Vector3 flat = b - a;
            flat.y = 0f;
            float length = flat.magnitude;
            if (length < 0.05f) return true;
            Vector3 origin = a + Vector3.up;
            int count = Physics.SphereCastNonAlloc(origin, 0.24f,
                flat / length, _castHits, length,
                RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
            if (count >= MaxCastHits) return false;
            bool blocked = false;
            for (int i = 0; i < count; ++i)
            {
                Collider col = _castHits[i].collider;
                _castHits[i] = default(RaycastHit);
                if (col == null || !col.enabled || col.transform == transform ||
                    col.transform.IsChildOf(transform)) continue;
                blocked = true;
            }
            if (!blocked) return true;
            // Only exactly identified original map/barricade doors may be
            // considered passable once physically opened by the motor.
            return _doors != null && length <= 2.6f &&
                _doors.IsTraversableUnlockedDoorEdgeServer(
                    origin, flat / length, 0.24f,
                    length, RayMasks.BLOCK_COLLISION);
        }

        private bool ValidateEdge(Vector3 a, Vector3 b)
        {
            if (!Finite(a) || !Finite(b) || !Level.isLoaded ||
                Mathf.Abs(a.y - b.y) > MaxStepRise) return false;
            Vector3 delta = b - a;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            if (horizontal < 0.04f || horizontal > 3.8f) return false;
            // Floors and walls are sampled through the whole segment;
            // checking only endpoints would cut across window sills.
            int samples = Mathf.CeilToInt(horizontal / 0.7f);
            Vector3 previous = a;
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 expected = Vector3.Lerp(a, b, t);
                RaycastHit ground;
                if (!Physics.Raycast(expected + Vector3.up * 1.35f,
                    Vector3.down, out ground, 2.75f, Line2NpcFloorGeometry.SupportMask,
                    QueryTriggerInteraction.Ignore) ||
                    ground.normal.y < 0.70f ||
                    Mathf.Abs(ground.point.y - expected.y) > 0.57f)
                    return false;
                Vector3 step = ground.point + Vector3.up * 0.07f;
                if (!ClearCapsule(step) || !ClearEdgeCast(previous, step))
                    return false;
                previous = step;
            }
            return true;
        }
    }
}
