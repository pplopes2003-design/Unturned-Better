using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Adapter of Line 1's MobileNPCHumanoidPerception priority:
    /// hostile PLAYER (within 20m, actor's existing verified eyesight),
    /// visible opposing humanoid, and finally a visible vanilla zombie.
    /// This is a LIVE visibility snapshot, never radio hearsay or a remote
    /// service target. Native ZombieManager owns the zombie candidate list.
    /// </summary>
    internal sealed class Line2NPCCombatPerception : MonoBehaviour
    {
        internal enum TargetKind { None, Player, Npc, Zombie }

        private const float MaxNpcSight = 28f;
        private const float MaxZombieSight = 25f;
        private const float ScanInterval = 0.32f;
        private const int MaxRayHits = 16;
        private readonly RaycastHit[] _hits = new RaycastHit[MaxRayHits];
        private readonly List<Zombie> _zombies = new List<Zombie>(64);
        private readonly List<PrototypeActor> _actors = new List<PrototypeActor>(32);
        private SteamNpcPrototypeRuntime _runtime;
        private PrototypeActor _owner;
        private float _nextScan;
        private float _nextLogAt;
        private int _globalZombieCursor;

        internal TargetKind Kind { get; private set; }
        internal Transform VisibleTarget { get; private set; }
        internal Vector3 LastVisiblePosition { get; private set; }

        internal void Initialize(SteamNpcPrototypeRuntime runtime, PrototypeActor owner)
        {
            _runtime = runtime;
            _owner = owner;
            _nextScan = Time.time + UnityEngine.Random.Range(0.05f, 0.3f);
            Clear();
        }

        private static bool IsLocalAuthorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                (Provider.clients == null || Provider.clients.Count <= 1) &&
                Provider.hasCheats && Level.isLoaded && Player.player != null;
        }

        private void Clear()
        {
            Kind = TargetKind.None;
            VisibleTarget = null;
            _zombies.Clear();
            _actors.Clear();
        }

        private void OnDisable() { Clear(); }

        private void Update()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + ScanInterval;
            if (!IsLocalAuthorized() || _runtime == null || _owner == null ||
                !_owner.IsCombatEnabled || _owner.IsDead)
            {
                Clear();
                return;
            }

            // The host actor already checked 20m, actual view cone and
            // obstruction for the local player. Never replace this with
            // the player's unconstrained Transform.
            Player player = Player.player;
            if (player != null && player.life != null &&
                !player.life.isDead &&
                Line2NPCCombatRules.CanEngagePlayer(
                    _owner.Disposition, _owner.CombatMode, _owner.PlayerVisible,
                    (player.transform.position - transform.position).sqrMagnitude))
            {
                SetTarget(TargetKind.Player, player.transform);
                return;
            }

            // The original Line 1 decision hierarchy selects hostile NPCs
            // before zombies. Until full faction IDs are ported, hostility
            // is only between explicit 'hostile' and 'friendly' roles.
            // Neutrals remain uninvolved rather than guessing alliances.
            _runtime.CopyLiveActors(_actors, 32);
            PrototypeActor bestNpc = null;
            float bestNpcSq = MaxNpcSight * MaxNpcSight;
            foreach (PrototypeActor candidate in _actors)
            {
                if (candidate == null || candidate == _owner || candidate.IsDead ||
                    !CanEngageNpcTarget(candidate))
                    continue;
                Vector3 d = candidate.transform.position - transform.position;
                float sq = d.sqrMagnitude;
                if (sq >= bestNpcSq || !HasVerifiedSight(
                        candidate.transform, TargetKind.Npc, MaxNpcSight))
                    continue;
                bestNpcSq = sq;
                bestNpc = candidate;
            }
            _actors.Clear();
            if (bestNpc != null)
            {
                SetTarget(TargetKind.Npc, bestNpc.transform);
                return;
            }

            if (!Line2NPCCombatRules.CanEngageZombie(
                    _owner.Disposition, _owner.CombatMode))
            {
                Clear();
                return;
            }
            _zombies.Clear();
            // One native navigation region, with the original zombie list
            // supplied by the game. No FindObjectsOfType scans per actor.
            ZombieManager.getZombiesInRadius(transform.position,
                MaxZombieSight * MaxZombieSight, _zombies);
            Zombie best = null;
            float bestZombieSq = MaxZombieSight * MaxZombieSight;
            int checkedCount = 0;
            foreach (Zombie candidate in _zombies)
            {
                if (++checkedCount > 80) break;
                if (candidate == null || candidate.isDead ||
                    !candidate.gameObject.activeInHierarchy)
                    continue;
                float sq = (candidate.transform.position - transform.position).sqrMagnitude;
                if (sq >= bestZombieSq ||
                    !HasVerifiedSight(candidate.transform, TargetKind.Zombie,
                        MaxZombieSight))
                    continue;
                bestZombieSq = sq;
                best = candidate;
            }
            _zombies.Clear();

            // Native getZombiesInRadius searches only our current navigation
            // region. An NPC standing at a region boundary must still be
            // allowed to SEE a zombie across the line. Check at most 48
            // existing vanilla zombies from a rolling global window; no
            // population changes, no FindObjectsOfType, no allocations.
            if (best == null && ZombieManager.AllZombies != null)
            {
                List<Zombie> all = ZombieManager.AllZombies;
                int total = all.Count;
                int inspected = Mathf.Min(total, 48);
                for (int i = 0; i < inspected; i++)
                {
                    int index = (_globalZombieCursor + i) % total;
                    Zombie candidate = all[index];
                    if (candidate == null || candidate.isDead ||
                        !candidate.gameObject.activeInHierarchy)
                        continue;
                    float sq = (candidate.transform.position - transform.position).sqrMagnitude;
                    if (sq >= bestZombieSq ||
                        !HasVerifiedSight(candidate.transform, TargetKind.Zombie,
                            MaxZombieSight))
                        continue;
                    bestZombieSq = sq;
                    best = candidate;
                }
                if (total > 0)
                    _globalZombieCursor = (_globalZombieCursor + inspected) % total;
            }
            // Keep a continuously verified nearby zombie as our selected
            // target even if it was outside this frame's rolling sample.
            if (best == null && Kind == TargetKind.Zombie &&
                VisibleTarget != null)
            {
                Zombie prior = VisibleTarget.GetComponentInParent<Zombie>();
                if (prior != null && !prior.isDead &&
                    (prior.transform.position - transform.position).sqrMagnitude <=
                        MaxZombieSight * MaxZombieSight &&
                    HasVerifiedSight(prior.transform, TargetKind.Zombie,
                        MaxZombieSight))
                    best = prior;
            }
            if (best != null)
            {
                SetTarget(TargetKind.Zombie, best.transform);
                return;
            }
            Clear();
        }

        // Combat executors must use the same authoritative faction gate as
        // perception. Recheck immediately before applying health mutations
        // because a target may change group after the last scan.
        internal bool CanEngageNpcTarget(PrototypeActor candidate)
        {
            if (candidate == null || candidate.IsDead || _owner == null ||
                _owner.IsDead || !_owner.IsCombatEnabled ||
                _owner.CombatMode == "passive") return false;
            // Both actors have authoritative group/faction memberships.
            // Do not regress to a generic role check when they are actually
            // allied or neutral under the imported Line 1 relationship table.
            Line2NPCGroupMember self = _owner.GetComponent<Line2NPCGroupMember>();
            Line2NPCGroupMember other =
                candidate.GetComponent<Line2NPCGroupMember>();
            bool bothJoined = self != null && self.Joined &&
                other != null && other.Joined;
            if (Line2NpcCombatTotalPolicy.Enabled(_owner.CombatMode))
                return Line2NpcCombatTotalSquadPolicy.CanEngageRival(
                    true, bothJoined,
                    self != null ? self.GroupId : 0,
                    other != null ? other.GroupId : 0,
                    self != null ? self.Faction :
                        Line2NPCFactionPolicy.Id.Scavengers,
                    other != null ? other.Faction :
                        Line2NPCFactionPolicy.Id.Scavengers);
            if (bothJoined)
                return Line2NPCFactionPolicy.AreHostile(
                    self.Faction, other.Faction);
            // Normal mode only: legacy ungrouped role compatibility.
            // Combat Total must NEVER attack a missing/invalid group.
            return Line2NPCCombatRules.AreOpposed(
                _owner.Disposition, candidate.Disposition, _owner.CombatMode);
        }

        private void SetTarget(TargetKind kind, Transform target)
        {
            if (target == null) { Clear(); return; }
            if ((kind != Kind || target != VisibleTarget) &&
                Time.time >= _nextLogAt)
            {
                ProbeLogger.Log("NPC_COMBAT_SPOTTED: actor=" + _owner.Identity +
                    " target=" + kind + " (verified local LOS)");
                _nextLogAt = Time.time + 12f;
            }
            Kind = kind;
            VisibleTarget = target;
            LastVisiblePosition = target.position;
        }

        internal bool HasVerifiedSight(Transform target, TargetKind kind,
                                      float maxDistance)
        {
            if (target == null || target == transform || maxDistance <= 0f)
                return false;
            Vector3 origin = transform.position + Vector3.up * 1.45f;
            Vector3 aim = target.position + Vector3.up *
                (kind == TargetKind.Zombie ? 0.95f : 1.05f);
            Vector3 displacement = aim - origin;
            float length = displacement.magnitude;
            if (length < 0.15f || length > maxDistance)
                return false;

            Vector3 flattened = target.position - transform.position;
            flattened.y = 0f;
            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (flattened.sqrMagnitude < 0.0001f ||
                facing.sqrMagnitude < 0.0001f ||
                Vector3.Dot(flattened.normalized, facing.normalized) < 0.15f)
                return false;

            // DAMAGE_SERVER omits CLIP and VEHICLE in the original
            // Unturned mask. Always include physical motor blockers so a
            // valid target behind an invisible clip or car is not exposed.
            int mask = RayMasks.DAMAGE_SERVER | RayMasks.BLOCK_COLLISION |
                       RayMasks.ENTITY | RayMasks.PLAYER | RayMasks.ENEMY;
            int count = Physics.RaycastNonAlloc(origin, displacement / length,
                _hits, length + 0.18f, mask, QueryTriggerInteraction.Collide);
            // A saturated buffer may omit the nearest wall: fail closed.
            if (count >= _hits.Length) return false;
            Transform first = null;
            float firstDistance = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                Transform hit = _hits[i].transform;
                if (hit == null || hit == transform || hit.IsChildOf(transform))
                    continue;
                if (_hits[i].distance < firstDistance)
                {
                    firstDistance = _hits[i].distance;
                    first = hit;
                }
            }
            if (first == null)
            {
                // Some vanilla zombie prefabs have no collider along the
                // midpoint ray. Only permit the known zombie fallback when
                // no world collider was encountered at all.
                return kind == TargetKind.Zombie;
            }

            switch (kind)
            {
                case TargetKind.Player:
                    Player expectedPlayer = target.GetComponentInParent<Player>();
                    return expectedPlayer != null &&
                           first.GetComponentInParent<Player>() == expectedPlayer;
                case TargetKind.Zombie:
                    Zombie expectedZombie = target.GetComponentInParent<Zombie>();
                    return expectedZombie != null &&
                           first.GetComponentInParent<Zombie>() == expectedZombie;
                case TargetKind.Npc:
                    PrototypeActor expectedNpc =
                        target.GetComponentInParent<PrototypeActor>();
                    return expectedNpc != null &&
                           first.GetComponentInParent<PrototypeActor>() == expectedNpc;
                default:
                    return false;
            }
        }

        internal bool ValidateStrike(TargetKind kind, Transform target, float reach)
        {
            if (kind == TargetKind.None || kind != Kind ||
                target == null || target != VisibleTarget ||
                !_owner.IsCombatEnabled || _owner.IsDead ||
                !IsLocalAuthorized())
                return false;

            Vector3 d = target.position - transform.position;
            d.y = 0f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (d.sqrMagnitude < 0.0001f || d.sqrMagnitude > reach * reach ||
                forward.sqrMagnitude < 0.0001f ||
                Vector3.Dot(forward.normalized, d.normalized) < 0.72f)
                return false;

            if (kind == TargetKind.Player &&
                !Line2NPCCombatRules.CanEngagePlayer(_owner.Disposition,
                    _owner.CombatMode, _owner.PlayerVisible,
                    (target.position - transform.position).sqrMagnitude))
                return false;
            if (kind == TargetKind.Npc)
            {
                PrototypeActor other = target.GetComponentInParent<PrototypeActor>();
                if (other == null || other.IsDead || !CanEngageNpcTarget(other))
                    return false;
            }
            if (kind == TargetKind.Zombie)
            {
                Zombie z = target.GetComponentInParent<Zombie>();
                if (z == null || z.isDead) return false;
            }
            return HasVerifiedSight(target, kind, reach + 0.3f);
        }
    }
}
