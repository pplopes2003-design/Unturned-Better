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
            public float CleanupAt;
        }

        [Header("Population")]
        [SerializeField] private GameObject mobileNpcPrefab;
        [SerializeField] private bool populationEnabled = true;
        [SerializeField, Min(1)] private int maximumAlive = 12;
        [SerializeField, Min(1)] private int maximumAttemptsPerSpawn = 24;
        [SerializeField, Min(1f)] private float spawnInterval = 18f;
        [SerializeField, Min(1f)] private float deadBodyLifetime = 30f;
        [SerializeField] private MobileNPCHumanoidDisposition.Role role = MobileNPCHumanoidDisposition.Role.Hostile;

        [Header("Spawn ring around the player")]
        [SerializeField, Min(10f)] private float spawnDistance = 52f;
        [SerializeField, Min(1f)] private float spawnRegionRadius = 12f;
        [SerializeField, Min(1f)] private float groundSearchHeight = 80f;
        [SerializeField, Min(0f)] private float maximumDropDistance = 120f;
        [SerializeField, Range(0f, 60f)] private float maximumSlopeAngle = 38f;
        [SerializeField] private LayerMask groundMask = ~0;

        private readonly List<MobileNPCBrainDriver> activeDrivers = new List<MobileNPCBrainDriver>(32);
        private readonly List<GameObject> spawnedThisAttempt = new List<GameObject>(1);
        private readonly List<TrackedNPC> trackedNPCs = new List<TrackedNPC>(32);
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

        private void Update()
        {
            CleanupDeadBodies();

            if (!populationEnabled || !Provider.isServer || mobileNpcPrefab == null || Time.time < nextSpawnTime)
            {
                return;
            }

            nextSpawnTime = Time.time + Mathf.Max(1f, spawnInterval);
            MobileNPCRegistry.CopyActiveDrivers(activeDrivers);
            if (activeDrivers.Count >= Mathf.Max(1, maximumAlive))
            {
                return;
            }

            Player player = FindLivingPlayer();
            if (player == null)
            {
                return;
            }

            // Spawn outside immediate combat range. The batch service randomizes candidates
            // inside this small region and validates map bounds, slope, occupancy and spacing.
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 regionCenter = player.transform.position
                + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Mathf.Max(10f, spawnDistance);

            spawnedThisAttempt.Clear();
            MobileNPCSpawnService.TrySpawnBatchAroundServer(
                mobileNpcPrefab,
                regionCenter,
                Mathf.Max(1f, spawnRegionRadius),
                1,
                Mathf.Clamp(maximumAttemptsPerSpawn, 1, 1000),
                Mathf.Max(1f, groundSearchHeight),
                Mathf.Max(0f, maximumDropDistance),
                groundMask,
                maximumSlopeAngle,
                role,
                spawnedThisAttempt,
                out int spawnedCount,
                out string error);

            if (spawnedCount > 0)
            {
                for (int i = 0; i < spawnedThisAttempt.Count; ++i)
                {
                    GameObject instance = spawnedThisAttempt[i];
                    if (instance == null)
                    {
                        continue;
                    }

                    trackedNPCs.Add(new TrackedNPC
                    {
                        Instance = instance,
                        Vitals = instance.GetComponent<MobileNPCHumanoidVitals>(),
                        CleanupAt = float.PositiveInfinity
                    });
                }
            }
            else if (!string.IsNullOrEmpty(error))
            {
                // Skip this region until the next interval; never retry every frame.
                Debug.LogWarning("[MobileNPCPopulationDirector] No safe spawn found: " + error, this);
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

                if (float.IsPositiveInfinity(tracked.CleanupAt))
                {
                    tracked.CleanupAt = Time.time + Mathf.Max(1f, deadBodyLifetime);
                }

                if (Time.time >= tracked.CleanupAt && Provider.isServer)
                {
                    MobileNPCSpawnService.TryDespawnServer(tracked.Instance, out _);
                    trackedNPCs.RemoveAt(i);
                }
            }
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