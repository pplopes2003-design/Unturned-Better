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

        private void OnDisable()
        {
            _actors.Clear();
            _engagedGroups.Clear();
            Line2NpcEncounterPacing.Configure(2, 0f);
        }
    }
}
