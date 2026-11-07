using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Local, bounded A-Life routine derived from Line 1's autonomy priorities
    /// and InteriorSurvey's authored-site discovery. It does NOT know whether
    /// an item spawned at a map-authored spawnpoint, nor does it collect it.
    /// Movement is always owned by PrototypeActor + actual A* pathing.
    /// Combat and emergency retreat take priority over routine exploration.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcWorldSurvey : MonoBehaviour
    {
        internal enum Job { Patrol, ExploreAuthoredSite, GuardGroup, Recover }
        private const int MaxSiteChecks = 80;
        private const int RecentCapacity = 6;
        private const float MinimumDestinationSq = 7f * 7f;
        private const float MaximumDestinationSq = 70f * 70f;
        private readonly List<RegionCoordinate> _regions = new List<RegionCoordinate>(9);
        private readonly Vector3[] _recent = new Vector3[RecentCapacity];
        private readonly float[] _recentUntil = new float[RecentCapacity];
        private PrototypeActor _actor;
        private Line2NPCGroupMember _group;
        private Line2NpcNeeds _needs;
        private Vector3 _destination;
        private Job _job;
        private bool _hasDestination;
        private float _expiresAt;
        private float _nextSelectionAt;
        private float _nextLogAt;
        private int _recentSlot;
        private int _failedChoices;
        private float _lastGuardAt;
        internal Job CurrentJob { get { return _job; } }
        private int _verifiedItemsCollected;
        internal void RecordVerifiedWorldPickup()
        {
            if (!Provider.isServer || _actor == null || _actor.IsDead) return;
            _verifiedItemsCollected = Mathf.Min(1000000, _verifiedItemsCollected + 1);
            if (_hasDestination)
            {
                // Confirmed native item transfer completes the locally
                // observed site objective, not an artificial loot report.
                Remember(_destination);
                _hasDestination = false;
                _nextSelectionAt = Time.time + 3f;
            }
        }


        internal void Initialize(PrototypeActor actor)
        {
            _actor = actor;
            _group = GetComponent<Line2NPCGroupMember>();
            _needs = GetComponent<Line2NpcNeeds>();
            _hasDestination = false;
            _nextSelectionAt = Time.time + UnityEngine.Random.Range(2.0f, 7.0f);
            _lastGuardAt = Time.time;
        }

        private static bool Finite(Vector3 p)
        {
            return !float.IsNaN(p.x) && !float.IsNaN(p.y) &&
                !float.IsNaN(p.z) && !float.IsInfinity(p.x) &&
                !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
        }

        private void Remember(Vector3 point)
        {
            _recent[_recentSlot] = point;
            _recentUntil[_recentSlot] = Time.time + 120f;
            _recentSlot = (_recentSlot + 1) % RecentCapacity;
        }

        private bool RecentlyVisited(Vector3 point)
        {
            for (int i = 0; i < RecentCapacity; ++i)
                if (Time.time < _recentUntil[i] &&
                    (point - _recent[i]).sqrMagnitude < 8f * 8f)
                    return true;
            return false;
        }

        private void ClearGoal(bool reached)
        {
            if (_hasDestination)
            {
                Remember(_destination);
                if (reached) _failedChoices = 0;
                else _failedChoices = Mathf.Min(4, _failedChoices + 1);
            }
            _hasDestination = false;
            _nextSelectionAt = Time.time + (reached ? 1.5f : 4f);
        }

        internal bool TryGetRoutineGoalServer(out Vector3 goal)
        {
            goal = Vector3.zero;
            if (_actor == null || _actor.IsDead || !Provider.isServer ||
                Dedicator.IsDedicatedServer || !Level.isLoaded ||
                _actor.IsRecruited || _actor.IsTacticallyRetreating)
                return false;
            Vector3 current = transform.position;
            bool depleted = (_needs != null && _needs.IsUrgent) ||
                _actor.HealthFraction < 0.38f;
            if (depleted && _actor.CombatMode != "total")
            {
                _job = Job.Recover;
                // An urgent NPC first returns to its known home. It does not
                // pretend that supplies are present in a nearby building.
                goal = _actor.HomePosition;
                _hasDestination = false;
                // Hold this goal even after arriving: an exhausted survivor
                // must not immediately wander off because the original
                // Patrol fallback picked a random point.
                return true;
            }

            if (_hasDestination)
            {
                Vector3 delta = _destination - current;
                float flatSq = delta.x * delta.x + delta.z * delta.z;
                if (flatSq <= 2f * 2f && Mathf.Abs(delta.y) < 1.25f)
                    ClearGoal(true);
                else if (Time.time >= _expiresAt)
                    ClearGoal(false);
                else
                {
                    goal = _destination;
                    return true;
                }
            }
            if (Time.time < _nextSelectionAt) return false;
            _nextSelectionAt = Time.time + 9f;
            if (_actor.CombatMode != "total" && _group != null &&
                _group.Joined && Time.time - _lastGuardAt > 140f)
            {
                _lastGuardAt = Time.time;
                _job = Job.GuardGroup;
                Vector3 groupHome = _actor.HomePosition;
                PrototypeActor member;
                if (Line2NPCGroupMember.TryGetLivingMember(_group.GroupId,
                    out member) && member != null)
                    groupHome = member.HomePosition;
                if (TryChooseGroundNear(groupHome, 5f, 14f,
                    out _destination))
                    return Commit(_destination, out goal);
            }
            if (_actor.CombatMode != "total" &&
                TryChooseAuthoredSite(current, out _destination))
            {
                _job = Job.ExploreAuthoredSite;
                return Commit(_destination, out goal);
            }
            _job = Job.Patrol;
            if (TryChooseGroundNear(current, 9f,
                _actor.CombatMode == "total" ? 40f : 28f,
                out _destination))
                return Commit(_destination, out goal);
            _failedChoices = Mathf.Min(4, _failedChoices + 1);
            _nextSelectionAt = Time.time + 6f + 5f * _failedChoices;
            return false;
        }

        private bool Commit(Vector3 location, out Vector3 goal)
        {
            goal = location;
            _hasDestination = true;
            _expiresAt = Time.time + Mathf.Clamp(
                Vector3.Distance(transform.position, location) * 2.4f + 18f,
                25f, 150f);
            if (Time.time >= _nextLogAt)
            {
                _nextLogAt = Time.time + 16f;
                ProbeLogger.Log("NPC_ROUTINE: id=" + _actor.Identity +
                    " job=" + _job + " distance=" +
                    Vector3.Distance(transform.position, location).ToString("F1") +
                    " (physical route required)");
            }
            return true;
        }

        private bool TryChooseGroundNear(Vector3 anchor,
            float minimum, float maximum, out Vector3 destination)
        {
            destination = Vector3.zero;
            for (int attempt = 0; attempt < 16; ++attempt)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = UnityEngine.Random.Range(minimum, maximum);
                Vector3 sample = anchor + new Vector3(
                    Mathf.Cos(angle) * radius, 0f,
                    Mathf.Sin(angle) * radius);
                Vector3 foot;
                if (!TryValidateGround(sample, out foot) ||
                    RecentlyVisited(foot)) continue;
                destination = foot;
                return true;
            }
            return false;
        }

        private bool TryValidateGround(Vector3 estimate, out Vector3 foot)
        {
            foot = Vector3.zero;
            if (!Finite(estimate) ||
                !Level.checkSafeIncludingClipVolumes(estimate))
                return false;
            RaycastHit hit;
            if (!Physics.Raycast(estimate + Vector3.up * 2f,
                Vector3.down, out hit, 4.5f, RayMasks.GROUND2,
                QueryTriggerInteraction.Ignore) ||
                hit.normal.y < 0.72f ||
                Mathf.Abs(hit.point.y - estimate.y) > 2f)
                return false;
            foot = hit.point + Vector3.up * 0.07f;
            return Level.checkSafeIncludingClipVolumes(foot) &&
                !Physics.CheckCapsule(
                    foot + Vector3.up * 0.55f,
                    foot + Vector3.up * 1.60f,
                    0.32f, RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore);
        }

        private bool TryChooseAuthoredSite(Vector3 start, out Vector3 destination)
        {
            destination = Vector3.zero;
            if (LevelItems.spawns == null) return false;
            _regions.Clear();
            Regions.getRegionsInRadius(start, 72f, _regions);
            int examined = 0;
            float bestScore = float.NegativeInfinity;
            for (int r = 0; r < _regions.Count && examined < MaxSiteChecks; ++r)
            {
                RegionCoordinate region = _regions[r];
                if (region.x >= LevelItems.spawns.GetLength(0) ||
                    region.y >= LevelItems.spawns.GetLength(1))
                    continue;
                List<ItemSpawnpoint> hints = LevelItems.spawns[region.x, region.y];
                if (hints == null || hints.Count == 0) continue;
                int quota = Mathf.Min(20, MaxSiteChecks - examined);
                int step = Mathf.Max(1, Mathf.CeilToInt(hints.Count / (float)quota));
                for (int i = 0; i < hints.Count && examined < MaxSiteChecks; i += step)
                {
                    ++examined;
                    ItemSpawnpoint hint = hints[i];
                    if (hint == null) continue;
                    Vector3 position = hint.point;
                    Vector3 offset = position - start;
                    float flatSq = offset.x * offset.x + offset.z * offset.z;
                    if (flatSq < MinimumDestinationSq ||
                        flatSq > MaximumDestinationSq ||
                        Mathf.Abs(offset.y) > 3.5f || RecentlyVisited(position))
                        continue;
                    Vector3 foot;
                    if (!TryValidateGround(position, out foot)) continue;
                    // Native authored hints are map POIs, not evidence an
                    // item currently exists or belongs to this survivor.
                    float score = 120f - Mathf.Sqrt(flatSq) * 0.6f;
                    if (NearRealBuilding(foot)) score += 25f;
                    score += UnityEngine.Random.Range(0f, 12f);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    destination = foot;
                }
            }
            return bestScore > float.NegativeInfinity;
        }

        private bool NearRealBuilding(Vector3 site)
        {
            if (LevelObjects.objects == null) return false;
            // Small, real LevelObject search in the site's region only.
            byte x, y;
            if (!Regions.tryGetCoordinate(site, out x, out y) ||
                x >= LevelObjects.objects.GetLength(0) ||
                y >= LevelObjects.objects.GetLength(1))
                return false;
            List<LevelObject> objects = LevelObjects.objects[x, y];
            if (objects == null) return false;
            int step = Mathf.Max(1, Mathf.CeilToInt(objects.Count / 48f));
            for (int i = 0; i < objects.Count; i += step)
            {
                LevelObject obj = objects[i];
                if (obj == null || obj.asset == null || obj.transform == null ||
                    obj.asset.type != EObjectType.LARGE) continue;
                Vector3 distance = obj.transform.position - site;
                if (Mathf.Abs(distance.y) < 4f &&
                    new Vector2(distance.x, distance.z).sqrMagnitude < 18f * 18f)
                    return true;
            }
            return false;
        }
    }
}
