////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using UnityEngine;
using SDG.Unturned;

namespace Unturned2.Line2
{
    /// <summary>
    /// Small, bounded, per-individual online experience model. NOT a neural network.
    /// Learns only from resolved server-side outcomes; no target transform, path or
    /// faction-wide static cache is stored. A freshly spawned NPC has an empty model.
    /// An existing survivor may save/restore its own experiences via persistence.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PrototypeNpcVitals))]
    public sealed class Line2MobileNPCAdaptiveMemory : MonoBehaviour
    {
        public enum ExperienceKind
        {
            LootAccess = 0,
            FireAtPlayer = 1,
            FireAtZombie = 2,
            FireAtHostileNPC = 3,
            CoverAccess = 4,
            FlankAccess = 5
        }

        [Serializable]
        public sealed class ExperienceRecord
        {
            public int kind;
            public int sectorX;
            public int sectorZ;
            public float positiveEvidence;
            public float negativeEvidence;
            public int directObservations;
            public int heardObservations;
        }

        /// <summary>One first-hand observation, not a full copy of the sender's memory.</summary>
        public struct SpokenLesson
        {
            public ExperienceKind Kind;
            public Vector3 ApproximatePlace;
            public bool WasSuccessful;
            public int Sequence;
        }

        private const float SectorSize = 20f;
        private const int MaximumRecords = 48;
        private const float MaximumEvidence = 12f;
        private readonly List<ExperienceRecord> records = new List<ExperienceRecord>(MaximumRecords);
        private PrototypeNpcVitals vitals;
        private int nextSequence;
        private SpokenLesson pendingSpokenLesson;
        private bool hasUnspokenLesson;

        public int RecordCount => records.Count;
        public bool HasUnspokenLesson => hasUnspokenLesson;

        private void Awake()
        {
            vitals = GetComponent<PrototypeNpcVitals>();
        }

        /// <summary>Records one genuine resolved loot transfer or committed weapon shot.</summary>
        public bool RecordOwnOutcomeServer(ExperienceKind kind, Vector3 place, bool success)
        {
            if (!Provider.isServer || vitals == null || vitals.IsDead
                || !ValidKind(kind) || !ValidPosition(place))
                return false;

            ExperienceRecord entry = GetOrCreateRecord(kind, place);
            if (entry == null)
                return false;
            Adjust(entry, success, 1f, true);

            ++nextSequence;
            if (nextSequence <= 0)
                nextSequence = 1;
            pendingSpokenLesson = new SpokenLesson
            {
                Kind = kind,
                ApproximatePlace = QuantizeToSectorCenter(place),
                WasSuccessful = success,
                Sequence = nextSequence
            };
            hasUnspokenLesson = true;
            return true;
        }

        /// <summary>
        /// Receives a single report only after the group speech service validated the
        /// audible conversation. Heard advice is deliberately weaker than first-hand evidence
        /// and is never automatically relayed, preventing instant knowledge propagation.
        /// </summary>
        public bool ReceiveSpokenLessonServer(SpokenLesson report)
        {
            if (!Provider.isServer || vitals == null || vitals.IsDead
                || !ValidKind(report.Kind) || !ValidPosition(report.ApproximatePlace))
                return false;

            ExperienceRecord entry = GetOrCreateRecord(report.Kind, report.ApproximatePlace);
            if (entry == null)
                return false;
            Adjust(entry, report.WasSuccessful, 0.35f, false);
            return true;
        }

        public bool TryGetUnspokenLessonServer(out SpokenLesson report)
        {
            report = default(SpokenLesson);
            if (!Provider.isServer || vitals == null || vitals.IsDead || !hasUnspokenLesson)
                return false;
            report = pendingSpokenLesson;
            return true;
        }

        public void ConfirmLessonSpokenServer(int sequence)
        {
            if (Provider.isServer && hasUnspokenLesson && pendingSpokenLesson.Sequence == sequence)
                hasUnspokenLesson = false;
        }

