using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// A native A* waypoint is not proof that the humanoid has physically
    /// reached the brain's requested destination. The graph may snap the
    /// final node to the other side of an obstacle or onto another floor.
    /// </summary>
    internal static class Line2NpcNativeArrivalPolicy
    {
        private const float HorizontalTolerance = 1.20f;
        private const float VerticalTolerance = 0.85f;

        internal static bool HasPhysicallyArrived(Vector3 actual, Vector3 goal)
        {
            if (!Finite(actual) || !Finite(goal))
                return false;
            Vector3 difference = goal - actual;
            return difference.x * difference.x +
                difference.z * difference.z <=
                    HorizontalTolerance * HorizontalTolerance &&
                System.Math.Abs(difference.y) <= VerticalTolerance;
        }

        private static bool Finite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
                !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
                !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }
    }
}
