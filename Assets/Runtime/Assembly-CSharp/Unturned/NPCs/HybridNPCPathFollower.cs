using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Observes actual feet movement along a planned route. Physical movement
    /// remains owned by the NPC motor. A route progresses only when the motor
    /// approaches a waypoint, never merely because the actor moves sideways.
    /// </summary>
    public sealed class HybridNPCPathFollower
    {
        public enum RouteState { Idle, Following, Arrived, Stalled }
        private readonly List<Vector3> path = new List<Vector3>();
        private int waypointIndex;
        private Vector3 lastSamplePosition;
        private float lastDistance;
        private float sampleTime;
        private float stalledTime;
        private bool hasSample;

        public RouteState State { get; private set; }
        public Vector3 DesiredWaypoint
        {
            get { return path.Count == 0 ? Vector3.zero :
                path[Mathf.Min(waypointIndex, path.Count - 1)]; }
        }
        public int WaypointIndex { get { return waypointIndex; } }
        public int WaypointCount { get { return path.Count; } }
        public bool IsMakingProgress { get; private set; }
        public float SecondsWithoutProgress { get { return stalledTime; } }

        public void SetPath(IList<Vector3> newWaypoints)
        {
            path.Clear();
            if (newWaypoints != null)
                for (int i = 0; i < newWaypoints.Count; ++i)
                    path.Add(newWaypoints[i]);
            waypointIndex = 0;
            sampleTime = 0f;
            stalledTime = 0f;
            hasSample = false;
            IsMakingProgress = false;
            State = path.Count == 0 ? RouteState.Idle : RouteState.Following;
        }

        public void Clear()
        {
            path.Clear();
            waypointIndex = 0;
            sampleTime = 0f;
            stalledTime = 0f;
            hasSample = false;
            IsMakingProgress = false;
            State = RouteState.Idle;
        }

        /// <summary>
        /// Called AFTER the motor moves. Require both planar and vertical
        /// proximity: being directly below a stair landing is not arrival.
        /// Only genuine approach to the next waypoint resets stall detection.
        /// </summary>
        public RouteState Tick(Vector3 feet, float deltaTime,
            float waypointTolerance = 0.65f, float stallSeconds = 2.5f)
        {
            if (State != RouteState.Following) return State;
            float horizontalTolerance = Mathf.Max(0.1f, waypointTolerance);
            float verticalTolerance = Mathf.Min(0.55f, horizontalTolerance);
            bool advanced = false;
            while (waypointIndex < path.Count)
            {
                Vector3 difference = path[waypointIndex] - feet;
                if (new Vector2(difference.x, difference.z).magnitude > horizontalTolerance
                    || Mathf.Abs(difference.y) > verticalTolerance) break;
                ++waypointIndex;
                advanced = true;
            }
            if (waypointIndex >= path.Count)
            {
                State = RouteState.Arrived;
                IsMakingProgress = true;
                return State;
            }
            float remaining = Vector3.Distance(feet, path[waypointIndex]);
            if (advanced)
            {
                stalledTime = 0f;
                sampleTime = 0f;
                hasSample = false;
                IsMakingProgress = true;
                return State;
            }
            sampleTime += Mathf.Max(0f, deltaTime);
            if (!hasSample)
            {
                lastSamplePosition = feet;
                lastDistance = remaining;
                hasSample = true;
                sampleTime = 0f;
                IsMakingProgress = false;
            }
            else if (sampleTime >= 0.5f)
            {
                float gain = lastDistance - remaining;
                // Do not treat walking into a wall, lateral oscillation, or
                // movement away from the target as successful route progress.
                IsMakingProgress = gain >= 0.10f;
                if (IsMakingProgress) stalledTime = 0f;
                else stalledTime += sampleTime;
                lastSamplePosition = feet;
                lastDistance = remaining;
                sampleTime = 0f;
                if (stalledTime >= Mathf.Max(0.5f, stallSeconds))
                    State = RouteState.Stalled;
            }
            return State;
        }
    }
}
