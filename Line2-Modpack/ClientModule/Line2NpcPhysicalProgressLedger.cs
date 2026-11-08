using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Bounded physical-movement history for NPC route health. A two-point
    /// wall/corner oscillation is NOT progress, even though the actor moves.
    /// A genuinely new corridor segment is progress even if it temporarily
    /// increases straight-line distance to the destination.
    ///
    /// Records actual CharacterController positions only, never predicted
    /// waypoints. The owner enforces separate absolute journey deadlines.
    /// </summary>
    internal sealed class Line2NpcPhysicalProgressLedger
    {
        private const int MaximumSamples = 16;
        private readonly Vector3[] _visited = new Vector3[MaximumSamples];
        private int _used;
        private int _next;

        internal void Reset(Vector3 origin)
        {
            _used = 0;
            _next = 0;
            if (Finite(origin))
                Record(origin);
        }

        internal bool TryObserveNewPosition(Vector3 actual, float minimumSpacing)
        {
            if (!Finite(actual) || float.IsNaN(minimumSpacing) ||
                float.IsInfinity(minimumSpacing) || minimumSpacing < 0.25f)
                return false;
            float square = minimumSpacing * minimumSpacing;
            for (int i = 0; i < _used; i++)
            {
                if ((actual - _visited[i]).sqrMagnitude < square)
                    return false;
            }
            Record(actual);
            return true;
        }

        private void Record(Vector3 point)
        {
            _visited[_next] = point;
            _next = (_next + 1) % MaximumSamples;
            if (_used < MaximumSamples) _used++;
        }

        private static bool Finite(Vector3 point)
        {
            return !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
                !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
                !float.IsNaN(point.z) && !float.IsInfinity(point.z);
        }
    }
}
