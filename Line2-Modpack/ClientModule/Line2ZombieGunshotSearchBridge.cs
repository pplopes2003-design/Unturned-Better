using System;
using System.Collections.Generic;
using System.Reflection;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Local Steam adapter for real gunshot-driven investigation by EXISTING
    /// vanilla zombies. Does not replace Zombie AI or A*: uses only native
    /// Zombie.alert(Vector3,false), which owns target state and movement.
    ///
    /// Native POINT hunts cannot be redirected to a farther point once
    /// hunting. We track only personally alerted zombies and, after a real
    /// physical stall, try at most two closer, ground-verified corridor
    /// waypoints. No wall phasing, teleporting, new zombie population,
    /// shooter identity, or modification of actual PLAYER pursuit.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2ZombieGunshotSearchBridge : MonoBehaviour
    {
        private const int MaxPerDischarge = 14;
        private const int MaxInspected = 96;
        private const int MaxTracked = 72;
        private const float TickSeconds = 0.9f;
        private const float DischargeThrottle = 0.28f;
        private const int HitBuffer = 20;

        private struct Search
        {
            internal Zombie actor;
            internal Vector3 currentGoal;
            internal Vector3 lastObserved;
            internal float lastProgressAt;
            internal int revisions;
        }

        private static readonly FieldInfo NativePlayer =
            typeof(Zombie).GetField("player",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Dictionary<int, Search> _active =
            new Dictionary<int, Search>(MaxTracked);
        private readonly Dictionary<int, float> _lastSoundAt =
            new Dictionary<int, float>(MaxTracked);
        private readonly List<int> _keys = new List<int>(MaxTracked);
        private readonly RaycastHit[] _hits = new RaycastHit[HitBuffer];
        private float _nextUpdateAt;
        private float _lastDischargeAt = -1000f;
        private float _nextLogAt;
        private int _cursor;

        internal static bool Enabled { get; set; }

        private static bool Authorized()
        {
            return Enabled && Provider.isServer &&
                !Dedicator.IsDedicatedServer && Provider.hasCheats &&
                Level.isLoaded && Player.player != null &&
                (Provider.clients == null || Provider.clients.Count <= 1);
        }

        private void OnEnable()
        {
            UseableGun.onBulletSpawned += OnNativeBullet;
        }

        private void OnDisable()
        {
            UseableGun.onBulletSpawned -= OnNativeBullet;
            _active.Clear();
            _lastSoundAt.Clear();
            _keys.Clear();
        }

        private static bool HasNativePlayer(Zombie zombie)
        {
            if (NativePlayer == null ||
                NativePlayer.FieldType != typeof(Player))
                return true; // do not steal a player target if SDK changed
            try { return NativePlayer.GetValue(zombie) != null; }
            catch { return true; }
        }

        private static bool TrySupportedPoint(Vector3 estimate,
            Vector3 reference, out Vector3 point)
        {
            point = Vector3.zero;
            if (!Level.checkSafeIncludingClipVolumes(estimate))
                return false;
            RaycastHit ground;
            if (!Physics.Raycast(estimate + Vector3.up * 1.5f,
                Vector3.down, out ground, 5.5f,
                RayMasks.GROUND2, QueryTriggerInteraction.Ignore) ||
                ground.collider == null || ground.normal.y < 0.72f)
                return false;
            point = ground.point + Vector3.up * 0.07f;
            return Mathf.Abs(point.y - reference.y) <=
                    Line2ZombieSoundPolicy.MaxFloorGap &&
                Level.checkSafeIncludingClipVolumes(point) &&
                LevelNavigation.checkNavigation(point);
        }

        private void OnNativeBullet(UseableGun gun, BulletInfo bullet)
        {
            if (!Authorized() || gun == null || bullet == null ||
                bullet.pellet != 0 || NativePlayer == null)
                return;
            float now = Time.time;
            if (now - _lastDischargeAt < DischargeThrottle) return;
            Vector3 origin = bullet.origin;
            if (float.IsNaN(origin.x) || float.IsInfinity(origin.x) ||
                float.IsNaN(origin.y) || float.IsInfinity(origin.y) ||
                float.IsNaN(origin.z) || float.IsInfinity(origin.z))
                return;
            _lastDischargeAt = now;

            List<Zombie> all = ZombieManager.AllZombies;
            if (all == null || all.Count == 0) return;
            int inspected = Mathf.Min(MaxInspected, all.Count);
            int assigned = 0;
            for (int i = 0; i < inspected &&
                assigned < MaxPerDischarge; i++)
            {
                Zombie zombie = all[(_cursor + i) % all.Count];
                if (zombie == null || zombie.isDead ||
                    !zombie.gameObject.activeInHierarchy ||
                    zombie.isHunting || HasNativePlayer(zombie))
                    continue;
                Vector3 current = zombie.transform.position;
                if ((current - origin).sqrMagnitude >
                    Line2ZombieSoundPolicy.MaxHearingMeters *
                    Line2ZombieSoundPolicy.MaxHearingMeters)
                    continue;
                Vector3 point;
                if (!TrySupportedPoint(origin, current, out point))
                    continue;
                int id = zombie.GetInstanceID();
                float lastAt;
                if (!_lastSoundAt.TryGetValue(id, out lastAt))
                    lastAt = -1000f;
                if (!Line2ZombieSoundPolicy.MayInvestigate(
                    Authorized(), true, true, zombie.isHunting,
                    false, true, current, point, now, lastAt))
                    continue;

                zombie.alert(point, false); // vanilla point target + Seeker
                if (!zombie.isHunting) continue;
                _lastSoundAt[id] = now;
                if (_active.Count < MaxTracked)
                    _active[id] = new Search
                    {
                        actor = zombie,
                        currentGoal = point,
                        lastObserved = current,
                        lastProgressAt = now,
                        revisions = 0
                    };
                assigned++;
            }
            _cursor = (_cursor + inspected) % all.Count;
            if (_lastSoundAt.Count > 200) _lastSoundAt.Clear();
            if (assigned > 0 && now >= _nextLogAt)
            {
                _nextLogAt = now + 12f;
                ProbeLogger.Log("ZOMBIE_REAL_SHOT_SEARCH: newlyAlerted=" +
                    assigned + " inspected=" + inspected +
                    " (native Seeker; no synthetic visual player target)");
            }
        }

        private bool TryLocalCorridor(Zombie z, Search record,
            out Vector3 proposed)
        {
            proposed = Vector3.zero;
            Vector3 current = z.transform.position;
            Vector3 direction = record.currentGoal - current;
            direction.y = 0f;
            if (direction.sqrMagnitude < 6f) return false;
            direction.Normalize();
            // Alternate sides of the blocked line, trying a short
            // position actually reachable within the current nav bound.
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                float angle = attempt == 0 ? -50f :
                    attempt == 1 ? 50f : 0f;
                Vector3 delta = Quaternion.AngleAxis(angle,
                    Vector3.up) * direction;
                Vector3 estimate = current + delta * 2.8f;
                Vector3 foot;
                if (!TrySupportedPoint(estimate, current, out foot))
                    continue;
                if (Mathf.Abs(foot.y - current.y) > 0.8f ||
                    Physics.CheckCapsule(foot + Vector3.up * 0.5f,
                        foot + Vector3.up * 1.55f, 0.31f,
                        RayMasks.BLOCK_COLLISION,
                        QueryTriggerInteraction.Ignore))
                    continue;
                Vector3 flat = foot - current;
                flat.y = 0f;
                float length = flat.magnitude;
                if (length < 1.75f || length > 3.4f) continue;
                int count = Physics.SphereCastNonAlloc(
                    current + Vector3.up * 0.9f, 0.25f,
                    flat / length, _hits, length,
                    RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore);
                if (count >= HitBuffer) continue;
                bool blocked = false;
                for (int j = 0; j < count; ++j)
                {
                    RaycastHit hit = _hits[j];
                    Collider c = hit.collider;
                    _hits[j] = default(RaycastHit);
                    if (c == null || c.transform == z.transform ||
                        c.transform.IsChildOf(z.transform))
                        continue;
                    if (hit.distance < length - 0.4f)
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked) continue;
                if (!Line2ZombieSoundPolicy.MayRedirectStalledPoint(
                    Authorized(), z.isHunting, HasNativePlayer(z),
                    true, true, current, record.currentGoal, foot,
                    Time.time - record.lastProgressAt,
                    record.revisions))
                    continue;
                proposed = foot;
                return true;
            }
            return false;
        }

        private void Update()
        {
            if (Time.time < _nextUpdateAt) return;
            _nextUpdateAt = Time.time + TickSeconds;
            if (!Authorized() || NativePlayer == null)
            {
                _active.Clear();
                return;
            }
            if (_active.Count == 0) return;
            _keys.Clear();
            foreach (int id in _active.Keys)
                _keys.Add(id);
            int checkedCount = 0;
            foreach (int id in _keys)
            {
                Search observation;
                if (!_active.TryGetValue(id, out observation))
                    continue;
                Zombie z = observation.actor;
                if (z == null || z.isDead || !z.isHunting ||
                    HasNativePlayer(z) || ++checkedCount > 28)
                {
                    if (checkedCount <= 28) _active.Remove(id);
                    continue;
                }
                Vector3 actual = z.transform.position;
                if ((actual - observation.lastObserved).sqrMagnitude >=
                    Line2ZombieSoundPolicy.MinimumMovementMeters *
                    Line2ZombieSoundPolicy.MinimumMovementMeters)
                {
                    observation.lastObserved = actual;
                    observation.lastProgressAt = Time.time;
                    _active[id] = observation;
                    continue;
                }
                if (Time.time - observation.lastProgressAt <
                    Line2ZombieSoundPolicy.NoProgressSeconds)
                    continue;
                Vector3 better;
                if (TryLocalCorridor(z, observation, out better))
                {
                    // Native Zombie.alert only adopts closer point targets
                    // while already hunting. Policy verifies the range first.
                    z.alert(better, false);
                    observation.currentGoal = better;
                    observation.lastObserved = actual;
                    observation.lastProgressAt = Time.time;
                    observation.revisions++;
                    _active[id] = observation;
                    if (Time.time >= _nextLogAt)
                    {
                        _nextLogAt = Time.time + 12f;
                        ProbeLogger.Log("ZOMBIE_STUCK_SEARCH_REPROPOSED: " +
                            "original-native-point-hunt, verified 2.8m corridor");
                    }
                }
                else
                {
                    // No confirmed nearby path. Don't spin/teleport or
                    // hammer Seeker with invalid requests each frame.
                    observation.revisions++;
                    observation.lastProgressAt = Time.time;
                    if (observation.revisions >=
                        Line2ZombieSoundPolicy.MaxRouteRevisions)
                        _active.Remove(id);
                    else _active[id] = observation;
                }
            }
            _keys.Clear();
        }
    }
}
