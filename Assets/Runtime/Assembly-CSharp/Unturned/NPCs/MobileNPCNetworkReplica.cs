using System.Collections.Generic;
using SDG.NetTransport;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Minimal server->client visual representation for real MobileNPCs.
    /// AI, damage, pathfinding and inventory remain server authoritative.
    /// These client objects are visual-only and MUST NOT run an AI brain or
    /// participate in the server's damage/loot simulation.
    /// </summary>
    internal sealed class MobileNPCNetworkReplica : MonoBehaviour
    {
        private const string ResourcePath = "Characters/MobileNPC_Client";
        private const float SnapshotInterval = 0.35f;
        // Idle NPCs retain a short heartbeat for late joins and the 6-second
        // client replica expiry. Moving/combat state keeps the normal cadence.
        private const float IdleHeartbeatInterval = 1.4f;
        private const float RemoteExpiry = 6f;
        private const int MaxSnapshotsPerTick = 128;
        private const float ReplicationRadius = 165f;
        private const float GunAudioRadius = 90f;
        private const float GrenadeVisualRadius = 140f;

        private sealed class Replica
        {
            public GameObject Root;
            public Vector3 Position;
            public Quaternion Rotation;
            public float LastUpdate;
            public ushort GunId;
            public ushort DisplayedGunId;
            public Transform WeaponModel;
            public Animation NativeAnimation;
            public string ActiveLocomotion;
            public byte MotionFlags;
            public float NextWeaponRefresh;
            public float NextAnimationRefresh;
            public float ShotVisualUntil;
            public string GunHoldState;
            public string GunShootState;
            public string GunReloadState;
            public int AppearanceSeed;
            public string DisplayName;
            public byte HeadPitchByte = 90;
            public Transform Skull;
            public Quaternion SkullNeutralRotation;
            public TextMesh NameLabel;
            public Transform NameTransform;

        }

        private static MobileNPCNetworkReplica instance;
        // Position updates are unreliable; the weapon and locomotion state
        // are repeated each snapshot to repair packet loss / late joins.
        private static readonly ClientStaticMethod<int, Vector3, byte, ushort, byte, byte>
            SendNpcSnapshot =
                ClientStaticMethod<int, Vector3, byte, ushort, byte, byte>.Get(ReceiveNpcSnapshot);
        private static readonly ClientStaticMethod<int, Vector3, ushort>
            SendNpcShot = ClientStaticMethod<int, Vector3, ushort>.Get(ReceiveNpcShot);
        private static readonly ClientStaticMethod<int, Vector3>
            SendNpcDeath = ClientStaticMethod<int, Vector3>.Get(ReceiveNpcDeath);
        private static readonly ClientStaticMethod<int, ushort>
            SendNpcReload = ClientStaticMethod<int, ushort>.Get(ReceiveNpcReload);
        // Identity is sent separately and reliably; it is not repeated in
        // every 0.35-second movement packet.
        private static readonly ClientStaticMethod<int, string, int>
            SendNpcIdentity = ClientStaticMethod<int, string, int>.Get(ReceiveNpcIdentity);
        private static readonly ClientStaticMethod<int, ushort, Vector3, Vector3, float>
            SendNpcGrenade = ClientStaticMethod<int, ushort, Vector3, Vector3, float>.Get(
                ReceiveNpcGrenade);
        private static readonly ClientStaticMethod<int, ushort, ushort, ushort, ushort, ushort>
            SendNpcOutfit = ClientStaticMethod<int, ushort, ushort, ushort, ushort, ushort>.Get(
                ReceiveNpcOutfit);

        private readonly List<MobileNPCBrainDriver> drivers =
            new List<MobileNPCBrainDriver>(64);
        private readonly Dictionary<int, Replica> remote =
            new Dictionary<int, Replica>(64);
        private readonly List<int> expired = new List<int>(32);
        private readonly List<GameObject> grenadeVisuals = new List<GameObject>(16);
        // Reliable identity/outfit may precede the unreliable first snapshot.
        private struct PendingIdentity
        {
            public string Name;
            public int Seed;
            public float ReceivedAt;
        }
        private struct PendingOutfit
        {
            public ushort Hat, Shirt, Pants, Vest, Backpack;
            public float ReceivedAt;
        }
        private readonly Dictionary<int, PendingIdentity> pendingIdentities =
            new Dictionary<int, PendingIdentity>(64);
        private readonly Dictionary<int, PendingOutfit> pendingOutfits =
            new Dictionary<int, PendingOutfit>(64);
        private const int MaximumPendingMetadata = 128;
        private const float PendingMetadataLifetime = 12f;
        // Delayed unreliable position packets must not resurrect a dead NPC.
        private readonly Dictionary<int, float> deathTombstones =
            new Dictionary<int, float>(32);
        private const float TombstoneSeconds = 20f;
        private struct LastSentSnapshot
        {
            public Vector3 Position;
            public byte Yaw;
            public ushort GunId;
            public byte Motion;
            public byte Pitch;
            public float SentAt;
        }
        private readonly Dictionary<int, LastSentSnapshot> sentSnapshots =
            new Dictionary<int, LastSentSnapshot>(64);
        private readonly HashSet<int> activeSnapshotIds = new HashSet<int>();
        private readonly List<int> staleSnapshotIds = new List<int>(32);
        private int lastSnapshotsEmitted;
        private int lastIdleSnapshotsSkipped;
        private float nextSnapshotAt;
        private float nextIdentityAt;
        private float nextReplicationAuditAt;
        private bool firstRemoteReplicaLogged;
        private int nextDriverIndex;
        private static GameObject cachedVisualPrefab;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (instance != null)
                return;
            GameObject obj = new GameObject("MobileNPC_NetworkReplica");
            DontDestroyOnLoad(obj);
            instance = obj.AddComponent<MobileNPCNetworkReplica>();
        }

        private void OnEnable()
        {
            Level.onPostLevelLoaded += OnLevelChanged;
        }

        private void OnDisable()
        {
            Level.onPostLevelLoaded -= OnLevelChanged;
            ClearClientReplicas();
            sentSnapshots.Clear();
        }

        private void OnLevelChanged(int level)
        {
            ClearClientReplicas();
            sentSnapshots.Clear();
            nextDriverIndex = 0;
            nextSnapshotAt = Time.time + 0.5f;
            nextIdentityAt = Time.time + 1f;
        }

        private static bool IsFiniteFloat(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static byte QuantizeYaw(float yaw)
        {
            return (byte)(Mathf.RoundToInt(Mathf.Repeat(yaw, 360f)
                * (255f / 360f)) & 0xff);
        }

        private static Quaternion DecodeYaw(byte yaw)
        {
            return Quaternion.Euler(0f, yaw * (360f / 255f), 0f);
        }

        private void Update()
        {
            if (Provider.isServer && Dedicator.IsDedicatedServer && Level.isLoaded
                && Time.time >= nextSnapshotAt)
            {
                nextSnapshotAt = Time.time + SnapshotInterval;
                BroadcastServerSnapshots();
                if (Time.time >= nextIdentityAt)
                {
                    nextIdentityAt = Time.time + 4f;
                    BroadcastServerIdentities();
                }
            }
            if (!Provider.isServer && Provider.isConnected && Level.isLoaded)
            {
                UpdateClientVisuals();
                for (int i = grenadeVisuals.Count - 1; i >= 0; --i)
                    if (grenadeVisuals[i] == null)
                        grenadeVisuals.RemoveAt(i);
            }
            else if (remote.Count > 0 || grenadeVisuals.Count > 0)
                ClearClientReplicas();
        }

        private void LateUpdate()
        {
            // Unity evaluates legacy Animation between Update and LateUpdate.
            // Applying the procedural head pitch in Update would be
            // overwritten by the rig each frame.
            if (Provider.isServer || !Provider.isConnected || !Level.isLoaded)
                return;
            foreach (Replica replica in remote.Values)
            {
                if (replica == null || replica.Root == null)
                    continue;
                UpdateRemoteHeadLook(replica);
                UpdateRemoteNameplate(replica);
            }
        }

        // Pure decision helper: motion, weapon and pitch changes are never
        // suppressed. Idle snapshots are suppressed only when both pose and
        // position remain stable, and only until the heartbeat is due.
        internal static bool ShouldTransmitSnapshot(bool hasPrevious,
            Vector3 position, byte yaw, ushort gunId, byte motion, byte pitch,
            Vector3 previousPosition, byte previousYaw, ushort previousGunId,
            byte previousMotion, byte previousPitch, float elapsed)
        {
            if (!hasPrevious || elapsed >= IdleHeartbeatInterval
                || motion != 0 || previousMotion != motion
                || gunId != previousGunId || pitch != previousPitch)
                return true;
            int angle = Mathf.Abs((int)yaw - (int)previousYaw);
            angle = Mathf.Min(angle, 256 - angle);
            return (position - previousPosition).sqrMagnitude >= 0.04f
                || angle >= 2;
        }

        private void BroadcastServerSnapshots()
        {
            MobileNPCRegistry.CopyActiveDrivers(drivers);
            activeSnapshotIds.Clear();
            for (int i = 0; i < drivers.Count; ++i)
                if (drivers[i] != null && drivers[i].isActiveAndEnabled)
                    activeSnapshotIds.Add(drivers[i].GetInstanceID());
            staleSnapshotIds.Clear();
            foreach (int id in sentSnapshots.Keys)
                if (!activeSnapshotIds.Contains(id)) staleSnapshotIds.Add(id);
            for (int i = 0; i < staleSnapshotIds.Count; ++i)
                sentSnapshots.Remove(staleSnapshotIds[i]);
            lastSnapshotsEmitted = 0;
            lastIdleSnapshotsSkipped = 0;
            if (drivers.Count == 0)
            {
                nextDriverIndex = 0;
                return;
            }
            if (nextDriverIndex >= drivers.Count)
                nextDriverIndex = 0;
            int limit = Mathf.Min(MaxSnapshotsPerTick, drivers.Count);
            for (int i = 0; i < limit; ++i)
            {
                int index = (nextDriverIndex + i) % drivers.Count;
                MobileNPCBrainDriver driver = drivers[index];
                if (driver == null || !driver.isActiveAndEnabled)
                    continue;
                MobileNPCHumanoidVitals vitals =
                    driver.GetComponent<MobileNPCHumanoidVitals>();
                if (vitals == null || vitals.IsDead)
                    continue;
                MobileNPCHumanoidRangedCombat gun =
                    driver.GetComponent<MobileNPCHumanoidRangedCombat>();
                ushort gunId = gun != null && gun.SelectedGunAsset != null
                    ? gun.SelectedGunAsset.id : (ushort)0;
                MobileNPCHumanoidMotor motor =
                    driver.GetComponent<MobileNPCHumanoidMotor>();
                byte motionFlags = 0;
                if (motor != null && motor.IsMoving) motionFlags |= 1;
                if (motor != null && motor.IsSprinting) motionFlags |= 2;
                if (gun != null && gun.IsReloading) motionFlags |= 4;
                byte headPitch = 90;
                MobileNPCHumanoidAim aim =
                    driver.GetComponent<MobileNPCHumanoidAim>();
                if (aim != null && driver.Target != null
                    && aim.TryGetCurrentAimPointServer(driver.Target,
                        out Vector3 aimedPosition))
                {
                    Vector3 delta = aimedPosition
                        - (driver.transform.position + Vector3.up * 1.6f);
                    float elevation = Mathf.Atan2(delta.y,
                        new Vector2(delta.x, delta.z).magnitude)
                        * Mathf.Rad2Deg;
                    headPitch = (byte)Mathf.Clamp(
                        Mathf.RoundToInt(90f + Mathf.Clamp(elevation, -55f, 55f)),
                        0, 180);
                }
                int npcId = driver.GetInstanceID();
                Vector3 position = driver.transform.position;
                byte yaw = QuantizeYaw(driver.transform.eulerAngles.y);
                bool hasPrevious = sentSnapshots.TryGetValue(npcId,
                    out LastSentSnapshot previous);
                if (!ShouldTransmitSnapshot(hasPrevious, position, yaw, gunId,
                    motionFlags, headPitch, previous.Position, previous.Yaw,
                    previous.GunId, previous.Motion, previous.Pitch,
                    hasPrevious ? Time.time - previous.SentAt : float.PositiveInfinity))
                {
                    ++lastIdleSnapshotsSkipped;
                    continue;
                }
                sentSnapshots[npcId] = new LastSentSnapshot
                {
                    Position = position, Yaw = yaw, GunId = gunId,
                    Motion = motionFlags, Pitch = headPitch, SentAt = Time.time
                };
                ++lastSnapshotsEmitted;
                // No loopback: the dedicated host owns real NPC objects.
                // Unreliable snapshots are bounded by the idle heartbeat,
                // including for clients joining after a stationary period.
                SendNpcSnapshot.Invoke(ENetReliability.Unreliable,
                    Provider.GatherRemoteClientConnectionsWithinSphere(
                        position, ReplicationRadius),
                    npcId, position, yaw, gunId, motionFlags, headPitch);
            }
            nextDriverIndex = (nextDriverIndex + limit) % drivers.Count;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Time.time >= nextReplicationAuditAt)
            {
                nextReplicationAuditAt = Time.time + 15f;
                Debug.Log("[MobileNPCReplication] dedicated snapshots sampled=" + limit
                    + " emitted=" + lastSnapshotsEmitted
                    + " idleSkipped=" + lastIdleSnapshotsSkipped
                    + " totalLiveDrivers=" + drivers.Count
                    + " interval=" + SnapshotInterval + "s");
            }
#endif
        }

        private static int StableNameAppearanceSeed(string name)
        {
            unchecked
            {
                uint hash = 2166136261u;
                if (name != null)
                    for (int i = 0; i < name.Length; ++i)
                    {
                        hash ^= name[i];
                        hash *= 16777619u;
                    }
                return (int)hash;
            }
        }

        private void BroadcastServerIdentities()
        {
            // Periodic reliable metadata allows late-joiners to discover
            // names and deterministic appearances without a client request.
            for (int i = 0; i < drivers.Count; ++i)
            {
                MobileNPCBrainDriver driver = drivers[i];
                if (driver == null || !driver.isActiveAndEnabled)
                    continue;
                MobileNPCHumanoidVitals vitals =
                    driver.GetComponent<MobileNPCHumanoidVitals>();
                if (vitals != null && vitals.IsDead)
                    continue;
                MobileNPCHumanoidDisposition disposition =
                    driver.GetComponent<MobileNPCHumanoidDisposition>();
                string label = disposition != null ? disposition.DisplayName : "Sobrevivente";
                if (string.IsNullOrEmpty(label)) label = "Sobrevivente";
                if (label.Length > 40) label = label.Substring(0, 40);
                // Persistent GUID survives reload and distinguishes NPCs
                // with identical names. Never send the GUID itself over
                // the wire; the client only needs a deterministic seed.
                MobileNPCPersistentIdentity identity =
                    driver.GetComponent<MobileNPCPersistentIdentity>();
                int seed = identity != null
                    ? identity.AppearanceSeedServer()
                    : StableNameAppearanceSeed(label);
                BroadcastServerOutfit(driver);
                SendNpcIdentity.Invoke(ENetReliability.Reliable,
                    Provider.GatherRemoteClientConnectionsWithinSphere(
                        driver.transform.position, ReplicationRadius),
                    driver.GetInstanceID(), label, seed);
            }
        }

        /// <summary>
        /// Inventory is server-owned. Derive visible real clothing IDs from
        /// the five physical item types, never from client preferences.
        /// </summary>
        private static void BroadcastServerOutfit(MobileNPCBrainDriver driver)
        {
            MobileNPCInventory inventory = driver != null
                ? driver.GetComponent<MobileNPCInventory>() : null;
            if (inventory == null) return;
            ushort hat = 0, shirt = 0, pants = 0, vest = 0, backpack = 0;
            for (int i = 0; i < inventory.UsedSlots; ++i)
            {
                if (!inventory.TryGetItemSnapshot(i, out Item item)
                    || item == null) continue;
                ItemClothingAsset asset =
                    Assets.find(EAssetType.ITEM, item.id) as ItemClothingAsset;
                if (asset == null) continue;
                switch (asset.type)
                {
                    case EItemType.HAT: if (hat == 0) hat = item.id; break;
                    case EItemType.SHIRT: if (shirt == 0) shirt = item.id; break;
                    case EItemType.PANTS: if (pants == 0) pants = item.id; break;
                    case EItemType.VEST: if (vest == 0) vest = item.id; break;
                    case EItemType.BACKPACK:
                        if (backpack == 0) backpack = item.id; break;
                }
            }
            SendNpcOutfit.Invoke(ENetReliability.Reliable,
                Provider.GatherRemoteClientConnectionsWithinSphere(
                    driver.transform.position, ReplicationRadius),
                driver.GetInstanceID(), hat, shirt, pants, vest, backpack);
        }

        [SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
        public static void ReceiveNpcOutfit(int id, ushort hat, ushort shirt,
            ushort pants, ushort vest, ushort backpack)
        {
            if (Provider.isServer || !Provider.isConnected || !Level.isLoaded
                || instance == null || id == 0)
                return;
            if (instance.deathTombstones.TryGetValue(id, out float deathAt)
                && Time.time - deathAt < TombstoneSeconds)
                return;
            if (!instance.remote.TryGetValue(id, out Replica replica)
                || replica.Root == null)
            {
                if (instance.pendingOutfits.Count >= MaximumPendingMetadata
                    && !instance.pendingOutfits.ContainsKey(id))
                    instance.pendingOutfits.Clear();
                instance.pendingOutfits[id] = new PendingOutfit
                {
                    Hat = hat, Shirt = shirt, Pants = pants, Vest = vest,
                    Backpack = backpack, ReceivedAt = Time.time
                };
                return;
            }
            HumanClothes clothes =
                replica.Root.GetComponentInChildren<HumanClothes>(true);
            if (clothes == null) return;
            if (clothes.hat == hat && clothes.shirt == shirt
                && clothes.pants == pants && clothes.vest == vest
                && clothes.backpack == backpack)
                return;
            clothes.hat = hat;
            clothes.shirt = shirt;
            clothes.pants = pants;
            clothes.vest = vest;
            clothes.backpack = backpack;
            clothes.apply();
            // Real clothing meshes can introduce colliders. They must NOT
            // participate in client-authoritative combat or obstruction.
            foreach (Collider collider in
                replica.Root.GetComponentsInChildren<Collider>(true))
                if (collider.GetComponent<MobileNPCBodyHitbox>() == null)
                    collider.enabled = false;
        }

        [SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
        public static void ReceiveNpcIdentity(int id, string name, int seed)
        {
            if (Provider.isServer || !Provider.isConnected || !Level.isLoaded
                || instance == null || id == 0 || name == null
                || name.Length > 40)
                return;
            if (instance.deathTombstones.TryGetValue(id, out float deathAt)
                && Time.time - deathAt < TombstoneSeconds)
                return;
            if (!instance.remote.TryGetValue(id, out Replica replica)
                || replica.Root == null)
            {
                if (instance.pendingIdentities.Count >= MaximumPendingMetadata
                    && !instance.pendingIdentities.ContainsKey(id))
                    instance.pendingIdentities.Clear();
                instance.pendingIdentities[id] = new PendingIdentity
                {
                    Name = name, Seed = seed, ReceivedAt = Time.time
                };
                return;
            }
            if (replica.NameLabel == null)
            {
                GameObject label = new GameObject("RemoteNPCNameplate");
                label.layer = replica.Root.transform.Find("HumanoidVisual") != null
                    ? replica.Root.transform.Find("HumanoidVisual").gameObject.layer
                    : replica.Root.layer;
                label.transform.SetParent(replica.Root.transform, false);
                label.transform.localPosition = Vector3.up * 2.15f;
                replica.NameTransform = label.transform;
                replica.NameLabel = label.AddComponent<TextMesh>();
                replica.NameLabel.anchor = TextAnchor.LowerCenter;
                replica.NameLabel.alignment = TextAlignment.Center;
                replica.NameLabel.fontSize = 48;
                replica.NameLabel.characterSize = 0.018f;
                replica.NameLabel.color = Color.white;
                replica.NameLabel.richText = false;
                MeshRenderer renderer = label.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.sortingOrder = 20;
                }
            }
            replica.DisplayName = name;
            replica.NameLabel.text = name;
            if (replica.AppearanceSeed != seed)
            {
                replica.AppearanceSeed = seed;
                HumanClothes clothes =
                    replica.Root.GetComponentInChildren<HumanClothes>(true);
                MobileNPCAppearance.ApplyAppearanceSeed(clothes, seed);
            }
        }

        [SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
        public static void ReceiveNpcSnapshot(int instanceId, Vector3 position,
            byte yaw, ushort gunId, byte motionFlags, byte headPitch)
        {
            if (Provider.isServer || !Provider.isConnected || !Level.isLoaded
                || instance == null || instanceId == 0
                || float.IsNaN(position.x) || float.IsNaN(position.y)
                || float.IsNaN(position.z) || float.IsInfinity(position.x)
                || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                return;

            instance.ApplySnapshot(instanceId, position, DecodeYaw(yaw),
                gunId, motionFlags, headPitch);
        }

        private void ApplySnapshot(int id, Vector3 pos, Quaternion yaw,
            ushort gunId, byte motionFlags, byte headPitch)
        {
            if (deathTombstones.TryGetValue(id, out float deathAt)
                && Time.time - deathAt < TombstoneSeconds)
                return;
            if (!remote.TryGetValue(id, out Replica replica))
            {
                GameObject root = CreateVisualRoot(id, pos, yaw);
                if (root == null)
                    return;
                replica = new Replica { Root = root };
                replica.Skull = root.transform.Find(
                    "HumanoidVisual/Root/Skeleton/Spine/Skull");
                if (replica.Skull != null)
                    replica.SkullNeutralRotation = replica.Skull.localRotation;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (!firstRemoteReplicaLogged)
                {
                    firstRemoteReplicaLogged = true;
                    Debug.Log("[MobileNPCReplication] First remote visual created."
                        + " Server NPC id=" + id + " gunAssetId=" + gunId
                        + ". Rendered weapon and native animation sync pending.");
                }
#endif
            }
            replica.Position = pos;
            replica.Rotation = yaw;
            replica.GunId = gunId;
            replica.MotionFlags = motionFlags;
            replica.HeadPitchByte = headPitch;
            replica.LastUpdate = Time.time;
            remote[id] = replica;
            if (pendingIdentities.TryGetValue(id, out PendingIdentity identity))
            {
                pendingIdentities.Remove(id);
                if (Time.time - identity.ReceivedAt <= PendingMetadataLifetime)
                    ReceiveNpcIdentity(id, identity.Name, identity.Seed);
            }
            if (pendingOutfits.TryGetValue(id, out PendingOutfit outfit))
            {
                pendingOutfits.Remove(id);
                if (Time.time - outfit.ReceivedAt <= PendingMetadataLifetime)
                    ReceiveNpcOutfit(id, outfit.Hat, outfit.Shirt, outfit.Pants,
                        outfit.Vest, outfit.Backpack);
            }
        }

        private static GameObject CreateVisualRoot(int id, Vector3 pos,
            Quaternion yaw)
        {
            if (cachedVisualPrefab == null)
                cachedVisualPrefab = Resources.Load<GameObject>(ResourcePath);
            if (cachedVisualPrefab == null)
            {
                Debug.LogWarning("[MobileNPCReplication] Missing client visual prefab "
                    + ResourcePath);
                return null;
            }

            GameObject root = new GameObject("RemoteMobileNPC_" + id);
            root.SetActive(false);
            root.transform.SetPositionAndRotation(pos, yaw);
            GameObject model = Instantiate(cachedVisualPrefab, root.transform, false);
            model.name = "HumanoidVisual";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // Player/NPC native prefab is used ONLY as a mesh + animated rig.
            // Never let its bundled gameplay components control this ghost.
            foreach (Behaviour component in model.GetComponentsInChildren<Behaviour>(true))
            {
                if (component == null || component is Animator
                    || component is Animation || component is HumanClothes)
                    continue;
                component.enabled = false;
            }
            // The original NPC_Client prefab contains renderers beneath
            // disabled nodes. Re-enable the visual path just as the server's
            // singleplayer bootstrap does; do not enable gameplay scripts.
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.allowOcclusionWhenDynamic = false;
                if (renderer is SkinnedMeshRenderer skinned)
                    skinned.updateWhenOffscreen = true;
                Transform node = renderer.transform;
                while (node != null && node != root.transform)
                {
                    node.gameObject.SetActive(true);
                    node = node.parent;
                }
            }
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            // Client-side anatomical ray targets allow UseableGun to
            // report a hit POINT on the visible NPC. The server still
            // reconstructs and validates the first unobstructed hit against
            // its own authoritative NPC colliders before applying damage.
            CreateClientRayTargets(model.transform);
            root.SetActive(true);
            return root;
        }

        private static void CreateClientRayTargets(Transform model)
        {
            Transform skeleton = model.Find("Root/Skeleton");
            Transform spine = skeleton != null ? skeleton.Find("Spine") : null;
            Transform skull = spine != null ? spine.Find("Skull") : null;
            Transform leftShoulder = spine != null ? spine.Find("Left_Shoulder") : null;
            Transform rightShoulder = spine != null ? spine.Find("Right_Shoulder") : null;
            Transform leftArm = leftShoulder != null ? leftShoulder.Find("Left_Arm") : null;
            Transform rightArm = rightShoulder != null ? rightShoulder.Find("Right_Arm") : null;
            Transform leftHip = skeleton != null ? skeleton.Find("Left_Hip") : null;
            Transform rightHip = skeleton != null ? skeleton.Find("Right_Hip") : null;
            Transform leftLeg = leftHip != null ? leftHip.Find("Left_Leg") : null;
            Transform rightLeg = rightHip != null ? rightHip.Find("Right_Leg") : null;
            if (skull == null || spine == null || leftArm == null || rightArm == null
                || leftLeg == null || rightLeg == null)
            {
                Debug.LogWarning("[MobileNPCReplication] Remote rig missing native"
                    + " anatomical bones; visible NPC may not be shootable.");
                return;
            }
            AddRayTarget(skull, "Head", MobileNPCInjurySystem.BodyPart.Head,
                new Vector3(-0.45f, 0f, 0f), new Vector3(0.7f, 0.7f, 0.7f));
            AddRayTarget(spine, "Torso", MobileNPCInjurySystem.BodyPart.Torso,
                new Vector3(-0.4f, 0f, 0f), new Vector3(0.9f, 1f, 0.6f));
            AddRayTarget(leftArm, "LeftArm", MobileNPCInjurySystem.BodyPart.LeftArm,
                new Vector3(-0.4f, 0f, 0f), new Vector3(0.8f, 0.4f, 0.4f));
            AddRayTarget(rightArm, "RightArm", MobileNPCInjurySystem.BodyPart.RightArm,
                new Vector3(-0.4f, 0f, 0f), new Vector3(0.8f, 0.4f, 0.4f));
            AddRayTarget(leftLeg, "LeftLeg", MobileNPCInjurySystem.BodyPart.LeftLeg,
                new Vector3(-0.4f, -0.05f, 0f), new Vector3(0.8f, 0.4f, 0.4f));
            AddRayTarget(rightLeg, "RightLeg", MobileNPCInjurySystem.BodyPart.RightLeg,
                new Vector3(-0.4f, 0.05f, 0f), new Vector3(0.8f, 0.4f, 0.4f));
        }

        private static void AddRayTarget(Transform bone, string name,
            MobileNPCInjurySystem.BodyPart region, Vector3 center, Vector3 size)
        {
            GameObject target = new GameObject("Hit_" + name);
            target.layer = LayerMasks.ENTITY;
            target.transform.SetParent(bone, false);
            BoxCollider box = target.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            box.isTrigger = false;
            target.AddComponent<MobileNPCBodyHitbox>().Configure(region);
        }

        private void UpdateClientVisuals()
        {
            expired.Clear();
            foreach (KeyValuePair<int, Replica> pair in remote)
            {
                Replica replica = pair.Value;
                if (replica.Root == null || Time.time - replica.LastUpdate > RemoteExpiry)
                {
                    if (replica.Root != null)
                        Destroy(replica.Root);
                    expired.Add(pair.Key);
                    continue;
                }
                replica.Root.transform.position = Vector3.Lerp(
                    replica.Root.transform.position, replica.Position,
                    Mathf.Clamp01(Time.deltaTime * 8f));
                replica.Root.transform.rotation = Quaternion.Slerp(
                    replica.Root.transform.rotation, replica.Rotation,
                    Mathf.Clamp01(Time.deltaTime * 8f));
                UpdateRemoteWeaponAndAnimation(ref replica);
            }
            foreach (int id in expired)
                remote.Remove(id);
            expired.Clear();
            foreach (KeyValuePair<int, float> dead in deathTombstones)
                if (Time.time - dead.Value >= TombstoneSeconds)
                    expired.Add(dead.Key);
            foreach (int id in expired)
                deathTombstones.Remove(id);
        }

        /// <summary>
        /// A shot comes from the committed server-side fire event, never a
        /// client's input or a locally predicted combat simulation.
        /// No damage is applied by this visual notification.
        /// </summary>
        /// <summary>
        /// Sends presentation of an already-consumed and server-committed
        /// native grenade. The replica NEVER receives a Grenade component,
        /// deals damage, or independently applies an explosion.
        /// </summary>
        internal static void NotifyGrenadeThrowServer(MobileNPCBrainDriver driver,
            ushort throwableId, Vector3 origin, Vector3 velocity, float fuseSeconds)
        {
            if (!Provider.isServer || !Dedicator.IsDedicatedServer
                || driver == null || throwableId == 0
                || !IsFiniteFloat(origin.x) || !IsFiniteFloat(origin.y)
                || !IsFiniteFloat(origin.z)
                || !IsFiniteFloat(velocity.x) || !IsFiniteFloat(velocity.y)
                || !IsFiniteFloat(velocity.z)
                || fuseSeconds <= 0f || fuseSeconds > 10f)
                return;
            SendNpcGrenade.Invoke(ENetReliability.Reliable,
                Provider.GatherRemoteClientConnectionsWithinSphere(
                    origin, GrenadeVisualRadius),
                driver.GetInstanceID(), throwableId, origin, velocity, fuseSeconds);
        }

        [SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
        public static void ReceiveNpcGrenade(int npcId, ushort throwableId,
            Vector3 origin, Vector3 velocity, float fuseSeconds)
        {
            if (Provider.isServer || !Provider.isConnected || !Level.isLoaded
                || instance == null || npcId == 0
                || fuseSeconds <= 0f || fuseSeconds > 10f
                || float.IsNaN(origin.x) || float.IsNaN(origin.y)
                || float.IsNaN(origin.z) || float.IsInfinity(origin.x)
                || float.IsInfinity(origin.y) || float.IsInfinity(origin.z)
                || !IsFiniteFloat(velocity.x) || !IsFiniteFloat(velocity.y)
                || !IsFiniteFloat(velocity.z) || velocity.sqrMagnitude > 3000f)
                return;
            ItemThrowableAsset asset = Assets.find(EAssetType.ITEM,
                throwableId) as ItemThrowableAsset;
            if (asset == null || asset.throwable == null)
                return;
            // The client physics object has no explosive logic. The actual
            // native Grenade on the server owns fuse, damage and effects.
            GameObject visual = Object.Instantiate(asset.throwable,
                origin, Quaternion.LookRotation(
                    velocity.sqrMagnitude > 0.001f ? velocity : Vector3.forward));
            if (visual == null)
                return;
            visual.name = "RemoteNPC_GrenadeVisual";
            foreach (Grenade native in visual.GetComponentsInChildren<Grenade>(true))
                Object.Destroy(native);
            Rigidbody rigidbody = visual.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                Object.Destroy(visual);
                return;
            }
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rigidbody.AddForce(velocity * rigidbody.mass, ForceMode.Impulse);
            instance.grenadeVisuals.Add(visual);
            Object.Destroy(visual, fuseSeconds);
        }

        internal static void NotifyCommittedShotServer(MobileNPCBrainDriver driver,
            ushort gunId)
        {
            if (!Provider.isServer || !Dedicator.IsDedicatedServer
                || driver == null || gunId == 0)
                return;
            SendNpcShot.Invoke(ENetReliability.Reliable,
                Provider.GatherRemoteClientConnectionsWithinSphere(
                    driver.transform.position, GunAudioRadius),
                driver.GetInstanceID(), driver.transform.position, gunId);
        }

        [SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
        public static void ReceiveNpcShot(int id, Vector3 position, ushort gunId)
        {
            if (Provider.isServer || !Provider.isConnected || !Level.isLoaded
                || instance == null || id == 0)
                return;
            if (!instance.remote.TryGetValue(id, out Replica replica)
                || replica.Root == null)
                return; // snapshot may be in flight; never create a ghost for a shot
            replica.ShotVisualUntil = Time.time + 0.16f;
            PlayRemoteGunAction(replica, replica.GunShootState);
            instance.remote[id] = replica;
            ItemGunAsset gun = Assets.find(EAssetType.ITEM, gunId) as ItemGunAsset;
            if (gun != null && gun.shoot != null)
            {
                OneShotAudioParameters sound =
                    new OneShotAudioParameters(replica.Root.transform, gun.shoot);
                sound.volume = 0.65f;
                sound.SetLinearRolloff(1f, 64f);
                sound.Play();
            }
        }

        internal static void NotifyReloadStartedServer(MobileNPCBrainDriver driver,
            ushort gunId)
        {
            if (!Provider.isServer || !Dedicator.IsDedicatedServer
                || driver == null || gunId == 0) return;
            SendNpcReload.Invoke(ENetReliability.Reliable,
                Provider.GatherRemoteClientConnectionsWithinSphere(
                    driver.transform.position, GunAudioRadius),
                driver.GetInstanceID(), gunId);
        }

        [SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
        public static void ReceiveNpcReload(int id, ushort gunId)
        {
            if (Provider.isServer || !Provider.isConnected || !Level.isLoaded
                || instance == null || id == 0
                || !instance.remote.TryGetValue(id, out Replica replica)
                || replica.Root == null)
                return;
            PlayRemoteGunAction(replica, replica.GunReloadState);
            ItemGunAsset gun = Assets.find(EAssetType.ITEM, gunId) as ItemGunAsset;
            if (gun == null || gun.reload == null) return;
            OneShotAudioParameters sound =
                new OneShotAudioParameters(replica.Root.transform, gun.reload);
            sound.volume = 0.7f;
            sound.SetLinearRolloff(1f, 64f);
            sound.Play();
        }

        internal static void NotifyConfirmedDeathServer(MobileNPCBrainDriver driver)
        {
            if (!Provider.isServer || !Dedicator.IsDedicatedServer
                || driver == null)
                return;
            SendNpcDeath.Invoke(ENetReliability.Reliable,
                Provider.GatherRemoteClientConnectionsWithinSphere(
                    driver.transform.position, ReplicationRadius),
                driver.GetInstanceID(), driver.transform.position);
        }

        [SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
        public static void ReceiveNpcDeath(int id, Vector3 position)
        {
            if (Provider.isServer || !Provider.isConnected || !Level.isLoaded
                || instance == null || id == 0)
                return;
            instance.deathTombstones[id] = Time.time;
            if (instance.remote.TryGetValue(id, out Replica replica))
            {
                if (replica.Root != null)
                {
                    // A cosmetic, non-authoritative ragdoll uses the native
                    // Ragdoll_Player bone/joint layout and keeps this NPC's
                    // visible clothing. Fallback to immediate cleanup if
                    // the native source rig cannot be validated.
                    Vector3 lastMotion = position - replica.Root.transform.position;
                    Vector3 impulse = lastMotion.sqrMagnitude > 0.1f
                        ? lastMotion.normalized * 2.5f
                        : replica.Root.transform.forward * 1.2f;
                    if (replica.NameLabel != null)
                        Destroy(replica.NameLabel.gameObject);
                    if (!MobileNPCRemoteDeathRagdoll.TryRelease(replica.Root, impulse))
                        Destroy(replica.Root);
                }
                instance.remote.Remove(id);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCReplication] Server confirmed NPC death id=" + id);
#endif
        }

        private static string FindClip(Animation animation, params string[] matches)
        {
            if (animation == null) return null;
            foreach (AnimationState state in animation)
            {
                if (state == null || string.IsNullOrEmpty(state.name))
                    continue;
                for (int i = 0; i < matches.Length; ++i)
                    if (state.name.IndexOf(matches[i],
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return state.name;
            }
            return null;
        }

        private static void UpdateRemoteHeadLook(Replica replica)
        {
            if (replica.Skull == null || replica.Skull.parent == null)
                return;
            // Apply after legacy animation locomotion evaluation. The NPC
            // root owns yaw; the Skull receives only server-authorized pitch.
            float pitch = Mathf.Clamp(replica.HeadPitchByte - 90f, -55f, 55f);
            Quaternion reference = replica.Skull.parent.rotation
                * replica.SkullNeutralRotation;
            replica.Skull.rotation = Quaternion.AngleAxis(-pitch,
                replica.Root.transform.right) * reference;
        }

        private static void UpdateRemoteNameplate(Replica replica)
        {
            if (replica.NameLabel == null || replica.NameTransform == null)
                return;
            Camera camera = Camera.main;
            if (camera == null)
            {
                replica.NameLabel.gameObject.SetActive(false);
                return;
            }
            Vector3 view = replica.NameTransform.position - camera.transform.position;
            float distance = view.magnitude;
            bool canRead = distance > 0.1f && distance <= 9f
                && Vector3.Dot(camera.transform.forward,
                    view / Mathf.Max(distance, 0.01f)) >= 0.92f
                && !Physics.Linecast(camera.transform.position,
                    replica.NameTransform.position, RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore);
            if (replica.NameLabel.gameObject.activeSelf != canRead)
                replica.NameLabel.gameObject.SetActive(canRead);
            if (canRead)
                replica.NameTransform.rotation = camera.transform.rotation;
        }

        private static void ClearRemoteGunClips(Replica replica)
        {
            if (replica.NativeAnimation != null && replica.DisplayedGunId != 0)
            {
                string prefix = "RemoteNPC_Item_" + replica.DisplayedGunId + "_";
                // Iterate a snapshot of our own clip names because removing
                // states while enumerating Animation can invalidate iteration.
                var names = new List<string>(12);
                foreach (AnimationState state in replica.NativeAnimation)
                    if (state != null && state.name.StartsWith(prefix,
                        System.StringComparison.Ordinal))
                        names.Add(state.name);
                for (int i = 0; i < names.Count; ++i)
                    replica.NativeAnimation.RemoveClip(names[i]);
            }
            replica.GunHoldState = null;
            replica.GunShootState = null;
            replica.GunReloadState = null;
        }

        private static void BindRemoteGunClips(Replica replica, ItemGunAsset gun,
            Transform weaponModel)
        {
            if (replica.NativeAnimation == null)
                replica.NativeAnimation = replica.Root.GetComponentInChildren<Animation>(true);
            Animation animation = replica.NativeAnimation;
            if (animation == null || gun == null || gun.animations == null)
                return;
            Transform spine = replica.Root.transform.Find(
                "HumanoidVisual/Root/Skeleton/Spine");
            Transform left = spine != null ? spine.Find("Left_Shoulder") : null;
            Transform right = spine != null ? spine.Find("Right_Shoulder") : null;
            if (left == null || right == null) return;
            for (int i = 0; i < gun.animations.Length; ++i)
            {
                AnimationClip clip = gun.animations[i];
                if (clip == null || !clip.legacy) continue;
                string name = "RemoteNPC_Item_" + gun.id + "_" + i + "_" + clip.name;
                if (animation[name] != null)
                    animation.RemoveClip(name);
                animation.AddClip(clip, name);
                AnimationState state = animation[name];
                if (state == null) continue;
                state.AddMixingTransform(left, true);
                state.AddMixingTransform(right, true);
                state.layer = 1;
                state.blendMode = AnimationBlendMode.Blend;
                string clipName = clip.name.ToLowerInvariant();
                if (clipName.Contains("aim") || clipName == "idle")
                    replica.GunHoldState = name;
                if (clipName.Contains("shoot") || clipName.Contains("fire"))
                    replica.GunShootState = name;
                if (clipName.Contains("reload"))
                    replica.GunReloadState = name;
            }
            if (replica.GunHoldState != null)
            {
                AnimationState hold = animation[replica.GunHoldState];
                if (hold != null) hold.wrapMode = WrapMode.Loop;
                animation.CrossFade(replica.GunHoldState, 0.15f);
            }
        }

        private static void PlayRemoteGunAction(Replica replica, string clip)
        {
            if (replica == null || replica.NativeAnimation == null
                || string.IsNullOrEmpty(clip))
                return;
            AnimationState action = replica.NativeAnimation[clip];
            if (action == null) return;
            action.wrapMode = WrapMode.Once;
            replica.NativeAnimation.CrossFade(clip, 0.05f);
        }

        private static void UpdateRemoteWeaponAndAnimation(ref Replica replica)
        {
            if (replica.Root == null) return;
            if (replica.GunId != replica.DisplayedGunId
                && Time.time >= replica.NextWeaponRefresh)
            {
                replica.NextWeaponRefresh = Time.time + 0.5f;
                if (replica.WeaponModel != null)
                    Destroy(replica.WeaponModel.gameObject);
                ClearRemoteGunClips(replica);
                replica.WeaponModel = null;
                replica.DisplayedGunId = 0;
                if (replica.GunId != 0)
                {
                    ItemGunAsset gun = Assets.find(EAssetType.ITEM,
                        replica.GunId) as ItemGunAsset;
                    Transform spine = replica.Root.transform.Find(
                        "HumanoidVisual/Root/Skeleton/Spine");
                    Transform rightHook = spine != null ? spine.Find(
                        "Right_Shoulder/Right_Arm/Right_Hand/Right_Hook") : null;
                    Transform leftHook = spine != null ? spine.Find(
                        "Left_Shoulder/Left_Arm/Left_Hand/Left_Hook") : null;
                    Transform spineHook = spine != null ? spine.Find("Spine_Hook") : null;
                    if (gun != null && spine != null)
                    {
                        Transform parent = rightHook != null ? rightHook
                            : spineHook != null ? spineHook : spine;
                        switch (gun.EquipableModelParent)
                        {
                            case EEquipableModelParent.LeftHook:
                                if (leftHook != null) parent = leftHook;
                                break;
                            case EEquipableModelParent.Spine:
                                parent = spine;
                                break;
                            case EEquipableModelParent.SpineHook:
                                if (spineHook != null) parent = spineHook;
                                break;
                        }
                        GameObject prefab = gun.equipablePrefab != null
                            ? gun.equipablePrefab : gun.item;
                        if (prefab != null)
                        {
                            Material temporaryMaterial;
                            Transform itemModel = ItemTool.InstantiateItem(
                                100, gun.getState(), false, gun, null, true,
                                null, out temporaryMaterial, null,
                                prefabOverride: prefab);
                            if (itemModel != null)
                            {
                                itemModel.SetParent(parent, false);
                                itemModel.localPosition = Vector3.zero;
                                itemModel.localRotation = Quaternion.Euler(0f, 0f, 90f);
                                itemModel.localScale = Vector3.one;
                                itemModel.DestroyRigidbody();
                                Layerer.enemy(itemModel);
                                replica.WeaponModel = itemModel;
                                BindRemoteGunClips(replica, gun, itemModel);
                            }
                        }
                        replica.DisplayedGunId = replica.GunId;
                    }
                }
            }
            if (replica.NativeAnimation == null)
                replica.NativeAnimation = replica.Root.GetComponentInChildren<Animation>(true);
            if (replica.NativeAnimation == null
                || Time.time < replica.NextAnimationRefresh)
                return;
            if (replica.GunHoldState != null && Time.time >= replica.ShotVisualUntil
                && !replica.NativeAnimation.IsPlaying(replica.GunHoldState))
                replica.NativeAnimation.CrossFade(replica.GunHoldState, 0.2f);
            replica.NextAnimationRefresh = Time.time + 0.12f;
            bool moving = (replica.MotionFlags & 1) != 0;
            bool running = (replica.MotionFlags & 2) != 0;
            string desired = moving
                ? (running ? FindClip(replica.NativeAnimation, "Run", "Sprint")
                    : FindClip(replica.NativeAnimation, "Walk", "Move"))
                : FindClip(replica.NativeAnimation, "Idle", "Stand");
            if (string.IsNullOrEmpty(desired))
                return;
            if (replica.ActiveLocomotion != desired)
            {
                AnimationState clip = replica.NativeAnimation[desired];
                if (clip != null) clip.wrapMode = WrapMode.Loop;
                replica.NativeAnimation.CrossFade(desired, 0.2f);
                replica.ActiveLocomotion = desired;
            }
        }

        private void ClearClientReplicas()
        {
            foreach (Replica replica in remote.Values)
                if (replica.Root != null)
                    Destroy(replica.Root);
            remote.Clear();
            pendingIdentities.Clear();
            pendingOutfits.Clear();
            for (int i = 0; i < grenadeVisuals.Count; ++i)
                if (grenadeVisuals[i] != null)
                    Destroy(grenadeVisuals[i]);
            grenadeVisuals.Clear();
            expired.Clear();
            deathTombstones.Clear();
        }
    }
}
