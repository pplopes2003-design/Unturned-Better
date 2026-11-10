using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Engine-agnostic driver decisions (NOT a physics implementation).
    /// Produces bounded steering/throttle/brake targets from a verified
    /// vehicle-route waypoint and physical obstacle measurements.
    /// Native and Workshop adapters must translate the output via supported
    /// vehicle controls. Never touches passenger or Player objects.
    /// </summary>
    internal static class Line2NpcVehicleSteeringPolicy
    {
        internal struct Command
        {
            internal float Steer;
            internal float Throttle;
            internal bool Brake;
            internal bool Reverse;
        }

        internal static Command Decide(bool validRoadWaypoint,
            float headingErrorDegrees, float waypointDistanceMeters,
            float speedMetersPerSecond, float obstacleDistanceMeters,
            float stalledSeconds, bool rearClear)
        {
            Command emergency = new Command { Steer = 0f,
                Throttle = 0f, Brake = true, Reverse = false };
            if (!validRoadWaypoint || !Valid(headingErrorDegrees) ||
                !Valid(waypointDistanceMeters) || !Valid(speedMetersPerSecond) ||
                !Valid(obstacleDistanceMeters) || !Valid(stalledSeconds) ||
                waypointDistanceMeters < 0f || speedMetersPerSecond < 0f ||
                obstacleDistanceMeters < 0f || stalledSeconds < 0f)
                return emergency;
            if (waypointDistanceMeters < 2.5f)
                return emergency;
            float steer = Clamp(headingErrorDegrees / 35f, -1f, 1f);
            if (obstacleDistanceMeters < 3.5f)
            {
                if (stalledSeconds >= 3f && rearClear &&
                    speedMetersPerSecond < 0.7f)
                    return new Command { Steer = -steer,
                        Throttle = -0.25f, Reverse = true, Brake = false };
                return emergency;
            }
            // Reduce permitted speed at tight corners or near obstacles.
            float corner = Math.Abs(headingErrorDegrees);
            float targetSpeed = corner > 70f ? 2.0f :
                corner > 35f ? 4f : 8f;
            if (obstacleDistanceMeters < 10f)
                targetSpeed = Math.Min(targetSpeed, 3f);
            if (speedMetersPerSecond > targetSpeed + 0.7f)
                return new Command { Steer = steer, Throttle = 0f,
                    Brake = true, Reverse = false };
            return new Command { Steer = steer,
                Throttle = speedMetersPerSecond < targetSpeed ? 0.65f : 0.1f,
                Brake = false, Reverse = false };
        }

        private static float Clamp(float x, float min, float max)
        {
            return Math.Max(min, Math.Min(max, x));
        }

        private static bool Valid(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }
    }
}
