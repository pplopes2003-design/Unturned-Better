using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Pure advisory policy for a FUTURE zombie adapter. Does not move Zombies,
    /// mutate zombie regions, set native alert targets or override native AI.
    /// Native sight/chase/attack must always preempt this optional routine.
    /// </summary>
    internal sealed class ZombieNoiseSearchPolicy
    {
        internal enum Phase { Inactive, Search, Alert, ReturnHome }

        internal Phase CurrentPhase { get; private set; }
        internal Vector3 SoundCenter { get; private set; }
        internal Vector3 Home { get; private set; }
        internal float PhaseEndsAt { get; private set; }

        private int nextSample;
        private float nextPointAt;
        private readonly int seed;

        internal ZombieNoiseSearchPolicy(int stableSeed)
        {
            seed = stableSeed;
            CurrentPhase = Phase.Inactive;
        }

        internal void HearSound(Vector3 source, Vector3 home, float now)
        {
            if (!Valid(source) || !Valid(home) || !ValidTime(now))
                return;
            SoundCenter = source;
            Home = home;
            nextSample = 0;
            nextPointAt = now;
            CurrentPhase = Phase.Search;
            PhaseEndsAt = now + 30f + Unit(seed, 0) * 15f;
        }

        internal Phase Advance(float now, bool nativeTargetOrAlertActive)
        {
            if (!ValidTime(now))
                return CurrentPhase;
            if (nativeTargetOrAlertActive)
            {
                // Do not replace or fight native sight/chase/combat.
                Cancel();
                return CurrentPhase;
            }
            if (CurrentPhase == Phase.Search && now >= PhaseEndsAt)
            {
                CurrentPhase = Phase.Alert;
                PhaseEndsAt = now + 30f;
            }
            else if (CurrentPhase == Phase.Alert && now >= PhaseEndsAt)
            {
                CurrentPhase = Phase.ReturnHome;
                PhaseEndsAt = float.PositiveInfinity;
            }
            return CurrentPhase;
        }

        internal bool TryGetSearchPoint(float now, out Vector3 point)
        {
            point = SoundCenter;
            if (CurrentPhase != Phase.Search || !ValidTime(now)
                || now < nextPointAt)
                return false;

            // Deterministic samples within a 15-metre disk. The real adapter
            // MUST project points onto native navigable geometry and reject
            // inaccessible floors, walls and cross-region teleport paths.
            int sample = ++nextSample;
            float angle = Unit(seed, sample * 2) * Mathf.PI * 2f;
            float radius = 15f * Mathf.Sqrt(Unit(seed, sample * 2 + 1));
            point = SoundCenter + new Vector3(Mathf.Cos(angle) * radius,
                0f, Mathf.Sin(angle) * radius);
            nextPointAt = now + 2.5f + Unit(seed, sample + 1000) * 2.5f;
            return true;
        }

        internal void CompleteReturn()
        {
            if (CurrentPhase == Phase.ReturnHome)
                Cancel();
        }

        internal void Cancel()
        {
            CurrentPhase = Phase.Inactive;
            PhaseEndsAt = 0f;
        }

        private static bool Valid(Vector3 p)
        {
            return !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)
                && !float.IsInfinity(p.x) && !float.IsInfinity(p.y)
                && !float.IsInfinity(p.z);
        }

        private static bool ValidTime(float time)
        {
            return !float.IsNaN(time) && !float.IsInfinity(time);
        }

        private static float Unit(int value, int salt)
        {
            unchecked
            {
                uint x = (uint)value ^ ((uint)salt * 747796405u) ^ 2891336453u;
                x ^= x >> 16;
                x *= 2246822519u;
                x ^= x >> 13;
                x *= 3266489917u;
                x ^= x >> 16;
                return (x & 0x00ffffffu) / 16777216f;
            }
        }
    }
}
