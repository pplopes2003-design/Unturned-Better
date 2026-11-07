using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// F11 read-only zombie snapshots. Never moves, spawns, kills or retargets
    /// zombies; avoids invoking any Unturned-specific method. Sampling is
    /// manually triggered only, so idle frame cost is negligible.
    /// </summary>
    internal sealed class ZombieTrackingProbe : MonoBehaviour
    {
        private const int MaxZombies = 128;
        private const int MaxFields = 3;
        private readonly Dictionary<int, Sample> _previous = new Dictionary<int, Sample>();
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly string[] RegionNames = { "bound", "region", "nav" };

        private struct Sample
        {
            public Vector3 position;
            public string region;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F11))
                Snapshot();
        }

        private static string RegionInfo(Type type, object instance)
        {
            foreach (string name in RegionNames)
            {
                try
                {
                    var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
                    if (f != null && (f.FieldType.IsPrimitive || f.FieldType.IsEnum))
                        return name + ":" + Convert.ToString(f.GetValue(instance), Invariant);
                    var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                    if (p != null && p.GetIndexParameters().Length == 0 &&
                        (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum))
                        return name + ":" + Convert.ToString(p.GetValue(instance, null), Invariant);
                }
                catch { /* unavailable field: do not impact gameplay */ }
            }
            return "unavailable";
        }

        private static string F(float v) { return v.ToString("R", Invariant); }

        private static string JsonEscape(string value)
        {
            if (value == null) return "null";
            var s = new StringBuilder();
            s.Append('"');
            foreach (char c in value)
            {
                if (c == '"') s.Append("\\\"");
                else if (c == '\\') s.Append("\\\\");
                else if (c == '\n') s.Append("\\n");
                else if (c == '\r') s.Append("\\r");
                else if (c < 32) s.Append("?");
                else s.Append(c);
            }
            return s.Append('"').ToString();
        }

        private static string Vec(Vector3 v)
        {
            return "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
        }

        private void Snapshot()
        {
            try
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    ProbeLogger.Log("ZOMBIE_SNAPSHOT_SKIPPED: no camera; load Washington.");
                    return;
                }
                // One-time F11 scan. Do not use this every frame: finding all
                // MonoBehaviours is relatively expensive.
                MonoBehaviour[] components = FindObjectsOfType<MonoBehaviour>();
                int written = 0, moved = 0, regionChanged = 0;
                var current = new Dictionary<int, Sample>();
                var output = new StringBuilder(32000);
                output.Append("{\"timestamp\":").Append(JsonEscape(DateTime.UtcNow.ToString("o")))
                    .Append(",\"camera\":").Append(Vec(cam.transform.position))
                    .Append(",\"zombies\":[");
                foreach (MonoBehaviour component in components)
                {
                    if (written >= MaxZombies) break;
                    if (component == null) continue;
                    Type type = component.GetType();
                    if (type.Name != "Zombie" || type.Namespace != "SDG.Unturned")
                        continue;
                    Transform tr = component.transform;
                    int id = component.GetInstanceID();
                    string region = RegionInfo(type, component);
                    Vector3 pos = tr.position;
                    Sample old;
                    bool known = _previous.TryGetValue(id, out old);
                    float distance = known ? Vector3.Distance(pos, old.position) : 0f;
                    bool crossed = known && old.region != "unavailable" &&
                                   region != "unavailable" && old.region != region;
                    if (known && distance > 0.5f) moved++;
                    if (crossed) regionChanged++;
                    if (written > 0) output.Append(",");
                    output.Append("{\"instanceId\":").Append(id)
                        .Append(",\"position\":").Append(Vec(pos))
                        .Append(",\"region\":").Append(JsonEscape(region))
                        .Append(",\"previouslyObserved\":").Append(known ? "true" : "false")
                        .Append(",\"metersSincePrevious\":").Append(F(distance))
                        .Append(",\"regionChanged\":").Append(crossed ? "true" : "false")
                        .Append("}");
                    current[id] = new Sample { position = pos, region = region };
                    written++;
                }
                output.Append("],\"count\":").Append(written)
                    .Append(",\"movedSincePrevious\":").Append(moved)
                    .Append(",\"regionChangedSincePrevious\":").Append(regionChanged)
                    .Append(",\"sampleLimit\":").Append(MaxZombies)
                    .Append("}");
                ProbeLogger.SaveZombieSnapshot(output.ToString());
                _previous.Clear();
                foreach (var pair in current) _previous.Add(pair.Key, pair.Value);
                ProbeLogger.Log("ZOMBIE_SNAPSHOT_OK: count=" + written + " moved=" + moved +
                    " regionChanged=" + regionChanged +
                    ". F11 again after approaching/leaving a zombie area.");
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("ZOMBIE_SNAPSHOT_FAILED: " + ex);
            }
        }
    }
}
