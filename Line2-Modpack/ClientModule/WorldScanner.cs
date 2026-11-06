using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// On-demand, read-only world probe. F8 scans nearby loaded colliders.
    /// Candidate labels are heuristics, NOT validated traversable portals.
    /// </summary>
    internal sealed class WorldScanner : MonoBehaviour
    {
        private const float Radius = 22f;
        private const int MaxColliders = 1024;
        private const int MaxEntries = 400;
        private readonly Collider[] _hits = new Collider[MaxColliders];
        private readonly HashSet<int> _seen = new HashSet<int>();
        private static readonly string[] DoorWords = { "door", "gate", "hatch" };
        private static readonly string[] WindowWords = { "window", "glass", "pane", "shutter" };
        private static readonly string[] PassageWords = { "stairs", "stair", "ladder", "ramp", "entrance", "portal" };
        private static readonly string[] WallWords = { "wall", "fence", "barricade" };
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                Scan();
        }

        private void Scan()
        {
            try
            {
                // Do not depend on SDG player APIs for the bootstrap.
                var camera = Camera.main;
                if (camera == null)
                {
                    ProbeLogger.Log("SCAN_SKIPPED: Camera.main is null (enter the world first).");
                    return;
                }
                var origin = camera.transform.position;
                _seen.Clear();
                int count = Physics.OverlapSphereNonAlloc(
                    origin, Radius, _hits, ~0, QueryTriggerInteraction.Collide);
                var output = new StringBuilder(1024 * 48);
                string scannedAt = DateTime.UtcNow.ToString("o");
                output.Append("{\"type\":\"scan\",\"timestamp\":").Append(Quote(scannedAt))
                    .Append(",\"radius\":").Append(Num(Radius))
                    .Append(",\"origin\":").Append(V3(origin))
                    .Append(",\"collidersFound\":").Append(count)
                    .Append(",\"bufferFull\":").Append(count == _hits.Length ? "true" : "false")
                    .Append(",\"objects\":[");
                int written = 0;
                int candidates = 0;
                for (int i = 0; i < count && written < MaxEntries; i++)
                {
                    Collider c = _hits[i];
                    if (c == null) continue;
                    Transform t = c.transform;
                    if (!_seen.Add(t.GetInstanceID())) continue;
                    string path = HierarchyPath(t);
                    string category = CandidateCategory(path);
                    if (category != "unknown") candidates++;
                    Bounds b = c.bounds;
                    if (written > 0) output.Append(',');
                    output.Append("{\"instanceId\":").Append(t.GetInstanceID())
                        .Append(",\"name\":").Append(Quote(t.name))
                        .Append(",\"path\":").Append(Quote(path))
                        .Append(",\"candidate\":").Append(Quote(category))
                        .Append(",\"position\":").Append(V3(t.position))
                        .Append(",\"boundsCenter\":").Append(V3(b.center))
                        .Append(",\"boundsSize\":").Append(V3(b.size))
                        .Append(",\"layer\":").Append(t.gameObject.layer)
                        .Append(",\"layerName\":").Append(Quote(LayerMask.LayerToName(t.gameObject.layer)))
                        .Append(",\"colliderType\":").Append(Quote(c.GetType().Name))
                        .Append(",\"trigger\":").Append(c.isTrigger ? "true" : "false")
                        .Append(",\"enabled\":").Append(c.enabled ? "true" : "false")
                        .Append('}');
                    written++;
                }
                output.Append("],\"written\":").Append(written)
                    .Append(",\"candidateCount\":").Append(candidates)
                    .Append(",\"truncated\":").Append(count > written ? "true" : "false")
                    .Append('}');
                ProbeLogger.SaveScan(output.ToString());
                ProbeLogger.Log("SCAN_OK: colliders=" + count + ", uniqueWritten=" +
                                written + ", heuristicCandidates=" + candidates +
                                ". F8 can be pressed again at another location.");
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("SCAN_FAILED: " + ex);
            }
            finally
            {
                Array.Clear(_hits, 0, _hits.Length);
                _seen.Clear();
            }
        }

        private static string CandidateCategory(string path)
        {
            string s = path.ToLowerInvariant();
            if (ContainsAny(s, DoorWords)) return "door_candidate";
            if (ContainsAny(s, WindowWords)) return "window_candidate";
            if (ContainsAny(s, PassageWords)) return "passage_candidate";
            if (ContainsAny(s, WallWords)) return "wall_candidate";
            return "unknown";
        }

        private static bool ContainsAny(string input, string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
                if (input.Contains(terms[i])) return true;
            return false;
        }

        private static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            int depth = 0;
            while (t != null && depth++ < 16)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string Num(float n)
        {
            return n.ToString("R", Invariant);
        }

        private static string V3(Vector3 v)
        {
            return "[" + Num(v.x) + "," + Num(v.y) + "," + Num(v.z) + "]";
        }

        private static string Quote(string value)
        {
            if (value == null) return "null";
            var b = new StringBuilder(value.Length + 8);
            b.Append('"');
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (ch < 32) b.Append("\\u").Append(((int)ch).ToString("x4"));
                        else b.Append(ch);
                        break;
                }
            }
            return b.Append('"').ToString();
        }
    }

    internal static class ProbeLogger
    {
        private static readonly object Sync = new object();
        private static string DirectoryPath
        {
            get { return Path.Combine(Environment.CurrentDirectory, "Unturned2-Line2"); }
        }

        public static void Log(string message)
        {
            var line = DateTime.UtcNow.ToString("o") + " [Unturned2.Line2] " + message;
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(DirectoryPath);
                    File.AppendAllText(Path.Combine(DirectoryPath, "probe.log"), line + Environment.NewLine);
                }
            }
            catch (Exception) { /* do not crash game due to logging */ }
            Debug.Log(line);
        }

        public static void SaveDiagnostic(string fileName, string content)
        {
            // Only a single hardcoded diagnostic file is accepted.
            if (fileName != "npc-api-discovery.txt")
                throw new ArgumentException("Unexpected diagnostic filename");
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(Path.Combine(DirectoryPath, fileName), content, Encoding.UTF8);
            }
        }

        public static void SaveScan(string json)
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(Path.Combine(DirectoryPath, "scans.jsonl"), json + Environment.NewLine);
            }
        }
    }
}
