using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Waypoint progress and stall detection. Does not move transforms, set animation,
    /// or assume a specific MobileNPC motor API. Consume DesiredWaypoint from
    /// the existing humanoid motor and call Tick with its actual feet position.
    /// </summary>
    public sealed class HybridNPCPathFollower
    {
        public enum RouteState { Idle, Following, Arrived, Stalled }
        private readonly List<Vector3> path = new List<Vector3>();
        private int waypointIndex;
        private Vector3 lastSamplePosition;
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
            State = path.Count == 0 ? RouteState.Idle : RouteState.Following;
        }

        public void Clear()
        {
            path.Clear();
            waypointIndex = 0;
            hasSample = false;
            stalledTime = 0f;
            State = RouteState.Idle;
        }

        /// <summary>
        /// Real motor should call this once per update with world feet position.
        /// Returns Stalled after repeated windows with insufficient progress.
        /// Caller must then try local sidestep / blacklist portal / replan.
        /// </summary>
        public RouteState Tick(Vector3 feet, float deltaTime,
            float waypointTolerance = 0.65f, float stallSeconds = 2.5f)
        {
            if (State != RouteState.Following) return State;
            while (waypointIndex < path.Count &&
                Vector3.Distance(feet, path[waypointIndex]) <= waypointTolerance)
                ++waypointIndex;
            if (waypointIndex >= path.Count)
            {
                State = RouteState.Arrived;
                return State;
            }

            sampleTime += Mathf.Max(0f, deltaTime);
            if (!hasSample)
            {
                lastSamplePosition = feet;
                hasSample = true;
                sampleTime = 0f;
            }
            else if (sampleTime >= 0.5f)
            {
                float moved = Vector3.Distance(lastSamplePosition, feet);
                if (moved < 0.12f) stalledTime += sampleTime;
                else stalledTime = 0f;
                lastSamplePosition = feet;
                sampleTime = 0f;
                if (stalledTime >= stallSeconds) State = RouteState.Stalled;
            }
            return State;
        }
    }
}
