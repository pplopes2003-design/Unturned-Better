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
        private const float RemoteExpiry = 6f;
        private const int MaxSnapshotsPerTick = 128;

        private struct Replica
        {
            public GameObject Root;
            public Vector3 Position;
            public Quaternion Rotation;
            public float LastUpdate;
            public ushort GunId;
        }

        private static MobileNPCNetworkReplica instance;
        private static readonly ClientStaticMethod<int, Vector3, byte, ushort>
            SendNpcSnapshot =
                ClientStaticMethod<int, Vector3, byte, ushort>.Get(ReceiveNpcSnapshot);

        private readonly List<MobileNPCBrainDriver> drivers =
            new List<MobileNPCBrainDriver>(64);
        private readonly Dictionary<int, Replica> remote =
            new Dictionary<int, Replica>(64);
        private readonly List<int> expired = new List<int>(32);
        private float nextSnapshotAt;
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
        }

        private void OnLevelChanged(int level)
        {
            ClearClientReplicas();
            nextDriverIndex = 0;
            nextSnapshotAt = Time.time + 0.5f;
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
            }
            if (!Provider.isServer && Provider.isConnected && Level.isLoaded)
                UpdateClientVisuals();
            else if (remote.Count > 0)
                ClearClientReplicas();
        }

        private void BroadcastServerSnapshots()
        {
            MobileNPCRegistry.CopyActiveDrivers(drivers);
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
                // No loopback: the dedicated host owns real NPC objects.
                // Unreliable updates are refreshed periodically; no hidden
                // target coordinates or AI memory are sent to clients.
                SendNpcSnapshot.Invoke(ENetReliability.Unreliable,
                    Provider.GatherRemoteClientConnections(),
                    driver.GetInstanceID(), driver.transform.position,
                    QuantizeYaw(driver.transform.eulerAngles.y), gunId);
            }
            nextDriverIndex = (nextDriverIndex + limit) % drivers.Count;
        }

        [SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
        public static void ReceiveNpcSnapshot(int instanceId, Vector3 position,
            byte yaw, ushort gunId)
        {
            if (Provider.isServer || !Provider.isConnected || !Level.isLoaded
                || instance == null || instanceId == 0
                || float.IsNaN(position.x) || float.IsNaN(position.y)
                || float.IsNaN(position.z) || float.IsInfinity(position.x)
                || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                return;

            instance.ApplySnapshot(instanceId, position, DecodeYaw(yaw), gunId);
        }

        private void ApplySnapshot(int id, Vector3 pos, Quaternion yaw,
            ushort gunId)
        {
            if (!remote.TryGetValue(id, out Replica replica))
            {
                GameObject root = CreateVisualRoot(id, pos, yaw);
                if (root == null)
                    return;
                replica = new Replica { Root = root };
            }
            replica.Position = pos;
            replica.Rotation = yaw;
            replica.GunId = gunId;
            replica.LastUpdate = Time.time;
            remote[id] = replica;
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
            root.SetActive(true);
            return root;
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
            }
            foreach (int id in expired)
                remote.Remove(id);
        }

        private void ClearClientReplicas()
        {
            foreach (Replica replica in remote.Values)
                if (replica.Root != null)
                    Destroy(replica.Root);
            remote.Clear();
            expired.Clear();
        }
    }
}
