using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Experimental SINGLEPLAYER humanoid world adapter. Uses the original
    /// NPC_Client visual prefab, not procedural white capsules. NPCs have
    /// basic roaming/following only: no player damage, inventory, dialogue,
    /// networking or persistent game save yet. Disabled in settings by default.
    /// </summary>
    internal sealed class SteamNpcPrototypeRuntime : MonoBehaviour, ILine2NpcRuntime
    {
        private const string VisualPath = "Characters/NPC_Client";
        private readonly List<PrototypeActor> _actors = new List<PrototypeActor>(32);
        private readonly RaycastHit[] _savedFloorHits = new RaycastHit[16];
        private GameObject _visualPrefab;
        private bool _enabled;
        private bool _natural;
        private int _cap = 8;
        private string _mode = "normal";
        private float _nextNaturalAt;
        private bool _missingPrefabLogged;
        private readonly List<Line2NpcPersistence.ActorRecord> _pendingRestore =
            new List<Line2NpcPersistence.ActorRecord>(32);
        private readonly List<Line2NpcPersistence.ReplacementRecord> _pendingReplacements =
            new List<Line2NpcPersistence.ReplacementRecord>(32);
        private bool _useSpawnAnchor;
        private Vector3 _spawnAnchor;
        private float _nextReplacementAttemptAt;
        private string _saveMap;
        private bool _restoreLoaded;
        private bool _saveAllowed;
        private bool _saveSourceVerified;
        private bool _orphanCampCleanupDone;
        private bool _restoringActor;
        private int _restoreCapacity;
        private float _nextSaveAt;
        private float _nextRestoreAt;
        private const float SaveIntervalSeconds = 30f;


        internal int SceneActorCount { get { return ActiveCount; } }
        public int ActiveCount
        {
            get
            {
                _actors.RemoveAll(a => a == null || a.IsDead);
                return _actors.Count;
            }
        }

        internal void NotifyPlayerFootstep(Vector3 heardPosition, float radius,
                                           float confidence)
        {
            if (!_enabled || !IsAuthorized() || radius <= 0f) return;
            float rangeSquared = radius * radius;
            float now = Time.time;
            foreach (PrototypeActor actor in _actors)
            {
                if (actor == null || actor.IsDead || !actor.isActiveAndEnabled)
                    continue;
                if ((actor.transform.position - heardPosition).sqrMagnitude <= rangeSquared)
                    actor.HearVerifiedFootstep(heardPosition, confidence, now);
            }
        }

        internal void CopyLiveActors(List<PrototypeActor> output, int limit)
        {
            if (output == null) throw new ArgumentNullException("output");
            output.Clear();
            if (!IsAuthorized() || !_enabled) return;
            _actors.RemoveAll(a => a == null || a.IsDead);
            for (int i = 0; i < _actors.Count && output.Count < limit; i++)
                if (_actors[i] != null && _actors[i].isActiveAndEnabled)
                    output.Add(_actors[i]);
        }

        private static bool IsAuthorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                   (Provider.clients == null || Provider.clients.Count <= 1) &&
                   Provider.hasCheats && Level.isLoaded && Player.player != null;
        }

        private void OnEnable()
        {
            Line2NpcRuntimeRegistry.Register(this);
        }

        private void OnDisable()
        {
            SaveCheckpoint();
            Line2NpcRuntimeRegistry.Unregister(this);
            ClearActors();
        }

        private void Update()
        {
            if (!IsAuthorized())
            {
                if (_actors.Count > 0)
                {
                    SaveCheckpoint();
                    ClearActors();
                    ProbeLogger.Log("NPC_PROTOTYPE_SUSPENDED: no singleplayer authority");
                }
                _restoreLoaded = false;
                _saveAllowed = false;
                _saveSourceVerified = false;
                _orphanCampCleanupDone = false;
                _pendingRestore.Clear();
                _pendingReplacements.Clear();
                return;
            }

            string map = Line2NpcPersistence.CurrentMapKey;
            if (string.IsNullOrEmpty(map)) return;
            if (!string.Equals(_saveMap, map, StringComparison.Ordinal))
            {
                SaveCheckpoint(); // Old map snapshot before teardown.
                ClearActors();
                _saveMap = map;
                _restoreLoaded = false;
                _saveAllowed = false;
                _saveSourceVerified = false;
                _orphanCampCleanupDone = false;
                _pendingRestore.Clear();
                _pendingReplacements.Clear();
            }

            if (!_enabled) return;
            if (!_restoreLoaded)
            {
                List<Line2NpcPersistence.ActorRecord> records;
                List<Line2NpcPersistence.ReplacementRecord> tickets;
                List<Line2NpcPersistence.SettlementRecord> settlements;
                string detail;
                bool ok = Line2NpcPersistence.TryRead(_saveMap, out records,
                    out tickets, out settlements, out detail);
                _restoreLoaded = true;
                if (!ok)
                {
                    ProbeLogger.Log("NPC_SAVE_LOAD_REJECTED: " + detail +
                        " (automatic overwrites disabled)");
                    return;
                }
                Line2NpcSettlementLedger.Restore(_saveMap, settlements);
                foreach (Line2NpcPersistence.ReplacementRecord ticket in tickets)
                    Line2NPCGroupMember.ReserveHistoricalGroupId(
                        ticket.formerGroupId);
                _restoreCapacity = Mathf.Min(32, Mathf.Max(_cap, records.Count));
                _saveSourceVerified = true;
                _pendingRestore.AddRange(records);
                _pendingReplacements.AddRange(tickets);
                _saveAllowed = _pendingRestore.Count == 0;
                _nextRestoreAt = Time.time + 0.5f;
                _nextSaveAt = Time.time + SaveIntervalSeconds;
                ProbeLogger.Log("NPC_SAVE_READ: " + detail);
            }

            if (_pendingRestore.Count > 0 && Time.time >= _nextRestoreAt)
            {
                _nextRestoreAt = Time.time + 2f;
                string reason = "none currently reachable";
                // Distant survivors must not block restoration of nearby ones.
                for (int i = 0; i < _pendingRestore.Count; ++i)
                {
                    if (!TryRestoreOne(_pendingRestore[i], out reason)) continue;
                    _pendingRestore.RemoveAt(i);
                    ProbeLogger.Log("NPC_RESTORE_OK: remaining=" + _pendingRestore.Count);
                    if (_pendingRestore.Count == 0)
                    {
                        _saveAllowed = true;
                        _nextSaveAt = Time.time + SaveIntervalSeconds;
                    }
                    break; // Restore at most one actor per window.
                }
                if (_pendingRestore.Count > 0 && Time.time >= _nextRestoreLogAt)
                {
                    _nextRestoreLogAt = Time.time + 20f;
                    ProbeLogger.Log("NPC_RESTORE_DEFERRED: pending=" +
                        _pendingRestore.Count + " reason=" + reason);
                }
            }

            // Merged snapshots retain still-unloaded survivors by exact
            // identity, while recording deaths and changed live actors.
            if (_pendingRestore.Count > 0)
            {
                if (Time.time >= _nextSaveAt)
                {
                    _nextSaveAt = Time.time + SaveIntervalSeconds;
                    SaveCheckpoint();
                }
                return;
            }
            if (!_saveAllowed) return;
            RetireOrphanedSavedCampsAfterRestore();
            if (_natural && _pendingReplacements.Count > 0 &&
                Time.time >= _nextReplacementAttemptAt)
                TryProcessReplacement();
            if (Time.time >= _nextSaveAt)
            {
                _nextSaveAt = Time.time + SaveIntervalSeconds;
                SaveCheckpoint();
            }
            if (!_natural || Time.time < _nextNaturalAt) return;
            _nextNaturalAt = Time.time + 18f;
            if (ActiveCount + _pendingReplacements.Count >= _cap) return;
            string ignored;
            TrySpawn(ChooseNaturalRole(), 1, out ignored);
        }

        private void RetireOrphanedSavedCampsAfterRestore()
        {
            if (_orphanCampCleanupDone || !_saveSourceVerified ||
                !_saveAllowed || _pendingRestore.Count != 0) return;
            _orphanCampCleanupDone = true;
            // Only after ALL saved survivors have had an opportunity to
            // rejoin their real historical groups can we distinguish
            // already-extinct camps from still-unloaded living owners.
            List<Line2NpcPersistence.SettlementRecord> records =
                Line2NpcSettlementLedger.Capture();
            int retired = 0;
            foreach (Line2NpcPersistence.SettlementRecord site in records)
            {
                if (site == null || site.groupId <= 0) continue;
                PrototypeActor member;
                if (Line2NPCGroupMember.TryGetLivingMember(
                    site.groupId, out member))
                    continue;
                if (Line2NpcSettlementLedger.RetireExtinctGroup(
                    site.groupId, true))
                    retired++;
            }
            if (retired == 0) return;
            ProbeLogger.Log("NPC_ORPHAN_CAMP_MANIFESTS_RETIRED: " +
                retired + " (native structures preserved as ruins)");
            RequestPriorityCheckpoint();
        }

        private float _nextRestoreLogAt;

        private string ChooseNaturalRole()
        {
            if (_mode == "total") return "hostile";
            float value = UnityEngine.Random.value;
            if (_mode == "passive")
                return value < 0.55f ? "friendly" : "neutral";
            return value < 0.40f ? "hostile" : value < 0.70f ? "friendly" : "neutral";
        }

        public void UpdateSettings(bool enabled, bool naturalSpawn,
                                   int maxNPCs, string combatMode)
        {
            if (_enabled && !enabled)
                SaveCheckpoint(); // Save before changing the enabled flag.
            _enabled = enabled;
            _natural = naturalSpawn;
            _cap = Mathf.Clamp(maxNPCs, 1, 32);
            _mode = combatMode == "passive" || combatMode == "total"
                ? combatMode : "normal";
            _nextNaturalAt = Time.time + 18f;
            foreach (PrototypeActor actor in _actors)
                if (actor != null) actor.SetCombatMode(_mode);
            // Disable only after saving all restored survivors, avoiding stale snapshots.
            if (!enabled)
            {
                ClearActors();
                _restoreLoaded = false;
                _saveAllowed = false;
                _pendingRestore.Clear();
                _pendingReplacements.Clear();
            }
            else if (ActiveCount > _cap)
            {
                // Never destroy saved survivors just because a menu setting
                // was reduced. New natural/manual spawns remain blocked until
                // the living count drops under the new ceiling.
                ProbeLogger.Log("NPC_CAP_REDUCED: surviving=" + ActiveCount +
                    " newCap=" + _cap + " (existing survivors preserved)");
            }
        }

        public bool TrySpawn(string disposition, int count, out string reason)
        {
            reason = "unknown";
            if (!IsAuthorized())
            {
                reason = "Local singleplayer map with cheats required";
                return false;
            }
            if (!_enabled)
            {
                reason = "Enable NPCs first: !u2 npc on";
                return false;
            }
            if (!_restoringActor && _pendingRestore.Count > 0)
            {
                reason = "Pending NPC restoration; manual spawn temporarily blocked";
                return false;
            }
            if (disposition != "hostile" && disposition != "neutral" &&
                disposition != "friendly")
            {
                reason = "Invalid NPC disposition";
                return false;
            }
            int activeCap = _restoringActor
                ? Mathf.Max(_cap, _restoreCapacity) : _cap;
            if (count < 1 || count > 5 || ActiveCount + count > activeCap)
            {
                reason = "Requested count exceeds per-command limit or population cap";
                return false;
            }
            if (_visualPrefab == null)
                _visualPrefab = Resources.Load<GameObject>(VisualPath);
            if (_visualPrefab == null)
            {
                if (!_missingPrefabLogged)
                {
                    ProbeLogger.Log("NPC_VISUAL_MISSING: Resources/" + VisualPath +
                                    " not available in installed Unturned");
                    _missingPrefabLogged = true;
                }
                reason = "Native NPC_Client visual is unavailable; no fallback cubes created";
                return false;
            }

            var created = new List<PrototypeActor>(count);
            var groupCandidates = new List<PrototypeActor>(_actors);
            for (int i = 0; i < count; i++)
            {
                Vector3 location;
                if (!TryFindGround(i, created, out location))
                {
                    Rollback(created);
                    reason = "No verified nearby ground, spawn aborted";
                    return false;
                }
                GameObject instance = null;
                try
                {
                    instance = new GameObject("Line2_PrototypeNPC_" + Guid.NewGuid().ToString("N"));
                    instance.transform.position = location;
                    int agentLayer = LayerMask.NameToLayer("Agent");
                    // DamageTool explosions overlap RayMasks.AGENT when they
                    // can hurt zombies. Abort if the physical root is on
                    // another layer: otherwise NPCs would be invulnerable.
                    if (agentLayer < 0 ||
                        (RayMasks.AGENT & (1 << agentLayer)) == 0)
                        throw new InvalidOperationException(
                            "Unturned Agent layer unavailable/mismatched; explosion damage unsafe");
                    instance.layer = agentLayer;
                    CharacterController movement = instance.AddComponent<CharacterController>();
                    movement.height = 2.0f;
                    movement.radius = 0.38f;
                    movement.center = new Vector3(0f, 1f, 0f);
                    movement.stepOffset = 0.4f;
                    movement.slopeLimit = 50f;
                    GameObject visual = UnityEngine.Object.Instantiate(
                        _visualPrefab, instance.transform, false);
                    visual.name = "NPC_ClientVisual";
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    if (visual.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
                        throw new InvalidOperationException("Native visual missing skinned mesh");
                    // Native template has a kinematic root Rigidbody; do not
                    // allow it to become a second physics movement owner.
                    foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>(true))
                    {
                        body.isKinematic = true;
                        body.detectCollisions = false;
                    }
                    // The CharacterController is the sole physical movement
                    // owner. Additional native prefab colliders must never
                    // trap the humanoid in its own skeleton or double-hit.
                    foreach (Collider visualCollider in
                             visual.GetComponentsInChildren<Collider>(true))
                        visualCollider.enabled = false;
                    // The original NPC prefab can include interactive scripts.
                    // Disable everything except purely visual Human* components;
                    // our prototype is NOT a native quest NPC.
                    foreach (MonoBehaviour behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (behaviour == null) continue;
                        string name = behaviour.GetType().Name;
                        if (!name.StartsWith("Human", StringComparison.Ordinal))
                            behaviour.enabled = false;
                    }
                    ApplyNativeAppearance(visual);
                    var motion = instance.AddComponent<PrototypeAnimation>();
                    motion.Initialize(visual);
                    var navigation = instance.AddComponent<PrototypeObstacleNavigator>();
                    navigation.Initialize();
                    // Connect the A* Seeker bundled in the ORIGINAL Steam
                    // game. No Pathfinding DLL or licensed source is shipped.
                    var nativePath = instance.AddComponent<Line2NativeAStarRouteProvider>();
                    var follower = instance.AddComponent<PrototypeAStarFollower>();
                    follower.Initialize(nativePath, navigation);
                    instance.AddComponent<PrototypeCoverPlanner>();
                    // DamageTool.explode discovers IExplosionDamageable
                    // through the root Agent CharacterController collider.
                    instance.AddComponent<PrototypeNpcVitals>();
                    var ownedInventory = instance.AddComponent<Line2MobileNPCInventory>();
                    instance.AddComponent<Line2NpcDeathLoot>();
                    instance.AddComponent<Line2MobileNPCInjurySystem>();
                    instance.AddComponent<Line2MobileNPCDoorNavigation>();
                    var coarseNav = instance.AddComponent<Line2CoarseRouteBridge>();
                    coarseNav.Initialize();
                    follower.AttachCoarseFallback(coarseNav);
                    instance.AddComponent<Line2MobileNPCAdaptiveMemory>();
                    instance.AddComponent<Line2MobileNPCTravelMemory>();
                    string anatomyError;
                    if (!Line2NPCBodyAnatomy.TryBind(visual, out anatomyError))
                        throw new InvalidOperationException(
                            "Original NPC_Client anatomy invalid: " + anatomyError);
                    var actor = instance.AddComponent<PrototypeActor>();
                    actor.Initialize(disposition, movement, instance.name);
                    actor.SetCombatMode(_mode);
                    var collisionAudit = instance.AddComponent<Line2NpcObstacleAudit>();
                    collisionAudit.Initialize(actor);
                    actor.AttachVault(instance.AddComponent<Line2MobileNPCVaultTraversal>());
                    var needs = instance.AddComponent<Line2NpcNeeds>();
                    needs.Initialize(actor);
                    var group = instance.AddComponent<Line2NPCGroupMember>();
                    group.Initialize(actor, groupCandidates);
                    var survey = instance.AddComponent<Line2NpcWorldSurvey>();
                    survey.Initialize(actor);
                    actor.AttachWorldSurvey(survey);
                    groupCandidates.Add(actor);
                    string kitStatus;
                    if (Line2NPCStartingEquipment.GiveFreshKitServer(
                        ownedInventory, disposition, _mode, out kitStatus))
                        ProbeLogger.Log("NPC_STARTING_EQUIPMENT: " + kitStatus);
                    Line2NPCStartingEquipment.GiveAuthenticRadioServer(
                        ownedInventory, disposition, _mode);
                    var combatSight = instance.AddComponent<Line2NPCCombatPerception>();
                    combatSight.Initialize(this, actor);
                    actor.AttachCombatPerception(combatSight);
                    var builder = instance.AddComponent<Line2NpcSettlementBuilder>();
                    builder.Initialize(this, actor, combatSight);
                    var stockpile = instance.AddComponent<Line2NpcCampStockpile>();
                    stockpile.Initialize(this, actor, combatSight);
                    var looting = instance.AddComponent<Line2NpcWorldLootCollector>();
                    looting.Initialize(this, actor, combatSight);
                    var squad = instance.AddComponent<Line2NpcSquadRegroup>();
                    squad.Initialize(actor, combatSight);
                    var supplySharing =
                        instance.AddComponent<Line2NpcSquadSupplySharing>();
                    supplySharing.Initialize(this, actor, combatSight);
                    var medicalCare = instance.AddComponent<Line2NpcMedicalCare>();
                    medicalCare.Initialize(actor, combatSight);
                    var melee = instance.AddComponent<Line2NPCMeleeExecutor>();
                    melee.Initialize(actor, combatSight);
                    var ranged = instance.AddComponent<Line2NPCRangedExecutor>();
                    ranged.Initialize(actor, combatSight, ownedInventory);
            // Adapted from Line 1: render only a genuinely owned gun or
            // melee tool at the native NPC hand hook; effects cannot fire
            // damage, and remain absent on dedicated servers.
            instance.AddComponent<Line2NpcWeaponPresentation>();
                    actor.AttachRangedExecutor(ranged);
                    var grenades = instance.AddComponent<Line2NpcGrenadeThrower>();
                    grenades.Initialize(this, actor, combatSight, ownedInventory);
                    var breacher = instance.AddComponent<Line2NpcRaidBreacher>();
                    breacher.Initialize(actor, combatSight);
                    var raid = instance.AddComponent<Line2NpcRaidObserver>();
                    raid.Initialize(this, actor);
                    var radio = instance.AddComponent<Line2NPCRadioOutbox>();
                    radio.Initialize(this, actor, combatSight, ownedInventory);
                    actor.Died += OnActorDied;
                    created.Add(actor);
                }
                catch (Exception ex)
                {
                    if (instance != null)
                        UnityEngine.Object.Destroy(instance);
                    Rollback(created);
                    reason = "Instantiation failed: " + ex.GetType().Name;
                    ProbeLogger.Log("NPC_SPAWN_FAILED: " + ex);
                    return false;
                }
            }

            _actors.AddRange(created);
            reason = "Created " + created.Count + " local Steam humanoids with A*, " +
                     "local combat, radio, inventory and survivor checkpoint adapters; " +
                     "Steam runtime/gameplay integration remains unvalidated";
            ProbeLogger.Log("NPC_PROTOTYPE_SPAWN_OK: " + reason);
            return true;
        }

        private static void ApplyNativeAppearance(GameObject visual)
        {
            HumanClothes clothes = visual.GetComponentInChildren<HumanClothes>(true);
            if (clothes == null) return;
            Color[] skins = {
                new Color(0.91f, 0.75f, 0.62f),
                new Color(0.77f, 0.56f, 0.41f),
                new Color(0.63f, 0.43f, 0.31f),
                new Color(0.47f, 0.32f, 0.23f),
                new Color(0.96f, 0.82f, 0.70f)
            };
            Color[] hair = {
                new Color(0.14f, 0.09f, 0.07f),
                new Color(0.42f, 0.25f, 0.12f),
                new Color(0.64f, 0.46f, 0.25f),
                new Color(0.1f, 0.1f, 0.1f)
            };
            clothes.skin = skins[UnityEngine.Random.Range(0, skins.Length)];
            clothes.color = hair[UnityEngine.Random.Range(0, hair.Length)];
            // Native prefab controls actual face/mesh assets. Do not guess
            // IDs of hair, beard or clothing that may be absent in Steam.
            clothes.apply();
        }

        private static void Rollback(List<PrototypeActor> actors)
        {
            foreach (PrototypeActor actor in actors)
                if (actor != null) UnityEngine.Object.Destroy(actor.gameObject);
            actors.Clear();
        }

        private void OnActorDied(PrototypeActor actor, int formerGroup)
        {
            if (!_enabled || !IsAuthorized() || actor == null)
                return;
            // Natural replacement is optional, but death is NEVER optional
            // persistence. The previous path returned without saving when
            // natural spawn was disabled and resurrected a dead survivor
            // (potentially duplicating its native dropped inventory) on
            // the next load. Preserve the group ticket in the same checkpoint.
            if (_natural && _pendingReplacements.Count < 32)
            {
                _pendingReplacements.Add(new Line2NpcPersistence.ReplacementRecord
                {
                    role = actor.Disposition,
                    formerGroupId = formerGroup,
                    readyAtUtcTicks = DateTime.UtcNow.AddSeconds(60).Ticks
                });
                ProbeLogger.Log("NPC_REPLACEMENT_SCHEDULED: role=" +
                    actor.Disposition + " formerGroup=" + formerGroup +
                    " delay=60s");
            }
            // A nearby groupmate may make a voluntary, physically checked
            // withdrawal only after genuinely witnessing a VIOLENT casualty.
            // This is separate from natural replacement and never transfers
            // the attacker Transform or permits a blind weapon hit.
            if (formerGroup > 0)
            {
                int evaluated = 0;
                foreach (PrototypeActor teammate in _actors)
                {
                    if (++evaluated > 32) break;
                    if (teammate == null || teammate == actor ||
                        teammate.IsDead) continue;
                    teammate.TryReactToWitnessedSquadCasualty(
                        actor, formerGroup);
                }
            }
            // NPC membership is removed synchronously before this event.
            // Release the extinct GROUP ledger only when both the living
            // roster and the not-yet-rehydrated survivor queue are empty.
            // Native barricades, storage and loose items remain untouched:
            // they are physical ruins, not a free prefab deletion.
            bool awaitingGroupSurvivor = false;
            foreach (Line2NpcPersistence.ActorRecord pending in _pendingRestore)
                if (pending != null && pending.groupId == formerGroup)
                {
                    awaitingGroupSurvivor = true;
                    break;
                }
            PrototypeActor otherLiving;
            if (formerGroup > 0 && !awaitingGroupSurvivor &&
                !Line2NPCGroupMember.TryGetLivingMember(
                    formerGroup, out otherLiving) &&
                Line2NpcSettlementLedger.RetireExtinctGroup(
                    formerGroup, true))
                ProbeLogger.Log("NPC_GROUP_EXTINCT_CAMP_RETIRED: group=" +
                    formerGroup + " world barricades retained as ruins");

            // Actor has already been marked dead by native vitals.
            // SaveCheckpoint skips it while retaining other loaded and
            // still-pending survivors, so reloading cannot resurrect it.
            RequestPriorityCheckpoint();
        }

        private void TryProcessReplacement()
        {
            _nextReplacementAttemptAt = Time.time + 3f;
            if (!IsAuthorized() || _pendingReplacements.Count == 0 ||
                ActiveCount >= _cap) return;
            for (int i = 0; i < _pendingReplacements.Count; i++)
            {
                Line2NpcPersistence.ReplacementRecord ticket = _pendingReplacements[i];
                if (ticket.readyAtUtcTicks > DateTime.UtcNow.Ticks) continue;
                PrototypeActor formerMember;
                bool stillAlive = Line2NPCGroupMember.TryGetLivingMember(
                    ticket.formerGroupId, out formerMember);
                if (stillAlive)
                {
                    _spawnAnchor = formerMember.transform.position;
                    _useSpawnAnchor = true;
                }
                string reason = "spawn not attempted";
                bool spawned = false;
                try { spawned = TrySpawn(ticket.role, 1, out reason); }
                catch (Exception error)
                {
                    reason = "spawn exception: " + error.GetType().Name;
                    ProbeLogger.Log("NPC_REPLACEMENT_SPAWN_FAILED: " + error);
                }
                finally { _useSpawnAnchor = false; }
                if (!spawned)
                {
                    if (Time.time >= _nextRestoreLogAt)
                    {
                        _nextRestoreLogAt = Time.time + 20f;
                        ProbeLogger.Log("NPC_REPLACEMENT_DEFERRED: " + reason);
                    }
                    continue;
                }
                PrototypeActor next = _actors[_actors.Count - 1];
                Line2NPCGroupMember group =
                    next.GetComponent<Line2NPCGroupMember>();
                if (stillAlive && group != null)
                    group.TryJoinLivingGroup(next, ticket.formerGroupId);
                _pendingReplacements.RemoveAt(i);
                ProbeLogger.Log("NPC_REPLACEMENT_CREATED: newIdentity=" +
                    next.Identity + " group=" + (group != null ? group.GroupId : 0));
                break;
            }
        }

        /// <summary>Checkpoints only fully loaded, living actors on the
        /// authoritative local map. Never replaces a partially restored save.</summary>
        internal void RequestPriorityCheckpoint()
        {
            // Called only after an authoritative native world-item transfer or
            // placed barricade. Never overwrite unverified/corrupt saves.
            if (_enabled && _saveSourceVerified && IsAuthorized())
                SaveCheckpoint();
        }

        private void SaveCheckpoint()
        {
            if (!_enabled || !_saveSourceVerified ||
                !Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || string.IsNullOrEmpty(_saveMap)) return;
            var records = new List<Line2NpcPersistence.ActorRecord>(
                _actors.Count + _pendingRestore.Count);
            // The pending actors still belong to the world even when their
            // regions are not loaded. Keep their ORIGINAL snapshots, rather
            // than silently resurrecting dead loaded NPCs or deleting distant
            // survivors during an autosave.
            records.AddRange(_pendingRestore);
            foreach (PrototypeActor actor in _actors)
            {
                if (actor == null || actor.IsDead) continue;
                Line2MobileNPCInventory inventory = actor.Inventory;
                Line2NpcNeeds needs = actor.GetComponent<Line2NpcNeeds>();
                Line2MobileNPCInjurySystem injuries =
                    actor.GetComponent<Line2MobileNPCInjurySystem>();
                if (inventory == null)
                {
                    ProbeLogger.Log("NPC_SAVE_ABORTED: actor missing inventory");
                    return;
                }
                Line2NpcPersistence.ItemRecord[] items = inventory.CaptureItemsServer();
                if (items == null)
                {
                    ProbeLogger.Log("NPC_SAVE_ABORTED: cannot capture owned inventory");
                    return;
                }
                Line2NPCGroupMember group = actor.GetComponent<Line2NPCGroupMember>();
                Vector3 p = actor.transform.position;
                Vector3 h = actor.HomePosition;
                records.Add(new Line2NpcPersistence.ActorRecord
                {
                    identity = actor.Identity,
                    role = actor.Disposition,
                    displayName = actor.DisplayName,
                    recruited = actor.IsRecruited,
                    groupId = group != null ? group.GroupId : 0,
                    health = actor.HealthFraction * 100f,
                    hasNeeds = needs != null,
                    hunger = needs != null ? needs.Hunger : 100f,
                    thirst = needs != null ? needs.Thirst : 100f,
                    injuries = injuries != null ? injuries.ExportSnapshotServer() : null,
                    x = p.x, y = p.y, z = p.z,
                    homeX = h.x, homeY = h.y, homeZ = h.z,
                    items = items
                });
            }
            string detail;
            if (Line2NpcPersistence.TryWrite(_saveMap, records,
                _pendingReplacements, Line2NpcSettlementLedger.Capture(),
                out detail))
                ProbeLogger.Log("NPC_SAVE_OK: " + detail);
            else
                ProbeLogger.Log("NPC_SAVE_FAILED: " + detail);
        }

        private bool TryRestoreOne(Line2NpcPersistence.ActorRecord saved, out string reason)
        {
            reason = "invalid_restore";
            if (saved == null || !IsAuthorized() ||
                _actors.Count >= Mathf.Max(_cap, _restoreCapacity))
                return false;
            Player player = Player.player;
            Vector3 requested = saved.Position;
            // Only recreate an NPC when its destination is physically loaded.
            // Distant survivors remain preserved in the snapshot until loaded.
            if (player == null || (player.transform.position - requested).sqrMagnitude >
                120f * 120f)
            {
                reason = "saved region not loaded/near player";
                return false;
            }
            RaycastHit ground;
            if (!Line2NpcFloorGeometry.TryFindStaticSupport(
                    requested, 1.5f, 4f, 1f, 0.7f,
                    _savedFloorHits, out ground))
            {
                reason = "saved position has no verified floor";
                return false;
            }
            Vector3 destination = ground.point + Vector3.up * 0.08f;
            if (Physics.CheckCapsule(destination + Vector3.up * 0.55f,
                destination + Vector3.up * 1.55f, 0.32f,
                RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore))
            {
                reason = "saved position blocked by world geometry";
                return false;
            }
            foreach (PrototypeActor other in _actors)
                if (other != null && !other.IsDead &&
                    (other.transform.position - destination).sqrMagnitude < 2.25f)
                {
                    reason = "saved position occupied by NPC";
                    return false;
                }

            _restoringActor = true;
            PrototypeActor created = null;
            try
            {
                string spawnDetail;
                if (!TrySpawn(saved.role, 1, out spawnDetail))
                {
                    reason = "spawn rejected: " + spawnDetail;
                    return false;
                }
                PrototypeActor actor = _actors[_actors.Count - 1];
                created = actor;
                CharacterController controller = actor.GetComponent<CharacterController>();
                Line2MobileNPCInventory inventory = actor.Inventory;
                Line2NPCGroupMember group = actor.GetComponent<Line2NPCGroupMember>();
                Line2NpcNeeds needs = actor.GetComponent<Line2NpcNeeds>();
                Line2MobileNPCInjurySystem injuries =
                    actor.GetComponent<Line2MobileNPCInjurySystem>();
                bool valid = controller != null && inventory != null && group != null &&
                    needs != null &&
                    inventory.TryRestoreItemsServer(saved.items) &&
                    actor.TryRestoreIdentity(saved) &&
                    (!saved.hasNeeds || needs.TryRestore(saved.hunger, saved.thirst)) &&
                    injuries != null &&
                    (saved.groupId == 0 || group.TryRestoreMembership(actor, saved.groupId));
                if (!valid)
                {
                    _actors.Remove(actor);
                    UnityEngine.Object.Destroy(actor.gameObject);
                    created = null;
                    reason = "saved actor state/group/inventory rejected";
                    return false;
                }
                if (saved.injuries != null)
                    injuries.RestoreSnapshotServer(saved.injuries);
                controller.enabled = false;
                actor.transform.position = destination;
                controller.enabled = true;
                created = null;
                reason = "restored";
                return true;
            }
            catch (Exception error)
            {
                if (created != null)
                {
                    _actors.Remove(created);
                    UnityEngine.Object.Destroy(created.gameObject);
                }
                reason = "restore error: " + error.GetType().Name;
                ProbeLogger.Log("NPC_RESTORE_EXCEPTION: " + error);
                return false;
            }
            finally
            {
                _restoringActor = false;
            }
        }

        private bool TryFindGround(int index, List<PrototypeActor> inFlight, out Vector3 location)
        {
            location = Vector3.zero;
            Player local = Player.player;
            if (local == null) return false;
            Vector3 origin = _useSpawnAnchor ? _spawnAnchor : local.transform.position;
            for (int trial = 0; trial < 12; trial++)
            {
                float angle = (index * 97f + trial * 43f) * Mathf.Deg2Rad;
                float distance = 8f + (trial % 4) * 2.5f;
                Vector3 point = origin + new Vector3(
                    Mathf.Cos(angle) * distance, 6f, Mathf.Sin(angle) * distance);
                RaycastHit hit;
                if (!Physics.Raycast(point, Vector3.down, out hit, 16f,
                                     ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.72f) continue;
                if (Mathf.Abs(hit.point.y - origin.y) > 3.0f) continue;
                Vector3 candidate = hit.point + Vector3.up * 0.08f;
                // Do not instantiate a humanoid intersecting a building,
                // vehicle, barricade or another solid physics object.
                if (Physics.CheckCapsule(candidate + Vector3.up * 0.55f,
                                         candidate + Vector3.up * 1.55f,
                                         0.30f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if ((candidate - origin).sqrMagnitude < 25f) continue;
                bool nearOther = false;
                foreach (PrototypeActor a in _actors)
                    if (a != null && (a.transform.position - candidate).sqrMagnitude < 9f)
                        nearOther = true;
                foreach (PrototypeActor a in inFlight)
                    if (a != null && (a.transform.position - candidate).sqrMagnitude < 9f)
                        nearOther = true;
                if (nearOther) continue;
                location = candidate;
                return true;
            }
            return false;
        }

        private void ClearActors()
        {
            foreach (PrototypeActor a in _actors)
                if (a != null) UnityEngine.Object.Destroy(a.gameObject);
            _actors.Clear();
            Line2NPCGroupMember.ResetSession();
            Line2NpcEncounterPacing.ResetSession();
            Line2NpcLootClaims.ResetSession();
            Line2NpcSquadSupplySharing.ResetSession();
            Line2NpcCampStockpile.ResetSession();
            Line2NpcSettlementLedger.ResetSession();
            Line2NPCRadioOutbox.ResetSession();
            _pendingReplacements.Clear();
        }
    }

    /// <summary>
    /// Low-cost primitive movement only. NEVER applies damage or modifies
    /// zombie AI. This is explicitly not the finished tactical NPC brain.
    /// </summary>
    internal sealed class PrototypeActor : MonoBehaviour
    {
        private CharacterController _controller;
        private string _disposition;
        private string _combatMode = "normal";
        private const float ThreatRadius = 20f;
        private string _identity;
        internal event Action<PrototypeActor, int> Died;
        internal string Identity { get { return _identity; } }
        internal string DisplayName { get { return _displayName; } }
        internal Vector3 HomePosition { get { return _home; } }
        /// <summary>Sets saved survivor identity, not a new identity for a replacement.</summary>
        internal bool TryRestoreIdentity(Line2NpcPersistence.ActorRecord saved)
        {
            if (saved == null || IsDead || _vitals == null ||
                string.IsNullOrEmpty(saved.identity) ||
                saved.displayName == null || saved.displayName.Length > 80 ||
                !_vitals.TryRestoreHealthServer(saved.health)) return false;
            _identity = saved.identity;
            _recruited = saved.recruited;
            gameObject.name = saved.identity;
            _displayName = saved.displayName;
            _home = saved.Home;
            _goal = _home;
            _legacyBrain.SetSpawnPosition(_home);
            _legacyBrain.Reset();
            _brain.Reset();
            _tactics.ReportHealth(_vitals.Health, 100f);
            return true;
        }

        internal string Disposition { get { return _disposition; } }
        private bool _recruited;
        internal bool IsRecruited { get { return _recruited; } }
        internal bool TrySetRecruitedServer(bool follow)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                IsDead || _disposition == "hostile") return false;
            _recruited = follow;
            if (!follow) _goal = _home;
            return true;
        }
        internal string CombatMode { get { return _combatMode; } }
        internal bool PlayerVisible { get { return _threatVisible; } }
        internal bool IsCombatEnabled
        {
            get
            {
                return _combatMode != "passive" &&
                    !IsDead && Provider.isServer &&
                    !Dedicator.IsDedicatedServer &&
                    (Provider.clients == null || Provider.clients.Count <= 1) &&
                    Provider.hasCheats && Level.isLoaded;
            }
        }
        internal bool IsTacticallyRetreating
        {
            get { return _tactics.CurrentGoal ==
                Line2MobileNPCTactics.Goal.Retreat ||
                _brain.Intent == NpcIntent.Retreat; }
        }
        internal void AttachCombatPerception(Line2NPCCombatPerception sensor)
        {
            _combatPerception = sensor;
        }
        private string _aiAction;
        private float _aiValidUntil;
        private Vector3? _lastKnownThreat;
        private float _lastSeenAt;
        private float _lastSuppressedAt = -100f;
        private Vector3 _lastVerifiedSuppressingShooter;
        // Vanilla NPC_Client has anatomical ENTITY trigger volumes: a
        // first-hit Physics.Raycast against ~0 can strike our own arm and
        // incorrectly report that the player behind it is invisible.
        private readonly RaycastHit[] _playerSightHits = new RaycastHit[16];
        private float _lastGunshotAt;
        private float _lastFootstepAt;
        private float _lastRadioAt;
        private Vector3 _lastHeardFootstepPoint;
        private Vector3 _lastHeardPoint; 
        private bool _threatVisible;
        private string _displayName;
        private PrototypeAnimation _animation;
        private PrototypeObstacleNavigator _navigation;
        private PrototypeAStarFollower _astarFollower;
        private PrototypeNpcVitals _vitals;
        private Line2MobileNPCInjurySystem _injuries;
        private Line2MobileNPCInventory _inventory;
        internal Line2MobileNPCInventory Inventory { get { return _inventory; } }
        private Line2MobileNPCDoorNavigation _doors;
        private Line2MobileNPCVaultTraversal _vault;
        private bool _vaultActive;
        private Line2MobileNPCVaultTraversal.JumpPlan _vaultPlan;
        private Vector3 _vaultStart;
        private float _vaultStartedAt;
        private float _nextVaultAt;
        private Vector3 _lastMotorProgress;
        private float _lastMotorProgressAt;
        private Line2MobileNPCAdaptiveMemory _experience;
        private Line2MobileNPCTravelMemory _travelMemory;
        private Line2NpcWorldSurvey _worldSurvey;
        private PrototypeCoverPlanner _coverPlanner;
        private Line2NPCCombatPerception _combatPerception;
        private Line2NPCRangedExecutor _rangedExecutor;
        internal bool IsMoving { get; private set; }
        internal bool HasUsableGun
        {
            get { return _rangedExecutor != null && _rangedExecutor.HasUsableGun; }
        }
        internal void AttachRangedExecutor(Line2NPCRangedExecutor executor)
        {
            _rangedExecutor = executor;
        }
        internal void AttachWorldSurvey(Line2NpcWorldSurvey survey)
        {
            _worldSurvey = survey;
        }
        internal void AttachVault(Line2MobileNPCVaultTraversal vault)
        {
            _vault = vault;
        }
        private Vector3 _coverGoal;
        private Vector3 _retreatGoal;
        private bool _hasRetreat;
        internal bool IsDead { get { return _vitals != null && _vitals.IsDead; } }
        internal float HealthFraction { get { return _vitals != null ? _vitals.HealthFraction : 1f; } }
        internal bool RecentlyDamaged { get { return _vitals != null && Time.time - _vitals.LastDamageAt < 5f; } }
        internal bool UnderVerifiedSuppression
        {
            get
            {
                return Line2NpcSuppressionPolicy.IsCurrent(
                    Time.time, _lastSuppressedAt);
            }
        }
        internal bool HasVerifiedCover { get { return
            (RecentlyDamaged ||
             Line2NpcSuppressionPolicy.IsCurrent(Time.time, _lastSuppressedAt)) &&
             _coverPlanner != null && _coverPlanner.HasCover; } }
        private Vector3 _goal;
        private Vector3 _home;
        private float _nextThink;
        private readonly RaycastHit[] _flankFloorHits = new RaycastHit[16];
        private float _nextFlankAt;
        private float _nextReloadCoverAt;
        private float _blockedSince;
        private float _verticalVelocity;
        private readonly NpcDecisionBrain _brain = new NpcDecisionBrain();
        // Original Line 1 pursuit/investigation state machine owns the
        // physical search destination; the high-level tactical selector
        // alone chooses priority.
        private readonly Line2MobileNPCBrain _legacyBrain = new Line2MobileNPCBrain();
        private readonly Line2MobileNPCPlayerKnowledge _knowledge =
            new Line2MobileNPCPlayerKnowledge();
        private readonly Line2NpcInvestigationProbe _investigationProbe =
            new Line2NpcInvestigationProbe();
        private readonly Line2NpcConcealedApproachPlanner _stalkPlanner =
            new Line2NpcConcealedApproachPlanner();
        private float _nextStalkAt;
        private float _ambushArmedUntil;
        private float _ambushReleaseAt;
        private Transform _ambushVictim;
        private float _nextInvestigationCheckAt;
        private float _nextEvidenceHintAt;
        private float _nextSearchDiagnosticAt;
        private readonly Line2MobileNPCLocomotionPolicy _locomotion =
            new Line2MobileNPCLocomotionPolicy();
        private float _nextLegacyArrivalCheckAt;
        private readonly Line2MobileNPCTactics _tactics = new Line2MobileNPCTactics();
        private readonly Line2NpcTacticalJourneyWatchdog _tacticalJourney =
            new Line2NpcTacticalJourneyWatchdog();
        private Vector3 _lastFailedTacticalPoint;
        private Line2MobileNPCTactics.Goal _lastFailedTacticalKind;
        private float _tacticalRetryAfter;
        private float _nextTacticalBlockedLog;
        internal NpcIntent CurrentIntent { get { return _brain.Intent; } }
        internal Vector3 CurrentDestination { get { return _goal; } }

        internal void Initialize(string disposition, CharacterController controller,
                                 string identity)
        {
            _disposition = disposition;
            _controller = controller;
            _animation = GetComponent<PrototypeAnimation>();
            _navigation = GetComponent<PrototypeObstacleNavigator>();
            _astarFollower = GetComponent<PrototypeAStarFollower>();
            _vitals = GetComponent<PrototypeNpcVitals>();
            _injuries = GetComponent<Line2MobileNPCInjurySystem>();
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _doors = GetComponent<Line2MobileNPCDoorNavigation>();
            _experience = GetComponent<Line2MobileNPCAdaptiveMemory>();
            _travelMemory = GetComponent<Line2MobileNPCTravelMemory>();
            if (_astarFollower != null)
                _astarFollower.AttachTravelMemory(_travelMemory);
            if (_vitals != null)
            {
                _vitals.Damaged += OnValidatedDamage;
                _vitals.Died += OnValidatedDeath;
                _tactics.ReportHealth(_vitals.Health, 100f);
            }
            _coverPlanner = GetComponent<PrototypeCoverPlanner>();
            if (_coverPlanner != null) _coverPlanner.Initialize(_experience);
            _identity = identity;
            _aiAction = null;
            _lastKnownThreat = null;
            _lastSeenAt = -100f;
            _lastSuppressedAt = -100f;
            _lastVerifiedSuppressingShooter = Vector3.zero;
            _lastGunshotAt = -100f;
            _lastFootstepAt = -100f;
            _lastRadioAt = -100f;
            _brain.Reset();
            _legacyBrain.SetSpawnPosition(transform.position);
            _legacyBrain.Reset();
            _knowledge.Clear();
            _locomotion.Reset();
            _nextLegacyArrivalCheckAt = Time.time + 0.75f;
            string[] names = { "Alex", "Morgan", "Sam", "Casey", "Jordan", "Riley", "Taylor", "Robin" };
            _displayName = names[UnityEngine.Random.Range(0, names.Length)] +
                           " (" + disposition + ")";
            _home = transform.position;
            _goal = _home;
            _nextThink = Time.time;
            _blockedSince = Time.time;
            _lastMotorProgress = transform.position;
            _lastMotorProgressAt = Time.time;
            _nextVaultAt = Time.time + 4f;
        }

        private void OnValidatedDamage(float damage, Vector3 impulse)
        {
            if (_vitals != null)
                _tactics.ReportDamage(_vitals.Health, 100f, Time.time);
            // An actual wound interrupts a surprise preparation, never
            // forcing the actor to wait to defend while being shot.
            _ambushVictim = null;
            _ambushArmedUntil = 0f;
            _ambushReleaseAt = 0f;
        }

        private void OnValidatedDeath()
        {
            // Dead members vacate their group immediately, not after corpse
            // cleanup. Remaining living members retain their saved group ID.
            Line2NPCGroupMember membership = GetComponent<Line2NPCGroupMember>();
            int oldGroup = membership != null ? membership.GroupId : 0;
            if (membership != null) membership.Leave();
            Died?.Invoke(this, oldGroup);
            if (_experience != null)
                _experience.ForgetOnDeathServer();
            if (_travelMemory != null)
                _travelMemory.ForgetOnDeathServer();
            _legacyBrain.Reset();
            _knowledge.Clear();
            Line2NpcEncounterPacing.Release(GetInstanceID());
            _ambushVictim = null;
            _ambushArmedUntil = 0f;
            _tactics.ClearGoal();
            _tacticalJourney.Reset();
            _locomotion.Reset();
            _vaultActive = false;
        }

        private void OnDestroy()
        {
            Line2NpcEncounterPacing.Release(GetInstanceID());
            if (_vitals != null)
            {
                _vitals.Damaged -= OnValidatedDamage;
                _vitals.Died -= OnValidatedDeath;
            }
        }

        internal void SetCombatMode(string mode)
        {
            _combatMode = mode == "passive" || mode == "total" ? mode : "normal";
            _tactics.CombatOnlyMode = _combatMode == "total";
            if (_inventory != null)
                _inventory.SetModeServer(_combatMode == "total");
            if (_combatMode == "passive")
            {
                _brain.Reset();
                _legacyBrain.Reset();
                _knowledge.Clear();
                Line2NpcEncounterPacing.Release(GetInstanceID());
                _ambushVictim = null;
                _ambushArmedUntil = 0f;
                _locomotion.Reset();
                _tactics.ClearGoal();
                _tacticalJourney.Reset();
                _aiAction = null;
                _threatVisible = false;
                _lastKnownThreat = null;
                _lastGunshotAt = -100f;
                _lastFootstepAt = -100f;
                _lastRadioAt = -100f;
                _goal = _home;
            }
        }

        /// <summary>
        /// Approaches a physically observed rival construction from validated
        /// ground only. Damage still requires ammunition/grenade and native
        /// line-of-sight. An AI or a base ledger cannot grant a blind hit.
        /// </summary>
        internal bool TryReactToWitnessedSquadCasualty(
            PrototypeActor fallen, int formerGroupId)
        {
            if (fallen == null || fallen == this || IsDead ||
                _combatMode == "passive" || _disposition != fallen._disposition ||
                fallen._vitals == null || !fallen.IsDead ||
                !fallen._vitals.HasVerifiedDamageOrigin ||
                Time.time - fallen._vitals.LastDamageAt > 3f ||
                _coverPlanner == null)
                return false;
            Line2NPCGroupMember member =
                GetComponent<Line2NPCGroupMember>();
            bool sameGroup = member != null && member.Joined &&
                member.GroupId == formerGroupId;
            Vector3 casualty = fallen.transform.position;
            Vector3 diff = casualty - transform.position;
            bool audible = diff.sqrMagnitude <= 8f * 8f;
            bool visible = false;
            if (!audible && diff.sqrMagnitude <= 24f * 24f)
            {
                Vector3 eye = transform.position + Vector3.up * 1.4f;
                Vector3 last = casualty + Vector3.up * 1.1f;
                Vector3 ray = last - eye;
                RaycastHit[] hits = new RaycastHit[16];
                int count = Physics.RaycastNonAlloc(eye,
                    ray.normalized, hits, ray.magnitude, ~0,
                    QueryTriggerInteraction.Ignore);
                if (count < hits.Length)
                {
                    visible = true;
                    for (int i = 0; i < count; ++i)
                    {
                        Transform touched = hits[i].transform;
                        if (touched == null || touched == transform ||
                            touched.IsChildOf(transform) ||
                            touched == fallen.transform ||
                            touched.IsChildOf(fallen.transform))
                            continue;
                        visible = false;
                        break;
                    }
                }
            }
            if (!Line2NpcSquadMoralePolicy.MayWithdraw(
                    Provider.isServer && !Dedicator.IsDedicatedServer &&
                        (Provider.clients == null ||
                         Provider.clients.Count <= 1),
                    sameGroup, true, audible || visible,
                    IsTacticallyRetreating,
                    transform.position, casualty) ||
                (_tactics.CurrentGoal == Line2MobileNPCTactics.Goal.SeekCover ||
                 _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Retreat))
                return false;
            Vector3 cover, retreat;
            if (!_coverPlanner.TryGetDefense(
                fallen._vitals.LastDamageOrigin, out cover, out retreat) ||
                !_coverPlanner.HasRetreat ||
                !_tactics.TryStartValidatedSquadWithdrawal(
                    retreat, Time.time))
                return false;
            ProbeLogger.Log("NPC_SQUAD_WITHDRAWAL: actor=" + _identity +
                " fallen=" + fallen.Identity + " group=" + formerGroupId +
                " knownBy=" + (audible ? "nearby-audio" : "personal-LOS") +
                " route=" + retreat.ToString("F1") +
                " (no inherited enemy target)");
            return true;
        }

        internal bool TryStageObservedGroupRegroup(Vector3 verifiedPoint)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                (Provider.clients != null && Provider.clients.Count > 1) ||
                IsDead || IsRecruited ||
                _tactics.CurrentGoal != Line2MobileNPCTactics.Goal.None ||
                (_combatPerception != null &&
                 _combatPerception.Kind !=
                    Line2NPCCombatPerception.TargetKind.None &&
                 _combatPerception.VisibleTarget != null) ||
                !Level.checkSafeIncludingClipVolumes(verifiedPoint) ||
                Mathf.Abs(verifiedPoint.y - transform.position.y) > 1.0f)
                return false;
            return _tactics.TryStartGroupRegroup(verifiedPoint, Time.time);
        }

        internal bool TryStageObservedRaid(Vector3 verifiedPoint)
        {
            if (!IsCombatEnabled || _disposition != "hostile" ||
                _tactics.CurrentGoal != Line2MobileNPCTactics.Goal.None ||
                _combatPerception == null ||
                (_combatPerception.Kind !=
                    Line2NPCCombatPerception.TargetKind.None &&
                 _combatPerception.VisibleTarget != null) ||
                !Level.checkSafeIncludingClipVolumes(verifiedPoint))
                return false;
            return _tactics.TryStartRaidStaging(verifiedPoint, Time.time);
        }

        /// <summary>Real NPC gunfire that missed still creates audible,
        /// short-lived combat pressure. Call ONLY after the native
        /// inventory has debited a shot against this personally visible
        /// hostile actor. It cannot deal damage or give us direct sight
        /// of an enemy we did not see.</summary>
        internal bool TryRegisterVerifiedSuppressingShot(
            PrototypeActor shooter, float firedAt)
        {
            if (!IsCombatEnabled || IsDead || _combatMode == "passive" ||
                shooter == null || shooter == this || shooter.IsDead ||
                !Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded)
                return false;
            Line2NPCCombatPerception sensor =
                shooter.GetComponent<Line2NPCCombatPerception>();
            float distance = Vector3.Distance(
                shooter.transform.position, transform.position);
            bool verified = sensor != null &&
                sensor.CanEngageNpcTarget(this) &&
                sensor.HasVerifiedSight(transform,
                    Line2NPCCombatPerception.TargetKind.Npc,
                    Line2NpcSuppressionPolicy.MaximumAudiblePressureDistance);
            if (!Line2NpcSuppressionPolicy.CanReport(
                true, verified, true, false, distance,
                firedAt, _lastSuppressedAt))
                return false;

            _lastVerifiedSuppressingShooter = shooter.transform.position;
            _lastSuppressedAt = firedAt;
            _tactics.ReportIncomingFire(firedAt);
            // A nearby actual muzzle report interrupts waiting in ambush,
            // but does not invent an exact future movement/aim point.
            _ambushVictim = null;
            _ambushArmedUntil = 0f;
            return true;
        }

        internal void HearVerifiedGunshot(Vector3 position, float heardAt)
        {
            if (_disposition != "hostile" || _combatMode == "passive") return;
            if (heardAt < 0 || float.IsNaN(heardAt) || float.IsInfinity(heardAt))
                return;
            // A sound provides only coarse direction, NEVER exact live aim.
            // The acoustic bridge has already verified that a native shot
            // occurred nearby. No line-of-sight is implied by hearing.
            if (heardAt - _lastGunshotAt < 0.9f)
                return; // One clue per burst, not a new endless chase per bullet.
            Vector3 heardSector = new Vector3(
                Mathf.Floor(position.x / 12f) * 12f + 6f,
                position.y,
                Mathf.Floor(position.z / 12f) * 12f + 6f);
            // An acoustic timestamp is evidence ONLY when the sensory
            // memory accepts that clue. Rejected noise must not also make
            // the higher-level brain invent a fresh 'heard threat' intent.
            // Genuine nearby gunfire still counts as incoming-fire pressure.
            if (_knowledge.TryObserveSound(heardSector, 0.64f, heardAt))
            {
                _lastHeardPoint = heardSector;
                _lastGunshotAt = heardAt;
                _legacyBrain.ReportNoise(heardSector, heardAt, 17f);
            }
            _tactics.ReportIncomingFire(heardAt);
        }

        internal void HearVerifiedFootstep(Vector3 position, float confidence, float at)
        {
            if (_disposition != "hostile" || _combatMode == "passive" ||
                float.IsNaN(at) || float.IsInfinity(at) || at < 0f ||
                at - _lastFootstepAt < 0.85f)
                return;
            // Player displacement was observed by the migrated footstep
            // sensor: approximate the acoustic area at 6m resolution.
            Vector3 heardFootstepSector = new Vector3(
                Mathf.Floor(position.x / 6f) * 6f + 3f, position.y,
                Mathf.Floor(position.z / 6f) * 6f + 3f);
            if (_knowledge.TryObserveSound(heardFootstepSector, confidence, at))
            {
                _lastHeardFootstepPoint = heardFootstepSector;
                _lastFootstepAt = at;
                _legacyBrain.ReportNoise(heardFootstepSector, at, 8f);
            }
        }

        internal void ReceiveVerifiedRadioReport(Vector3 sector, float observedAt)
        {
            if (_disposition != "hostile" || _combatMode == "passive" ||
                IsDead || float.IsNaN(observedAt) ||
                float.IsInfinity(observedAt) || observedAt < 0f ||
                observedAt - _lastRadioAt < 6f)
                return;
            // Faction/group/equipment authorization is verified by
            // Line2NPCRadioOutbox before this is called. The radio only
            // supplies an uncertain sector. Never grant LOS, an aim target,
            // ammo, direct damage rights or a permanent player identity.
            if (!_knowledge.TryObserveReport(sector, 0.58f, observedAt))
                return;
            _lastRadioAt = observedAt;
            _legacyBrain.ReportNoise(sector, observedAt, 15f);
            Vector3 locallyProposed, validated;
            if (Line2NpcRadioReinforcementPolicy.TryPropose(
                    Provider.isServer && !Dedicator.IsDedicatedServer &&
                        (Provider.clients == null ||
                         Provider.clients.Count <= 1),
                    true,
                    _combatPerception != null &&
                        _combatPerception.VisibleTarget != null,
                    RecentlyDamaged,
                    _tactics.CurrentGoal != Line2MobileNPCTactics.Goal.None,
                    _recruited,
                    transform.position, sector, out locallyProposed) &&
                _investigationProbe.TryValidate(
                    locallyProposed, transform.position, out validated) &&
                _tactics.TryStartRadioReinforcement(
                    validated, Time.time))
                ProbeLogger.Log("NPC_RADIO_REINFORCE: actor=" + _identity +
                    " sector=" + sector.ToString("F1") +
                    " waypoint=" + validated.ToString("F1") +
                    " (coarse, real floor; native A* must verify route)");
        }

        internal void ObserveThreat(Player player, out bool visible, out Vector3? lastKnown)
        {
            visible = false;
            lastKnown = null;
            if (_disposition != "hostile" || _combatMode == "passive") return;
            if (player != null && player.life != null &&
                !player.life.isDead &&
                (player.transform.position - transform.position).sqrMagnitude <= ThreatRadius * ThreatRadius &&
                CanSeePlayer(player))
            {
                visible = true;
                _threatVisible = true;
                _lastKnownThreat = player.transform.position;
                _lastSeenAt = Time.time;
                _knowledge.ObserveSight(player.transform.position, Time.time);
                _legacyBrain.ReportVisibleTarget(player.transform, Time.time);
                Line2NpcEncounterPacing.Release(GetInstanceID());
            }
            else
            {
                _threatVisible = false;
                if (Time.time - _lastSeenAt > 8f)
                    _lastKnownThreat = null;
            }
            if (_lastKnownThreat.HasValue && Time.time - _lastSeenAt <= 8f)
                lastKnown = _lastKnownThreat;
            else if (Time.time - _lastGunshotAt <= 7f)
                lastKnown = _lastHeardPoint;
            else if (Time.time - _lastFootstepAt <= 4f)
                lastKnown = _lastHeardFootstepPoint;
            else if (_legacyBrain.CurrentState ==
                         Line2MobileNPCBrain.State.Investigating)
            {
                // This is a decaying, uncertain area clue from the
                // imported Line 1 memory. It does NOT grant LOS, damage,
                // current player position, or remote target identity.
                Vector3 center;
                float confidence, uncertainty;
                Line2MobileNPCPlayerKnowledge.Sense source;
                if (_knowledge.TryGetEvidence(Time.time, out center,
                                               out confidence, out uncertainty,
                                               out source) && confidence >= 0.25f)
                    lastKnown = center;
            }
        }

        internal bool TryApplyAiAction(string action, float expiresRealtime)
        {
            if (_disposition != "hostile" || _combatMode == "passive") return false;
            if (action != "idle" && action != "engage" && action != "investigate" &&
                action != "seek_cover" && action != "retreat")
                return false;
            // Defensive decisions require locally measured damage and geometry.
            // A localhost response can never authorize a blind or invented route.
            if (action == "seek_cover" && !HasVerifiedCover) return false;
            if (action == "retreat" && (!RecentlyDamaged || HealthFraction >= 0.30f ||
                _coverPlanner == null || !_coverPlanner.HasRetreat)) return false;
            if (expiresRealtime <= Time.realtimeSinceStartup ||
                expiresRealtime - Time.realtimeSinceStartup > 1.0f)
                return false;
            if (action == "engage")
            {
                // The sensor may have last seen the player up to one think
                // interval ago. Never accept a delayed remote engage order
                // without fresh, firsthand game-side line of sight.
                Player livePlayer = Player.player;
                if (!_threatVisible || livePlayer == null ||
                    livePlayer.life == null || livePlayer.life.isDead ||
                    (livePlayer.transform.position - transform.position).sqrMagnitude >
                        ThreatRadius * ThreatRadius ||
                    !CanSeePlayer(livePlayer))
                    return false;
            }
            if (action == "investigate" && !_lastKnownThreat.HasValue &&
                Time.time - _lastGunshotAt > 7f &&
                Time.time - _lastFootstepAt > 4f &&
                !(_legacyBrain.HasDestination &&
                  _legacyBrain.CurrentState ==
                      Line2MobileNPCBrain.State.Investigating))
                return false;
            // A localhost suggestion is advisory input to the next local
            // decision tick. It must not write the motor goal immediately:
            // doing so could replace an ongoing cover, retreat, flank,
            // regroup or raid route between local brain evaluations.
            _aiAction = action;
            _aiValidUntil = expiresRealtime;
            return true;
        }

        private void Update()
        {
            if (_controller == null || IsDead || !Provider.isServer || !Level.isLoaded) return;
            Player player = Player.player;
            if (Time.time >= _nextThink)
            {
                _nextThink = Time.time + 0.4f;

                // Own game senses are authoritative. Python suggestions
                // cannot create invisible enemies or bypass passive mode.
                bool visible;
                Vector3? lastKnown;
                ObserveThreat(player, out visible, out lastKnown);
                _legacyBrain.Tick(Time.time, visible);
                // Line1-style search spiral endpoints are hypotheses. Never
                // send an unsupported rooftop, invalid floor or obstructed
                // capsule to A* as an actionable waypoint. If the native
                // route is proven disconnected, advance the finite sweep.
                if (_legacyBrain.CurrentState ==
                    Line2MobileNPCBrain.State.Investigating &&
                    Time.time >= _nextInvestigationCheckAt)
                {
                    _nextInvestigationCheckAt = Time.time + 2.0f;
                    // A disconnected route only invalidates the clue when the
                    // clue actually owns locomotion. A stale A* failure from
                    // retreat, regroup or another goal must not consume steps
                    // from this finite investigation.
                    if (_astarFollower != null &&
                        !_tactics.HasTacticalDestination &&
                        _astarFollower.Status == "line1-no-connected-route" &&
                        (_goal - _legacyBrain.Destination).sqrMagnitude < 2.5f)
                        _legacyBrain.ReportDestinationReached();
                    ValidateInvestigationSweep();
                }
                bool nearPlayer = player != null &&
                    (player.transform.position - transform.position).sqrMagnitude < 32f * 32f;
                string validSuggestion = _aiAction != null &&
                    Time.realtimeSinceStartup <= _aiValidUntil ? _aiAction : null;
                bool suppressedByVerifiedShot =
                    Line2NpcSuppressionPolicy.IsCurrent(
                        Time.time, _lastSuppressedAt);
                bool hasVerifiedThreatOrigin =
                    RecentlyDamaged && _vitals != null &&
                    _vitals.HasVerifiedDamageOrigin;
                bool shouldDefend = hasVerifiedThreatOrigin ||
                    suppressedByVerifiedShot;
                Vector3 defenseSource = hasVerifiedThreatOrigin
                    ? _vitals.LastDamageOrigin
                    : _lastVerifiedSuppressingShooter;
                bool hasCover = false;
                _hasRetreat = false;
                if (shouldDefend && _coverPlanner != null)
                {
                    _coverPlanner.TryGetDefense(defenseSource,
                        out _coverGoal, out _retreatGoal);
                    hasCover = _coverPlanner.HasCover;
                    _hasRetreat = _coverPlanner.HasRetreat;
                }
                if (shouldDefend && _combatMode != "passive" &&
                    (_disposition == "hostile" || _disposition == "friendly"))
                {
                    // A physically blocked point cannot immediately become
                    // a valid cover request again on the next think tick.
                    if (hasCover &&
                        _lastFailedTacticalKind ==
                            Line2MobileNPCTactics.Goal.SeekCover &&
                        Time.time < _tacticalRetryAfter &&
                        (_coverGoal - _lastFailedTacticalPoint).sqrMagnitude < 5f)
                        hasCover = false;
                    if (_hasRetreat &&
                        _lastFailedTacticalKind ==
                            Line2MobileNPCTactics.Goal.Retreat &&
                        Time.time < _tacticalRetryAfter &&
                        (_retreatGoal - _lastFailedTacticalPoint).sqrMagnitude < 5f)
                        _hasRetreat = false;
                    Vector3? coverPoint = hasCover ? (Vector3?)_coverGoal : null;
                    bool canReturnFire = _rangedExecutor != null &&
                        _rangedExecutor.HasUsableGun &&
                        _combatPerception != null &&
                        _combatPerception.Kind !=
                            Line2NPCCombatPerception.TargetKind.None &&
                        _combatPerception.VisibleTarget != null &&
                        _combatPerception.HasVerifiedSight(
                            _combatPerception.VisibleTarget,
                            _combatPerception.Kind, 35f);
                    _tactics.TryChooseDefensiveGoal(transform.position,
                        coverPoint, defenseSource, Time.time, canReturnFire);
                    if (_tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Retreat &&
                        !_tactics.HasTacticalDestination && _hasRetreat)
                        _tactics.ReportValidatedRetreatPoint(_retreatGoal, Time.time);
                }
                else if (_tactics.IsGoalExpired(Time.time))
                    _tactics.ClearGoal();
                // Exact Line1 BrainDriver priority: personally observed
                // combat immediately cancels noncombat group rendezvous
                // or preliminary base reconnaissance. Never allow a stale
                // regroup/raid waypoint to override new defensive action.
                if (_combatPerception != null &&
                    _combatPerception.Kind !=
                        Line2NPCCombatPerception.TargetKind.None &&
                    _combatPerception.VisibleTarget != null &&
                    (_tactics.CurrentGoal ==
                        Line2MobileNPCTactics.Goal.Regroup ||
                     _tactics.CurrentGoal ==
                        Line2MobileNPCTactics.Goal.RaidStaging ||
                     _tactics.CurrentGoal ==
                        Line2MobileNPCTactics.Goal.Stalk ||
                     _tactics.CurrentGoal ==
                        Line2MobileNPCTactics.Goal.Reinforce))
                {
                    if (_tactics.CurrentGoal ==
                        Line2MobileNPCTactics.Goal.Stalk)
                        _ambushArmedUntil = Time.time + 2.5f;
                    _tactics.ClearGoal();
                    _tacticalJourney.Reset();
                }
                TryPlanConcealedApproach();
                // Port Line 1 reload-cover behavior without treating a
                // magazine change as incoming fire. This NPC must personally
                // see a living threat and the geometry must offer real cover.
                if (_rangedExecutor != null && _coverPlanner != null &&
                    Time.time >= _nextReloadCoverAt &&
                    _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.None &&
                    _combatPerception != null &&
                    _combatPerception.VisibleTarget != null &&
                    !RecentlyDamaged &&
                    Line2NpcFiringPositionPolicy.CanSeekReloadCover(
                        _rangedExecutor.Reloading,
                        _combatPerception.HasVerifiedSight(
                            _combatPerception.VisibleTarget,
                            _combatPerception.Kind, 45f),
                        false,
                        Vector3.Distance(transform.position,
                            _combatPerception.LastVisiblePosition)))
                {
                    _nextReloadCoverAt = Time.time + 5f;
                    Vector3 actualCover, escape;
                    if (_coverPlanner.TryGetDefense(
                            _combatPerception.LastVisiblePosition,
                            out actualCover, out escape) &&
                        _coverPlanner.HasCover &&
                        Level.checkSafeIncludingClipVolumes(actualCover) &&
                        !(_lastFailedTacticalKind ==
                            Line2MobileNPCTactics.Goal.SeekCover &&
                          Time.time < _tacticalRetryAfter &&
                          (actualCover - _lastFailedTacticalPoint).sqrMagnitude < 5f) &&
                        _tactics.TryStartReloadCover(actualCover, Time.time))
                        ProbeLogger.Log("NPC_RELOAD_COVER: actor=" + _identity +
                            " point=" + actualCover.ToString("F1") +
                            " (verified real solid shielding; A* still required)");
                }
                // Line 1 tactical flank is optional and never allowed to
                // override emergency cover or a committed retreat.
                if (_tactics.CurrentGoal == Line2MobileNPCTactics.Goal.None &&
                    Time.time >= _nextFlankAt && !RecentlyDamaged &&
                    _combatMode != "passive" && HasUsableGun &&
                    _combatPerception != null &&
                    _combatPerception.Kind !=
                        Line2NPCCombatPerception.TargetKind.None &&
                    _combatPerception.VisibleTarget != null)
                {
                    Transform opponent = _combatPerception.VisibleTarget;
                    Vector3 flank;
                    if (_combatPerception.HasVerifiedSight(opponent,
                        _combatPerception.Kind, 45f) &&
                        TryFindVerifiedFlank(opponent.position, out flank) &&
                        !(_lastFailedTacticalKind ==
                            Line2MobileNPCTactics.Goal.Flank &&
                          Time.time < _tacticalRetryAfter &&
                          (flank - _lastFailedTacticalPoint).sqrMagnitude < 5f) &&
                        _tactics.TryStartFlank(flank, Time.time))
                    {
                        _nextFlankAt = Time.time + 32f;
                        ProbeLogger.Log("NPC_TACTICAL_FLANK: actor=" + _identity +
                            " destination=" + flank.ToString("F1"));
                    }
                    else _nextFlankAt = Time.time + 8f;
                }
                var facts = new NpcDecisionInput
                {
                    IsHostile = _disposition == "hostile",
                    // Follow is an explicitly accepted alliance contract:
                    // ordinary friendly NPCs keep their own A-Life routine.
                    IsFriendly = _disposition == "friendly" && _recruited,
                    PassiveMode = _combatMode == "passive",
                    PlayerNearby = nearPlayer,
                    ThreatVisible = visible,
                    HasRecentMemory = lastKnown.HasValue,
                    HeardThreat = Time.time - _lastGunshotAt <= 7f ||
                                  Time.time - _lastFootstepAt <= 4f ||
                                  Time.time - _lastRadioAt <= 6f,
                    // Only real explosion hits currently update HP and
                    // the threat point. The cover planner checks real solid
                    // geometry, while A* separately owns route feasibility.
                    HasRealHealth = _vitals != null,
                    HealthFraction = HealthFraction,
                    UnderFire = RecentlyDamaged || suppressedByVerifiedShot,
                    HasVerifiedCover = hasCover,
                    RemoteSuggestion = validSuggestion
                };
                NpcIntent intent = _brain.Evaluate(Time.time, facts);
                switch (intent)
                {
                    case NpcIntent.Follow:
                        if (nearPlayer && player != null)
                        {
                            _legacyBrain.SetFollowDestination(
                                player.transform.position - player.transform.forward * 3f);
                            _goal = _legacyBrain.Destination;
                        }
                        break;
                    case NpcIntent.Pursue:
                        if (_legacyBrain.HasDestination &&
                            _legacyBrain.CurrentState == Line2MobileNPCBrain.State.Pursuing)
                            _goal = _legacyBrain.Destination;
                        else if (visible && player != null)
                            _goal = player.transform.position;
                        break;
                    case NpcIntent.Investigate:
                        if (_legacyBrain.HasDestination)
                            _goal = _legacyBrain.Destination;
                        else if (lastKnown.HasValue)
                            _goal = lastKnown.Value;
                        break;
                    case NpcIntent.Retreat:
                        // Emergency withdrawal only to a locally verified
                        // ground candidate; A* must still find the route.
                        _goal = _hasRetreat ? _retreatGoal : transform.position;
                        break;
                    case NpcIntent.SeekCover:
                        _goal = hasCover ? _coverGoal : transform.position;
                        break;
                    case NpcIntent.Patrol:
                        // The original brain continues a bounded physical
                        // search after the short tactical target memory expires.
                        if (_disposition == "hostile" && _legacyBrain.HasDestination &&
                            (_legacyBrain.CurrentState == Line2MobileNPCBrain.State.Investigating ||
                             _legacyBrain.CurrentState == Line2MobileNPCBrain.State.Returning))
                        {
                            _goal = _legacyBrain.Destination;
                            break;
                        }
                        Vector3 routineGoal;
                        if (_worldSurvey != null &&
                            _worldSurvey.TryGetRoutineGoalServer(out routineGoal))
                        {
                            _goal = routineGoal;
                            break;
                        }
                        if ((_goal - transform.position).sqrMagnitude < 2.5f ||
                            Time.time >= _blockedSince + 7f)
                        {
                            float rad = UnityEngine.Random.Range(0f, 6.28318f);
                            _goal = _home + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) *
                                UnityEngine.Random.Range(4f, 15f);
                            _blockedSince = Time.time;
                        }
                        break;
                    default:
                        _goal = _home;
                        break;
                }
                // The Line 1 sensory priority is player > opposing NPC >
                // zombie. Hostile player pursuit still wins, while
                // secondary live visible targets can drive native A* routes.
                if (_combatPerception != null &&
                    _combatPerception.Kind !=
                        Line2NPCCombatPerception.TargetKind.None &&
                    _combatPerception.Kind !=
                        Line2NPCCombatPerception.TargetKind.Player &&
                    _combatPerception.VisibleTarget != null &&
                    !_threatVisible && _combatMode != "passive" &&
                    _tactics.CurrentGoal != Line2MobileNPCTactics.Goal.Retreat &&
                    _tactics.CurrentGoal != Line2MobileNPCTactics.Goal.SeekCover)
                    _goal = _combatPerception.LastVisiblePosition;

                if ((_tactics.CurrentGoal == Line2MobileNPCTactics.Goal.SeekCover ||
                     _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Retreat ||
                     _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Flank ||
                     _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.RaidStaging ||
                     _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Regroup ||
                     _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Stalk ||
                     _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Reinforce) &&
                    _tactics.HasTacticalDestination &&
                    !(_tactics.IsGoalExpired(Time.time)) &&
                    _combatMode != "passive")
                {
                    _goal = _tactics.TacticalDestination;
                    // Cover/retreat movement wins over renewed aggression.
                }
                else if ((_tactics.CurrentGoal ==
                            Line2MobileNPCTactics.Goal.Retreat ||
                          _tactics.CurrentGoal ==
                            Line2MobileNPCTactics.Goal.SeekCover) &&
                         !_tactics.HasTacticalDestination)
                {
                    // Exact original Line 1 BrainDriver defensive pending
                    // semantics: without a verified escape path, hold ground
                    // and return fire only when gun/LOS safety allows it.
                    // Never fall through to player pursuit at this stage.
                    _goal = transform.position;
                }
                else if (!_recruited && GetComponent<Line2NpcNeeds>() != null &&
                    GetComponent<Line2NpcNeeds>().IsUrgent &&
                    (_combatPerception == null ||
                     _combatPerception.Kind == Line2NPCCombatPerception.TargetKind.None))
                {
                    // Exhausted survivors return to their group's home anchor
                    // without abandoning an observed opponent or defense.
                    _goal = _home;
                }
                else if (_recruited && player != null &&
                    (player.transform.position - transform.position).sqrMagnitude < 80f * 80f &&
                    (_combatPerception == null ||
                     _combatPerception.Kind == Line2NPCCombatPerception.TargetKind.None))
                {
                    // Persistent alliance follows the player, but never
                    // overrides an actual visible enemy or defensive escape.
                    _goal = player.transform.position -
                        player.transform.forward * 3.5f;
                }

                // A survivor who can actually fire from a validated lane
                // should not sprint into melee range. Never freeze a search,
                // an unarmed NPC, a retreat, or an occluded target. Native
                // weapon range and a current local sensor determine the hold.
                if (_tactics.CurrentGoal == Line2MobileNPCTactics.Goal.None &&
                    _combatMode != "passive" && !RecentlyDamaged &&
                    HasUsableGun && _inventory != null &&
                    _combatPerception != null &&
                    _combatPerception.Kind !=
                        Line2NPCCombatPerception.TargetKind.None &&
                    _combatPerception.VisibleTarget != null)
                {
                    uint ownedGunInstance;
                    Item actualGun;
                    ItemGunAsset gunAsset;
                    if (_inventory.TryGetBestGun(out ownedGunInstance,
                            out actualGun, out gunAsset) &&
                        gunAsset != null)
                    {
                        Transform enemy = _combatPerception.VisibleTarget;
                        float firingDistance = Vector3.Distance(
                            transform.position, enemy.position);
                        bool sightNow = _combatPerception.HasVerifiedSight(
                            enemy, _combatPerception.Kind,
                            Mathf.Min(65f, gunAsset.range));
                        if (Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                            sightNow, true, false, RecentlyDamaged,
                            firingDistance, gunAsset.range))
                            _goal = transform.position;
                    }
                }
            }

            Vector3 delta = _goal - transform.position;
            bool requiresVerticalRoute = Mathf.Abs(delta.y) > 1.25f;
            delta.y = 0f;
            bool needsRoute = delta.sqrMagnitude > 1.5f || requiresVerticalRoute;
            var locomotion = new Line2MobileNPCLocomotionPolicy.Situation
            {
                HasDestination = needsRoute,
                TargetVisible = _threatVisible,
                Swimming = false, // Separate native water traversal adapter required.
                UnderFire = RecentlyDamaged,
                Injured = HealthFraction < 0.55f,
                NearTarget = delta.sqrMagnitude < 5f * 5f,
                InCover = false,
                Investigating = _legacyBrain.CurrentState ==
                    Line2MobileNPCBrain.State.Investigating,
                Stalking = _tactics.CurrentGoal ==
                    Line2MobileNPCTactics.Goal.Stalk,
                Retreating = _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Retreat,
                Pursuing = _brain.Intent == NpcIntent.Pursue,
                Flanking = _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Flank,
                DestinationDistance = Mathf.Sqrt(delta.sqrMagnitude)
            };
            var gait = _locomotion.Decide(locomotion, Time.time);
            float speed = gait == Line2MobileNPCLocomotionPolicy.Gait.Sprint ? 3.0f
                : gait == Line2MobileNPCLocomotionPolicy.Gait.Sneak ? 1.15f
                : gait == Line2MobileNPCLocomotionPolicy.Gait.Hold ? 0f : 1.8f;
            if (_injuries != null)
                speed *= Mathf.Clamp(_injuries.MovementMultiplier, 0.1f, 1f);
            Vector3 direction = delta.sqrMagnitude > 1.5f && speed > 0f
                ? delta.normalized : Vector3.zero;
            // Vertical goals on another floor have zero horizontal delta.
            // Still consult native A* for a real staircase rather than
            // becoming idle directly below the destination. Do not invent
            // vertical movement or force the CharacterController upward.
            if (needsRoute && speed > 0f && _astarFollower != null)
                direction = _astarFollower.GetHeading(_goal);
            else if (direction.sqrMagnitude > 0 && _navigation != null)
                direction = _navigation.GetHeading(direction, _goal);
            if (direction.sqrMagnitude > 0 && _doors != null &&
                _doors.TryHandleBlockingDoorServer(
                    transform.position + Vector3.up * 1.0f,
                    direction, 0.20f, 1.75f, RayMasks.BLOCK_COLLISION))
            {
                // A verified unlocked door is opening through native
                // BarricadeManager or ObjectManager. Hold movement until
                // its real collider/animation clears. Never phase through.
                direction = Vector3.zero;
            }
            else if (direction.sqrMagnitude < 0.1f && _doors != null &&
                     delta.sqrMagnitude > 2f)
            {
                // The native Seeker may still be waiting for a graph path.
                // Only try physically blocking doors in the goal direction.
                // This never grants a path that A* has not verified.
                Vector3 intended = delta.normalized;
                _doors.TryHandleBlockingDoorServer(
                    transform.position + Vector3.up * 1.0f,
                    intended, 0.20f, 1.6f, RayMasks.BLOCK_COLLISION);
            }
            // The original Line 1 jump preflight can clear a REAL low sill
            // with ordinary gravity and CharacterController movement.
            // Never invent a portal through intact glass, a high wall or
            // a destination lacking observed support/capsule clearance.
            ApplyVerifiedVault(ref direction, ref speed, delta);
            // Line 1 melee faces the genuinely visible enemy even when
            // CharacterController is already close enough to stop walking.
            Vector3 face = direction;
            if (face.sqrMagnitude <= 0.01f && _combatPerception != null &&
                _combatPerception.VisibleTarget != null &&
                _combatPerception.Kind != Line2NPCCombatPerception.TargetKind.None)
            {
                face = _combatPerception.LastVisiblePosition - transform.position;
                face.y = 0f;
                if (face.sqrMagnitude > 0.01f) face.Normalize();
            }
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(face), 420f * Time.deltaTime);
            else if (delta.sqrMagnitude > 2f)
                _blockedSince = Time.time - 6.8f;
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1.0f;
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;
            // Complete the actual physics move before interpreting arrival.
            // The legacy investigation state may advance only from measured
            // CharacterController position, never from a predicted waypoint.
            Vector3 beforeMove = transform.position;
            _controller.Move((direction * speed +
                              Vector3.up * _verticalVelocity) * Time.deltaTime);
            // Physics, not the requested heading, determines whether this
            // survivor is walking. A blocked wall must not produce footstep
            // animation, movement accuracy penalties or moving status.
            Vector3 actualStep = transform.position - beforeMove;
            actualStep.y = 0f;
            IsMoving = Time.deltaTime > 0f &&
                actualStep.sqrMagnitude > (Time.deltaTime * 0.12f) *
                    (Time.deltaTime * 0.12f);
            if (_animation != null) _animation.SetMoving(IsMoving);
            // Arrival is a physical observation, not merely a requested
            // waypoint. The migrated Line 1 brain advances its three-point
            // investigation spiral only when the CharacterController arrives.
            // Do not credit an investigation waypoint while another system
            // (combat retreat, cover, regroup, etc.) owns locomotion.
            // Merely passing close to a clue during an unrelated journey is
            // not evidence that the clue was deliberately investigated.
            if (_legacyBrain.HasDestination && !_tactics.HasTacticalDestination &&
                (_goal - _legacyBrain.Destination).sqrMagnitude < 2.5f &&
                Time.time >= _nextLegacyArrivalCheckAt)
            {
                Vector3 difference = _legacyBrain.Destination - transform.position;
                if (Mathf.Abs(difference.y) < 1.35f &&
                    new Vector2(difference.x, difference.z).sqrMagnitude < 1.7f * 1.7f)
                {
                    _legacyBrain.ReportDestinationReached();
                    _nextLegacyArrivalCheckAt = Time.time + 0.65f;
                    ValidateInvestigationSweep();
                    TryPromoteVerifiedEvidenceHint();
                    if (_legacyBrain.HasDestination &&
                        !_tactics.HasTacticalDestination)
                        _goal = _legacyBrain.Destination;
                }
            }
            MonitorTacticalProgress();
        }

        private void TryPlanConcealedApproach()
        {
            if (_disposition != "hostile" || _combatMode == "passive" ||
                _tactics.CurrentGoal != Line2MobileNPCTactics.Goal.None ||
                _legacyBrain.CurrentState !=
                    Line2MobileNPCBrain.State.Investigating ||
                _threatVisible || RecentlyDamaged ||
                Time.time < _nextStalkAt ||
                (_combatPerception != null &&
                 _combatPerception.VisibleTarget != null))
                return;
            _nextStalkAt = Time.time + 9.0f;
            Vector3 clue;
            float confidence, uncertainty;
            Line2MobileNPCPlayerKnowledge.Sense evidence;
            if (!_knowledge.TryGetEvidence(Time.time, out clue,
                out confidence, out uncertainty, out evidence) ||
                evidence == Line2MobileNPCPlayerKnowledge.Sense.Report)
                return; // radio bearing alone cannot plan a concealed ambush
            Vector3 diff = clue - transform.position;
            diff.y = 0f;
            if (!Line2NpcAmbushPolicy.CanPlan(true,
                IsCombatEnabled, false, RecentlyDamaged, true,
                _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.None,
                confidence, uncertainty, diff.magnitude) ||
                !TryLeaseOptionalEvidenceSearch())
                return;
            Vector3 concealed;
            if (!_stalkPlanner.TryPlan(transform.position, clue,
                transform, out concealed) ||
                !_tactics.TryStartStalk(concealed, Time.time))
            {
                Line2NpcEncounterPacing.Release(GetInstanceID());
                return;
            }
            ProbeLogger.Log("NPC_STALK_SHELTERED: actor=" + _identity +
                " evidence=" + evidence + " uncertainty=" +
                uncertainty.ToString("F1") + " point=" +
                concealed.ToString("F1") +
                " (observed clue only; A* still required)");
        }

        /// <summary>
        /// A prepared ambush delays an ordinary shot for at most 0.85 s
        /// against a target THIS NPC has now personally seen. Does not
        /// authorize attack; every weapon executor still checks its own
        /// vanilla sight, faction, distance, angle and finite ammunition.
        /// </summary>
        internal bool ShouldHoldPreparedAmbushShot(Transform target)
        {
            if (_disposition != "hostile" || _combatMode == "passive" ||
                target == null || IsDead || RecentlyDamaged ||
                _combatPerception == null ||
                _combatPerception.VisibleTarget != target ||
                _combatPerception.Kind ==
                    Line2NPCCombatPerception.TargetKind.None)
                return false;

            if (_ambushVictim != null)
            {
                if (_ambushVictim != target)
                {
                    _ambushVictim = null;
                    return false;
                }
                bool wait = Line2NpcAmbushPolicy.WithholdPreparedAttack(
                    true, true, Time.time, _ambushReleaseAt);
                if (!wait) _ambushVictim = null;
                return wait;
            }
            if (_tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Stalk)
            {
                // First confirmed enemy cancels the stalk movement, but
                // does not create an attack against its remembered location.
                _tactics.ClearGoal();
                _tacticalJourney.Reset();
                _ambushArmedUntil = Time.time + 2.5f;
            }
            if (Time.time > _ambushArmedUntil ||
                !_combatPerception.HasVerifiedSight(target,
                    _combatPerception.Kind, 18f))
                return false;
            _ambushVictim = target;
            _ambushArmedUntil = 0f;
            _ambushReleaseAt = Time.time +
                UnityEngine.Random.Range(0.45f, 0.85f);
            return true;
        }

        private bool TryLeaseOptionalEvidenceSearch()
        {
            if (_disposition != "hostile" || _combatMode == "passive" ||
                _threatVisible || RecentlyDamaged ||
                _legacyBrain.CurrentState !=
                    Line2MobileNPCBrain.State.Investigating)
                return false;
            Line2NPCGroupMember member =
                GetComponent<Line2NPCGroupMember>();
            int groupID = member != null ? member.GroupId : 0;
            return Line2NpcEncounterPacing.TryLease(
                groupID, GetInstanceID(), Time.time, true);
        }

        private void ValidateInvestigationSweep()
        {
            if (_legacyBrain.CurrentState !=
                Line2MobileNPCBrain.State.Investigating ||
                _tactics.HasTacticalDestination)
                return;
            // The underlying Line1 state machine has a maximum of three
            // additional spiral waypoints. We may skip invalid ones now,
            // but never start a new infinite investigation timer.
            for (int attempt = 0; attempt < 4 &&
                _legacyBrain.CurrentState ==
                    Line2MobileNPCBrain.State.Investigating; attempt++)
            {
                Vector3 endpoint;
                if (_legacyBrain.HasDestination &&
                    _investigationProbe.TryValidate(
                        _legacyBrain.Destination, transform.position,
                        out endpoint))
                {
                    _legacyBrain.TrySetInvestigationHint(endpoint, Time.time);
                    return;
                }
                if (Time.time >= _nextSearchDiagnosticAt)
                {
                    _nextSearchDiagnosticAt = Time.time + 15f;
                    ProbeLogger.Log("NPC_SEARCH_POINT_REJECTED: actor=" +
                        _identity + " proposed=" +
                        _legacyBrain.Destination.ToString("F1") +
                        " (no verified same-floor footprint)");
                }
                _legacyBrain.ReportDestinationReached();
            }
        }

        private void TryPromoteVerifiedEvidenceHint()
        {
            if (_disposition != "hostile" || _combatMode == "passive" ||
                _legacyBrain.CurrentState !=
                    Line2MobileNPCBrain.State.Investigating ||
                !_legacyBrain.HasDestination ||
                _tactics.CurrentGoal != Line2MobileNPCTactics.Goal.None ||
                _threatVisible || Time.time < _nextEvidenceHintAt)
                return;
            _nextEvidenceHintAt = Time.time + 6.0f;
            Vector3 observed;
            float confidence, uncertainty;
            Line2MobileNPCPlayerKnowledge.Sense sense;
            if (!_knowledge.TryGetEvidence(Time.time, out observed,
                out confidence, out uncertainty, out sense) ||
                !TryLeaseOptionalEvidenceSearch())
                return;
            for (int i = 0; i < 2; ++i)
            {
                Vector3 proposed, verified;
                if (!_knowledge.TrySuggestSearchPoint(Time.time,
                    out proposed))
                {
                    Line2NpcEncounterPacing.Release(GetInstanceID());
                    return;
                }
                if (!Line2NpcInvestigationPolicy.MayProbe(
                    true, true, false, confidence, uncertainty,
                    observed, proposed, transform.position,
                    Mathf.Clamp(uncertainty + 3f, 4f, 17f)) ||
                    !_investigationProbe.TryValidate(proposed,
                        transform.position, out verified) ||
                    !_legacyBrain.TrySetInvestigationHint(verified, Time.time))
                    continue;
                _knowledge.ConfirmSearchPointAccepted();
                ProbeLogger.Log("NPC_SEARCH_HYPOTHESIS_ACCEPTED: actor=" +
                    _identity + " evidence=" + sense +
                    " uncertainty=" + uncertainty.ToString("F1") +
                    " goal=" + verified.ToString("F1") +
                    " (no enemy Transform; A* must prove route)");
                return;
            }
            // No valid floor/capsule was found for this optional clue. Free
            // the squad's search budget for a different investigator.
            Line2NpcEncounterPacing.Release(GetInstanceID());
        }

        private void MonitorTacticalProgress()
        {
            bool hasTactical = _tactics.HasTacticalDestination &&
                _tactics.CurrentGoal != Line2MobileNPCTactics.Goal.None;
            if (!hasTactical)
            {
                _tacticalJourney.Reset();
                return;
            }
            Line2MobileNPCTactics.Goal current = _tactics.CurrentGoal;
            Vector3 destination = _tactics.TacticalDestination;
            string pathStatus = _astarFollower != null ?
                _astarFollower.Status : "";
            bool pathPending = pathStatus.IndexOf("waiting",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathStatus.IndexOf("search",
                StringComparison.OrdinalIgnoreCase) >= 0 ||
                pathStatus.IndexOf("pending",
                StringComparison.OrdinalIgnoreCase) >= 0;
            Line2NpcTacticalJourneyWatchdog.Outcome progress =
                _tacticalJourney.Observe(current, destination,
                    transform.position, Time.time, pathPending);
            if (progress == Line2NpcTacticalJourneyWatchdog.Outcome.Reached)
            {
                if (current == Line2MobileNPCTactics.Goal.Stalk)
                {
                    _ambushArmedUntil = Time.time + 4.0f;
                    _nextStalkAt = Time.time + 18f;
                }
                if (_experience != null &&
                    current == Line2MobileNPCTactics.Goal.SeekCover)
                    _experience.RecordOwnOutcomeServer(
                        Line2MobileNPCAdaptiveMemory.ExperienceKind.CoverAccess,
                        destination, true);
                _tactics.ReportGoalReached();
                return;
            }
            if (progress == Line2NpcTacticalJourneyWatchdog.Outcome.Holding)
                return; // keep finite retreat/flank goal until deadline
            if (progress != Line2NpcTacticalJourneyWatchdog.Outcome.Blocked)
                return;
            // The native CharacterController never made progress for this
            // committed tactical journey. Register failure for next candidate
            // ranking; no snap, warp, collider disabling or free retreat.
            _lastFailedTacticalPoint = destination;
            _lastFailedTacticalKind = current;
            _tacticalRetryAfter = Time.time + 16f;
            if (_experience != null &&
                current == Line2MobileNPCTactics.Goal.SeekCover)
                _experience.RecordOwnOutcomeServer(
                    Line2MobileNPCAdaptiveMemory.ExperienceKind.CoverAccess,
                    destination, false);
            _tactics.ClearGoal();
            _nextFlankAt = Mathf.Max(_nextFlankAt, Time.time + 6f);
            if (current == Line2MobileNPCTactics.Goal.Stalk)
            {
                _ambushArmedUntil = 0f;
                _ambushVictim = null;
                _nextStalkAt = Time.time + 22f;
            }
            _goal = transform.position; // prevent stale pursuit on this frame
            if (Time.time >= _nextTacticalBlockedLog)
            {
                _nextTacticalBlockedLog = Time.time + 12f;
                ProbeLogger.Log("NPC_TACTICAL_ROUTE_BLOCKED: actor=" + _identity +
                    " task=" + current + " blockedGoal=" +
                    destination.ToString("F1") + " nativeRoute=" +
                    pathStatus + " (physical progress failure)");
            }
        }

        private void OnGUI()
        {
            Camera cam = Camera.main;
            if (cam == null || Player.player == null || string.IsNullOrEmpty(_displayName))
                return;
            Vector3 cameraPosition = cam.transform.position;
            if ((cameraPosition - transform.position).sqrMagnitude > 17f * 17f)
                return;
            Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.25f);
            if (screen.z <= 0 || screen.x < 0 || screen.x > Screen.width ||
                screen.y < 0 || screen.y > Screen.height)
                return;
            Rect bounds = new Rect(screen.x - 100f, Screen.height - screen.y - 12f,
                                   200f, 22f);
            GUI.Label(bounds, _displayName + (_vitals != null
                ? " HP " + Mathf.CeilToInt(_vitals.Health) : ""));
        }

        private void ApplyVerifiedVault(ref Vector3 heading,
            ref float speed, Vector3 delta)
        {
            if (_vault == null || _controller == null || !Level.isLoaded)
                return;
            if (_vaultActive)
            {
                float elapsed = Time.time - _vaultStartedAt;
                float progressed = Vector3.Dot(
                    transform.position - _vaultStart, _vaultPlan.Direction);
                if (elapsed > _vaultPlan.MaximumDuration + 0.25f ||
                    (_controller.isGrounded && elapsed > 0.45f &&
                     _verticalVelocity < 0f))
                {
                    _vaultActive = false;
                    _nextVaultAt = Time.time + 2.4f;
                    return;
                }
                // Physics owns the ascent. Delay forward motion until the
                // body is above the observed sill, then stop at the
                // preflighted landing distance. No SetPosition/teleport.
                if (elapsed >= _vaultPlan.ForwardDelay &&
                    progressed < _vaultPlan.TravelDistance)
                {
                    heading = _vaultPlan.Direction;
                    speed = _vaultPlan.HorizontalSpeed;
                }
                else
                {
                    heading = Vector3.zero;
                    speed = 0f;
                }
                return;
            }

            if ((transform.position - _lastMotorProgress).sqrMagnitude >
                0.5f * 0.5f)
            {
                _lastMotorProgress = transform.position;
                _lastMotorProgressAt = Time.time;
            }
            if (!_controller.isGrounded || Time.time < _nextVaultAt ||
                Time.time - _lastMotorProgressAt < 2.0f ||
                delta.sqrMagnitude < 2.5f * 2.5f)
                return;
            _nextVaultAt = Time.time + 1.6f;
            Vector3 attempted = delta.normalized;
            Line2MobileNPCVaultTraversal.JumpPlan proposed;
            if (!_vault.TryPlanJumpServer(_goal, attempted,
                Line2NpcFloorGeometry.SupportMask, 45f, RayMasks.BLOCK_COLLISION,
                Mathf.Abs(Physics.gravity.y), out proposed))
                return;
            _vaultPlan = proposed;
            _vaultActive = true;
            _vaultStart = transform.position;
            _vaultStartedAt = Time.time;
            _verticalVelocity = proposed.Impulse;
            heading = Vector3.zero;
            speed = 0f;
            ProbeLogger.Log("NPC_NATIVE_VAULT_STARTED: actor=" + _identity +
                " landing=" + proposed.Landing.ToString("F1") +
                " lowObstacleTop=" + proposed.LandingOnObjectTop);
        }

        private bool TryFindVerifiedFlank(Vector3 enemy, out Vector3 result)
        {
            result = Vector3.zero;
            Vector3 toward = enemy - transform.position;
            toward.y = 0f;
            float distance = toward.magnitude;
            if (distance < 8f || distance > 33f) return false;
            toward /= distance;
            Vector3 lateral = Vector3.Cross(Vector3.up, toward).normalized;
            for (int sideIndex = 0; sideIndex < 2; ++sideIndex)
            {
                float side = sideIndex == 0 ? 1f : -1f;
                Vector3 estimate = transform.position +
                    lateral * side * 7f + toward * 2.5f;
                RaycastHit floor;
                if (!Line2NpcFloorGeometry.TryFindStaticSupport(
                        estimate, 1.8f, 4.5f, 1.15f, 0.72f,
                        _flankFloorHits, out floor))
                    continue;
                Vector3 candidate = floor.point + Vector3.up * 0.08f;
                if (!Level.checkSafeIncludingClipVolumes(candidate) ||
                    Physics.CheckCapsule(candidate + Vector3.up * 0.5f,
                        candidate + Vector3.up * 1.6f, 0.32f,
                        RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore))
                    continue;
                // A flank needs a firing line verified from the proposed
                // destination, not the enemy's hidden live location after
                // contact is lost. A* remains responsible for route access.
                Vector3 start = candidate + Vector3.up * 1.45f;
                Vector3 aim = enemy + Vector3.up * 1.1f;
                Vector3 ray = aim - start;
                if (ray.sqrMagnitude < 1f) continue;
                RaycastHit blocking;
                if (Physics.Raycast(start, ray.normalized, out blocking,
                    ray.magnitude - 0.08f,
                    RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore))
                    continue;
                result = candidate;
                return true;
            }
            return false;
        }

        private bool CanSeePlayer(Player player)
        {
            if (player == null || player.life == null || player.life.isDead)
                return false;
            Vector3 flat = player.transform.position - transform.position;
            flat.y = 0f;
            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (flat.sqrMagnitude < 0.0001f ||
                facing.sqrMagnitude < 0.0001f ||
                !Line2NPCCombatRules.WithinVisualArc(
                    Vector3.Dot(flat.normalized, facing.normalized)))
                return false;
            Vector3 source = transform.position + Vector3.up * 1.5f;
            Vector3 target = player.transform.position + Vector3.up * 1.3f;
            Vector3 to = target - source;
            float distance = to.magnitude;
            if (distance < 0.15f || distance > ThreatRadius + 0.5f)
                return false;
            // Filter physics like the combat perception adapter: ignore our
            // own six anatomical triggers but respect actual walls, CLIP,
            // vehicles, barricades, survivors and zombies. NonAlloc's hit
            // order is unspecified, so find the nearest non-self collider.
            int mask = RayMasks.BLOCK_COLLISION | RayMasks.ENTITY |
                       RayMasks.PLAYER | RayMasks.ENEMY;
            int count = Physics.RaycastNonAlloc(source, to / distance,
                _playerSightHits, distance, mask,
                QueryTriggerInteraction.Collide);
            // A saturated buffer can omit a nearer blocking wall.
            if (count >= _playerSightHits.Length) return false;
            Transform nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                Collider c = _playerSightHits[i].collider;
                if (c == null || c.transform == transform ||
                    c.transform.IsChildOf(transform))
                    continue;
                if (_playerSightHits[i].distance < nearestDistance)
                {
                    nearestDistance = _playerSightHits[i].distance;
                    nearest = c.transform;
                }
            }
            // If the known player has no collider along this particular
            // eye ray, allow only a wholly unobstructed physical ray.
            return nearest == null ||
                nearest == player.transform ||
                nearest.IsChildOf(player.transform);
        }
    }
}
