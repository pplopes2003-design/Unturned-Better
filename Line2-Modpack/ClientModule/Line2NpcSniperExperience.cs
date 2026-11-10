using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Lifetime, per-actor sniper practice. Distinct from the transient
    /// impact-correction loop. Bounded expertise never grants target
    /// awareness, bullet hits or perfect accuracy; death discards it.
    /// JSON snapshots are restored ONLY for this surviving actor identity.
    /// </summary>
    internal sealed class Line2NpcSniperExperience
    {
        [Serializable]
        internal sealed class Record
        {
            public int weaponId; // Unity JsonUtility-compatible primitive
            public int distanceBand; // 0=short, 1=medium, 2=long
            public int shots;
            public int strikes;
        }

        private readonly Record[] _records = new Record[12];

        internal static int Band(float distance)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) ||
                distance < 0f) return -1;
            return distance < 35f ? 0 : distance < 75f ? 1 : 2;
        }

        internal void Observe(ushort weapon, float distance, bool observedShot,
            bool confirmedImpactOnTarget)
        {
            int band = Band(distance);
            if (weapon == 0 || band < 0 || distance > 120f ||
                !observedShot) return;
            Record record = null;
            for (int i = 0; i < _records.Length; ++i)
                if (_records[i] != null && _records[i].weaponId == weapon &&
                    _records[i].distanceBand == band)
                { record = _records[i]; break; }
            if (record == null)
            {
                for (int i = 0; i < _records.Length; ++i)
                    if (_records[i] == null)
                    { record = new Record { weaponId = weapon,
                        distanceBand = band }; _records[i] = record; break; }
            }
            if (record == null) return; // Memory has a finite footprint.
            record.shots = Math.Min(400, record.shots + 1);
            if (confirmedImpactOnTarget)
                record.strikes = Math.Min(record.shots, record.strikes + 1);
        }

        internal float SpreadMultiplier(ushort weapon, float distance)
        {
            int band = Band(distance);
            if (weapon == 0 || band < 0) return 1f;
            for (int i = 0; i < _records.Length; ++i)
            {
                Record r = _records[i];
                if (r == null || r.weaponId != weapon ||
                    r.distanceBand != band) continue;
                // Practice improves control by no more than 15%. Actual
                // accuracy still includes native ballistics, randomized
                // spread, suppression, injury and movement penalties.
                float practice = Math.Min(1f, r.shots / 80f);
                float feedback = (r.strikes + 2f) / (r.shots + 5f);
                return 1f - 0.15f * practice *
                    (0.65f + 0.35f * feedback);
            }
            return 1f;
        }

        internal Record[] Export()
        {
            int count = 0;
            foreach (Record r in _records) if (r != null) count++;
            Record[] data = new Record[count];
            int at = 0;
            foreach (Record r in _records)
                if (r != null)
                    data[at++] = new Record { weaponId = r.weaponId,
                        distanceBand = r.distanceBand, shots = r.shots,
                        strikes = r.strikes };
            return data;
        }

        internal bool Restore(Record[] snapshot)
        {
            if (snapshot != null && snapshot.Length > 12) return false;
            if (snapshot != null)
            {
                foreach (Record r in snapshot)
                    if (r == null || r.weaponId < 1 ||
                        r.weaponId > ushort.MaxValue ||
                        r.distanceBand < 0 || r.distanceBand > 2 ||
                        r.shots < 0 || r.shots > 400 ||
                        r.strikes < 0 || r.strikes > r.shots)
                        return false;
            }
            Array.Clear(_records, 0, _records.Length);
            if (snapshot == null) return true;
            for (int i = 0; i < snapshot.Length; ++i)
            {
                foreach (Record previous in _records)
                    if (previous != null &&
                        previous.weaponId == snapshot[i].weaponId &&
                        previous.distanceBand == snapshot[i].distanceBand)
                        return false;
                Record r = snapshot[i];
                _records[i] = new Record { weaponId = r.weaponId,
                    distanceBand = r.distanceBand, shots = r.shots,
                    strikes = r.strikes };
            }
            return true;
        }
    }
}
