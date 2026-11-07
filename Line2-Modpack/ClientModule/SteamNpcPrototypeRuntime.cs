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
                _actors.RemoveAll(a => a == null);
                return _actors.Count;
            }
        }

        private static bool IsAuthorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
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
            if (!IsAuthorized() || !_enabled || !_natural ||
                Time.time < _nextNaturalAt)
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
            // Setting NPCs off removes runtime-only prototype entities.
            if (!enabled) ClearActors();
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
                    if (agentLayer >= 0) instance.layer = agentLayer;
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
                    var actor = instance.AddComponent<PrototypeActor>();
                    actor.Initialize(disposition, movement, instance.name);
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
                     "combat/dialogue/save are not implemented";
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
                Vector3 candidate = hit.point + Vector3.up * 0.08f;
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
        private string _identity;
        private string _displayName;
        private float _nextObstacleProbeAt;
        private bool _pathBlocked;
        private Vector3 _goal;
        private Vector3 _home;
        private float _nextThink;
        private float _blockedSince;
        private float _verticalVelocity;

        internal void Initialize(string disposition, CharacterController controller,
                                 string identity)
        {
            _disposition = disposition;
            _controller = controller;
            _identity = identity;
            string[] names = { "Alex", "Morgan", "Sam", "Casey", "Jordan", "Riley", "Taylor", "Robin" };
            _displayName = names[UnityEngine.Random.Range(0, names.Length)] +
                           " (" + disposition + ")";
            _home = transform.position;
            _goal = _home;
            _nextThink = Time.time;
        }

        private void Update()
        {
            if (_controller == null || !Provider.isServer || !Level.isLoaded) return;
            Player player = Player.player;
            if (Time.time >= _nextThink)
            {
                _nextThink = Time.time + 0.4f;
                if (player != null && _disposition == "friendly" &&
                    (player.transform.position - transform.position).sqrMagnitude < 32f * 32f)
                {
                    Vector3 target = player.transform.position;
                    _goal = target - player.transform.forward * 3f;
                }
                else if (_disposition == "hostile" && player != null &&
                         (player.transform.position - transform.position).sqrMagnitude < 24f * 24f &&
                         CanSeePlayer(player))
                {
                    _goal = player.transform.position;
                }
                else if ((_goal - transform.position).sqrMagnitude < 2.5f ||
                         Time.time >= _blockedSince + 7f)
                {
                    float rad = UnityEngine.Random.Range(0f, 6.28318f);
                    _goal = _home + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) *
                        UnityEngine.Random.Range(4f, 15f);
                    _blockedSince = Time.time;
                }
            }

            Vector3 delta = _goal - transform.position;
            delta.y = 0;
            float speed = _disposition == "hostile" ? 2.8f : 1.8f;
            Vector3 direction = delta.sqrMagnitude > 1.5f ? delta.normalized : Vector3.zero;
            // Physics raycast prevents walking directly into nearby walls.
            if (direction.sqrMagnitude > 0)
            {
                if (Time.time >= _nextObstacleProbeAt)
                {
                    _nextObstacleProbeAt = Time.time + 0.2f;
                    RaycastHit hit;
                    _pathBlocked = Physics.SphereCast(
                        transform.position + Vector3.up, 0.25f,
                        direction, out hit, 1.25f, ~0,
                        QueryTriggerInteraction.Ignore) &&
                        hit.transform != transform &&
                        !hit.transform.IsChildOf(transform);
                }
                if (_pathBlocked)
                {
                    direction = Vector3.zero;
                    _blockedSince = Time.time - 6.8f;
                }
                else
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(direction), 420f * Time.deltaTime);
            }
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1.0f;
            _verticalVelocity += Physics.gravity.y * Time.deltaTime;
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
            GUI.Label(bounds, _displayName);
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
