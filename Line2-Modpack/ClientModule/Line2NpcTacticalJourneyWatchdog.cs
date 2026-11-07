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
        internal enum Outcome { None, Moving, Reached, Blocked }

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
        private bool _initialized;

        internal void Reset()
        {
            _initialized = false;
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
            if (flatDistance <= ReachedDistance &&
                Math.Abs(diff.y) <= VerticalTolerance)
            {
                Reset();
                return Outcome.Reached;
            }
            if (!_initialized || _goal != goal ||
                (target - _target).sqrMagnitude > 0.9f * 0.9f)
            {
                _initialized = true;
                _goal = goal;
                _target = target;
                _startedAt = now;
                _lastProgressAt = now;
                _bestDistance = flatDistance;
                return Outcome.Moving;
            }
            // A new best *physical* position proves progress. A proposal to
            // walk closer or an A* response is not a movement observation.
            if (_bestDistance - flatDistance >= MinimumProgress)
            {
                _bestDistance = flatDistance;
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
