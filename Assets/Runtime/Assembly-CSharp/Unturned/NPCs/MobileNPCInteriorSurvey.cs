////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Unturned Better mobile NPC source integration.             //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// One NPC's finite, first-hand "inspect neighboring rooms" intention.
    /// This does NOT know whether a world item spawned, which items exist, or
    /// whether a room is accessible. Native ItemSpawnpoints are uncertain
    /// authored search hints; the existing motor/doors/route planner must
    /// physically reach every proposed point, and MobileNPCLooting must
    /// separately validate every actual ItemManager pickup.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCBrainDriver))]
    public sealed class MobileNPCInteriorSurvey : MonoBehaviour
    {
        private const int MaxInspectionsPerSite = 4;
        private const float SearchClusterRadius = 16f;
        // Query our own region first. On dense maps the old -1,-1 loop
        // exhausted the global limit before even visiting our current block.
        private static readonly Vector2Int[] NearbyRegionOffsets =
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(-1, 1),
            new Vector2Int(1, -1), new Vector2Int(-1, -1)
        };
        private readonly Vector3[] visited = new Vector3[MaxInspectionsPerSite];
        private readonly List<ItemSpawnpoint> candidates = new List<ItemSpawnpoint>(96);
        private readonly RaycastHit[] approachHits = new RaycastHit[12];
        private MobileNPCAutonomyPlanner autonomy;
        private MobileNPCBrainDriver brain;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCDoorNavigation doorNavigation;
        private MobileNPCHumanoidMotor motor;
        private bool isActive;
        private bool awaitingScan;
        private Vector3 origin;
        private Vector3 structureAnchor;
        private readonly List<RegionCoordinate> structureRegions = new List<RegionCoordinate>(9);
        private Vector3 assigned;
        private float expiresAt;
        private float awaitingScanUntil;
        private float nextCandidateAttemptAt;
        private int visitedCount;
        private int scannedCount;
        private int successfulLootGoals;
        private int failedWaypoints;

        private void Awake()
        {
            autonomy = GetComponent<MobileNPCAutonomyPlanner>();
            brain = GetComponent<MobileNPCBrainDriver>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            doorNavigation = GetComponent<MobileNPCDoorNavigation>();
            motor = GetComponent<MobileNPCHumanoidMotor>();
        }

        /// <summary>
        /// Begin a local survey only around a real spawn-site hint. A lone
        /// roadside item spawn is not enough to invent a whole building.
        /// A cluster needs multiple distinct map-authored search points.
        /// </summary>
        public bool TryBeginAtAuthoredSiteServer(Vector3 firstSite)
        {
            if (!CanExploreServer() || LevelItems.spawns == null)
                return false;
            if (isActive && Time.time < expiresAt)
                return false;

            // A collection of roadside item spots alone is not a house.
            // Confirm there is a map-authored LARGE world object near the
            // cluster before committing to the interior survey.
            if (!TryResolveAuthoredStructureServer(firstSite, out Vector3 building))
                return false;
            candidates.Clear();
            GatherNearbyAuthoredSites(firstSite, SearchClusterRadius, 96);
            int distinct = 0;
            for (int i = 0; i < candidates.Count; ++i)
            {
                ItemSpawnpoint candidate = candidates[i];
                if (candidate == null || Mathf.Abs(candidate.point.y - firstSite.y) > 4.5f)
                    continue;
                if ((candidate.point - firstSite).sqrMagnitude < 1f)
                    continue;
                ++distinct;
                if (distinct >= 2)
                    break;
            }
            if (distinct < 2)
                return false;

            origin = firstSite;
            structureAnchor = building;
            assigned = firstSite;
            visitedCount = 0;
            scannedCount = 0;
            successfulLootGoals = 0;
            failedWaypoints = 0;
            expiresAt = Time.time + 125f;
            awaitingScan = false;
            nextCandidateAttemptAt = 0f;
            isActive = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCInteriorSurvey] BEGIN npc=" + name
                + " origin=" + origin + " nearbyAuthoredSites=" + candidates.Count, this);
