using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Deterministic physical-progress gate for normal A-Life destinations:
    /// authored site, group guard, patrol and observed world loot.
    /// An unreachable doorway must cause a NEW task selection rather than
    /// indefinite steering/side-to-side bouncing. A real A* detour is given
    /// time to head away from the destination before being declared stuck.
    ///
    /// The only progress evidence is the CharacterController's observed
    /// world position. No synthetic path or teleport is emitted here.
    /// </summary>
    internal sealed class Line2NpcRoutineRouteHealth
    {
        internal enum State { Moving, Reached, Blocked }
        private const float PhysicalImprovementMeters = 0.9f;
        private const float InvalidRouteGrace = 4f;
        private const float NoProgressGrace = 24f;
        private const float WaitingAStarGrace = 34f;
        private const float MaxGoalSeconds = 140f;
        private Vector3 _goal;
        private float _firstAt;
        private float _lastImprovementAt;
        private float _bestFlatDistance;
        // A real detour around a building can initially increase distance
        // to the goal. Track measured travel as well as goal improvement.
        private readonly Line2NpcPhysicalProgressLedger _physicalProgress =
            new Line2NpcPhysicalProgressLedger();
        private float _invalidSince;
        private bool _initialized;

        internal void Reset()
        {
            _initialized = false;
            _invalidSince = -1f;
        }

        private static bool Finite(Vector3 p)
        {
            return !float.IsNaN(p.x) && !float.IsNaN(p.y) &&
                !float.IsNaN(p.z) && !float.IsInfinity(p.x) &&
                !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
        }

        internal State Observe(Vector3 goal, Vector3 actual,
            float now, bool noConnectedPath, bool waitingForNativePath)
        {
            if (!Finite(goal) || !Finite(actual) ||
                float.IsNaN(now) || float.IsInfinity(now) || now < 0f)
            {
                Reset();
                return State.Blocked;
            }
            Vector3 d = goal - actual;
            float flat = (float)Math.Sqrt(d.x * d.x + d.z * d.z);
            if (flat <= 1.8f && Math.Abs(d.y) <= 1.25f)
            {
                Reset();
                return State.Reached;
            }
            if (!_initialized || (_goal - goal).sqrMagnitude > 1.0f)
            {
                _initialized = true;
                _goal = goal;
                _firstAt = now;
                _lastImprovementAt = now;
                _physicalProgress.Reset(actual);
                _bestFlatDistance = flat;
                _invalidSince = noConnectedPath ? now : -1f;
                return State.Moving;
            }
            if (_bestFlatDistance - flat >= PhysicalImprovementMeters)
            {
                _bestFlatDistance = flat;
                _physicalProgress.TryObserveNewPosition(actual,
                    PhysicalImprovementMeters);
                _lastImprovementAt = now;
            }
            else if (_physicalProgress.TryObserveNewPosition(
                actual, PhysicalImprovementMeters))
            {
                // A genuine new corridor location counts as movement;
                // returning between the same two blocked-wall positions
                // does not renew progress. The absolute 140s limit remains.
                _lastImprovementAt = now;
            }
            if (noConnectedPath)
            {
                if (_invalidSince < 0f) _invalidSince = now;
                if (now - _invalidSince >= InvalidRouteGrace)
                {
                    Reset();
                    return State.Blocked;
                }
            }
            else _invalidSince = -1f;

            float grace = waitingForNativePath
                ? WaitingAStarGrace : NoProgressGrace;
            if (now - _lastImprovementAt >= grace ||
                now - _firstAt >= MaxGoalSeconds)
            {
                Reset();
                return State.Blocked;
            }
            return State.Moving;
        }
    }
}
