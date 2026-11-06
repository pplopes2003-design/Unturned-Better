using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// NPC-only geometry-derived A* routing provider. No Pathfinding namespace,
    /// Seeker, baked navmesh, or Unturned zombie graph dependency.
    /// Runs on the server Unity thread with a shared planning throttle.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class MobileNPCIndependentRouteProvider : MonoBehaviour
    {
        [SerializeField] private LayerMask navigationMask = ~0;
        [SerializeField] private float searchRadius = 30f;
        [SerializeField] private int maximumExpansions = 1700;
        [SerializeField] private float replanningInterval = 1.0f;

        private CharacterController controller;
        private MobileNPCSpatialMemory spatialMemory;
        private readonly List<Vector3> approachLeg = new List<Vector3>(48);
        private readonly List<Vector3> finalLeg = new List<Vector3>(48);
        private float nextRequestAt;
        private static float nextSharedRequestAt;
        private string diagnosticStatus = "ready";
        public bool IsAvailable => isActiveAndEnabled;
        public bool IsRequestPending => false; // synchronous, bounded request
        public bool LastPathFailed { get; private set; }
        public string DiagnosticStatus => diagnosticStatus;
        public string ObservedBackendName => "npc-owned-physics-astar";

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            // Do not treat authored pathfinding and door marker volumes
            // as blocking physical geometry during scans or route search.
            navigationMask = (int)navigationMask & ~(RayMasks.NAVMESH | RayMasks.LOGIC);
            spatialMemory = GetComponent<MobileNPCSpatialMemory>();
            if (spatialMemory == null)
                spatialMemory = gameObject.AddComponent<MobileNPCSpatialMemory>();
        }

        private void Update()
        {
            if (!Provider.isServer || controller == null || spatialMemory == null) return;
            // Excluding the entire NPC layer also hides building geometry when a
            // map happens to reuse that layer. The sensor already filters the
            // owner's transform hierarchy, so preserve world collision layers.
            spatialMemory.Scan(transform.position, navigationMask, transform);
        }

        /// <summary>
        /// Only a verified short gateway stage for a far-away objective,
        /// never a promise that the entire distant route was solved.
        /// </summary>
        public bool TryGetConfirmedGatewayStage(Vector3 start, Vector3 distantGoal,
            List<Vector3> output)
        {
            if (output == null) return false;
            output.Clear();
            if (!Provider.isServer || !isActiveAndEnabled || controller == null
                || Time.time < nextRequestAt || Time.time < nextSharedRequestAt)
                return false;
            nextRequestAt = Time.time + Mathf.Max(0.5f, replanningInterval);
            nextSharedRequestAt = Time.time + 0.15f;
            HybridNPCGridPlanner.Settings settings = BuildSettings();
            bool staged = HybridNPCObservedPortalRegistry.TryStageThroughConfirmedCrossing(
                start, distantGoal, settings, output);
            diagnosticStatus = staged ? "verified-gateway-stage" : "no-confirmed-gateway";
            return staged;
        }

        private HybridNPCGridPlanner.Settings BuildSettings()
        {
            HybridNPCGridPlanner.Settings settings = HybridNPCGridPlanner.Settings.Default;
            settings.cellSize = Mathf.Clamp(controller.radius * 1.1f, 0.35f, 0.65f);
            settings.agentRadius = Mathf.Max(0.15f, controller.radius - controller.skinWidth * 0.5f);
            settings.agentHeight = Mathf.Max(1.1f, controller.height);
            settings.maxStepUp = Mathf.Max(0.05f, controller.stepOffset);
            settings.maxSlopeDegrees = controller.slopeLimit;
            settings.maxSearchRadius = searchRadius;
            settings.maxExpandedNodes = Mathf.Clamp(maximumExpansions, 200, 3200);
            settings.collisionMask = navigationMask;
            settings.ignoredRoot = transform;
            return settings;
        }

        public bool TryGetRoute(Vector3 start, Vector3 goal, List<Vector3> output)
        {
            if (output == null) return false;
            output.Clear();
            if (!isActiveAndEnabled || controller == null) return false;
            // Out-of-horizon calls must not consume the global planning
            // throttle: a real staging request may follow this same frame.
            if (Vector3.Distance(start, goal) > searchRadius)
            {
                diagnosticStatus = "outside-local-horizon";
                LastPathFailed = true;
                return false;
            }
            if (Time.time < nextRequestAt || Time.time < nextSharedRequestAt)
            {
                diagnosticStatus = "throttled";
                return false;
            }
            nextRequestAt = Time.time + Mathf.Max(0.25f, replanningInterval);
            nextSharedRequestAt = Time.time + 0.15f;

            HybridNPCGridPlanner.Settings settings = BuildSettings();

            bool found = spatialMemory != null &&
                spatialMemory.TryRouteCrossedPassage(start, goal, settings, output);
            if (!found)
                found = HybridNPCObservedPortalRegistry.TryRoute(start, goal, settings, output);
            if (!found)
                found = HybridNPCGridPlanner.TryFindPath(start, goal, settings, output);
            if (!found && spatialMemory != null)
            {
                // A sensed position is only a proposal; physics-verified
                // A* must approve BOTH segments before it becomes a route.
                Vector3 waypoint;
                if (spatialMemory.TryGetObservedIntermediate(start, goal, out waypoint))
                {
                    approachLeg.Clear();
                    finalLeg.Clear();
                    if (HybridNPCGridPlanner.TryFindPath(start, waypoint, settings, approachLeg)
                        && HybridNPCGridPlanner.TryFindPath(waypoint, goal, settings, finalLeg))
                    {
                        output.Clear();
                        output.AddRange(approachLeg);
                        for (int i = 0; i < finalLeg.Count; ++i)
                            if (output.Count == 0 ||
                                (output[output.Count - 1] - finalLeg[i]).sqrMagnitude > 0.01f)
                                output.Add(finalLeg[i]);
                        found = output.Count >= 2;
                    }
                }
            }
            LastPathFailed = !found;
            diagnosticStatus = found ? "route-complete" : HybridNPCGridPlanner.LastFailureReason;
            if (!found || output.Count < 2)
            {
                output.Clear();
                return false;
            }
            return true;
        }
    }
}
