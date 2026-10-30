using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Dedicated, opt-in, append-only navigation telemetry for offline analysis/training.
    /// NOT an online learning model. Never records player identifiers, chat or IP addresses.
    /// Call public methods from Unity's main thread only.
    /// </summary>
    public sealed class HybridNPCNavigationTelemetry : MonoBehaviour
    {
        [Serializable]
        public sealed class Record
        {
            public int schemaVersion = 1;
            public string eventType;
            public string utc;
            public string session;
            public string agent;
            public string map;
            public string state;
            public string reason;
            public string action;
            public int regionFrom = -1;
            public int regionTo = -1;
            public int portalIndex = -1;
            public float time;
            public Vector3 position;
            public Vector3 target;
            public Vector3 waypoint;
            public float velocity;
            public float targetDistance;
            public int pathNodes;
            public float pathLength;
            public float planMilliseconds;
            public bool success;
        }

        [SerializeField] private bool loggingEnabled = false;
        [SerializeField, Range(0.1f, 5f)] private float movementSampleInterval = 0.5f;
        [SerializeField, Range(1, 100)] private int flushEveryRecords = 20;
        [SerializeField, Range(1, 120)] private int flushEverySeconds = 5;
        [SerializeField, Range(1, 128)] private int maxFileMegabytes = 16;
        [SerializeField, Range(1, 20)] private int maxFiles = 8;

        private readonly List<string> pending = new List<string>(32);
        private readonly Dictionary<string, float> lastMovementSample = new Dictionary<string, float>();
        private string sessionId;
        private string folder;
        private string activeFile;
        private float lastFlushTime;
        private int sequence;
        private bool ioFailed;

        public bool LoggingEnabled { get { return loggingEnabled && !ioFailed; } }
        public string LogFolder { get { return folder; } }

        private void Awake()
        {
            sessionId = Guid.NewGuid().ToString("N");
            folder = Path.Combine(Application.persistentDataPath, "MobileNPC", "NavigationReports");
            lastFlushTime = Time.realtimeSinceStartup;
        }

        public void SetEnabled(bool enabled)
        {
            if (!enabled) Flush();
            loggingEnabled = enabled;
        }

        /// <summary>Sample periodically, rather than writing per physics frame.</summary>
        public void RecordMovement(string agentId, string mapName, Vector3 position,
            Vector3 target, Vector3 waypoint, float speed, string state)
        {
            if (!LoggingEnabled) return;
            string key = SafeToken(agentId);
            float last;
            if (lastMovementSample.TryGetValue(key, out last) &&
                Time.realtimeSinceStartup - last < movementSampleInterval)
                return;
            lastMovementSample[key] = Time.realtimeSinceStartup;
            Emit(new Record { eventType = "movement", agent = key, map = SafeToken(mapName),
                position = position, target = target, waypoint = waypoint,
                velocity = speed, state = SafeToken(state),
                targetDistance = Vector3.Distance(position, target) });
        }

        public void RecordRoute(string agentId, string mapName, Vector3 start,
            Vector3 target, int fromRegion, int toRegion, int portalIndex, int nodeCount,
            float length, float planningMs, bool success, string reason)
        {
            Emit(new Record { eventType = "route", agent = SafeToken(agentId),
                map = SafeToken(mapName), position = start, target = target,
                regionFrom = fromRegion, regionTo = toRegion,
                portalIndex = portalIndex, pathNodes = nodeCount, pathLength = length,
                planMilliseconds = planningMs, success = success, reason = SafeToken(reason) });
        }

        public void RecordProblem(string agentId, string mapName, Vector3 position,
            Vector3 destination, string problem, string recoveryAction, bool recovered)
        {
            Emit(new Record { eventType = "problem", agent = SafeToken(agentId),
                map = SafeToken(mapName), position = position, target = destination,
                reason = SafeToken(problem), action = SafeToken(recoveryAction),
                success = recovered, targetDistance = Vector3.Distance(position, destination) });
        }

        public void RecordPortal(string agentId, string mapName, Vector3 position,
            Vector3 destination, int fromRegion, int toRegion, int portalIndex,
            bool passed, string reason)
        {
            Emit(new Record { eventType = "portal", agent = SafeToken(agentId),
                map = SafeToken(mapName), position = position, target = destination,
                regionFrom = fromRegion, regionTo = toRegion, portalIndex = portalIndex,
                success = passed, reason = SafeToken(reason) });
        }

        private void Emit(Record record)
        {
            if (!LoggingEnabled) return;
            record.session = sessionId;
            record.utc = DateTime.UtcNow.ToString("o");
            record.time = Time.realtimeSinceStartup;
            pending.Add(JsonUtility.ToJson(record));
            if (pending.Count >= flushEveryRecords) Flush();
        }

        private void Update()
        {
            if (LoggingEnabled && pending.Count > 0 &&
                Time.realtimeSinceStartup - lastFlushTime >= flushEverySeconds)
                Flush();
        }

        public void Flush()
        {
            if (pending.Count == 0 || ioFailed) return;
            try
            {
                Directory.CreateDirectory(folder);
                if (string.IsNullOrEmpty(activeFile) ||
                    (File.Exists(activeFile) &&
                     new FileInfo(activeFile).Length >= (long)maxFileMegabytes * 1024L * 1024L))
                {
                    activeFile = Path.Combine(folder,
                        "nav_" + sessionId + "_" + sequence.ToString("D3") + ".jsonl");
                    sequence++;
                    PruneOldFiles();
                }
                File.AppendAllText(activeFile, string.Join("\n", pending.ToArray()) + "\n");
                pending.Clear();
                lastFlushTime = Time.realtimeSinceStartup;
            }
            catch (Exception ex)
            {
                ioFailed = true;
                Debug.LogWarning("[HybridNPCNav] Telemetry disabled after IO error: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void PruneOldFiles()
        {
            var files = new List<FileInfo>(new DirectoryInfo(folder).GetFiles("nav_*.jsonl"));
            files.Sort((a, b) => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc));
            int remove = Mathf.Max(0, files.Count - maxFiles + 1);
            for (int i = 0; i < remove; i++)
                if (files[i].FullName != activeFile) files[i].Delete();
        }

        // Keep log labels bounded and avoid accidental free-form personal data.
        private static string SafeToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            int limit = Mathf.Min(value.Length, 64);
            var chars = new char[limit];
            for (int i = 0; i < limit; ++i)
            {
                char c = value[i];
                chars[i] = char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_';
            }
            return new string(chars);
        }

        private void OnDisable() { Flush(); }
        private void OnApplicationQuit() { Flush(); }
    }
}
