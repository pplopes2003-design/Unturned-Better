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
        private readonly Vector3[] visited = new Vector3[MaxInspectionsPerSite];
        private readonly List<ItemSpawnpoint> candidates = new List<ItemSpawnpoint>(32);
        private MobileNPCAutonomyPlanner autonomy;
        private MobileNPCBrainDriver brain;
        private MobileNPCHumanoidVitals vitals;
        private bool isActive;
        private bool awaitingScan;
        private Vector3 origin;
        private Vector3 assigned;
        private float expiresAt;
        private float awaitingScanUntil;
        private float nextCandidateAttemptAt;
        private int visitedCount;
        private int successfulLootGoals;
        private int failedWaypoints;

        private void Awake()
        {
            autonomy = GetComponent<MobileNPCAutonomyPlanner>();
            brain = GetComponent<MobileNPCBrainDriver>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
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
            assigned = firstSite;
            visitedCount = 0;
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
                || brain == null || vitals != null && vitals.IsDead)
                return false;
            return autonomy.CurrentJob == MobileNPCAutonomyPlanner.Job.Scavenge;
        }

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
                    || (raw - assigned).sqrMagnitude < 3.8f * 3.8f
                    || (raw - transform.position).sqrMagnitude < 3.8f * 3.8f
                    || (raw - home).sqrMagnitude > homeRadius * homeRadius
                    || (raw - origin).sqrMagnitude > radius * radius
                    || !autonomy.IsCandidateAllowedServer(raw))
                    continue;
                bool previouslyVisited = false;
                for (int j = 0; j < visitedCount; ++j)
                {
                    if ((raw - visited[j]).sqrMagnitude < 4.5f * 4.5f)
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
                    || Physics.CheckCapsule(grounded + Vector3.up * 0.35f,
                        grounded + Vector3.up * 1.65f, 0.34f,
                        RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore))
                    continue;

                float score = (grounded - transform.position).sqrMagnitude
                    * Random.Range(0.92f, 1.08f);
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

        private void GatherNearbyAuthoredSites(Vector3 site, float radius, int limit)
        {
            Vector2Int center = Regions.GetCoordinateVector2Int(site);
            int examined = 0;
            for (int dx = -1; dx <= 1 && examined < limit; ++dx)
                for (int dz = -1; dz <= 1 && examined < limit; ++dz)
                {
                    int x = center.x + dx, z = center.y + dz;
                    if (x < 0 || z < 0 || x >= Regions.WORLD_SIZE
                        || z >= Regions.WORLD_SIZE)
                        continue;
                    List<ItemSpawnpoint> sites = LevelItems.spawns[x, z];
                    if (sites == null || sites.Count == 0)
                        continue;
                    int stride = Mathf.Max(1, sites.Count / 32);
                    for (int i = 0; i < sites.Count && examined < limit; i += stride)
                    {
                        ++examined;
                        ItemSpawnpoint point = sites[i];
                        if (point == null) continue;
                        Vector3 delta = point.point - site;
                        // Nearby different floors remain eligible; the motor
                        // must physically traverse a valid native stair/ladder.
                        if (Mathf.Abs(delta.y) <= 4.5f
                            && new Vector2(delta.x, delta.z).sqrMagnitude <= radius * radius)
                            candidates.Add(point);
                    }
                }
        }

        private void FinishServer(string reason)
        {
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
