using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Navigation service to be invoked by the existing NPC brain/motor integration.
    /// Keeps route computation, report events and stuck detection in one place.
    /// It DOES NOT automatically attach to existing NPCs.
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

        public bool Plan(Vector3 currentFeet, Vector3 goal, int fromRegion, int toRegion,
            HybridNPCGridPlanner.Settings settings)
        {
            destination = goal;
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
            if (ok)
            {
                failureCount = 0;
                stallRecorded = false;
                follower.SetPath(route);
            }
            else
            {
                failureCount++;
                follower.Clear();
            }
            return ok;
        }

        /// <summary>
        /// Call after motor movement. Caller owns all physics and animation.
        /// Stalled routes remain stalled until a recovery or fresh Plan.
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
