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
        }

        public bool TryGetRoute(Vector3 start, Vector3 goal, List<Vector3> output)
        {
            if (output == null) return false;
            output.Clear();
            if (!isActiveAndEnabled || controller == null) return false;
            if (Time.time < nextRequestAt || Time.time < nextSharedRequestAt)
            {
                diagnosticStatus = "throttled";
                return false;
            }
            nextRequestAt = Time.time + Mathf.Max(0.25f, replanningInterval);
            nextSharedRequestAt = Time.time + 0.15f;
            if (Vector3.Distance(start, goal) > searchRadius)
            {
                diagnosticStatus = "outside-local-horizon";
                LastPathFailed = true;
                return false;
            }

            HybridNPCGridPlanner.Settings settings = HybridNPCGridPlanner.Settings.Default;
            settings.cellSize = 0.65f;
            settings.agentRadius = Mathf.Max(0.15f, controller.radius * 0.82f);
            settings.agentHeight = Mathf.Max(1.1f, controller.height);
            settings.maxStepUp = Mathf.Max(0.25f, controller.stepOffset);
            settings.maxSlopeDegrees = controller.slopeLimit;
            settings.maxSearchRadius = searchRadius;
            settings.maxExpandedNodes = Mathf.Clamp(maximumExpansions, 200, 3200);
            settings.collisionMask = navigationMask & ~(1 << gameObject.layer);
            settings.ignoredRoot = transform;

            bool found = HybridNPCObservedPortalRegistry.TryRoute(start, goal, settings, output);
            if (!found)
                found = HybridNPCGridPlanner.TryFindPath(start, goal, settings, output);
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
