using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Population scheduling adapted from Line 1
    /// MobileNPCPopulationDirector. SteamNpcPrototypeRuntime remains the
    /// sole owner of real actors, persistence, corpses and replacement tickets.
    /// This director only chooses the NEXT map-authored birth sector.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcPopulationDirector : MonoBehaviour
    {
        private const float SpawnInterval = 18f;
        private const float MinDistanceFromPlayer = 48f;
        private const float MaxRemoteSpawnDistance = 420f;
        private float _nextSpawn;
        private float _nextWarning;
        private SteamNpcPrototypeRuntime _runtime;

        private void OnEnable()
        {
            _runtime = GetComponent<SteamNpcPrototypeRuntime>();
            _nextSpawn = Time.time + SpawnInterval;
        }

        internal void Defer(float seconds)
        {
            _nextSpawn = Time.time + Mathf.Max(1f, seconds);
        }

        internal void TickNaturalPopulation()
        {
            if (_runtime == null || Time.time < _nextSpawn ||
                !Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || !Provider.hasCheats ||
                Player.player == null)
                return;
            _nextSpawn = Time.time + SpawnInterval;
            List<PlayerSpawnpoint> anchors = LevelPlayers.spawns;
            if (anchors == null || anchors.Count == 0)
            {
                Warn("no vanilla spawn anchors; natural population delayed");
                return;
            }
            Vector3 player = Player.player.transform.position;
            // Combat Total reinforcements use actual map-authored spawn
            // anchors at least 200m away; this is not a teleport toward
            // the current, unobserved player position by an existing NPC.
            // The local Player position is used solely to select a birth
            // region. Native ground and loaded-region checks must still pass.
            float minimum = _runtime.CombatTotalMode
                ? Line2NpcVehicleMissionPolicy.DistantDeploymentMeters
                : MinDistanceFromPlayer;
            // Compare only a bounded number of real map-authored anchors;
            // do not synthesize an omniscient world destination or use NPC
            // positions to determine where they should spawn.
            int offset = UnityEngine.Random.Range(0, anchors.Count);
            int limit = Mathf.Min(anchors.Count, 16);
            for (int i = 0; i < limit; ++i)
            {
                PlayerSpawnpoint anchor = anchors[(offset + i) % anchors.Count];
                if (anchor == null) continue;
                Vector3 site = anchor.point;
                Vector3 difference = site - player;
                difference.y = 0f;
                if (difference.sqrMagnitude < minimum * minimum ||
                    difference.sqrMagnitude >
                        MaxRemoteSpawnDistance * MaxRemoteSpawnDistance ||
                    !Level.checkSafeIncludingClipVolumes(site))
                    continue;
                if (_runtime.TryDirectorNaturalSpawn(site))
                    return;
            }
            Warn(_runtime.CombatTotalMode
                ? "no loaded, safe, map-authored Combat Total anchor at least 200m away"
                : "no safe native spawn anchor currently eligible");
        }

        private void Warn(string message)
        {
            if (Time.time < _nextWarning) return;
            _nextWarning = Time.time + 40f;
            ProbeLogger.Log("NPC_POPULATION_DIRECTOR_DEFERRED: " + message);
        }
    }
}
