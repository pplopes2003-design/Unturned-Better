using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// F12 local read-only snapshot of NPC-like Unity components.
    /// This does not create a mobile NPC or assign any movement/attack AI.
    /// Searches only once when pressed, not every frame.
    /// </summary>
    internal sealed class NpcInstanceProbe : MonoBehaviour
    {
        private const int MaxMatches = 80;
        private readonly Dictionary<int, Vector3> _previous = new Dictionary<int, Vector3>();
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private static string Num(float value)
        {
            return value.ToString("R", Invariant);
        }

        private static string Escape(string value)
        {
            if (value == null) return "null";
            StringBuilder result = new StringBuilder(value.Length + 8);
            result.Append('"');
            foreach (char c in value)
            {
                if (c == '"') result.Append("\\\"");
                else if (c == '\\') result.Append("\\\\");
                else if (c == '\n') result.Append("\\n");
                else if (c == '\r') result.Append("\\r");
                else if (c < 32) result.Append("?");
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }

        private static string Vec(Vector3 p)
        {
            return "[" + Num(p.x) + "," + Num(p.y) + "," + Num(p.z) + "]";
        }

        private static bool IsNpcLike(Type type)
        {
            string name = type.FullName ?? type.Name;
            // Avoid mistaking our own telemetry component for a real entity.
            if (type.Namespace == "Unturned2.Line2") return false;
            return name.IndexOf("npc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("human", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F12)) Snapshot();
        }

        private void Snapshot()
        {
            try
            {
                Camera camera = Camera.main;
                if (camera == null)
                {
                    ProbeLogger.Log("NPC_SNAPSHOT_SKIPPED: enter a map first.");
                    return;
                }

                MonoBehaviour[] all = FindObjectsOfType<MonoBehaviour>();
                int totalMatchingComponents = 0;
                int written = 0;
                var current = new Dictionary<int, Vector3>();
                var json = new StringBuilder(20480);
                json.Append("{\"timestamp\":").Append(Escape(DateTime.UtcNow.ToString("o")))
                    .Append(",\"cameraPosition\":").Append(Vec(camera.transform.position))
                    .Append(",\"instances\":[");

                foreach (MonoBehaviour component in all)
                {
                    if (component == null || !IsNpcLike(component.GetType())) continue;
                    totalMatchingComponents++;
                    if (written >= MaxMatches) continue;

                    int id = component.GetInstanceID();
                    Vector3 position = component.transform.position;
                    Vector3 previous;
                    bool seen = _previous.TryGetValue(id, out previous);
                    float distance = seen ? Vector3.Distance(previous, position) : 0f;
                    if (written > 0) json.Append(",");
                    json.Append("{\"component\":").Append(Escape(component.GetType().FullName))
                        .Append(",\"object\":").Append(Escape(component.gameObject.name))
                        .Append(",\"instanceId\":").Append(id)
                        .Append(",\"position\":").Append(Vec(position))
                        .Append(",\"distanceToCamera\":").Append(Num(Vector3.Distance(camera.transform.position, position)))
                        .Append(",\"seenPreviously\":").Append(seen ? "true" : "false")
                        .Append(",\"metersSincePrevious\":").Append(Num(distance))
                        .Append("}");
                    current[id] = position;
                    written++;
                }
                json.Append("],\"matchingComponents\":").Append(totalMatchingComponents)
                    .Append(",\"written\":").Append(written)
                    .Append(",\"truncated\":").Append(totalMatchingComponents > MaxMatches ? "true" : "false")
                    .Append("}");

                _previous.Clear();
                foreach (KeyValuePair<int, Vector3> item in current)
                    _previous[item.Key] = item.Value;
                ProbeLogger.SaveNpcSnapshot(json.ToString());
                ProbeLogger.Log("NPC_SNAPSHOT_OK: matchingComponents=" + totalMatchingComponents +
                    " written=" + written + ". Component matches do not prove mobile NPC AI.");
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("NPC_SNAPSHOT_FAILED: " + ex);
            }
        }
    }
}
