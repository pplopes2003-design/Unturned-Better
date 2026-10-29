////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Unturned Better mobile NPC source integration.             //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Alien: Isolation-inspired PACING layer, not a second combat brain.
    /// Unlike Alien's omniscient director, this intentionally has NO hidden
    /// player position, target Transform, objective coordinates or spawn power.
    /// It observes ONLY the verified visible-PLAYER state of existing NPC
    /// perception, then budgets optional, individually earned search hints.
    /// Native perception/BrainDriver/Tactics/Autonomy/Motor remain authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCEncounterDirector : MonoBehaviour
    {
        private struct OptionalSearchLease
        {
            public MobileNPCBrainDriver Owner;
            public float ExpiresAt;
        }

        private static MobileNPCEncounterDirector instance;
        [SerializeField, Range(0.5f, 4f)] private float sampleInterval = 1.25f;
        [SerializeField, Range(10f, 95f)] private float highPressureThreshold = 64f;
        [SerializeField, Range(5f, 35f)] private float recoverySeconds = 19f;
        private readonly List<MobileNPCBrainDriver> activeDrivers = new List<MobileNPCBrainDriver>(40);
        private readonly Dictionary<int, List<OptionalSearchLease>> leasesByGroup =
            new Dictionary<int, List<OptionalSearchLease>>(24);
        private readonly List<int> emptyGroups = new List<int>(16);
        private float nextSampleAt;
        private float lastSampleAt;
        private float pressure;
        private float respiteUntil;
        private bool respiteLogged;

        /// <summary>
        /// Early-exploration PRIOR, separate from the director's combat pacing.
        /// Knows ONLY potential world-authored spawn sectors. It doesn't ask
        /// ItemManager what actually spawned, inspect loot types, player location,
        /// enemy locations or hidden containers. The returned value is merely an
        /// uncertain BEARING, not an item location or approved navigation goal.
        /// </summary>
        public static bool TrySuggestNoviceExplorationBearingServer(
            MobileNPCBrainDriver agent, Vector3 origin, out Vector3 bearing)
        {
            bearing = Vector3.zero;
            if (!Provider.isServer || !Level.isLoaded || instance == null
                || !instance.isActiveAndEnabled || agent == null
                || !agent.isActiveAndEnabled || agent.Target != null
                || agent.CurrentGoal != MobileNPCTactics.Goal.None
                || agent.CurrentState != MobileNPCBrain.State.Idle
                || LevelItems.spawns == null)
                return false;

            MobileNPCHumanoidVitals vitals = agent.GetComponent<MobileNPCHumanoidVitals>();
            if (vitals != null && vitals.IsDead)
                return false;
            MobileNPCSupplyKnowledge supply = agent.GetComponent<MobileNPCSupplyKnowledge>();
            MobileNPCTravelMemory travel = agent.GetComponent<MobileNPCTravelMemory>();
            // Personal experience replaces startup orientation. The director
            // is never permitted to overwrite something this NPC learned.
            if (supply != null && supply.KnownSiteCount > 0
                || travel != null && travel.RouteCount > 1)
                return false;

            // Sample only immediate map regions, with a hard CPU budget.
            // This is not a global omniscient map index or item lookup.
            Vector2Int center = Regions.GetCoordinateVector2Int(origin);
            int seen = 0;
            Vector3 chosen = Vector3.zero;
            int eligible = 0;
            for (int dx = -1; dx <= 1; ++dx)
            {
                for (int dz = -1; dz <= 1; ++dz)
                {
                    int x = center.x + dx;
                    int z = center.y + dz;
                    if (x < 0 || z < 0 || x >= Regions.WORLD_SIZE
                        || z >= Regions.WORLD_SIZE)
                        continue;
                    List<ItemSpawnpoint> region = LevelItems.spawns[x, z];
                    if (region == null || region.Count == 0)
                        continue;
                    // Spread sampling across dense authored regions rather
                    // than reading every spawn point on every NPC tick.
                    int stride = Mathf.Max(1, region.Count / 16);
                    for (int i = 0; i < region.Count && seen < 144; i += stride)
                    {
                        ++seen;
                        ItemSpawnpoint point = region[i];
                        if (point == null)
                            continue;
                        Vector3 delta = point.point - origin;
                        delta.y = 0f;
                        float distanceSq = delta.sqrMagnitude;
                        if (distanceSq < 36f * 36f || distanceSq > 138f * 138f)
                            continue;
                        // Reservoir-sample a district bearing. Never reveal
                        // the actual location, table ID or availability.
                        if (Random.Range(0, ++eligible) == 0)
                            chosen = delta;
                    }
                    if (seen >= 144)
                        break;
                }
                if (seen >= 144)
                    break;
            }
            if (eligible == 0)
                return false;
            // Reasonable error simulates vague geographic familiarity.
            bearing = Quaternion.AngleAxis(Random.Range(-30f, 30f),
                Vector3.up) * chosen.normalized;
            bearing.y = 0f;
            return bearing.sqrMagnitude > 0.2f;
        }

        /// <summary>Global pressure is pacing only; never passed as a target.</summary>
        public static float ObservedPressureServer =>
            Provider.isServer && instance != null ? instance.pressure : 0f;

        public static bool AllowsOptionalEvidenceSearchServer(MobileNPCBrainDriver agent)
        {
            if (!Provider.isServer || agent == null)
                return false;
            // Director missing => keep native clue investigation intact.
            return instance == null || instance.TryLeaseOptionalEvidenceSearch(agent);
        }

        public static void ReleaseOptionalEvidenceSearchServer(MobileNPCBrainDriver agent)
        {
            if (Provider.isServer && instance != null && agent != null)
                instance.ReleaseOwnedLeases(agent);
        }

        private void Awake()
        {
            // Unity Destroy is deferred until end of frame. During a map
            // transition the old runtime director may still exist when the
            // NEW bootstrap installs this one. Retire the old component so
            // the new director, not a disabled duplicate, owns the session.
            if (instance != null && instance != this)
                instance.enabled = false;
            instance = this;
            lastSampleAt = Time.time;
            nextSampleAt = Time.time + 0.5f;
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || Time.time < nextSampleAt)
                return;

            float now = Time.time;
            float elapsed = Mathf.Clamp(now - lastSampleAt, 0f, 3f);
            lastSampleAt = now;
            nextSampleAt = now + Mathf.Max(0.5f, sampleInterval);
            MobileNPCRegistry.CopyActiveDrivers(activeDrivers);
            int genuineHostileSightings = 0;
            for (int i = 0; i < activeDrivers.Count; ++i)
            {
                MobileNPCBrainDriver driver = activeDrivers[i];
                if (driver == null || !driver.isActiveAndEnabled)
                    continue;
                MobileNPCHumanoidVitals vitals = driver.GetComponent<MobileNPCHumanoidVitals>();
                if (vitals != null && vitals.IsDead)
                    continue;
                MobileNPCHumanoidPerception perception =
                    driver.GetComponent<MobileNPCHumanoidPerception>();
                MobileNPCHumanoidDisposition disposition =
                    driver.GetComponent<MobileNPCHumanoidDisposition>();
                Player actuallyVisible = perception != null ? perception.VisiblePlayer : null;
                // Do NOT infer a target from mere proximity to the live player.
                if (actuallyVisible != null && disposition != null
                    && disposition.CanEngagePlayer(actuallyVisible))
                    ++genuineHostileSightings;
            }

            if (genuineHostileSightings > 0)
                pressure = Mathf.Min(100f, pressure
                    + elapsed * Mathf.Min(genuineHostileSightings, 4) * 3.8f);
            else
                pressure = Mathf.Max(0f, pressure - elapsed * 1.6f);

            // Do not arbitrarily stop a confirmed pursuit. The recovery window
            // gates only BONUS, evidence-based stalking/hypothesis expansions.
            // Footsteps, gunshots, direct sight and legitimate group messages
            // still travel through their existing server sensors.
            if (genuineHostileSightings == 0 && pressure >= highPressureThreshold
                && now >= respiteUntil)
            {
                respiteUntil = now + Mathf.Max(5f, recoverySeconds);
                pressure = highPressureThreshold * 0.5f;
                respiteLogged = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MobileNPCEncounterDirector] Recovery: suppress speculative searches for "
                    + Mathf.Max(5f, recoverySeconds).ToString("F0")
                    + "s; no NPC relocated and no position disclosed.", this);
