using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Unturned2.Line2
{
    // Manual IPC diagnostic only. No NPC commands or game state mutations.
    // F9 performs a nonblocking request to the local AI service.
    internal sealed class LocalAiBridge : MonoBehaviour
    {
        private const string Endpoint = "http://127.0.0.1:8765/v1/decide";
        private const int TimeoutMs = 700;
        private const float MaxAgeSeconds = 2f;
        private Task<string> _pending;
        private float _sentRealtime;
        private int _nextTick;
        private int _pendingTick;
        private bool _closing;

        [Serializable]
        private sealed class DecisionResponse
        {
            public int protocol;
            public string agent_id;
            public int tick;
            public string action;
            public int valid_ms;
        }

        private void Update()
        {
            if (_closing) return;

            // Only manual probes until the game integration is validated.
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (_pending != null)
                    ProbeLogger.Log("AI_BRIDGE_BUSY: previous probe in flight");
                else
                    SendProbe();
            }

            var task = _pending;
            if (task == null || !task.IsCompleted) return;
            _pending = null;
            float elapsed = (Time.realtimeSinceStartup - _sentRealtime) * 1000f;
            try
            {
                string raw = task.GetAwaiter().GetResult();
                // Unity JSON deserialization and logging only on main thread.
                DecisionResponse response = JsonUtility.FromJson<DecisionResponse>(raw);
                if (response == null || response.protocol != 1 ||
                    response.agent_id != "bridge-probe" ||
                    response.tick != _pendingTick ||
                    response.action != "idle" ||
                    response.valid_ms < 1 || response.valid_ms > 1000 ||
                    elapsed > MaxAgeSeconds * 1000f)
                {
                    ProbeLogger.Log("AI_BRIDGE_REJECTED: incompatible or stale response (elapsed_ms=" +
                                    elapsed.ToString("F0") + ")");
                    return;
                }
                ProbeLogger.Log("AI_BRIDGE_OK: agent=" + response.agent_id +
                                " tick=" + response.tick + " action=" + response.action +
                                " rtt_ms=" + elapsed.ToString("F0"));
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("AI_BRIDGE_UNAVAILABLE: " + ex.GetBaseException().Message);
            }
        }

        private void SendProbe()
        {
            _pendingTick = ++_nextTick;
            int tick = _pendingTick;
            _sentRealtime = Time.realtimeSinceStartup;
            _pending = Task.Run(() => PostProbe(tick));
            ProbeLogger.Log("AI_BRIDGE_SENT: tick=" + tick);
        }

        private static string PostProbe(int tick)
        {
            string body = "{\"protocol\":1,\"agent_id\":\"bridge-probe\",\"tick\":" + tick + "}";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            var req = (HttpWebRequest)WebRequest.Create(Endpoint);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = TimeoutMs;
            req.ReadWriteTimeout = TimeoutMs;
            req.Proxy = null;
            req.KeepAlive = false;
            req.ContentLength = bytes.Length;
            using (Stream stream = req.GetRequestStream())
                stream.Write(bytes, 0, bytes.Length);
            using (var response = (HttpWebResponse)req.GetResponse())
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new IOException("Unexpected HTTP code " + (int)response.StatusCode);
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    char[] buffer = new char[2049];
                    int n = reader.ReadBlock(buffer, 0, buffer.Length);
                    if (n <= 0 || n > 2048)
                        throw new IOException("Empty or oversized AI response");
                    return new string(buffer, 0, n);
                }
            }
        }

        private void OnDestroy()
        {
            _closing = true;
            // Never await network work on Unity's main thread.
            _pending = null;
        }
    }
}
