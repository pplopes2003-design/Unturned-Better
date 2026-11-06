using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unturned2.Line2
{
    // Read-only game observation via F9. Server suggestions are never executed.
    internal sealed class LocalAiBridge : MonoBehaviour
    {
        private const string Endpoint = "http://127.0.0.1:8765/v1/decide";
        private const int TimeoutMs = 700;
        private const int MaxResponseBytes = 2048;
        private const float MaxAgeMs = 1000f;
        private Task<string> _pending;
        private float _sentRealtime;
        private int _nextTick;
        private int _pendingTick;
        private bool _closing;

        [Serializable]
        private sealed class Observation
        {
            public string kind = "camera";
            public string scene;
            public float[] position;
            public float[] forward;
            public int nearby_colliders;
        }

        [Serializable]
        private sealed class DecisionRequest
        {
            public int protocol = 1;
            public string agent_id = "bridge-probe";
            public int tick;
            public Observation observation;
        }

        [Serializable]
        private sealed class DecisionResponse
        {
            public int protocol;
            public string agent_id;
            public int tick;
            public string action;
            public string reason;
            public int valid_ms;
        }

        private readonly Collider[] _nearby = new Collider[64];

        private void Update()
        {
            if (_closing) return;
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (_pending != null)
                    ProbeLogger.Log("AI_BRIDGE_BUSY: pending request");
                else
                    SendObservation();
            }

            Task<string> task = _pending;
            if (task == null || !task.IsCompleted) return;
            _pending = null;
            float elapsed = (Time.realtimeSinceStartup - _sentRealtime) * 1000f;
            try
            {
                string raw = task.GetAwaiter().GetResult();
                DecisionResponse response = JsonUtility.FromJson<DecisionResponse>(raw);
                if (response == null || response.protocol != 1 ||
                    response.agent_id != "bridge-probe" || response.tick != _pendingTick ||
                    response.action != "idle" || response.reason != "diagnostic_only" ||
                    response.valid_ms < 1 || response.valid_ms > 1000 ||
                    elapsed > MaxAgeMs || elapsed > response.valid_ms)
                {
                    ProbeLogger.Log("AI_BRIDGE_REJECTED: unexpected or expired response, rtt_ms=" +
                                    elapsed.ToString("F0"));
                    return;
                }
                ProbeLogger.Log("AI_BRIDGE_OK: tick=" + response.tick + " action=" +
                                response.action + " reason=" + response.reason +
                                " rtt_ms=" + elapsed.ToString("F0"));
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("AI_BRIDGE_UNAVAILABLE: " + ex.GetBaseException().Message);
            }
        }

        private void SendObservation()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                ProbeLogger.Log("AI_BRIDGE_SKIPPED: Camera.main missing; enter a level");
                return;
            }

            Vector3 pos = cam.transform.position;
            Vector3 forward = cam.transform.forward;
            int count = Physics.OverlapSphereNonAlloc(
                pos, 12f, _nearby, ~0, QueryTriggerInteraction.Collide);
            Array.Clear(_nearby, 0, _nearby.Length);
            var request = new DecisionRequest
            {
                tick = ++_nextTick,
                observation = new Observation
                {
                    scene = SceneManager.GetActiveScene().name,
                    position = new float[] { pos.x, pos.y, pos.z },
                    forward = new float[] { forward.x, forward.y, forward.z },
                    nearby_colliders = count
                }
            };
            // Serialize on Unity main thread. Background thread handles only bytes and HTTP.
            string json = JsonUtility.ToJson(request);
            _pendingTick = request.tick;
            _sentRealtime = Time.realtimeSinceStartup;
            _pending = Task.Run(() => Post(json));
            ProbeLogger.Log("AI_BRIDGE_SENT: tick=" + _pendingTick + " camera_scene=" +
                            request.observation.scene + " colliders=" + count);
        }

        private static string Post(string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
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
                    throw new IOException("Unexpected HTTP status " + (int)response.StatusCode);
                using (var stream = response.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    byte[] buffer = new byte[512];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (ms.Length + read > MaxResponseBytes)
                            throw new IOException("AI response too large");
                        ms.Write(buffer, 0, read);
                    }
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        private void OnDestroy()
        {
            _closing = true;
            _pending = null; // Do not wait for background HTTP work.
        }
    }
}
