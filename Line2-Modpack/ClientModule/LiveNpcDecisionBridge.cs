using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Real in-world NPC prototype observations -> localhost Python batch AI ->
    /// short-lived movement suggestions. HTTP only runs on a worker thread.
    /// GameObject/Unity APIs are accessed ONLY on the main Unity thread.
    ///
    /// Does not grant damage, create NPCs, manipulate vanilla zombies, or
    /// control a public/dedicated multiplayer server. Disables itself safely
    /// whenever the authorized singleplayer session ends.
    /// </summary>
    internal sealed class LiveNpcDecisionBridge : MonoBehaviour
    {
        private const string Endpoint = "http://127.0.0.1:8765/v1/decide_batch";
        private const int TimeoutMs = 480;
        private const int MaxResponseBytes = 24576;
        private const int MaxAgents = 32;
        private const float Interval = 0.50f;
        private const float MaximumAge = 0.70f;

        [Serializable]
        private sealed class Observation
        {
            public string kind = "npc";
            public float[] position;
            public float health = 1f; // Prototype has no damage model yet.
            public bool threat_visible;
            public bool under_fire = false;
            public bool has_cover = false;
            public float[] last_known_threat;
        }

        [Serializable]
        private sealed class AgentRequest
        {
            public int protocol = 1;
            public string agent_id;
            public int tick;
            public Observation observation;
        }

        [Serializable]
        private sealed class BatchRequest
        {
            public int protocol = 1;
            public AgentRequest[] agents;
        }

        [Serializable]
        private sealed class Decision
        {
            public int protocol;
            public string agent_id;
            public int tick;
            public string action;
            public string reason;
            public int valid_ms;
        }

        [Serializable]
        private sealed class BatchResponse
        {
            public int protocol;
            public Decision[] decisions;
        }

        private readonly List<PrototypeActor> _actors = new List<PrototypeActor>(MaxAgents);
        private readonly Dictionary<string, PrototypeActor> _pendingActors =
            new Dictionary<string, PrototypeActor>(MaxAgents);
        private Task<string> _pending;
        private int _tick;
        private int _pendingTick;
        private int _epoch;
        private int _pendingEpoch;
        private float _sentAt;
        private float _nextSendAt;
        private float _nextLogAt;
        private bool _closing;
        private SteamNpcPrototypeRuntime _runtime;
        internal static string Status { get; private set; } = "not started";

        private void OnEnable()
        {
            _closing = false;
            Status = "ready; waiting for prototype NPCs";
            Level.onLevelLoaded += OnLevelLoaded;
        }

        private void OnDisable()
        {
            Level.onLevelLoaded -= OnLevelLoaded;
            _closing = true;
            Status = "stopped";
            ResetPending();
        }

        private void OnLevelLoaded(int stage)
        {
            _epoch++;
            ResetPending();
        }

        private void ResetPending()
        {
            _pending = null; // Never wait on an HTTP worker on the Unity thread.
            _pendingActors.Clear();
            _actors.Clear();
            _nextSendAt = Time.realtimeSinceStartup + Interval;
        }

        private static bool IsLocalAuthority()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                   Provider.hasCheats && Level.isLoaded && Player.player != null;
        }

        private void Update()
        {
            if (_closing) return;
            if (!IsLocalAuthority() || !Line2NpcRuntimeRegistry.IsReady)
            {
                if (_pending != null)
                {
                    _epoch++;
                    ResetPending();
                }
                return;
            }

            if (_runtime == null)
                _runtime = GetComponent<SteamNpcPrototypeRuntime>();
            if (_runtime == null || _runtime.ActiveCount == 0)
                return;

            if (_pending != null)
            {
                if (_pending.IsCompleted)
                    Receive();
                else if (Time.realtimeSinceStartup - _sentAt > 1.2f)
                {
                    _epoch++;
                    ResetPending();
                }
            }

            if (_pending == null && Time.realtimeSinceStartup >= _nextSendAt)
                Dispatch();
        }

        private void Dispatch()
        {
            _actors.Clear();
            _runtime.CopyLiveActors(_actors, MaxAgents);
            if (_actors.Count == 0) return;
            var requests = new List<AgentRequest>(_actors.Count);
            _pendingActors.Clear();
            Player player = Player.player;
            int tick = ++_tick;
            foreach (PrototypeActor actor in _actors)
            {
                if (actor == null || !actor.isActiveAndEnabled) continue;
                if (_pendingActors.ContainsKey(actor.Identity)) continue;

                bool visible;
                Vector3? lastKnown;
                actor.ObserveThreat(player, out visible, out lastKnown);
                Vector3 position = actor.transform.position;
                requests.Add(new AgentRequest
                {
                    agent_id = actor.Identity,
                    tick = tick,
                    observation = new Observation
                    {
                        position = new[] { position.x, position.y, position.z },
                        threat_visible = visible,
                        last_known_threat = lastKnown.HasValue
                            ? new[] { lastKnown.Value.x, lastKnown.Value.y, lastKnown.Value.z }
                            : null
                    }
                });
                _pendingActors.Add(actor.Identity, actor);
            }
            if (requests.Count == 0) return;
            var batch = new BatchRequest { agents = requests.ToArray() };
            string json = JsonUtility.ToJson(batch);
            if (Encoding.UTF8.GetByteCount(json) > 16384)
            {
                _pendingActors.Clear();
                LogThrottled("NPC_AI_SKIPPED: request too large");
                return;
            }

            _pendingTick = tick;
            _pendingEpoch = _epoch;
            _sentAt = Time.realtimeSinceStartup;
            _nextSendAt = _sentAt + Interval;
            _pending = Task.Run(() => Post(json));
        }

        private void Receive()
        {
            Task<string> job = _pending;
            _pending = null;
            if (job == null || _pendingEpoch != _epoch)
            {
                _pendingActors.Clear();
                return;
            }
            float elapsed = Time.realtimeSinceStartup - _sentAt;
            try
            {
                string raw = job.GetAwaiter().GetResult();
                BatchResponse response = JsonUtility.FromJson<BatchResponse>(raw);
                if (response == null || response.protocol != 1 ||
                    response.decisions == null ||
                    response.decisions.Length != _pendingActors.Count ||
                    response.decisions.Length > MaxAgents || elapsed > MaximumAge)
                {
                    LogThrottled("NPC_AI_REJECTED: malformed or stale batch");
                    return;
                }

                // Validate ALL actions, identities, ticks and TTLs before
                // applying anything. No partial orders after a corrupt packet.
                var seen = new HashSet<string>();
                foreach (Decision decision in response.decisions)
                {
                    if (decision == null || decision.protocol != 1 ||
                        decision.tick != _pendingTick ||
                        string.IsNullOrEmpty(decision.agent_id) ||
                        !seen.Add(decision.agent_id) ||
                        !_pendingActors.ContainsKey(decision.agent_id) ||
                        decision.valid_ms < 1 || decision.valid_ms > 1000 ||
                        elapsed * 1000f > decision.valid_ms ||
                        (decision.action != "idle" &&
                         decision.action != "investigate" &&
                         decision.action != "engage" &&
                         decision.action != "seek_cover" &&
                         decision.action != "retreat"))
                    {
                        LogThrottled("NPC_AI_REJECTED: invalid decision identity/action");
                        return;
                    }
                }

                int applied = 0;
                foreach (Decision decision in response.decisions)
                {
                    PrototypeActor actor = _pendingActors[decision.agent_id];
                    if (actor == null || !actor.isActiveAndEnabled) continue;
                    if (actor.TryApplyAiAction(decision.action,
                          Time.realtimeSinceStartup + decision.valid_ms / 1000f))
                        applied++;
                }
                Status = "online; applied " + applied + "/" + response.decisions.Length + " movement decisions";
                if (Time.realtimeSinceStartup >= _nextLogAt)
                {
                    ProbeLogger.Log("NPC_AI_LIVE_BATCH_OK: applied=" + applied +
                        " tick=" + _pendingTick + " latency_ms=" +
                        (elapsed * 1000f).ToString("F0") +
                        " (movement only, no combat)");
                    _nextLogAt = Time.realtimeSinceStartup + 10f;
                }
            }
            catch (Exception error)
            {
                LogThrottled("NPC_AI_UNAVAILABLE: " + error.GetBaseException().Message);
            }
            finally
            {
                _pendingActors.Clear();
            }
        }

        private void LogThrottled(string line)
        {
            Status = line;
            if (Time.realtimeSinceStartup < _nextLogAt) return;
            ProbeLogger.Log(line);
            _nextLogAt = Time.realtimeSinceStartup + 10f;
        }

        private static string Post(string json)
        {
            byte[] requestBody = Encoding.UTF8.GetBytes(json);
            var request = (HttpWebRequest)WebRequest.Create(Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.Proxy = null;
            request.KeepAlive = false;
            request.ContentLength = requestBody.Length;

            using (Stream stream = request.GetRequestStream())
                stream.Write(requestBody, 0, requestBody.Length);
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new IOException("AI HTTP " + (int)response.StatusCode);
                using (Stream responseStream = response.GetResponseStream())
                using (var memory = new MemoryStream())
                {
                    byte[] buffer = new byte[1024];
                    int read;
                    while ((read = responseStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (memory.Length + read > MaxResponseBytes)
                            throw new IOException("AI response exceeds budget");
                        memory.Write(buffer, 0, read);
                    }
                    return Encoding.UTF8.GetString(memory.ToArray());
                }
            }
        }
    }
}
