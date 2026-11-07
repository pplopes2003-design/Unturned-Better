using System;
using System.Collections.Generic;
using System.Reflection;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Optional horde rally of EXISTING loaded vanilla zombies, using the
    /// game's point-hunt and pathfinding. Never spawns zombies, increases a
    /// nav region's count, teleports actors or removes the vanilla leash.
    /// This does not claim pursuit into unloaded regions.
    /// </summary>
    internal sealed class Line2ZombieHordeBridge : MonoBehaviour
    {
        private const int MinimumHorde = 8;
        private const int MaximumHorde = 18;
        private const int MaxLeaderInspections = 96;
        private const int MaxCandidatesInspected = 64;
        private const float RallyRadius = 25f;
        private const float TickInterval = 2.25f;
        private static readonly FieldInfo PlayerField = typeof(Zombie).GetField(
            "player", BindingFlags.NonPublic | BindingFlags.Instance);
        private readonly List<Zombie> _candidates = new List<Zombie>(64);
        private readonly List<Zombie> _eligible = new List<Zombie>(MaximumHorde);
        private readonly Dictionary<int, float> _lastAlertAt = new Dictionary<int, float>(128);
        private float _nextAt;
        private float _nextDiagnosticAt;
        private int _cursor;
        internal static bool Enabled { get; set; }

        private void OnDisable()
        {
            Enabled = false;
            _lastAlertAt.Clear();
            _candidates.Clear();
            _eligible.Clear();
        }

        private static bool LocalAuthority()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                (Provider.clients == null || Provider.clients.Count <= 1) &&
                Provider.hasCheats && Level.isLoaded && Player.player != null;
        }

        private static bool HasPlayerTarget(Zombie zombie)
        {
            if (PlayerField == null || PlayerField.FieldType != typeof(Player))
                return true;
            try { return PlayerField.GetValue(zombie) != null; }
            catch { return true; }
        }

        private void Update()
        {
            if (!Enabled || Time.time < _nextAt) return;
            _nextAt = Time.time + TickInterval;
            if (!LocalAuthority() || PlayerField == null) return;
            List<Zombie> all = ZombieManager.AllZombies;
            if (all == null || all.Count == 0) return;
            Player player = Player.player;
            int inspected = 0;
            Zombie leader = null;
            int maximum = Mathf.Min(MaxLeaderInspections, all.Count);
            for (int i = 0; i < maximum; ++i)
            {
                Zombie candidate = all[(_cursor + i) % all.Count];
                inspected++;
                if (candidate == null || candidate.isDead ||
                    !candidate.isHunting ||
                    (candidate.transform.position - player.transform.position)
                        .sqrMagnitude > 65f * 65f)
                    continue;
                // Already aggro'd players remain under native player pursuit;
                // followers investigate the leader's witnessed position.
                leader = candidate;
                break;
            }
            _cursor = (_cursor + inspected) % all.Count;
            if (leader == null) return;
            _candidates.Clear();
            _eligible.Clear();
            ZombieManager.getZombiesInRadius(
                leader.transform.position, RallyRadius * RallyRadius, _candidates);
            if (_candidates.Count < MinimumHorde) return;
            int checkedCount = Mathf.Min(MaxCandidatesInspected, _candidates.Count);
            for (int i = 0; i < checkedCount &&
                 _eligible.Count < MaximumHorde; i++)
            {
                Zombie candidate = _candidates[i];
                if (candidate == null || candidate.isDead) continue;
                // Never change the player target of an existing hunter.
                if (candidate != leader && HasPlayerTarget(candidate)) continue;
                _eligible.Add(candidate);
            }
            if (_eligible.Count < MinimumHorde) return;
            int rallied = 0;
            for (int i = 0; i < _eligible.Count; ++i)
            {
                Zombie follower = _eligible[i];
                if (follower == leader) continue;
                float previous;
                int id = follower.GetInstanceID();
                if (_lastAlertAt.TryGetValue(id, out previous) &&
                    Time.time - previous < 3.5f) continue;
                // Native A* stays responsible for physical movement.
                // A short rally does not grant sight or target identity.
                follower.alert(leader.transform.position, false);
                _lastAlertAt[id] = Time.time;
                rallied++;
            }
            if (_lastAlertAt.Count > 256) _lastAlertAt.Clear();
            if (rallied > 0 && Time.time >= _nextDiagnosticAt)
            {
                _nextDiagnosticAt = Time.time + 12f;
                ProbeLogger.Log("ZOMBIE_HORDE_RALLY: members=" +
                    _eligible.Count + " alerted=" + rallied +
                    " (vanilla population unchanged; same nav bound)");
            }
        }
    }
}
