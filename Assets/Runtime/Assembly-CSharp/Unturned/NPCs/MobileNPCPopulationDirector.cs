////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Optional, map-agnostic population controller for a configured mobile humanoid prefab.
    /// Add this component to a runtime bootstrap object and assign a dedicated mobile prefab.
    /// It creates local server instances only; persistence and replication are separate systems.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCPopulationDirector : MonoBehaviour
    {
        private sealed class TrackedNPC
        {
            public GameObject Instance;
            public MobileNPCHumanoidVitals Vitals;
            public int GroupId;
            public float CleanupAt;
            public bool DeathObserved;
        }

        private sealed class PendingRespawn
        {
            public float RespawnAt;
            public int FormerGroupId;
        }

        [Header("Population")]
        [SerializeField] private GameObject mobileNpcPrefab;
        [SerializeField] private bool populationEnabled = true;
        [SerializeField, Min(1)] private int maximumAttemptsPerSpawn = 24;
        [SerializeField, Min(1f)] private float spawnInterval = 18f;
        [SerializeField, Min(1f)] private float deadBodyLifetime = 30f;
        [SerializeField, Min(1f)] private float respawnDelay = 60f;

        [Header("Spawn regions around vanilla player-spawn anchors")]
        [SerializeField, Min(10f)] private float spawnDistance = 52f;
        [SerializeField, Min(1f)] private float spawnRegionRadius = 12f;
        [SerializeField, Min(1f)] private float groundSearchHeight = 80f;
        [SerializeField, Min(0f)] private float maximumDropDistance = 120f;
        [SerializeField, Range(0f, 60f)] private float maximumSlopeAngle = 38f;
        [SerializeField] private LayerMask groundMask = ~0;

        private readonly List<MobileNPCBrainDriver> activeDrivers = new List<MobileNPCBrainDriver>(32);
        private readonly List<GameObject> spawnedThisAttempt = new List<GameObject>(1);
        private readonly List<TrackedNPC> trackedNPCs = new List<TrackedNPC>(32);
        private readonly List<PendingRespawn> pendingRespawns = new List<PendingRespawn>(32);
        private float nextSpawnTime;

        /// <summary>Configures a runtime-created director without relying on Inspector serialization.</summary>
        public void Configure(GameObject prefab)
        {
            mobileNpcPrefab = prefab;
            populationEnabled = prefab != null;
            nextSpawnTime = Time.time + Mathf.Max(1f, spawnInterval);
        }

        private void OnEnable()
        {
            nextSpawnTime = Time.time + Mathf.Max(1f, spawnInterval);
        }

        private void OnApplicationQuit()
        {
            // The persistence helper is static; explicitly connect its final flush to
            // Unity's application lifecycle so the last changes do not wait for autosave.
            MobileNPCPersistence.SaveOnApplicationQuitServer();
        }

        private void Update()
        {
            // Development convenience: F8 requests one nearby NPC for a focused smoke test.
            // Manual test spawns bypass the automatic population cap, while normal world
            // population continues to obey it. This remains server-side only.
            if (Provider.isServer && Provider.hasCheats && Input.GetKeyDown(KeyCode.F8))
            {
                TrySpawnNPCNearPlayer(MobileNPCSettings.DefaultRole, true);
            }

            // Only the server owns corpses, respawn reservations, and persistence.
            if (!Provider.isServer)
                return;

            int populationLimit = Mathf.Clamp(MobileNPCSettings.MaximumAlive, 1, 40);
            MobileNPCPersistence.TickServer(mobileNpcPrefab, populationLimit);
            MobileNPCAirdropContest.TickServer();

            // Track restored/externally spawned actors before processing deaths, so a
            // newly discovered corpse receives its replacement reservation this frame.
            MobileNPCRegistry.CopyActiveDrivers(activeDrivers);
            TrackUntrackedActiveNPCs();
            CleanupDeadBodies();

            if (!populationEnabled || !MobileNPCSettings.Enabled || !Provider.isServer || mobileNpcPrefab == null || Time.time < nextSpawnTime)
            {
                return;
            }

            nextSpawnTime = Time.time + Mathf.Max(1f, spawnInterval);
            MobileNPCRegistry.CopyActiveDrivers(activeDrivers);
            if (MobileNPCRegistry.CountLivingDrivers() >= populationLimit)
            {
                return;
            }

            // Initial population can fill normally. Once deaths create pending replacements,
            // each replacement must wait for its own cooldown before a new individual appears.
            if (pendingRespawns.Count > 0 && Time.time < pendingRespawns[0].RespawnAt)
            {
                return;
            }

            Player player = FindLivingPlayer();
            if (player == null)
            {
                return;
            }

            // Use the map-authored vanilla player spawn points as portable anchors. Do not
            // silently invent coordinates on maps that have no usable spawn definitions.
            List<PlayerSpawnpoint> spawnAnchors = LevelPlayers.spawns;
            if (spawnAnchors == null || spawnAnchors.Count == 0)
            {
                Debug.LogWarning("[MobileNPCPopulationDirector] The loaded map has no vanilla player spawn points; population will retry after the next spawn interval.", this);
                return;
            }

            // A due replacement is a NEW person, but may join an existing group
            // if its members are still alive. Choose compatible role/faction before
            // spawning; a random role would make TryJoinExistingGroupServer fail.
            PendingRespawn dueReplacement = pendingRespawns.Count > 0
                && Time.time >= pendingRespawns[0].RespawnAt ? pendingRespawns[0] : null;
            MobileNPCHumanoidDisposition.Role groupRole;
            MobileNPCFaction.Id groupFaction;
            bool eligibleGroup = dueReplacement != null
                && MobileNPCGroupMember.TryGetSurvivingGroupIdentityServer(
                    dueReplacement.FormerGroupId, out groupRole, out groupFaction);
            // Explicit defaults keep the values defined even when short-circuit
            // evaluation skips the out-parameter call.
            if (!eligibleGroup)
            {
                groupRole = MobileNPCHumanoidDisposition.Role.Neutral;
                groupFaction = MobileNPCFaction.Id.Unaffiliated;
            }
            MobileNPCHumanoidDisposition.Role selectedRole = eligibleGroup
                ? groupRole : MobileNPCSettings.SelectPopulationRole();

            // Try a bounded number of anchors/sectors per interval. Anchors are only regional
            // hints: every actual candidate still passes ground, slope, occupancy, bounds,
            // player visibility and NPC spacing checks in the spawn service.
            int spawnedCount = 0;
            string error = null;
            int sectorAttempts = Mathf.Clamp(Mathf.Min(4, maximumAttemptsPerSpawn), 1, 4);
            for (int sector = 0; sector < sectorAttempts && spawnedCount == 0; ++sector)
            {
                PlayerSpawnpoint anchor = spawnAnchors[Random.Range(0, spawnAnchors.Count)];
                if (anchor == null)
                {
                    continue;
                }

                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Random.Range(
                    Mathf.Max(10f, spawnDistance * 0.85f),
                    Mathf.Max(12f, spawnDistance * 1.45f));
                Vector3 regionCenter = anchor.point
                    + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

                spawnedThisAttempt.Clear();
                MobileNPCSpawnService.TrySpawnBatchAroundServer(
                    mobileNpcPrefab,
                    regionCenter,
                    Mathf.Max(1f, spawnRegionRadius),
                    1,
                    Mathf.Clamp(maximumAttemptsPerSpawn / sectorAttempts, 1, 1000),
                    Mathf.Max(1f, groundSearchHeight),
                    Mathf.Max(0f, maximumDropDistance),
                    groundMask,
                    maximumSlopeAngle,
                    selectedRole,
                    spawnedThisAttempt,
                    player.transform,
                    out spawnedCount,
                    out error);
            }

            if (spawnedCount > 0)
            {
                if (dueReplacement != null && pendingRespawns.Count > 0
                    && object.ReferenceEquals(pendingRespawns[0], dueReplacement))
                {
                    // Consume the reservation only after successful world placement.
                    pendingRespawns.RemoveAt(0);
                }

                for (int i = 0; i < spawnedThisAttempt.Count; ++i)
                {
                    GameObject instance = spawnedThisAttempt[i];
                    if (instance == null)
                    {
                        continue;
                    }

                    MobileNPCGroupMember groupMember = instance.GetComponent<MobileNPCGroupMember>();
                    if (eligibleGroup && dueReplacement != null && groupMember != null)
                    {
                        // Set the new person's faction to match the surviving group
                        // BEFORE membership checks. This does not copy the previous
                        // NPC's memory, inventory, health or individual identity.
                        MobileNPCFaction faction = instance.GetComponent<MobileNPCFaction>();
                        if (faction != null && faction.CurrentFaction != groupFaction)
                            faction.SetFactionServer(groupFaction);
                        // Recheck existence/capacity and identity atomically at join.
                        // A group whose last member died during placement stays dissolved.
                        groupMember.TryJoinExistingGroupServer(dueReplacement.FormerGroupId);
                    }

                    // Refresh group provenance after optional reattachment.
                    RegisterSpawnedNPC(instance);
                }
            }
            else if (!string.IsNullOrEmpty(error))
            {
                // Retry only after the normal interval; never run spawn searches every frame.
                Debug.LogWarning("[MobileNPCPopulationDirector] No safe spawn found after trying multiple sectors: " + error, this);
            }
        }

        /// <summary>
        /// Register each successful spawn immediately, before a same-frame fatal hit
        /// can disable its brain and remove it from MobileNPCRegistry.
        /// This method is idempotent because multiple spawn paths can report an actor.
        /// </summary>
        public void RegisterSpawnedNPC(GameObject instance)
        {
            if (!Provider.isServer || instance == null)
                return;

            MobileNPCGroupMember groupMember = instance.GetComponent<MobileNPCGroupMember>();
            for (int i = 0; i < trackedNPCs.Count; ++i)
            {
                if (trackedNPCs[i].Instance == instance)
                {
                    // A fresh replacement can join its predecessor's group after
                    // the spawn service first registers it. Refresh while alive.
                    if (!trackedNPCs[i].DeathObserved && trackedNPCs[i].Vitals != null
                        && !trackedNPCs[i].Vitals.IsDead && groupMember != null)
                        trackedNPCs[i].GroupId = groupMember.GroupId;
                    return;
                }
            }
            trackedNPCs.Add(new TrackedNPC
            {
                Instance = instance,
                Vitals = instance.GetComponent<MobileNPCHumanoidVitals>(),
                GroupId = groupMember != null ? groupMember.GroupId : 0,
                CleanupAt = float.PositiveInfinity
            });
        }

        private void TrackUntrackedActiveNPCs()
        {
            for (int i = 0; i < activeDrivers.Count; ++i)
            {
                MobileNPCBrainDriver driver = activeDrivers[i];
                if (driver != null)
                    RegisterSpawnedNPC(driver.gameObject);
            }
        }

        private void CleanupDeadBodies()
        {
            for (int i = trackedNPCs.Count - 1; i >= 0; --i)
            {
                TrackedNPC tracked = trackedNPCs[i];
                if (tracked.Instance == null)
                {
                    trackedNPCs.RemoveAt(i);
                    continue;
                }

                if (tracked.Vitals == null || !tracked.Vitals.IsDead)
                {
                    continue;
                }

                if (!tracked.DeathObserved)
                {
                    tracked.DeathObserved = true;
                    // Each death gets its own replacement time. The corpse lifetime
                    // remains independent from the population cooldown.
                    float respawnAt = Time.time + Mathf.Max(1f, respawnDelay);
                    PendingRespawn pending = new PendingRespawn
                    {
                        RespawnAt = respawnAt,
                        FormerGroupId = tracked.GroupId
                    };
                    int insertIndex = pendingRespawns.Count;
                    while (insertIndex > 0 && pendingRespawns[insertIndex - 1].RespawnAt > respawnAt)
                    {
                        insertIndex--;
                    }
                    pendingRespawns.Insert(insertIndex, pending);
                    Debug.Log("[MobileNPCPopulationDirector] Death queued: npc=" + tracked.Instance.name
                        + ", group=" + tracked.GroupId + ", corpseCleanupAt=" + (Time.time + Mathf.Max(1f, deadBodyLifetime)).ToString("F1")
                        + ", replacementAt=" + respawnAt.ToString("F1") + ".", this);
                    if (tracked.GroupId > 0)
                    {
                        Debug.Log("[MobileNPCPopulationDirector] Mobile NPC from group " + tracked.GroupId
                            + " died; replacement is delayed and group membership has been retired.", this);
                    }
                }

                if (float.IsPositiveInfinity(tracked.CleanupAt))
                {
                    tracked.CleanupAt = Time.time + Mathf.Max(1f, deadBodyLifetime);
                }

                if (Time.time >= tracked.CleanupAt)
                {
                    // Do not silently forget a corpse if despawning failed.
                    // Retaining it allows a future update to retry cleanup.
                    if (MobileNPCSpawnService.TryDespawnServer(tracked.Instance, out string despawnError))
                    {
                        Debug.Log("[MobileNPCPopulationDirector] Corpse cleanup completed.", this);
                        trackedNPCs.RemoveAt(i);
                    }
                    else
                    {
                        Debug.LogError("[MobileNPCPopulationDirector] Corpse cleanup failed for "
                            + tracked.Instance.name + ": " + despawnError, this);
                        tracked.CleanupAt = Time.time + 5f;
                    }
                }
            }
        }

        /// <summary>Creates one nearby NPC for a local single-player chat command or developer hotkey.</summary>
        public bool TrySpawnNPCNearPlayer(MobileNPCHumanoidDisposition.Role requestedRole)
        {
            return TrySpawnNPCNearPlayer(requestedRole, false);
        }

        /// <summary>
        /// Creates one nearby NPC. Authorized manual test calls may bypass the automatic
        /// population cap; this does not change the cap used by the population director.
        /// The caller is responsible for gating bypass requests behind the game's cheats option.
        /// </summary>
        public bool TrySpawnNPCNearPlayer(MobileNPCHumanoidDisposition.Role requestedRole, bool bypassPopulationLimit)
        {
            if (!MobileNPCSettings.Enabled || mobileNpcPrefab == null)
            {
                Debug.Log("[MobileNPCPopulationDirector] Nearby test spawn skipped: mobile NPC population is disabled or prefab is missing.", this);
                return false;
            }

            int populationLimit = Mathf.Clamp(MobileNPCSettings.MaximumAlive, 1, 40);
            if (!bypassPopulationLimit && MobileNPCRegistry.CountLivingDrivers() >= populationLimit)
            {
                Debug.Log("[MobileNPCPopulationDirector] Nearby test spawn skipped: configured population cap is already reached (" + populationLimit + ").", this);
                return false;
            }

            Player player = FindLivingPlayer();
            if (player == null)
            {
                Debug.Log("[MobileNPCPopulationDirector] Nearby test spawn skipped: no living player was found.", this);
                return false;
            }

            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            // Try several nearby ground positions in front of the player. This bypasses
            // the normal distant spawn-anchor selection but still uses safe-ground checks.
            for (int attempt = 0; attempt < 8; ++attempt)
            {
                float angle = (attempt * 45f) * Mathf.Deg2Rad;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * forward;
                Vector3 candidate = player.transform.position + direction * 7f;
                if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate, 12f, 20f,
                    ~0, maximumSlopeAngle, out Vector3 groundPosition, out string groundError))
                {
                    continue;
                }

                Vector3 offset = groundPosition - player.transform.position;
                offset.y = 0f;
                if (offset.sqrMagnitude < 16f)
                {
                    continue;
                }

                Quaternion rotation = Quaternion.LookRotation(player.transform.position - groundPosition, Vector3.up);
                if (MobileNPCSpawnService.TrySpawnServer(mobileNpcPrefab, groundPosition, rotation,
                    requestedRole, out GameObject instance, out string spawnError))
                {
                    MobileNPCGroupMember groupMember = instance.GetComponent<MobileNPCGroupMember>();
                    RegisterSpawnedNPC(instance);
                    Debug.Log("[MobileNPCPopulationDirector] Test NPC (" + requestedRole + ") spawned near player at " + groundPosition + ".", instance);
                    return true;
                }

                if (attempt == 7)
                {
                    Debug.LogWarning("[MobileNPCPopulationDirector] Nearby test spawn failed. Last spawn error: " + spawnError, this);
                }
            }

            Debug.LogWarning("[MobileNPCPopulationDirector] Nearby test spawn could not find safe ground within about 7 metres of the player. Try an open, flat area.", this);
            return false;
        }

        private static Player FindLivingPlayer()
        {
            // Single-player is the current target. Returning the first living player avoids
            // allocations or distance bookkeeping beyond the one scene query.
            Player[] players = FindObjectsOfType<Player>();
            for (int i = 0; i < players.Length; ++i)
            {
                Player candidate = players[i];
                if (candidate != null && candidate.life != null && !candidate.life.isDead
                    && candidate.gameObject.activeInHierarchy)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}