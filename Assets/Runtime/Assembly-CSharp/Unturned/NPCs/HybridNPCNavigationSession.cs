using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Opt-in navigation session for the existing NPC brain and motor.
    /// Route ownership is kept until arrival, verified stall, or a material
    /// destination change. This class never moves transforms.
    /// </summary>
    public sealed class HybridNPCNavigationSession
    {
        private readonly HybridNPCRegionNavigator navigator;
        private readonly HybridNPCPathFollower follower = new HybridNPCPathFollower();
        private readonly HybridNPCNavigationTelemetry telemetry;
        private readonly string agentId;
        private readonly string mapName;
        private Vector3 destination;
        private int failureCount;
        private bool stallRecorded;

        public HybridNPCNavigationSession(HybridNPCRegionNavigator navigator,
            HybridNPCNavigationTelemetry telemetry, string agentId, string mapName)
        {
            this.navigator = navigator;
            this.telemetry = telemetry;
            this.agentId = agentId;
            this.mapName = mapName;
        }

        public HybridNPCPathFollower.RouteState State { get { return follower.State; } }
        public Vector3 Waypoint { get { return follower.DesiredWaypoint; } }
        public int ConsecutiveFailures { get { return failureCount; } }
        public bool IsRouteLocked
        {
            get { return follower.State == HybridNPCPathFollower.RouteState.Following; }
        }

        /// <summary>
        /// Reject incidental planner updates while a route is being followed.
        /// A materially different goal or explicit forced replan may interrupt.
        /// Use force only for urgent danger or motor-confirmed invalidation.
        /// </summary>
        public bool Plan(Vector3 currentFeet, Vector3 goal, int fromRegion, int toRegion,
            HybridNPCGridPlanner.Settings settings)
        {
            return Plan(currentFeet, goal, fromRegion, toRegion, settings, false);
        }

        public bool Plan(Vector3 currentFeet, Vector3 goal, int fromRegion, int toRegion,
            HybridNPCGridPlanner.Settings settings, bool force)
        {
            // Keep the existing route intact on an ordinary refresh.
            if (!force && IsRouteLocked &&
                (goal - destination).sqrMagnitude <= 2.25f)
                return true;
            if (navigator == null) return false;

            var route = new List<Vector3>();
            var watch = Stopwatch.StartNew();
            bool ok = navigator.TryRoute(currentFeet, goal, fromRegion, toRegion,
                settings, route);
            watch.Stop();
            float length = 0f;
            for (int i = 1; i < route.Count; ++i)
                length += Vector3.Distance(route[i - 1], route[i]);
            if (telemetry != null)
                telemetry.RecordRoute(agentId, mapName, currentFeet, goal,
                    fromRegion, toRegion, -1, route.Count, length,
                    (float)watch.Elapsed.TotalMilliseconds, ok,
                    ok ? "path_found" : "no_connected_path");
            if (ok && route.Count > 0)
            {
                destination = goal;
                failureCount = 0;
                stallRecorded = false;
                follower.SetPath(route);
                return true;
            }

            failureCount++;
            // Failed replacement is not permission to discard a working route.
            if (!IsRouteLocked || force)
                follower.Clear();
            return false;
        }

        /// <summary>
        /// Call after the physical motor moves, with actual feet coordinates.
        /// The route remains stalled until recovery or successful replanning.
        /// </summary>
        public HybridNPCPathFollower.RouteState Tick(Vector3 actualFeet,
            float deltaTime, float speed)
        {
            var state = follower.Tick(actualFeet, deltaTime);
            if (telemetry != null)
            {
                telemetry.RecordMovement(agentId, mapName, actualFeet, destination,
                    follower.DesiredWaypoint, speed, state.ToString());
                if (state == HybridNPCPathFollower.RouteState.Stalled && !stallRecorded)
                {
                    stallRecorded = true;
                    telemetry.RecordProblem(agentId, mapName, actualFeet,
                        destination, "movement_stalled", "request_replan", false);
                }
            }
            return state;
        }

        public void ReportRecovery(Vector3 position, string action, bool recovered)
        {
            if (telemetry != null)
                telemetry.RecordProblem(agentId, mapName, position, destination,
                    "movement_stalled", action, recovered);
        }
    }
}
