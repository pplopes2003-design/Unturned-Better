using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Pure, deterministic port of the Line 1 BrainDriver's retreat/flank
    /// progress monitor. It uses only OBSERVED actual CharacterController
    /// positions, never the intended movement vector or A* waypoint.
    ///
    /// A pending native search has a finite grace window. A committed route
    /// cannot be renewed indefinitely by changing its expiry every tick.
    /// Returning Blocked means retry at a different verified location, not
    /// permission to pass through scenery.
    /// </summary>
    internal sealed class Line2NpcTacticalJourneyWatchdog
    {
        internal enum Outcome { None, Moving, Reached, Holding, Blocked }

        private const float ReachedDistance = 1.50f;
        private const float MinimumProgress = 0.70f;
        private const float StuckSeconds = 5.0f;
        private const float PendingGraceSeconds = 8.0f;
        private const float AbsoluteTimeoutSeconds = 45.0f;
        private const float VerticalTolerance = 1.25f;

        private Line2MobileNPCTactics.Goal _goal;
        private Vector3 _target;
        private float _startedAt;
        private float _lastProgressAt;
        private float _bestDistance = float.PositiveInfinity;
        // A retreat/flank route often moves sideways or briefly away
        // from the target to go through an actual doorway or corridor.
        private readonly Line2NpcPhysicalProgressLedger _physicalProgress =
            new Line2NpcPhysicalProgressLedger();
        private bool _initialized;
        private bool _arrived;

        internal void Reset()
        {
            _initialized = false;
            _arrived = false;
            _goal = Line2MobileNPCTactics.Goal.None;
            _bestDistance = float.PositiveInfinity;
        }

        private static bool Finite(Vector3 position)
        {
            return !float.IsNaN(position.x) && !float.IsNaN(position.y) &&
                !float.IsNaN(position.z) && !float.IsInfinity(position.x) &&
                !float.IsInfinity(position.y) && !float.IsInfinity(position.z);
        }

        internal Outcome Observe(Line2MobileNPCTactics.Goal goal,
            Vector3 target, Vector3 actualPosition, float now,
            bool nativePathPending)
        {
            if (goal == Line2MobileNPCTactics.Goal.None)
            {
                Reset();
                return Outcome.None;
            }
            if (!Finite(target) || !Finite(actualPosition) ||
                float.IsNaN(now) || float.IsInfinity(now) || now < 0f)
            {
                Reset();
                return Outcome.Blocked;
            }
            Vector3 diff = target - actualPosition;
            float flatDistance = (float)Math.Sqrt(
                diff.x * diff.x + diff.z * diff.z);
            bool sameGoal = _initialized && _goal == goal &&
                (target - _target).sqrMagnitude <= 0.9f * 0.9f;
            bool atDestination = flatDistance <= ReachedDistance &&
                Math.Abs(diff.y) <= VerticalTolerance;
            // Port the Line 1 BrainDriver rule: a reached RETREAT is a
            // temporary safe position, not permission to rush the attacker
            // again. A reached FLANK is a firing vantage, not the end of
            // the tactical assignment. The existing goal's finite deadline
            // still expires it; no new timeout is granted here.
            bool hold = goal == Line2MobileNPCTactics.Goal.Retreat ||
                goal == Line2MobileNPCTactics.Goal.Flank;
            if (sameGoal && _arrived && atDestination)
                return Outcome.Holding;
            if (atDestination)
            {
                if (!hold)
                {
                    Reset();
                    return Outcome.Reached;
                }
                _initialized = true;
                _goal = goal;
                _target = target;
                if (!sameGoal) _startedAt = now;
                _lastProgressAt = now;
                _bestDistance = flatDistance;
                _physicalProgress.Reset(actualPosition);
                _arrived = true;
                return Outcome.Holding;
            }
            if (!sameGoal)
            {
                _initialized = true;
                _arrived = false;
                _goal = goal;
                _target = target;
                _startedAt = now;
                _lastProgressAt = now;
                _bestDistance = flatDistance;
                _physicalProgress.Reset(actualPosition);
                return Outcome.Moving;
            }
            if (_arrived)
            {
                // Physical displacement from a reached vantage starts a
                // fresh bounded return. Reaching once cannot give eternal
                // immunity from the stuck-route monitor.
                _arrived = false;
                _bestDistance = flatDistance;
                _physicalProgress.TryObserveNewPosition(actualPosition,
                    MinimumProgress);
                _lastProgressAt = now;
            }
            // A new best *physical* position proves progress. A proposal to
            // walk closer or an A* response is not a movement observation.
            if (_bestDistance - flatDistance >= MinimumProgress)
            {
                _bestDistance = flatDistance;
                _physicalProgress.TryObserveNewPosition(actualPosition,
                    MinimumProgress);
                _lastProgressAt = now;
            }
            else if (_physicalProgress.TryObserveNewPosition(
                actualPosition, MinimumProgress))
            {
                // A truly new physical position counts as progress even on
                // a detour. Repeated alternating points beside one wall
                // cannot keep a tactical goal alive indefinitely.
                _lastProgressAt = now;
            }
            float waitBudget = nativePathPending
                ? PendingGraceSeconds : StuckSeconds;
            if (now - _startedAt >= AbsoluteTimeoutSeconds ||
                now - _lastProgressAt >= waitBudget)
            {
                Reset();
                return Outcome.Blocked;
            }
            return Outcome.Moving;
        }
    }
}