#endif
            return true;
        }

        private bool CanExploreServer()
        {
            if (!Provider.isServer || !Level.isLoaded || autonomy == null
                || brain == null || motor == null || !motor.IsInitialized
                || vitals != null && vitals.IsDead)
                return false;
            return autonomy.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge;
        }

        public bool IsWaitingForInspectionServer => isActive && awaitingScan
            && Time.time < awaitingScanUntil;

        public void ReportArrivedServer(Vector3 actualDestination)
        {
            if (!isActive || !CanExploreServer() || Time.time >= expiresAt
                || (actualDestination - assigned).sqrMagnitude > 3.5f * 3.5f)
                return;
            // Prevent fake coverage from incidental visits or path intermediates.
            if (visitedCount < MaxInspectionsPerSite)
                visited[visitedCount++] = actualDestination;
            awaitingScan = true;
            awaitingScanUntil = Time.time + 7f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCInteriorSurvey] ARRIVED npc=" + name
                + " room=" + visitedCount + "/" + MaxInspectionsPerSite
                + " site=" + actualDestination + "; scan not yet done.", this);
#endif
        }

        public void ReportLocalScanCompletedServer(Vector3 inspectedAt,
            bool acquiredLootGoal)
        {
            if (!isActive || !awaitingScan || !Provider.isServer
                || (inspectedAt - assigned).sqrMagnitude > 3.5f * 3.5f)
                return;
            awaitingScan = false;
            ++scannedCount;
            if (acquiredLootGoal)
                ++successfulLootGoals;
            if (visitedCount >= MaxInspectionsPerSite)
                FinishServer("site-budget-exhausted");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            else
                Debug.Log("[MobileNPCInteriorSurvey] INSPECTED npc=" + name
                    + " room=" + visitedCount + " startedRealLootGoal="
                    + acquiredLootGoal + "; choosing another site.", this);
#endif
        }

        public bool TryChooseNextRoomServer(float radius, Vector3 home,
            float homeRadius, LayerMask groundMask, float slopeLimit,
            out Vector3 location)
        {
            location = Vector3.zero;
            if (!isActive)
                return false;
            if (!CanExploreServer() || Time.time >= expiresAt
                || failedWaypoints >= 2)
            {
                FinishServer("interrupted-or-expired");
                return false;
            }
            if (awaitingScan)
            {
                if (Time.time < awaitingScanUntil)
                    return false;
                awaitingScan = false; // missed scan, never claims coverage
            }
            if (brain.Target != null || brain.CurrentGoal != MobileNPCTactics.Goal.None
                || Time.time < nextCandidateAttemptAt)
                return false;

            if (visitedCount >= MaxInspectionsPerSite)
            {
                FinishServer("complete");
                return false;
            }

            // Only a small local cluster is scanned, not every item spawn
            // in a city. This is triggered by optional roaming arbitration
            // and must never run on each frame or during active combat.
            candidates.Clear();
            GatherNearbyAuthoredSites(origin, SearchClusterRadius, 96);
            int probes = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count && probes < 10; ++i)
            {
                ItemSpawnpoint point = candidates[i];
                if (point == null)
                    continue;
                Vector3 raw = point.point;
                if (Mathf.Abs(raw.y - origin.y) > 4.5f
                    || Mathf.Abs(raw.y - transform.position.y) > 4.5f
                    || new Vector2(raw.x - structureAnchor.x,
                        raw.z - structureAnchor.z).sqrMagnitude > 20f * 20f
                    || (raw - assigned).sqrMagnitude < 3.8f * 3.8f
                    || (raw - transform.position).sqrMagnitude < 3.8f * 3.8f
                    || (raw - home).sqrMagnitude > homeRadius * homeRadius
                    || (raw - origin).sqrMagnitude > radius * radius
                    || !autonomy.IsCandidateAllowedServer(raw))
                    continue;
                bool previouslyVisited = false;
                for (int j = 0; j < visitedCount; ++j)
                {
                    if (Mathf.Abs(raw.y - visited[j].y) < 1.8f
                        && new Vector2(raw.x - visited[j].x,
                            raw.z - visited[j].z).sqrMagnitude < 4.5f * 4.5f)
                    {
                        previouslyVisited = true;
                        break;
                    }
                }
                if (previouslyVisited)
                    continue;

                ++probes;
                if (!MobileNPCSpawnService.TryFindSafeGroundPosition(raw,
                    1.5f, 2f, groundMask, Mathf.Min(55f, slopeLimit),
                    out Vector3 grounded, out _)
                    || Mathf.Abs(grounded.y - raw.y) > 1.3f
                    || !autonomy.IsCandidateAllowedServer(grounded)
                    || !motor.IsValidScoutingFootingServer(grounded))
                    continue;

                // Prefer same-level connected-looking openings first; a
                // blocked ray is a routing COST, not evidence that the room
                // is unreachable. The motor's native doors/route planner
                // still makes the actual physical traversal decision.
                float verticalDifference = Mathf.Abs(grounded.y - transform.position.y);
                float score = (grounded - transform.position).sqrMagnitude
                    * Random.Range(0.92f, 1.08f)
                    + Mathf.Max(0f, verticalDifference - 0.65f) * 10f
                    + EvaluateLocalApproachPenaltyServer(grounded);
                // Prefer a genuinely different horizontal area or floor,
                // rather than counting four item spots on one shelf as rooms.
                float nearestVisited = float.PositiveInfinity;
                for (int j = 0; j < visitedCount; ++j)
                {
                    float planar = new Vector2(grounded.x - visited[j].x,
                        grounded.z - visited[j].z).magnitude;
                    float floorDifference = Mathf.Abs(grounded.y - visited[j].y);
                    nearestVisited = Mathf.Min(nearestVisited,
                        planar + Mathf.Min(floorDifference, 4f));
                }
                if (visitedCount > 0 && nearestVisited < 7.5f)
                    score += (7.5f - nearestVisited) * 3f;
                if (score < best)
                {
                    location = grounded;
                    best = score;
                }
            }
            if (best < float.PositiveInfinity)
                return true;
            FinishServer("no-more-valid-authored-sites");
            return false;
        }

        /// <summary>
        /// Short immediate passage assessment for SELECTED candidate rooms.
        /// This is a local planning hint, not a portal link and never opens
        /// doors. A closer solid wall remains a deterrent; no fabricated
        /// entrance or stair is authored into the world.
        /// </summary>
        private float EvaluateLocalApproachPenaltyServer(Vector3 target)
        {
            Vector3 direction = target - transform.position;
            direction.y = 0f;
            float planarDistance = direction.magnitude;
            if (planarDistance <= 0.2f)
                return 0f;
            // Far targets need the real long-range route solver; a 3m
            // forward test must not be extrapolated to the whole building.
            float rayDistance = Mathf.Min(3.1f, planarDistance);
            Vector3 start = transform.position + Vector3.up * 1.1f;
            int count = Physics.SphereCastNonAlloc(start, 0.14f,
                direction / planarDistance, approachHits, rayDistance,
                RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
            if (count >= approachHits.Length)
                return 7f; // saturated evidence is inconclusive
            Collider nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                Collider collider = approachHits[i].collider;
                if (collider == null
                    || collider.transform.IsChildOf(transform)
                    || collider.GetComponentInParent<Player>() != null
                    || collider.GetComponentInParent<Zombie>() != null
                    || collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null)
                    continue;
                if (approachHits[i].distance < nearestDistance)
                {
                    nearest = collider;
                    nearestDistance = approachHits[i].distance;
                }
            }
            if (nearest == null)
                return 0f;
            // An actual native unlocked door may be traversable after
            // interaction; all other geometry still needs a real detour.
            InteractableDoor door = nearest.GetComponentInParent<InteractableDoor>();
            if (door != null && door.isActiveAndEnabled
                && !door.IsChildOfVehicle && !door.IsLockedForMobileNPCNavigation)
            {
                if (doorNavigation != null
                    && doorNavigation.IsTraversableUnlockedDoorEdgeServer(
                        start, direction / planarDistance, 0.14f,
                        rayDistance, RayMasks.BLOCK_COLLISION))
                    return 1.5f;
            }
            return 9f;
        }

        /// <summary>Another squadmate owns this destination. Wait briefly
        /// rather than recording a fake navigation failure or visit.</summary>
        public void DeferUnclaimedRoomServer()
        {
            if (Provider.isServer && isActive)
                nextCandidateAttemptAt = Time.time + 6f;
        }

        public void ConfirmNextRoomAssignedServer(Vector3 destination)
        {
            if (!isActive || !CanExploreServer())
                return;
            assigned = destination;
            nextCandidateAttemptAt = Time.time + 1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCInteriorSurvey] NEXT npc=" + name
                + " room=" + (visitedCount + 1)
                + " destination=" + destination + ".", this);
