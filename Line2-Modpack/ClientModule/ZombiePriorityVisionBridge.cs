using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Opt-in local singleplayer player-priority gate for EXISTING zombies.
    /// Does not spawn/despawn zombies, touch vanilla region budgets, change
    /// zombie AI internals or use reflection. The normal Zombie.alert(Player)
    /// remains authoritative. It does NOT extend pursuit across unloaded regions.
    /// </summary>
    internal sealed class ZombiePriorityVisionBridge : MonoBehaviour
    {
        private const float Radius = 20f;
        private const int MaxColliders = 160;
        private const int MaxZombiesPerTick = 24;
        private const float Interval = 1.0f;
        private readonly Collider[] _nearby = new Collider[MaxColliders];
        private readonly HashSet<int> _seen = new HashSet<int>();
        private float _nextTickAt;
        private float _nextLogAt;

        internal static bool Enabled { get; set; }

        private void OnDisable()
        {
            Enabled = false;
            Array.Clear(_nearby, 0, _nearby.Length);
            _seen.Clear();
        }

        private void Update()
        {
            if (!Enabled || Time.time < _nextTickAt)
                return;
            _nextTickAt = Time.time + Interval;
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                (Provider.clients != null && Provider.clients.Count > 1) ||
                !Provider.hasCheats || !Level.isLoaded ||
                Player.player == null)
                return;

            Player local = Player.player;
            Vector3 playerPos = local.transform.position;
            int count = Physics.OverlapSphereNonAlloc(
                playerPos, Radius, _nearby, ~0, QueryTriggerInteraction.Collide);
            _seen.Clear();
            int evaluated = 0;
            int alerted = 0;
            try
            {
                // OverlapSphere can contain several colliders per zombie.
                // Cap work by distinct zombies rather than collider count.
                for (int i = 0; i < count && evaluated < MaxZombiesPerTick; i++)
                {
                    Collider collider = _nearby[i];
                    if (collider == null) continue;
                    Zombie zombie = collider.GetComponentInParent<Zombie>();
                    if (zombie == null || zombie.isDead) continue;
                    if (!_seen.Add(zombie.GetInstanceID())) continue;
                    evaluated++;
                    if (!zombie.checkAlert(local)) continue;
                    Vector3 eye = zombie.transform.position + Vector3.up * 1.55f;
                    Vector3 target = playerPos + Vector3.up * 1.25f;
                    Vector3 ray = target - eye;
                    if (ray.sqrMagnitude > Radius * Radius ||
                        ray.sqrMagnitude < 0.25f) continue;
                    RaycastHit hit;
                    if (Physics.Raycast(eye, ray.normalized, out hit,
                                        ray.magnitude, ~0,
                                        QueryTriggerInteraction.Ignore))
                    {
                        Transform t = hit.transform;
                        if (t != local.transform && !t.IsChildOf(local.transform))
                            continue;
                    }
                    zombie.alert(local);
                    alerted++;
                }
                if (Time.time >= _nextLogAt && alerted > 0)
                {
                    ProbeLogger.Log("ZOMBIE_PRIORITY_PLAYER: retargeted=" + alerted +
                        " existing=" + evaluated + " radius=" + Radius +
                        " (no new spawns; LOS required)");
                    _nextLogAt = Time.time + 12f;
                }
            }
            catch (Exception ex)
            {
                if (Time.time >= _nextLogAt)
                {
                    ProbeLogger.Log("ZOMBIE_PRIORITY_FAILED: " + ex.Message);
                    _nextLogAt = Time.time + 12f;
                }
            }
            finally
            {
                Array.Clear(_nearby, 0, _nearby.Length);
                _seen.Clear();
            }
        }
    }
}