#endif
            }
            if (respiteLogged && now >= respiteUntil)
            {
                respiteLogged = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MobileNPCEncounterDirector] Recovery ended; optional sensed searches eligible.", this);
#endif
            }
            RemoveStaleLeases(now);
        }

        private bool TryLeaseOptionalEvidenceSearch(MobileNPCBrainDriver agent)
        {
            if (!enabled || !gameObject.activeInHierarchy)
                return true;
            if (!agent.isActiveAndEnabled || agent.CurrentState != MobileNPCBrain.State.Investigating)
                return false;
            MobileNPCHumanoidVitals vitals = agent.GetComponent<MobileNPCHumanoidVitals>();
            if (vitals != null && vitals.IsDead)
                return false;
            // Only intrinsically hostile NPCs receive extra search
            // permission. Friendly/neutral NPCs still hear, investigate
            // and defend using their ORIGINAL sensory/tactical code;
            // the pacing system never nudges them to stalk a civilian.
            MobileNPCHumanoidDisposition disposition =
                agent.GetComponent<MobileNPCHumanoidDisposition>();
            if (disposition == null
                || disposition.CurrentRole != MobileNPCHumanoidDisposition.Role.Hostile)
                return false;
            if (Time.time < respiteUntil)
                return false;

            MobileNPCGroupMember member = agent.GetComponent<MobileNPCGroupMember>();
            // The director supplies only OPTIONAL investigation opportunities.
            // Never steal an actor currently serving as a covering or advancing
            // squad member, nor contradict an audible hold/withdraw order.
            // Firsthand sightings, real sound investigation, fire and retreat
            // remain independent of this speculative-search permission.
            if (member != null && (member.CurrentSquadRole != MobileNPCGroupMember.SquadRole.None
                || member.CurrentSquadDirective == MobileNPCGroupMember.SquadDirective.Hold
                || member.CurrentSquadDirective == MobileNPCGroupMember.SquadDirective.Withdraw))
                return false;
            int key = member != null && member.GroupId > 0
                ? member.GroupId : -agent.GetInstanceID();
            if (!leasesByGroup.TryGetValue(key, out List<OptionalSearchLease> list))
            {
                list = new List<OptionalSearchLease>(2);
                leasesByGroup.Add(key, list);
            }

            RemoveStaleEntries(list, Time.time);
            for (int i = 0; i < list.Count; ++i)
            {
                if (list[i].Owner != agent)
                    continue;
                list[i] = new OptionalSearchLease
                {
                    Owner = agent,
                    ExpiresAt = Time.time + 8f
                };
                return true;
            }

            // Higher pressure means fewer speculative searches by the same
            // squad. The others retain normal senses, tactical defense, loot
            // plans and A-Life jobs; only BONUS pursuit hints are budgeted.
            int maxOptionalSearchers = pressure >= 32f ? 1 : 2;
            if (list.Count >= maxOptionalSearchers)
                return false;

            list.Add(new OptionalSearchLease
            {
                Owner = agent,
                ExpiresAt = Time.time + 8f
            });
            return true;
        }

        private void RemoveStaleLeases(float now)
        {
            emptyGroups.Clear();
            foreach (KeyValuePair<int, List<OptionalSearchLease>> group in leasesByGroup)
            {
                RemoveStaleEntries(group.Value, now);
                if (group.Value.Count == 0)
                    emptyGroups.Add(group.Key);
            }
            for (int i = 0; i < emptyGroups.Count; ++i)
                leasesByGroup.Remove(emptyGroups[i]);
        }

        private static void RemoveStaleEntries(List<OptionalSearchLease> list, float now)
        {
            for (int i = list.Count - 1; i >= 0; --i)
            {
                MobileNPCBrainDriver owner = list[i].Owner;
                if (owner == null || !owner.isActiveAndEnabled
                    || now >= list[i].ExpiresAt
                    || owner.CurrentState != MobileNPCBrain.State.Investigating)
                {
                    list.RemoveAt(i);
                    continue;
                }
                MobileNPCHumanoidVitals vitals = owner.GetComponent<MobileNPCHumanoidVitals>();
                MobileNPCGroupMember member = owner.GetComponent<MobileNPCGroupMember>();
                if (vitals != null && vitals.IsDead
                    || member != null && (member.CurrentSquadRole != MobileNPCGroupMember.SquadRole.None
                        || member.CurrentSquadDirective == MobileNPCGroupMember.SquadDirective.Hold
                        || member.CurrentSquadDirective == MobileNPCGroupMember.SquadDirective.Withdraw))
                    list.RemoveAt(i);
            }
        }

        private void ReleaseOwnedLeases(MobileNPCBrainDriver agent)
        {
            foreach (KeyValuePair<int, List<OptionalSearchLease>> group in leasesByGroup)
                for (int i = group.Value.Count - 1; i >= 0; --i)
                    if (group.Value[i].Owner == agent)
                        group.Value.RemoveAt(i);
        }

        private void OnDisable()
        {
            if (instance == this)
                instance = null;
            leasesByGroup.Clear();
            activeDrivers.Clear();
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
