using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Steam adaptation of Line 1 MobileNPCEncounterDirector. Computes
    /// pressure ONLY from already verified firsthand hostile sight of the
    /// local player. Does not create targets, teleport, spawn, or command
    /// damage. Regulates optional evidence searches, not real combat.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcEncounterDirector : MonoBehaviour
    {
        private const float SampleInterval = 1.25f;
        private const float HighPressure = 64f;
        private const float RecoverySeconds = 19f;
        private readonly List<PrototypeActor> _actors =
            new List<PrototypeActor>(32);
        private readonly HashSet<int> _engagedGroups = new HashSet<int>();
        private SteamNpcPrototypeRuntime _runtime;
        private string _map;
        private float _nextSample;
        private float _lastSample;
        private float _respiteUntil;
        private float _pressure;
        private bool _recovering;
        internal float Pressure { get { return _pressure; } }

        private void OnEnable()
        {
            _runtime = GetComponent<SteamNpcPrototypeRuntime>();
            ResetForMap(null);
        }

        private void ResetForMap(string key)
        {
            _map = key;
            _pressure = 0f;
            _respiteUntil = 0f;
            _recovering = false;
            _lastSample = Time.time;
            _nextSample = Time.time + 0.5f;
            _engagedGroups.Clear();
            _actors.Clear();
            Line2NpcEncounterPacing.Configure(2, 0f);
        }

        private void Update()
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || !Provider.hasCheats ||
                Player.player == null || _runtime == null)
            {
                if (_pressure != 0f || _recovering)
                    ResetForMap(null);
                return;
            }
            string key = Line2NpcPersistence.CurrentMapKey;
            if (!string.Equals(_map, key, StringComparison.Ordinal))
                ResetForMap(key);
            if (Time.time < _nextSample) return;

            float now = Time.time;
            float elapsed = Mathf.Clamp(now - _lastSample, 0f, 3f);
            _lastSample = now;
            _nextSample = now + SampleInterval;
            _runtime.CopyLiveActors(_actors, 32);
            _engagedGroups.Clear();
            int sightings = 0;
            for (int i = 0; i < _actors.Count; i++)
            {
                PrototypeActor actor = _actors[i];
                if (actor == null || actor.IsDead ||
                    !actor.isActiveAndEnabled ||
                    actor.Disposition != "hostile" ||
                    actor.CombatMode == "passive" ||
                    !actor.PlayerVisible)
                    continue;
                Line2NPCGroupMember member =
                    actor.GetComponent<Line2NPCGroupMember>();
                int keyId = member != null && member.GroupId > 0
                    ? member.GroupId : -actor.GetInstanceID();
                if (_engagedGroups.Add(keyId))
                    sightings++;
            }
            if (sightings > 0)
                _pressure = Mathf.Min(100f, _pressure +
                    elapsed * Mathf.Min(4, sightings) * 3.8f);
            else
                _pressure = Mathf.Max(0f, _pressure - elapsed * 1.6f);

            if (sightings == 0 && _pressure >= HighPressure &&
                now >= _respiteUntil)
            {
                _respiteUntil = now + RecoverySeconds;
                _pressure = HighPressure * 0.5f;
                _recovering = true;
                ProbeLogger.Log("NPC_ENCOUNTER_RECOVERY: optional searches held " +
                    RecoverySeconds + "s; no real pursuit suspended");
            }
            if (_recovering && now >= _respiteUntil)
            {
                _recovering = false;
                ProbeLogger.Log("NPC_ENCOUNTER_RECOVERY_ENDED");
            }
            Line2NpcEncounterPacing.Configure(
                _pressure >= 32f ? 1 : 2, _respiteUntil);
        }

        /// <summary>
        /// Line 1 novice geographic prior: selects only an approximate
        /// BEARING toward map-authored loot sectors. No information about
        /// whether anything actually spawned, no target identity, and
        /// no approved route. The individual NPC must validate ground
        /// and ask native ASPFP to walk there.
        /// </summary>
        internal static bool TrySuggestNoviceBearing(PrototypeActor actor,
            Vector3 origin, out Vector3 bearing)
        {
            bearing = Vector3.zero;
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || actor == null || actor.IsDead ||
                actor.IsRecruited || actor.CombatMode == "total" ||
                actor.PlayerVisible || LevelItems.spawns == null)
                return false;
            Line2MobileNPCAdaptiveMemory experience =
                actor.GetComponent<Line2MobileNPCAdaptiveMemory>();
            Line2MobileNPCTravelMemory traveled =
                actor.GetComponent<Line2MobileNPCTravelMemory>();
            if (experience != null && experience.RecordCount > 0 ||
                traveled != null && traveled.RouteCount > 1)
                return false;
            var regions = new List<RegionCoordinate>(9);
            Regions.getRegionsInRadius(origin, 138f, regions);
            int sampled = 0;
            int eligible = 0;
            Vector3 chosen = Vector3.zero;
            for (int r = 0; r < regions.Count && sampled < 144; r++)
            {
                RegionCoordinate region = regions[r];
                if (region.x >= LevelItems.spawns.GetLength(0) ||
                    region.y >= LevelItems.spawns.GetLength(1))
                    continue;
                List<ItemSpawnpoint> points =
                    LevelItems.spawns[region.x, region.y];
                if (points == null || points.Count == 0) continue;
                int stride = Mathf.Max(1, points.Count / 16);
                for (int i = 0; i < points.Count && sampled < 144; i += stride)
                {
                    sampled++;
                    ItemSpawnpoint spawn = points[i];
                    if (spawn == null) continue;
                    Vector3 offset = spawn.point - origin;
                    offset.y = 0f;
                    if (offset.sqrMagnitude < 36f * 36f ||
                        offset.sqrMagnitude > 138f * 138f)
                        continue;
                    if (UnityEngine.Random.Range(0, ++eligible) == 0)
                        chosen = offset;
                }
            }
            if (eligible == 0) return false;
            bearing = Quaternion.AngleAxis(
                UnityEngine.Random.Range(-30f, 30f), Vector3.up) *
                chosen.normalized;
            bearing.y = 0f;
            return bearing.sqrMagnitude > 0.2f;
        }

        private void OnDisable()
        {
            _actors.Clear();
            _engagedGroups.Clear();
            Line2NpcEncounterPacing.Configure(2, 0f);
        }
    }
}