#endif
        }

        public void ReportRoomRouteBlockedServer(Vector3 rejected)
        {
            if (!isActive || (rejected - assigned).sqrMagnitude > 4f * 4f)
                return;
            ++failedWaypoints;
            // Failure is NOT an inspection; delegate blocked waypoint memory
            // to the existing personal navigation/autonomy systems.
            nextCandidateAttemptAt = Time.time + 6f;
            if (failedWaypoints >= 2)
                FinishServer("two-blocked-routes");
        }

        /// <summary>
        /// Ground the survey in an actual authored large structure, rather
        /// than a road with several spawnpoints. This is NOT a room polygon,
        /// so doors and the native navigation provider still decide whether
        /// each internal target can physically be visited.
        /// </summary>
        private bool TryResolveAuthoredStructureServer(Vector3 site,
            out Vector3 structure)
        {
            structure = Vector3.zero;
            if (LevelObjects.objects == null)
                return false;
            structureRegions.Clear();
            Regions.getRegionsInRadius(site, 22f, structureRegions);
            float best = 18f * 18f;
            for (int r = 0; r < structureRegions.Count; ++r)
            {
                RegionCoordinate region = structureRegions[r];
                if (region.x >= LevelObjects.objects.GetLength(0)
                    || region.y >= LevelObjects.objects.GetLength(1))
                    continue;
                List<LevelObject> objects = LevelObjects.objects[region.x, region.y];
                if (objects == null)
                    continue;
                int step = Mathf.Max(1, objects.Count / 64);
                for (int i = 0; i < objects.Count; i += step)
                {
                    LevelObject obj = objects[i];
                    if (obj == null || obj.transform == null || obj.asset == null
                        || obj.asset.type != EObjectType.LARGE)
                        continue;
                    Vector3 offset = obj.transform.position - site;
                    if (Mathf.Abs(offset.y) > 5f)
                        continue;
                    float distanceSq = new Vector2(offset.x, offset.z).sqrMagnitude;
                    if (distanceSq >= best)
                        continue;
                    structure = obj.transform.position;
                    best = distanceSq;
                }
            }
            return best < 18f * 18f;
        }

        private void GatherNearbyAuthoredSites(Vector3 site, float radius, int limit)
        {
            if (LevelItems.spawns == null)
                return;
            Vector2Int center = Regions.GetCoordinateVector2Int(site);
            int examined = 0;
            // Per-region quotas distribute the budget across nearby blocks,
            // rather than letting one very dense region monopolize all 96
            // authored hints. Most budget belongs to the current block.
            for (int regionIndex = 0;
                regionIndex < NearbyRegionOffsets.Length && examined < limit;
                ++regionIndex)
            {
                Vector2Int offset = NearbyRegionOffsets[regionIndex];
                int x = center.x + offset.x, z = center.y + offset.y;
                if (x < 0 || z < 0 || x >= Regions.WORLD_SIZE
                    || z >= Regions.WORLD_SIZE)
                    continue;
                List<ItemSpawnpoint> sites = LevelItems.spawns[x, z];
                if (sites == null || sites.Count == 0)
                    continue;
                int quota = Mathf.Min(limit - examined, regionIndex == 0 ? 32 : 8);
                int stride = Mathf.Max(1, Mathf.CeilToInt(sites.Count / (float)quota));
                int seenInRegion = 0;
                for (int i = 0; i < sites.Count && seenInRegion < quota; i += stride)
                {
                    ++examined;
                    ++seenInRegion;
                    ItemSpawnpoint point = sites[i];
                    if (point == null) continue;
                    Vector3 delta = point.point - site;
                    // The authored hint is NOT a promised item, doorway, or
                    // passable stair. Each final route must be walked.
                    if (Mathf.Abs(delta.y) <= 4.5f
                        && new Vector2(delta.x, delta.z).sqrMagnitude <= radius * radius)
                        candidates.Add(point);
                }
            }
        }

        private void FinishServer(string reason)
        {
            // A combat interruption, blocked route or timeout is NOT
            // evidence that the building was thoroughly searched. Retire
            // only when this search actually ran out of viable nearby
            // authored inspection sites or completed its fixed survey budget.
            if (isActive && autonomy != null && scannedCount >= 2
                && (reason == "site-budget-exhausted"
                    || reason == "no-more-valid-authored-sites"
                    || reason == "complete"))
                autonomy.ReportStructureSurveyCompletedServer(origin, scannedCount);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (isActive)
                Debug.Log("[MobileNPCInteriorSurvey] END npc=" + name
                    + " reason=" + reason + " physicallyVisited=" + visitedCount
                    + " realLootGoals=" + successfulLootGoals, this);
#endif
            isActive = false;
            awaitingScan = false;
            candidates.Clear();
        }

        private void OnDisable()
        {
            isActive = false;
            awaitingScan = false;
            candidates.Clear();
        }
    }
}