        /// <summary>
        /// A failed approach to one area modestly reduces the attractiveness of future
        /// loot there. It never blocks a medical/emergency objective or bypasses path tests.
        /// </summary>
        public float GetLootScoreAdjustment(Vector3 place)
        {
            ExperienceRecord record = FindRecord(ExperienceKind.LootAccess, place);
            if (record == null)
                return 0f;
            // Bayesian prior avoids making one unlucky attempt a permanent blacklist.
            float signedEvidence = record.positiveEvidence - record.negativeEvidence;
            return Mathf.Clamp(signedEvidence * 5f, -24f, 8f);
        }

        /// <summary>
        /// Route experience can influence which *already geometry-validated* cover
        /// waypoint the NPC attempts first. An old failure is a weak preference,
        /// never an assertion that new world construction makes a route impossible.
        /// </summary>
        public float GetCoverAccessAdjustment(Vector3 place)
        {
            ExperienceRecord record = FindRecord(ExperienceKind.CoverAccess, place);
            if (record == null)
                return 0f;
            return Mathf.Clamp((record.positiveEvidence - record.negativeEvidence) * 5f, -24f, 8f);
        }

        /// <summary>Only completed or physically failed flank routes count.
        /// Heard squad lessons have reduced evidence weight. This influences
        /// already verified candidates; it never creates a magic route.</summary>
        public float GetFlankAccessAdjustment(Vector3 place)
        {
            ExperienceRecord record = FindRecord(ExperienceKind.FlankAccess, place);
            if (record == null)
                return 0f;
            return Mathf.Clamp((record.positiveEvidence - record.negativeEvidence) * 1.5f, -5f, 2.5f);
        }

        /// <summary>
        /// An online contextual hit/miss policy: when an individual misses repeatedly,
        /// it conserves finite ammo by pausing longer between shots. Does not improve
        /// accuracy or predict hidden actors; shot validation still belongs to combat.
        /// </summary>
        public float GetShotCadenceMultiplier(ExperienceKind kind, Vector3 place)
        {
            ExperienceRecord record = FindRecord(kind, place);
            if (record == null)
                return 1f;
            float observations = record.positiveEvidence + record.negativeEvidence;
            if (observations < 3.5f)
                return 1f;
            float successEstimate = (record.positiveEvidence + 2f) / (observations + 4f);
            // A modest adjustment with exploratory variability left to the gun controller.
            return Mathf.Clamp(1f + (0.5f - successEstimate) * 0.8f, 0.85f, 1.3f);
        }

        /// <summary>
        /// Save individual experience entries only; never save a group-owned aggregate.
        /// </summary>
        public List<ExperienceRecord> ExportSnapshotServer()
        {
            List<ExperienceRecord> snapshot = new List<ExperienceRecord>();
            if (!Provider.isServer || vitals == null || vitals.IsDead)
                return snapshot;
            for (int i = 0; i < records.Count; ++i)
                snapshot.Add(Clone(records[i]));
            return snapshot;
        }

        /// <summary>Restores the same surviving individual's previously recorded evidence.</summary>
        public void RestoreSnapshotServer(List<ExperienceRecord> snapshot)
        {
            if (!Provider.isServer || vitals == null || vitals.IsDead)
                return;
            records.Clear();
            hasUnspokenLesson = false;
            nextSequence = 0;
            if (snapshot == null)
                return;
            for (int i = 0; i < snapshot.Count && records.Count < MaximumRecords; ++i)
            {
                ExperienceRecord item = snapshot[i];
                if (item == null || !ValidRecord(item) || ContainsKey(item))
                    continue;
                records.Add(Clone(item));
            }
        }

        public void ForgetOnDeathServer()
        {
            if (!Provider.isServer)
                return;
            records.Clear();
            hasUnspokenLesson = false;
        }

        private static void Adjust(ExperienceRecord entry, bool success, float weight, bool direct)
        {
            if (entry.positiveEvidence + entry.negativeEvidence >= MaximumEvidence)
            {
                entry.positiveEvidence *= 0.8f;
                entry.negativeEvidence *= 0.8f;
            }
            if (success)
                entry.positiveEvidence = Mathf.Min(MaximumEvidence, entry.positiveEvidence + weight);
            else
                entry.negativeEvidence = Mathf.Min(MaximumEvidence, entry.negativeEvidence + weight);
            if (direct)
                entry.directObservations = Mathf.Min(10000, entry.directObservations + 1);
            else
                entry.heardObservations = Mathf.Min(10000, entry.heardObservations + 1);
        }

