using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// A bounded cache of portals ACTUALLY verified by MobileNPCHumanoidMotor's
    /// facade scanner (continuous floor and unobstructed standing capsule).
    /// It deliberately never invents passages from rectangles or NavMesh bounds.
    /// Cache is per live scene; callers revalidate actual physical traversability.
    /// </summary>
    internal static class HybridNPCObservedPortalRegistry
    {
        internal struct Passage
        {
            internal Vector3 near;
            internal Vector3 far;
            internal float observedAt;
            // A passage may be physically sensed but not yet traversed.
            // Only real arrival at its far side promotes it to confirmed.
            internal bool crossed;
            internal float blockedUntil;
        }
        private static readonly List<Passage> entries = new List<Passage>(64);
        private const float Lifetime = 180f;
        // Confirmed topology persists for this loaded map. The cache is
        // bounded, and every actual route is revalidated against current physics.
        private const float ConfirmedLifetime = float.MaxValue;
        private const float BlockedCooldown = 8f;
        private const int Capacity = 64;
        // Shared across survivors, worlds and save slots, but isolated by map.
        // Only crossings proven by actual controller movement are serialized.
        private const int PersistentCapacity = 128;
        private const int FormatVersion = 1;
        private const float WriteInterval = 30f;
        private static string activeMapName;
        private static bool learningDirty;
        private static float nextWriteAt;
        private static bool preservationMode;

        [Serializable]
        private sealed class CollectiveFile
        {
            public int version = FormatVersion;
            public string mapName;
            public List<CollectiveCrossing> crossings = new List<CollectiveCrossing>();
        }

        [Serializable]
        private sealed class CollectiveCrossing
        {
            public Vector3 near;
            public Vector3 far;
        }

        private static bool Finite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private static string LearningFilePath(string mapName)
        {
            // Map names are scoped independently of the NPC save slot or
            // character ID. Any imported path characters are sanitized.
            char[] invalid = Path.GetInvalidFileNameChars();
            string key = mapName;
            for (int i = 0; i < invalid.Length; ++i)
                key = key.Replace(invalid[i], '_');
            key = key.Replace('/', '_').Replace('\\', '_');
            return Path.Combine(Application.persistentDataPath,
                "MobileNPCs", "CollectiveNavigation", key + ".json");
        }

        internal static void ConfigureForMapServer(string mapName)
        {
            if (!Provider.isServer || string.IsNullOrEmpty(mapName)) return;
            if (string.Equals(activeMapName, mapName, StringComparison.Ordinal))
                return;
            FlushLearningServer();
            entries.Clear();
            activeMapName = mapName;
            learningDirty = false;
            preservationMode = false;
            nextWriteAt = Time.time + WriteInterval;
            string path = LearningFilePath(mapName);
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
            bool loaded = false;
            string[] sources = { path, path + ".bak" };
            for (int attempt = 0; attempt < sources.Length && !loaded; ++attempt)
            {
                if (!File.Exists(sources[attempt])) continue;
                try
                {
                    CollectiveFile saved = JsonUtility.FromJson<CollectiveFile>(
                        File.ReadAllText(sources[attempt]));
                    if (saved == null || saved.version != FormatVersion
                        || saved.mapName != mapName || saved.crossings == null)
                        continue;
                    for (int i = 0; i < saved.crossings.Count && i < PersistentCapacity; ++i)
                    {
                        CollectiveCrossing edge = saved.crossings[i];
                        if (edge == null || !Finite(edge.near) || !Finite(edge.far)
                            || Vector3.Distance(edge.near, edge.far) < 0.4f
                            || Vector3.Distance(edge.near, edge.far) > 4.5f)
                            continue;
                        // Imported edges are hypotheses until the current
                        // NPC physically validates a path. Never teleport.
                        if (entries.Count == Capacity) entries.RemoveAt(0);
                        entries.Add(new Passage {
                            near = edge.near, far = edge.far,
                            observedAt = Time.time, crossed = true
                        });
                    }
                    loaded = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log("[NPCCollectiveNavigation] Loaded "
                        + entries.Count + " confirmed crossing candidates for " + mapName);
#endif
                }
                catch (Exception error)
                {
                    Debug.LogWarning("[NPCCollectiveNavigation] Load failed: " + error.Message);
                }
            }
            if (!loaded)
            {
                // Do not destroy a possibly recoverable existing file.
                preservationMode = true;
                Debug.LogWarning("[NPCCollectiveNavigation] Invalid learning file; preservation mode enabled.");
            }
        }

        internal static void TickLearningServer()
        {
            if (Provider.isServer && learningDirty && Time.time >= nextWriteAt)
                FlushLearningServer();
        }

        internal static void FlushLearningServer()
        {
            if (!Provider.isServer || !learningDirty || preservationMode
                || string.IsNullOrEmpty(activeMapName)) return;
            string path = LearningFilePath(activeMapName);
            string pending = path + ".tmp";
            try
            {
                CollectiveFile snapshot = new CollectiveFile { mapName = activeMapName };
                for (int i = entries.Count - 1;
                    i >= 0 && snapshot.crossings.Count < PersistentCapacity; --i)
                {
                    Passage p = entries[i];
                    if (!p.crossed || !Finite(p.near) || !Finite(p.far)) continue;
                    snapshot.crossings.Add(new CollectiveCrossing { near = p.near, far = p.far });
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(pending, JsonUtility.ToJson(snapshot, true));
                if (File.Exists(path))
                {
                    try { File.Replace(pending, path, path + ".bak"); }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(path, path + ".bak", true);
                        File.Delete(path);
                        File.Move(pending, path);
                    }
                }
                else File.Move(pending, path);
                learningDirty = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[NPCCollectiveNavigation] Saved "
                    + snapshot.crossings.Count + " confirmed crossings for " + activeMapName);
#endif
            }
            catch (Exception error)
            {
                Debug.LogWarning("[NPCCollectiveNavigation] Save failed: " + error.Message);
            }
            finally
            {
                try { if (File.Exists(pending)) File.Delete(pending); } catch { }
                nextWriteAt = Time.time + WriteInterval;
            }
        }


        internal static void Reset()
        {
            FlushLearningServer();
            entries.Clear();
            activeMapName = null;
            learningDirty = false;
            preservationMode = false;
        }

        internal static void Remember(Vector3 near, Vector3 far)
        {
            for (int i = entries.Count - 1; i >= 0; --i)
            {
                if (Time.time - entries[i].observedAt >
                    (entries[i].crossed ? ConfirmedLifetime : Lifetime)) entries.RemoveAt(i);
                else if ((entries[i].near - near).sqrMagnitude < 0.5f &&
                    (entries[i].far - far).sqrMagnitude < 0.5f)
                {
                    entries[i] = new Passage { near = near, far = far,
                        observedAt = Time.time, crossed = entries[i].crossed,
                        blockedUntil = entries[i].blockedUntil };
                    return;
                }
            }
            if (entries.Count >= Capacity) entries.RemoveAt(0);
            entries.Add(new Passage { near = near, far = far, observedAt = Time.time });
        }

        /// <summary>
        /// Share only an actually traversed doorway as confirmed topology.
        /// The registry is an advisory graph; consumers still perform full
        /// ground and capsule route validation before moving.
        /// </summary>
        internal static void ConfirmCrossing(Vector3 entry, Vector3 exit)
        {
            for (int i = 0; i < entries.Count; ++i)
            {
                Passage existing = entries[i];
                bool same = (existing.near - entry).sqrMagnitude < 0.64f
                    && (existing.far - exit).sqrMagnitude < 0.64f;
                bool opposite = (existing.near - exit).sqrMagnitude < 0.64f
                    && (existing.far - entry).sqrMagnitude < 0.64f;
                if (!same && !opposite) continue;
                existing.crossed = true;
                existing.observedAt = Time.time;
                existing.blockedUntil = 0f;
                entries[i] = existing;
                learningDirty = true;
                return;
            }
            if (entries.Count >= Capacity) entries.RemoveAt(0);
            entries.Add(new Passage {
                near = entry, far = exit, observedAt = Time.time, crossed = true
            });
            learningDirty = true;
        }

        internal static bool IsTemporarilyBlocked(Vector3 entry, Vector3 exit)
        {
            for (int i = 0; i < entries.Count; ++i)
            {
                Passage p = entries[i];
                if (Time.time >= p.blockedUntil) continue;
                bool same = (p.near - entry).sqrMagnitude < 0.64f
                    && (p.far - exit).sqrMagnitude < 0.64f;
                bool reverse = (p.near - exit).sqrMagnitude < 0.64f
                    && (p.far - entry).sqrMagnitude < 0.64f;
                if (same || reverse) return true;
            }
            return false;
        }

        /// <summary>
        /// Temporarily deprioritize a confirmed passage after sustained,
        /// actor-free physical obstruction immediately at its approach.
        /// This does NOT invalidate the discovered geometry or erase it
        /// from individual memory. A nearby success clears the cooldown.
        /// </summary>
        internal static bool ReportBlockedApproach(Vector3 actorPosition,
            Vector3 requestedWaypoint)
        {
            int nearestIndex = -1;
            float nearestScore = float.PositiveInfinity;
            for (int i = 0; i < entries.Count; ++i)
            {
                Passage passage = entries[i];
                if (!passage.crossed ||
                    Time.time - passage.observedAt > ConfirmedLifetime)
                    continue;
                float nearActor = (passage.near - actorPosition).sqrMagnitude;
                float farActor = (passage.far - actorPosition).sqrMagnitude;
                float nearWaypoint = (passage.near - requestedWaypoint).sqrMagnitude;
                float farWaypoint = (passage.far - requestedWaypoint).sqrMagnitude;
                // Reject unrelated nearby doorways and distant tactical goals.
                float actorScore = Mathf.Min(nearActor, farActor);
                float waypointScore = Mathf.Min(nearWaypoint, farWaypoint);
                if (actorScore > 9f || waypointScore > 4f)
                    continue;
                float score = actorScore + waypointScore;
                if (score >= nearestScore) continue;
                nearestScore = score;
                nearestIndex = i;
            }
            if (nearestIndex < 0) return false;
            Passage blocked = entries[nearestIndex];
            blocked.blockedUntil = Mathf.Max(blocked.blockedUntil,
                Time.time + BlockedCooldown);
            entries[nearestIndex] = blocked;
            return true;
        }

        /// <summary>
        /// Long-distance staging over ONE personally verified crossing
        /// learned by the shared scene registry. This is deliberately not a
        /// full route to the distant goal: the global planner remains
        /// responsible for the next region and must be run again after arrival.
        /// Both approach and crossing require fresh physical A* proof.
        /// </summary>
        internal static bool TryStageThroughConfirmedCrossing(Vector3 start,
            Vector3 distantGoal, HybridNPCGridPlanner.Settings config,
            List<Vector3> output)
        {
            if (output == null) return false;
            output.Clear();
            // Two physically proven candidates rather than blindly trusting
            // the highest-scoring doorway. A temporary obstruction may block
            // the first while another genuine passage remains available.
            int firstIndex = -1, secondIndex = -1;
            bool firstReverse = false, secondReverse = false;
            float firstScore = float.NegativeInfinity;
            float secondScore = float.NegativeInfinity;
            float startToGoal = Vector3.Distance(start, distantGoal);
            for (int i = 0; i < entries.Count; ++i)
            {
                Passage p = entries[i];
                if (!p.crossed || Time.time - p.observedAt > ConfirmedLifetime
                    || Time.time < p.blockedUntil)
                    continue;
                for (int orientation = 0; orientation < 2; ++orientation)
                {
                    Vector3 entry = orientation == 0 ? p.near : p.far;
                    Vector3 exit = orientation == 0 ? p.far : p.near;
                    float approach = Vector3.Distance(start, entry);
                    float across = Vector3.Distance(entry, exit);
                    if (approach > Mathf.Min(13f, config.maxSearchRadius * 0.6f)
                        || across < 0.5f || across > 4f)
                        continue;
                    float progress = startToGoal - Vector3.Distance(exit, distantGoal);
                    if (progress < 3f) continue;
                    float score = progress - approach * 0.4f;
                    if (score > firstScore)
                    {
                        secondIndex = firstIndex;
                        secondReverse = firstReverse;
                        secondScore = firstScore;
                        firstIndex = i;
                        firstReverse = orientation == 1;
                        firstScore = score;
                    }
                    else if (score > secondScore && i != firstIndex)
                    {
                        secondIndex = i;
                        secondReverse = orientation == 1;
                        secondScore = score;
                    }
                }
            }
            var approachPath = new List<Vector3>(32);
            var crossingPath = new List<Vector3>(12);
            for (int attempt = 0; attempt < 2; ++attempt)
            {
                int index = attempt == 0 ? firstIndex : secondIndex;
                if (index < 0) continue;
                Passage candidate = entries[index];
                bool reverse = attempt == 0 ? firstReverse : secondReverse;
                Vector3 from = reverse ? candidate.far : candidate.near;
                Vector3 to = reverse ? candidate.near : candidate.far;
                approachPath.Clear();
                crossingPath.Clear();
                if (!HybridNPCGridPlanner.TryFindPath(start, from, config, approachPath)
                    || !HybridNPCGridPlanner.TryFindPath(from, to, config, crossingPath))
                    continue;
                float crossingLength = 0f;
                for (int i = 1; i < crossingPath.Count; ++i)
                    crossingLength += Vector3.Distance(crossingPath[i - 1], crossingPath[i]);
                if (crossingLength > Mathf.Max(2.5f, Vector3.Distance(from, to) * 1.6f))
                    continue;
                Append(output, approachPath);
                Append(output, crossingPath);
                if (output.Count >= 2) return true;
                output.Clear();
            }
            return false;
        }

        /// <summary>
        /// Select a nearby observed crossing, then compute exact ground route
        /// to its entry and from its exit to the destination. Never cross
        /// without validating the entire portal-to-portal path again.
        /// </summary>
        internal static bool TryRoute(Vector3 start, Vector3 goal,
            HybridNPCGridPlanner.Settings config, List<Vector3> output)
        {
            output.Clear();
            // Retry several actually observed openings. One inaccessible
            // entrance must not prevent a second doorway from being used.
            const int MaxCandidates = 4;
            int[] indices = { -1, -1, -1, -1 };
            bool[] reverse = new bool[MaxCandidates];
            float[] scores = { float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity, float.PositiveInfinity };
            for (int i = 0; i < entries.Count; ++i)
            {
                Passage p = entries[i];
                if (Time.time - p.observedAt >
                    (p.crossed ? ConfirmedLifetime : Lifetime)
                    || Time.time < p.blockedUntil) continue;
                float forward = Vector3.Distance(start, p.near) +
                    Vector3.Distance(goal, p.far);
                float backward = Vector3.Distance(start, p.far) +
                    Vector3.Distance(goal, p.near);
                bool backwardPreferred = backward < forward;
                // Prefer successfully traversed connections without treating them
                // as collision-free: every leg is validated below.
                float score = Mathf.Min(forward, backward) - (p.crossed ? 2f : 0f);
                if (score > config.maxSearchRadius * 1.75f) continue;
                for (int slot = 0; slot < MaxCandidates; ++slot)
                {
                    if (score >= scores[slot]) continue;
                    for (int shift = MaxCandidates - 1; shift > slot; --shift)
                    {
                        scores[shift] = scores[shift - 1];
                        indices[shift] = indices[shift - 1];
                        reverse[shift] = reverse[shift - 1];
                    }
                    scores[slot] = score;
                    indices[slot] = i;
                    reverse[slot] = backwardPreferred;
                    break;
                }
            }

            var first = new List<Vector3>(32);
            var crossing = new List<Vector3>(16);
            var last = new List<Vector3>(32);
            for (int candidate = 0; candidate < MaxCandidates; ++candidate)
            {
                if (indices[candidate] < 0) break;
                Passage p = entries[indices[candidate]];
                Vector3 entry = reverse[candidate] ? p.far : p.near;
                Vector3 exit = reverse[candidate] ? p.near : p.far;
                first.Clear();
                crossing.Clear();
                last.Clear();
                // Every leg must be grounded and navigable. A doorway is
                // never treated as a teleport or an exception to collisions.
                if (!HybridNPCGridPlanner.TryFindPath(start, entry, config, first)
                    || !HybridNPCGridPlanner.TryFindPath(entry, exit, config, crossing)
                    || !HybridNPCGridPlanner.TryFindPath(exit, goal, config, last))
                    continue;

                float crossingLength = 0f;
                for (int i = 1; i < crossing.Count; ++i)
                    crossingLength += Vector3.Distance(crossing[i - 1], crossing[i]);
                if (crossingLength > Mathf.Max(2.5f,
                    Vector3.Distance(entry, exit) * 1.6f))
                    continue;
                output.Clear();
                Append(output, first);
                Append(output, crossing);
                Append(output, last);
                if (output.Count >= 2) return true;
            }
            output.Clear();
            return false;
        }

        private static void Append(List<Vector3> target, List<Vector3> source)
        {
            foreach (Vector3 point in source)
                if (target.Count == 0 ||
                    (target[target.Count - 1] - point).sqrMagnitude > 0.0025f)
                    target.Add(point);
        }
    }
}
