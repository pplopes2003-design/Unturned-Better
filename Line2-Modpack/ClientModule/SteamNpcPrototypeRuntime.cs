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
        private GameObject _visualPrefab;
        private bool _enabled;
        private bool _natural;
        private int _cap = 8;
        private string _mode = "normal";
        private float _nextNaturalAt;
        private bool _missingPrefabLogged;

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
            Line2NpcRuntimeRegistry.Unregister(this);
            ClearActors();
        }

        private void Update()
        {
            if (!IsAuthorized())
            {
                // If a friend joins, immediately remove the local-only
                // prototypes. They are not network-replicated actors.
                if (_actors.Count > 0)
                {
                    ClearActors();
                    ProbeLogger.Log("NPC_PROTOTYPE_SUSPENDED: local singleplayer authority no longer valid");
                }
                return;
            }
            if (!_enabled || !_natural || Time.time < _nextNaturalAt)
                return;
            _nextNaturalAt = Time.time + 18f;
            if (ActiveCount >= _cap) return;
            string ignored;
            TrySpawn(ChooseNaturalRole(), 1, out ignored);
        }

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
            _enabled = enabled;
            _natural = naturalSpawn;
            _cap = Mathf.Clamp(maxNPCs, 1, 32);
            _mode = combatMode == "passive" || combatMode == "total"
                ? combatMode : "normal";
            _nextNaturalAt = Time.time + 18f;
            foreach (PrototypeActor actor in _actors)
                if (actor != null) actor.SetCombatMode(_mode);
            // Setting NPCs off removes runtime-only prototype entities.
            if (!enabled) ClearActors();
            else
            {
                // A lower configured cap must be enforced immediately.
                _actors.RemoveAll(a => a == null);
                while (_actors.Count > _cap)
                {
                    int last = _actors.Count - 1;
                    PrototypeActor actor = _actors[last];
                    _actors.RemoveAt(last);
                    if (actor != null) UnityEngine.Object.Destroy(actor.gameObject);
                }
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
            if (disposition != "hostile" && disposition != "neutral" &&
                disposition != "friendly")
            {
                reason = "Invalid NPC disposition";
                return false;
            }
            if (count < 1 || count > 5 || ActiveCount + count > _cap)
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
                    instance.AddComponent<Line2MobileNPCInjurySystem>();
                    instance.AddComponent<Line2MobileNPCDoorNavigation>();
                    instance.AddComponent<Line2MobileNPCAdaptiveMemory>();
                    instance.AddComponent<Line2MobileNPCTravelMemory>();
                    string anatomyError;
                    if (!Line2NPCBodyAnatomy.TryBind(visual, out anatomyError))
                        throw new InvalidOperationException(
                            "Original NPC_Client anatomy invalid: " + anatomyError);
                    var actor = instance.AddComponent<PrototypeActor>();
                    actor.Initialize(disposition, movement, instance.name);
                    actor.SetCombatMode(_mode);
                    var group = instance.AddComponent<Line2NPCGroupMember>();
                    group.Initialize(actor, groupCandidates);
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
                    var melee = instance.AddComponent<Line2NPCMeleeExecutor>();
                    melee.Initialize(actor, combatSight);
                    var ranged = instance.AddComponent<Line2NPCRangedExecutor>();
                    ranged.Initialize(actor, combatSight, ownedInventory);
                    actor.AttachRangedExecutor(ranged);
                    var radio = instance.AddComponent<Line2NPCRadioOutbox>();
                    radio.Initialize(this, actor, combatSight, ownedInventory);
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
            reason = "Created " + created.Count + " local prototype humanoids, native visuals; " +
                     "validated melee vs player/zombie/opposing NPC is experimental; " +
                     "native gunfire, dialogue, radio and saves remain pending";
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

        private bool TryFindGround(int index, List<PrototypeActor> inFlight, out Vector3 location)
        {
            location = Vector3.zero;
            Player local = Player.player;
            if (local == null) return false;
            Vector3 origin = local.transform.position;
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
            Line2NPCRadioOutbox.ResetSession();
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
        internal string Identity { get { return _identity; } }
        internal string Disposition { get { return _disposition; } }
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
        private Line2MobileNPCAdaptiveMemory _experience;
        private Line2MobileNPCTravelMemory _travelMemory;
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
        private Vector3 _coverGoal;
        private Vector3 _retreatGoal;
        private bool _hasRetreat;
        internal bool IsDead { get { return _vitals != null && _vitals.IsDead; } }
        internal float HealthFraction { get { return _vitals != null ? _vitals.HealthFraction : 1f; } }
        internal bool RecentlyDamaged { get { return _vitals != null && Time.time - _vitals.LastDamageAt < 5f; } }
        internal bool HasVerifiedCover { get { return RecentlyDamaged && _coverPlanner != null && _coverPlanner.HasCover; } }
        private Vector3 _goal;
        private Vector3 _home;
        private float _nextThink;
        private float _blockedSince;
        private float _verticalVelocity;
        private readonly NpcDecisionBrain _brain = new NpcDecisionBrain();
        // Original Line 1 pursuit/investigation state machine owns the
        // physical search destination; the high-level tactical selector
        // alone chooses priority.
        private readonly Line2MobileNPCBrain _legacyBrain = new Line2MobileNPCBrain();
        private readonly Line2MobileNPCPlayerKnowledge _knowledge =
            new Line2MobileNPCPlayerKnowledge();
        private readonly Line2MobileNPCLocomotionPolicy _locomotion =
            new Line2MobileNPCLocomotionPolicy();
        private float _nextLegacyArrivalCheckAt;
        private readonly Line2MobileNPCTactics _tactics = new Line2MobileNPCTactics();
        internal NpcIntent CurrentIntent { get { return _brain.Intent; } }

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
        }

        private void OnValidatedDamage(float damage, Vector3 impulse)
        {
            if (_vitals != null)
                _tactics.ReportDamage(_vitals.Health, 100f, Time.time);
        }

        private void OnValidatedDeath()
        {
            if (_experience != null)
                _experience.ForgetOnDeathServer();
            if (_travelMemory != null)
                _travelMemory.ForgetOnDeathServer();
            _legacyBrain.Reset();
            _knowledge.Clear();
            _tactics.ClearGoal();
            _locomotion.Reset();
        }

        private void OnDestroy()
        {
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
                _locomotion.Reset();
                _tactics.ClearGoal();
                _aiAction = null;
                _threatVisible = false;
                _lastKnownThreat = null;
                _lastGunshotAt = -100f;
                _lastFootstepAt = -100f;
                _lastRadioAt = -100f;
                _goal = _home;
            }
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
            _lastHeardPoint = new Vector3(
                Mathf.Floor(position.x / 12f) * 12f + 6f,
                position.y,
                Mathf.Floor(position.z / 12f) * 12f + 6f);
            _lastGunshotAt = heardAt;
            _knowledge.ObserveSound(_lastHeardPoint, 0.64f, heardAt);
            _legacyBrain.ReportNoise(_lastHeardPoint, heardAt, 17f);
            _tactics.ReportIncomingFire(heardAt);
        }

        internal void HearVerifiedFootstep(Vector3 position, float confidence, float at)
        {
            if (_disposition != "hostile" || _combatMode == "passive" ||
                at - _lastFootstepAt < 0.85f)
                return;
            // Player displacement was observed by the migrated footstep
            // sensor: approximate the acoustic area at 6m resolution.
            _lastHeardFootstepPoint = new Vector3(
                Mathf.Floor(position.x / 6f) * 6f + 3f, position.y,
                Mathf.Floor(position.z / 6f) * 6f + 3f);
            _lastFootstepAt = at;
            _knowledge.ObserveSound(_lastHeardFootstepPoint, confidence, at);
            _legacyBrain.ReportNoise(_lastHeardFootstepPoint, at, 8f);
        }

        internal void ReceiveVerifiedRadioReport(Vector3 sector, float observedAt)
        {
            if (_disposition != "hostile" || _combatMode == "passive" ||
                IsDead || observedAt - _lastRadioAt < 6f)
                return;
            _lastRadioAt = observedAt;
            // Faction/group/equipment authorization is verified by
            // Line2NPCRadioOutbox before this is called. The radio only
            // supplies an uncertain sector. Never grant LOS, an aim target,
            // ammo, direct damage rights or a permanent player identity.
            _knowledge.ObserveReport(sector, 0.58f, observedAt);
            _legacyBrain.ReportNoise(sector, observedAt, 15f);
        }

        internal void ObserveThreat(Player player, out bool visible, out Vector3? lastKnown)
        {
            visible = false;
            lastKnown = null;
            if (_disposition != "hostile" || _combatMode == "passive") return;
            if (player != null &&
                (player.transform.position - transform.position).sqrMagnitude <= ThreatRadius * ThreatRadius &&
                CanSeePlayer(player))
            {
                visible = true;
                _threatVisible = true;
                _lastKnownThreat = player.transform.position;
                _lastSeenAt = Time.time;
                _knowledge.ObserveSight(player.transform.position, Time.time);
                _legacyBrain.ReportVisibleTarget(player.transform, Time.time);
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
            if (action == "engage" && !_threatVisible)
                return false;
            if (action == "investigate" && !_lastKnownThreat.HasValue &&
                Time.time - _lastGunshotAt > 7f &&
                Time.time - _lastFootstepAt > 4f &&
                !(_legacyBrain.HasDestination &&
                  _legacyBrain.CurrentState ==
                      Line2MobileNPCBrain.State.Investigating))
                return false;
            _aiAction = action;
            _aiValidUntil = expiresRealtime;
            if (action == "idle") _goal = _home;
            else if (action == "investigate")
            {
                if (_lastKnownThreat.HasValue)
                    _goal = _lastKnownThreat.Value;
                else if (Time.time - _lastGunshotAt <= 7f)
                    _goal = _lastHeardPoint;
                else if (Time.time - _lastFootstepAt <= 4f)
                    _goal = _lastHeardFootstepPoint;
                else if (_legacyBrain.HasDestination &&
                    _legacyBrain.CurrentState ==
                        Line2MobileNPCBrain.State.Investigating)
                    _goal = _legacyBrain.Destination;
            }
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
                bool nearPlayer = player != null &&
                    (player.transform.position - transform.position).sqrMagnitude < 32f * 32f;
                string validSuggestion = _aiAction != null &&
                    Time.realtimeSinceStartup <= _aiValidUntil ? _aiAction : null;
                bool hasCover = false;
                _hasRetreat = false;
                if (RecentlyDamaged && _vitals != null &&
                    _vitals.HasExplosionOrigin && _coverPlanner != null)
                {
                    _coverPlanner.TryGetDefense(_vitals.LastDamageOrigin,
                        out _coverGoal, out _retreatGoal);
                    hasCover = _coverPlanner.HasCover;
                    _hasRetreat = _coverPlanner.HasRetreat;
                }
                if (RecentlyDamaged && _vitals != null &&
                    _vitals.HasExplosionOrigin && _combatMode != "passive" &&
                    _disposition == "hostile")
                {
                    Vector3? coverPoint = hasCover ? (Vector3?)_coverGoal : null;
                    _tactics.TryChooseDefensiveGoal(transform.position,
                        coverPoint, _vitals.LastDamageOrigin, Time.time, false);
                    if (_tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Retreat &&
                        !_tactics.HasTacticalDestination && _hasRetreat)
                        _tactics.ReportValidatedRetreatPoint(_retreatGoal, Time.time);
                }
                else if (_tactics.IsGoalExpired(Time.time))
                    _tactics.ClearGoal();
                var facts = new NpcDecisionInput
                {
                    IsHostile = _disposition == "hostile",
                    IsFriendly = _disposition == "friendly",
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
                    UnderFire = RecentlyDamaged,
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
                     _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.Retreat) &&
                    _tactics.HasTacticalDestination &&
                    !(_tactics.IsGoalExpired(Time.time)) &&
                    _combatMode != "passive")
                {
                    _goal = _tactics.TacticalDestination;
                    // Cover/retreat movement wins over renewed aggression.
                }
            }

            Vector3 delta = _goal - transform.position;
            delta.y = 0;
            var locomotion = new Line2MobileNPCLocomotionPolicy.Situation
            {
                HasDestination = delta.sqrMagnitude > 1.5f,
                TargetVisible = _threatVisible,
                Swimming = false, // Separate native water traversal adapter required.
                UnderFire = RecentlyDamaged,
                Injured = HealthFraction < 0.55f,
                NearTarget = delta.sqrMagnitude < 5f * 5f,
                InCover = false,
                Investigating = _legacyBrain.CurrentState ==
                    Line2MobileNPCBrain.State.Investigating,
                Stalking = false,
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
            // Use bounded side-steering instead of repeatedly stopping in
            // front of the same wall. This does not open doors or navigate
            // entire buildings: a proper door-aware graph is still required.
            if (direction.sqrMagnitude > 0 && _astarFollower != null)
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
            if (_tactics.HasTacticalDestination &&
                (transform.position - _tactics.TacticalDestination).sqrMagnitude < 2.25f)
            {
                if (_experience != null &&
                    _tactics.CurrentGoal == Line2MobileNPCTactics.Goal.SeekCover)
                    _experience.RecordOwnOutcomeServer(
                        Line2MobileNPCAdaptiveMemory.ExperienceKind.CoverAccess,
                        _tactics.TacticalDestination, true);
                _tactics.ReportGoalReached();
            }
            // Arrival is a physical observation, not merely a requested
            // waypoint. The migrated Line 1 brain advances its three-point
            // investigation spiral only when the CharacterController arrives.
            if (_legacyBrain.HasDestination && Time.time >= _nextLegacyArrivalCheckAt)
            {
                Vector3 difference = _legacyBrain.Destination - transform.position;
                if (Mathf.Abs(difference.y) < 1.35f &&
                    new Vector2(difference.x, difference.z).sqrMagnitude < 1.7f * 1.7f)
                {
                    _legacyBrain.ReportDestinationReached();
                    _nextLegacyArrivalCheckAt = Time.time + 0.65f;
                    if (_legacyBrain.HasDestination &&
                        !_tactics.HasTacticalDestination)
                        _goal = _legacyBrain.Destination;
                }
            }
            IsMoving = direction.sqrMagnitude > 0.1f;
            if (_animation != null) _animation.SetMoving(IsMoving);
            _controller.Move((direction * speed +
                              Vector3.up * _verticalVelocity) * Time.deltaTime);
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

        private bool CanSeePlayer(Player player)
        {
            Vector3 source = transform.position + Vector3.up * 1.5f;
            Vector3 target = player.transform.position + Vector3.up * 1.3f;
            Vector3 to = target - source;
            RaycastHit hit;
            if (!Physics.Raycast(source, to.normalized, out hit, to.magnitude,
                                 ~0, QueryTriggerInteraction.Ignore))
                return true;
            return hit.transform == player.transform ||
                   hit.transform.IsChildOf(player.transform);
        }
    }
}