        private ExperienceRecord GetOrCreateRecord(ExperienceKind kind, Vector3 place)
        {
            ExperienceRecord entry = FindRecord(kind, place);
            if (entry != null)
                return entry;
            // Deterministically evict the least-supported entry rather than increasing
            // unbounded with combat events or full-map exploration.
            if (records.Count >= MaximumRecords)
            {
                int weakestIndex = 0;
                float weakestSupport = float.PositiveInfinity;
                for (int i = 0; i < records.Count; ++i)
                {
                    float support = records[i].positiveEvidence + records[i].negativeEvidence;
                    if (support < weakestSupport)
                    {
                        weakestIndex = i;
                        weakestSupport = support;
                    }
                }
                records.RemoveAt(weakestIndex);
            }
            entry = new ExperienceRecord
            {
                kind = (int)kind,
                sectorX = GetSector(place.x),
                sectorZ = GetSector(place.z)
            };
            records.Add(entry);
            return entry;
        }

        private ExperienceRecord FindRecord(ExperienceKind kind, Vector3 place)
        {
            if (!ValidKind(kind) || !ValidPosition(place))
                return null;
            int x = GetSector(place.x);
            int z = GetSector(place.z);
            for (int i = 0; i < records.Count; ++i)
            {
                ExperienceRecord entry = records[i];
                if (entry.kind == (int)kind && entry.sectorX == x && entry.sectorZ == z)
                    return entry;
            }
            return null;
        }

        private bool ContainsKey(ExperienceRecord record)
        {
            for (int i = 0; i < records.Count; ++i)
                if (records[i].kind == record.kind
                    && records[i].sectorX == record.sectorX && records[i].sectorZ == record.sectorZ)
                    return true;
            return false;
        }

        private static ExperienceRecord Clone(ExperienceRecord r)
        {
            return new ExperienceRecord
            {
                kind = r.kind, sectorX = r.sectorX, sectorZ = r.sectorZ,
                positiveEvidence = r.positiveEvidence, negativeEvidence = r.negativeEvidence,
                directObservations = r.directObservations, heardObservations = r.heardObservations
            };
        }

        private static bool ValidRecord(ExperienceRecord r)
        {
            return r.kind >= (int)ExperienceKind.LootAccess
                && r.kind <= (int)ExperienceKind.FlankAccess
                && r.sectorX > -50000 && r.sectorX < 50000
                && r.sectorZ > -50000 && r.sectorZ < 50000
                && !float.IsNaN(r.positiveEvidence) && !float.IsInfinity(r.positiveEvidence)
                && !float.IsNaN(r.negativeEvidence) && !float.IsInfinity(r.negativeEvidence)
                && r.positiveEvidence >= 0f && r.negativeEvidence >= 0f
                && r.positiveEvidence <= MaximumEvidence && r.negativeEvidence <= MaximumEvidence
                && r.directObservations >= 0 && r.heardObservations >= 0;
        }

        private static bool ValidKind(ExperienceKind kind)
        {
            return kind >= ExperienceKind.LootAccess && kind <= ExperienceKind.FlankAccess;
        }

        private static bool ValidPosition(Vector3 place)
        {
            return !float.IsNaN(place.x) && !float.IsInfinity(place.x)
                && !float.IsNaN(place.y) && !float.IsInfinity(place.y)
                && !float.IsNaN(place.z) && !float.IsInfinity(place.z)
                && Mathf.Abs(place.x) < 1000000f && Mathf.Abs(place.z) < 1000000f;
        }

        private static int GetSector(float coordinate)
        {
            return Mathf.FloorToInt(coordinate / SectorSize);
        }

        private static Vector3 QuantizeToSectorCenter(Vector3 place)
        {
            return new Vector3((GetSector(place.x) + 0.5f) * SectorSize,
                0f, (GetSector(place.z) + 0.5f) * SectorSize);
        }
    }
}
