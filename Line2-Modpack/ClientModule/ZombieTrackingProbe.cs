using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// F11 read-only snapshots of real active Steam zombies, using actual
    /// Regions.tryGetCoordinate rather than mislabeling Zombie.bound as a
    /// world region. Samples only on hotkey; NEVER affects zombie AI/count.
    /// </summary>
    internal sealed class ZombieTrackingProbe : MonoBehaviour
    {
        private const int MaxZombies = 128;
        private readonly Dictionary<int, Sample> _previous = new Dictionary<int, Sample>();
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private struct Sample
        {
            public Vector3 position;
            public byte x;
            public byte y;
            public bool validRegion;
            public ushort zombieId;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F11)) Snapshot();
        }

        private static string F(float value)
        {
            return value.ToString("R", Invariant);
        }

        private static string Vec(Vector3 p)
        {
            return "[" + F(p.x) + "," + F(p.y) + "," + F(p.z) + "]";
        }

        private static string Q(string value)
        {
            if (value == null) return "null";
            var s = new StringBuilder(value.Length + 8);
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

        private void Snapshot()
        {
            try
            {
                Camera camera = Camera.main;
                if (camera == null || !Level.isLoaded)
                {
                    ProbeLogger.Log("ZOMBIE_SNAPSHOT_SKIPPED: enter a loaded map first.");
                    return;
                }

                // One-time query, never called each frame. Strong-typed scan
                // avoids enumerating every MonoBehaviour or relying on
                // reflection guesses about 'bound' vs map region.
                Zombie[] zombies = FindObjectsOfType<Zombie>();
                var current = new Dictionary<int, Sample>();
                int written = 0;
                int moved = 0;
                int regionCrossed = 0;
                int hunting = 0;
                var output = new StringBuilder(24000);
                byte camX, camY;
                bool camValid = Regions.tryGetCoordinate(camera.transform.position,
                                                           out camX, out camY);
                output.Append("{\"timestamp\":").Append(Q(DateTime.UtcNow.ToString("o")))
                    .Append(",\"camera\":").Append(Vec(camera.transform.position))
                    .Append(",\"cameraRegionValid\":").Append(camValid ? "true" : "false")
                    .Append(",\"cameraRegion\":").Append(camValid
                        ? "[" + camX + "," + camY + "]" : "null")
                    .Append(",\"totalActiveZombies\":").Append(zombies.Length)
                    .Append(",\"zombies\":[");

                foreach (Zombie zombie in zombies)
                {
                    if (written >= MaxZombies) break;
                    if (zombie == null) continue;
                    int instance = zombie.GetInstanceID();
                    Vector3 pos = zombie.transform.position;
                    byte x, y;
                    bool valid = Regions.tryGetCoordinate(pos, out x, out y);
                    Sample old;
                    bool known = _previous.TryGetValue(instance, out old)
                        && old.zombieId == zombie.id;
                    float meters = known ? Vector3.Distance(old.position, pos) : 0f;
                    bool crossed = known && valid && old.validRegion &&
                        (old.x != x || old.y != y);
                    if (known && meters > 0.5f) moved++;
                    if (crossed) regionCrossed++;
                    if (zombie.isHunting) hunting++;
                    if (written > 0) output.Append(",");
                    output.Append("{\"instanceId\":").Append(instance)
                        .Append(",\"zombieId\":").Append(zombie.id)
                        .Append(",\"spawnBound\":").Append(zombie.bound)
                        .Append(",\"dead\":").Append(zombie.isDead ? "true" : "false")
                        .Append(",\"hunting\":").Append(zombie.isHunting ? "true" : "false")
                        .Append(",\"position\":").Append(Vec(pos))
                        .Append(",\"region\":").Append(valid
                            ? Q(x.ToString(Invariant) + "," + y.ToString(Invariant))
                            : "null")
                        .Append(",\"regionX\":").Append(valid ? x.ToString(Invariant) : "null")
                        .Append(",\"regionY\":").Append(valid ? y.ToString(Invariant) : "null")
                        .Append(",\"previouslyObserved\":").Append(known ? "true" : "false")
                        .Append(",\"metersSincePrevious\":").Append(F(meters))
                        .Append(",\"regionChanged\":").Append(crossed ? "true" : "false")
                        .Append("}");
                    current[instance] = new Sample {
                        position = pos, x = x, y = y,
                        validRegion = valid, zombieId = zombie.id
                    };
                    written++;
                }

                int vanished = 0;
                foreach (int previousId in _previous.Keys)
                    if (!current.ContainsKey(previousId)) vanished++;
                output.Append("],\"count\":").Append(written)
                    .Append(",\"movingSincePrevious\":").Append(moved)
                    .Append(",\"huntingCount\":").Append(hunting)
                    .Append(",\"regionChangedSincePrevious\":").Append(regionCrossed)
                    .Append(",\"vanishedSincePrevious\":").Append(vanished)
                    .Append(",\"truncated\":").Append(zombies.Length > MaxZombies ? "true" : "false")
                    .Append(",\"sampleLimit\":").Append(MaxZombies)
                    .Append("}");

                ProbeLogger.SaveZombieSnapshot(output.ToString());
                _previous.Clear();
                foreach (var item in current)
                    _previous.Add(item.Key, item.Value);
                ProbeLogger.Log("ZOMBIE_SNAPSHOT_OK: count=" + written +
                    " hunting=" + hunting + " moved=" + moved +
                    " actualRegionCrossings=" + regionCrossed +
                    " vanished=" + vanished + " (bound is spawn area, not region)");
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("ZOMBIE_SNAPSHOT_FAILED: " + ex);
            }
        }
    }
}
