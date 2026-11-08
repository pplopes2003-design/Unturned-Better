using System;
using System.Collections.Generic;
using System.Reflection;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Experimental, bounded server-side adapter: existing vanilla zombies may
    /// hunt the LAST visible position of a living local NPC, then perform a
    /// verified short-range melee hit. Does not create zombies, change vanilla
    /// population/region accounting, rewrite A*, or intercept player damage.
    /// </summary>
    internal sealed class Line2ZombieNpcCombatBridge : MonoBehaviour
    {
        private const int MaximumActorsPerTick = 8;
        private const int MaximumZombiesPerTick = 20;
        private const float SightRadius = 14f;
        private const float AttackRadius = 1.7f;
        private const float Interval = 0.40f;
        private const float MinimumAttackInterval = 1.65f;
        private static readonly FieldInfo VanillaPlayerTarget =
            typeof(Zombie).GetField("player", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Collider[] _nearby = new Collider[128];
        private readonly RaycastHit[] _sightHits = new RaycastHit[16];
        private readonly List<PrototypeActor> _actors = new List<PrototypeActor>(32);
        private readonly HashSet<int> _visited = new HashSet<int>();
        private readonly Dictionary<int, float> _attackReadyAt = new Dictionary<int, float>();
        private SteamNpcPrototypeRuntime _runtime;
        private float _nextAt;
        private float _nextLogAt;
        private int _actorCursor;

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                (Provider.clients == null || Provider.clients.Count <= 1) &&
                Provider.hasCheats && Level.isLoaded && Player.player != null;
        }

        private void OnDisable()
        {
            _actors.Clear();
            _visited.Clear();
            _attackReadyAt.Clear();
            Array.Clear(_nearby, 0, _nearby.Length);
        }

        private bool HasNativePlayerTarget(Zombie zombie)
        {
            // Native Zombie.player is private. Fail closed if its signature
            // differs from the checked-in original game source.
            if (VanillaPlayerTarget == null ||
                VanillaPlayerTarget.FieldType != typeof(Player)) return true;
            try { return VanillaPlayerTarget.GetValue(zombie) != null; }
            catch { return true; }
        }

        private bool HasClearSight(Transform sourceRoot,
            Transform targetRoot, Vector3 origin, Vector3 destination)
        {
            Vector3 ray = destination - origin;
            if (ray.sqrMagnitude < 0.01f) return false;
            int n = Physics.RaycastNonAlloc(origin, ray.normalized,
                _sightHits, ray.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (n >= _sightHits.Length) return false; // truncated, fail closed
            float nearest = float.PositiveInfinity;
            Transform blocker = null;
            for (int i = 0; i < n; i++)
            {
                RaycastHit hit = _sightHits[i];
                Transform t = hit.transform;
                _sightHits[i] = default(RaycastHit);
                if (t == null || t == sourceRoot || t.IsChildOf(sourceRoot)) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    blocker = t;
                }
            }
            return blocker == null || blocker == targetRoot ||
                blocker.IsChildOf(targetRoot);
        }

        private bool FirsthandView(Zombie zombie, PrototypeActor actor)
        {
            Vector3 eye = zombie.transform.position + Vector3.up * 1.45f;
            Vector3 victim = actor.transform.position + Vector3.up * 1.10f;
            Vector3 ray = victim - eye;
            return ray.sqrMagnitude >= 0.02f &&
                ray.sqrMagnitude <= SightRadius * SightRadius &&
                HasClearSight(zombie.transform, actor.transform, eye, victim);
        }

        private bool PlayerHasPriority(Zombie zombie)
        {
            Player player = Player.player;
            if (player == null || player.life == null || player.life.isDead) return false;
            Vector3 ray = player.transform.position - zombie.transform.position;
            if (ray.sqrMagnitude > 20f * 20f) return false;
            Vector3 start = zombie.transform.position + Vector3.up * 1.4f;
            Vector3 end = player.transform.position + Vector3.up * 1.25f;
            return HasClearSight(zombie.transform, player.transform, start, end);
        }

        private void Update()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + Interval;
            if (!Authorized() || VanillaPlayerTarget == null)
            {
                _attackReadyAt.Clear();
                return;
            }
            if (_runtime == null)
                _runtime = GetComponent<SteamNpcPrototypeRuntime>();
            if (_runtime == null) return;
            _runtime.CopyLiveActors(_actors, 32);
            if (_actors.Count == 0) return;

            int inspected = 0, attacks = 0, aggro = 0;
            _visited.Clear();
            int numActors = Mathf.Min(MaximumActorsPerTick, _actors.Count);
            for (int offset = 0; offset < numActors &&
                 inspected < MaximumZombiesPerTick; offset++)
            {
                int index = (_actorCursor + offset) % _actors.Count;
                PrototypeActor actor = _actors[index];
                if (actor == null || actor.IsDead || actor.GetComponent<PrototypeNpcVitals>() == null)
                    continue;
                int count = Physics.OverlapSphereNonAlloc(actor.transform.position,
                    SightRadius, _nearby, ~0, QueryTriggerInteraction.Collide);
                // Buffer saturation means there may be a hidden blocker.
                if (count >= _nearby.Length) continue;
                for (int i = 0; i < count && inspected < MaximumZombiesPerTick; i++)
                {
                    Collider collider = _nearby[i];
                    if (collider == null) continue;
                    Zombie zombie = collider.GetComponentInParent<Zombie>();
                    if (zombie == null || zombie.isDead ||
                        _visited.Contains(zombie.GetInstanceID())) continue;
                    inspected++;
                    if (HasNativePlayerTarget(zombie) || PlayerHasPriority(zombie) ||
                        !FirsthandView(zombie, actor)) continue;
                    Vector3 delta = actor.transform.position - zombie.transform.position;
                    float horizontalSq = delta.x * delta.x + delta.z * delta.z;
                    // A first-hand ray to an NPC on a disconnected upper
                    // storey is NOT an actionable contact for a ground-level
                    // zombie. Test height before claiming this zombie's tick,
                    // so it can still choose a visible NPC on its own floor.
                    if (Mathf.Abs(delta.y) > 2.1f) continue;
                    if (!_visited.Add(zombie.GetInstanceID())) continue;
                    // Native point-hunting uses the vanilla A* movement component.
                    // This is not a forced transform or fabricated player target.
                    if (horizontalSq > AttackRadius * AttackRadius)
                    {
                        zombie.alert(actor.transform.position, false);
                        aggro++;
                        continue;
                    }
                    int zombieId = zombie.GetInstanceID();
                    float readyAt;
                    if (_attackReadyAt.TryGetValue(zombieId, out readyAt) &&
                        Time.time < readyAt) continue;
                    delta.y = 0f;
                    if (delta.sqrMagnitude <= 0.01f) continue;
                    Vector3 forward = zombie.transform.forward;
                    forward.y = 0f;
                    if (Vector3.Dot(forward.normalized, delta.normalized) < 0.25f)
                    {
                        zombie.alert(actor.transform.position, false);
                        continue;
                    }
                    PrototypeNpcVitals vitals = actor.GetComponent<PrototypeNpcVitals>();
                    float damage = zombie.isMega ? 22f : 9f;
                    bool committed = vitals.ApplyDamageToPart(damage,
                        Line2MobileNPCInjurySystem.BodyPart.Torso,
                        false, false,
                        Line2MobileNPCInjurySystem.BleedingSeverity.None,
                        delta.normalized * damage, zombie.transform);
                    if (!committed) continue;
                    _attackReadyAt[zombieId] = Time.time + MinimumAttackInterval;
                    ZombieManager.sendZombieAttack(zombie,
                        (byte)UnityEngine.Random.Range(0, 5));
                    attacks++;
                }
                Array.Clear(_nearby, 0, _nearby.Length);
            }
            _actorCursor = (_actorCursor + numActors) % _actors.Count;
            if (_attackReadyAt.Count > 512) _attackReadyAt.Clear();
            if ((aggro > 0 || attacks > 0) && Time.time >= _nextLogAt)
            {
                _nextLogAt = Time.time + 10f;
                ProbeLogger.Log("ZOMBIE_NPC_BRIDGE: scanned=" + inspected +
                    " pursuing=" + aggro + " verifiedAttacks=" + attacks +
                    " (local only; no new zombie population)");
            }
        }
    }
}
