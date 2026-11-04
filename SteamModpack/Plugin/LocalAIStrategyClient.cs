// Portable transport layer for a future LDM/OpenMod server adapter.
// No Unturned or Unity dependency: call Submit only with main-thread snapshots,
// and read TryTakeDecision on the game thread. Never act inside an IO callback.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace UnturnedBetter.SteamModpack
{
    public sealed class LocalAIObservation
    {
        [JsonProperty("health_fraction")] public float HealthFraction;
        [JsonProperty("threat_visible")] public bool ThreatVisible;
        [JsonProperty("threat_has_aggro")] public bool ThreatHasAggro;
        [JsonProperty("threat_distance")] public float ThreatDistance;
        [JsonProperty("gun_ready")] public bool GunReady;
        [JsonProperty("heard_relevant_noise")] public bool HeardRelevantNoise;
        [JsonProperty("visible_zombie_count")] public int VisibleZombieCount;
    }

    public sealed class LocalAIAgent
    {
        [JsonProperty("npc_id")] public string NpcId;
        [JsonProperty("observation")] public LocalAIObservation Observation;
    }

    internal sealed class LocalAIBatchRequest
    {
        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("sequence")] public long Sequence;
        [JsonProperty("mode")] public string Mode;
        [JsonProperty("agents")] public List<LocalAIAgent> Agents;
    }

    public sealed class LocalAIDecision
    {
        [JsonProperty("npc_id")] public string NpcId;
        [JsonProperty("intent")] public string Intent;
        [JsonProperty("reason")] public string Reason;
    }

    internal sealed class LocalAIBatchResponse
    {
        [JsonProperty("version")] public int Version;
        [JsonProperty("sequence")] public long Sequence;
        [JsonProperty("decisions")] public List<LocalAIDecision> Decisions;
    }

    /// <summary>
    /// Strictly optional. The server already owns default decisions when this
    /// service is missing. Networking is asynchronous; no Unity objects are
    /// read or modified off thread. Host validates every returned intention.
    /// </summary>
    public sealed class LocalAIStrategyClient : IDisposable
    {
        private readonly object gate = new object();
        private readonly HttpClient http;
        private readonly Uri endpoint;
        private bool pending;
        private bool disposed;
        private long sequence;
        private long acceptedSequence;
        private DateTime latestResultUtc;
        private Dictionary<string, LocalAIDecision> latest;

        public LocalAIStrategyClient(int port = 18742)
        {
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            endpoint = new Uri("http://127.0.0.1:" + port + "/v1/decide");
            http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(300) };
        }

        public bool Submit(string mode, List<LocalAIAgent> snapshot)
        {
            if (mode != "normal" && mode != "combat_total") return false;
            if (snapshot == null || snapshot.Count > 64) return false;
            var clean = new List<LocalAIAgent>(snapshot.Count);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (LocalAIAgent agent in snapshot)
            {
                if (agent == null || string.IsNullOrEmpty(agent.NpcId)
                    || agent.NpcId.Length > 64 || agent.Observation == null
                    || !identifiers.Add(agent.NpcId))
                    return false;
                // Deep copy on Unity/game thread so the asynchronous serializer
                // cannot race with an evolving sensor state.
                LocalAIObservation o = agent.Observation;
                clean.Add(new LocalAIAgent {
                    NpcId = agent.NpcId,
                    Observation = new LocalAIObservation {
                        HealthFraction = o.HealthFraction,
                        ThreatVisible = o.ThreatVisible,
                        ThreatHasAggro = o.ThreatHasAggro,
                        ThreatDistance = o.ThreatDistance,
                        GunReady = o.GunReady,
                        HeardRelevantNoise = o.HeardRelevantNoise,
                        VisibleZombieCount = o.VisibleZombieCount
                    }
                });
            }
            long requestSequence;
            lock (gate)
            {
                if (disposed || pending) return false;
                pending = true;
                requestSequence = ++sequence;
            }
            // No blocking .Result/.Wait() on the game thread.
            _ = SendAsync(new LocalAIBatchRequest {
                Sequence = requestSequence, Mode = mode, Agents = clean
            });
            return true;
        }

        private async Task SendAsync(LocalAIBatchRequest request)
        {
            try
            {
                string json = JsonConvert.SerializeObject(request);
                using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var response = await http.PostAsync(endpoint, body).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var batch = JsonConvert.DeserializeObject<LocalAIBatchResponse>(jsonResponse);
                    if (batch == null || batch.Version != 1 || batch.Sequence != request.Sequence
                        || batch.Decisions == null || batch.Decisions.Count != request.Agents.Count)
                        return;
                    var accepted = new Dictionary<string, LocalAIDecision>(StringComparer.Ordinal);
                    var submitted = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var agent in request.Agents) submitted.Add(agent.NpcId);
                    foreach (var decision in batch.Decisions)
                    {
                        if (decision == null || string.IsNullOrEmpty(decision.NpcId)
                            || !submitted.Contains(decision.NpcId)
                            || !IsSupportedIntent(decision.Intent)
                            || accepted.ContainsKey(decision.NpcId))
                            return;
                        accepted.Add(decision.NpcId, decision);
                    }
                    lock (gate)
                    {
                        if (!disposed && request.Sequence >= acceptedSequence)
                        {
                            acceptedSequence = request.Sequence;
                            latest = accepted;
                            latestResultUtc = DateTime.UtcNow;
                        }
                    }
                }
            }
            catch (HttpRequestException) { /* sidecar absent: native AI remains */ }
            catch (TaskCanceledException) { /* local service timeout */ }
            catch (ObjectDisposedException) { /* stopping */ }
            catch (JsonException) { /* invalid payload: ignore */ }
            finally { lock (gate) { pending = false; } }
        }

        public bool TryTakeDecision(string npcId, out LocalAIDecision decision)
        {
            lock (gate)
            {
                decision = null;
                if (disposed || latest == null ||
                    DateTime.UtcNow - latestResultUtc > TimeSpan.FromSeconds(2))
                    return false;
                return latest.TryGetValue(npcId, out decision);
            }
        }

        public static bool IsSupportedIntent(string intent)
        {
            return intent == "patrol" || intent == "hold" ||
                   intent == "investigate" || intent == "engage" ||
                   intent == "withdraw";
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                latest = null;
            }
            http.Dispose();
        }
    }
}
