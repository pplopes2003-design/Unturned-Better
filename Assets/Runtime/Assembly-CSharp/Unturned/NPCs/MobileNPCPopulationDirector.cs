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
    /// It only creates local server instances; it does not implement persistence or replication.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCPopulationDirector : MonoBehaviour
    {
        [Header("Population")]
        [SerializeField] private GameObject mobileNpcPrefab;
        [SerializeField] private bool populationEnabled = true;
        [SerializeField, Min(1)] private int maximumAlive = 12;
        [SerializeField, Min(1)] private int maximumAttemptsPerSpawn = 24;
        [SerializeField, Min(1f)] private float spawnInterval = 18f;
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
        private float nextSpawnTime;

        private void OnEnable()
        {
            nextSpawnTime = Time.time + Mathf.Max(1f, spawnInterval);
        }

        private void Update()
        {
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

            Player player = FindNearestLivingPlayer();
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

            // A failed region is skipped until the next interval. Avoid per-frame retries
            // and noisy logs on maps whose collider mask or terrain is not configured yet.
            if (spawnedCount == 0 && !string.IsNullOrEmpty(error))
            {
                Debug.LogWarning("[MobileNPCPopulationDirector] No safe spawn found: " + error, this);
            }
        }

        private static Player FindNearestLivingPlayer()
        {
            Player[] players = FindObjectsOfType<Player>();
            Player best = null;
            float bestDistanceSquared = float.PositiveInfinity;
            Vector3 origin = Vector3.zero;
            bool hasOrigin = false;

            for (int i = 0; i < players.Length; ++i)
            {
                Player candidate = players[i];
                if (candidate == null || candidate.life == null || candidate.life.isDead
                    || !candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!hasOrigin)
                {
                    origin = candidate.transform.position;
                    best = candidate;
                    hasOrigin = true;
                    continue;
                }

                float distanceSquared = (candidate.transform.position - origin).sqrMagnitude;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    best = candidate;
                }
            }

            return best;
        }
    }
}