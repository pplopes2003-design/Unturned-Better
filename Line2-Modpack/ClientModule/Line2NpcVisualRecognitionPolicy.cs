using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Bounded per-NPC recognition accumulator. Physics and target identity
    /// are verified by the caller BEFORE any sample is applied.
    /// Suspicion never grants a combat target, exact coordinates or damage.
    /// This policy intentionally does not guess ambient light from time of day.
    /// A measured illumination factor can be supplied in a later adapter.
    /// </summary>
    internal sealed class Line2NpcVisualRecognitionPolicy
    {
        internal enum Recognition { None, Suspicious, Confirmed }
        private float _evidence;
        private float _lastSampleAt = -1f;
        private float _lastConfirmedAt = -100f;
        internal float Evidence { get { return _evidence; } }

        internal void Reset()
        {
            _evidence = 0f;
            _lastSampleAt = -1f;
            _lastConfirmedAt = -100f;
        }

        internal Recognition Sample(float now, bool lineOfSight,
            float forwardDot, float distance, float observedSpeed,
            float illumination)
        {
            if (!Finite(now) || !Finite(forwardDot) || !Finite(distance) ||
                !Finite(observedSpeed) || !Finite(illumination) || distance < 0f)
                return Recognition.None;
            float dt = _lastSampleAt < 0f ? 0.1f
                : Math.Max(0f, Math.Min(0.5f, now - _lastSampleAt));
            _lastSampleAt = now;
            // Peripheral motion can be noticed to the side, but does not
            // identify a target. Only central, sustained LOS can confirm.
            bool central = forwardDot >= 0.5f && forwardDot <= 1f;
            bool peripheral = forwardDot >= 0.15f && forwardDot < 0.5f &&
                observedSpeed >= 1.25f;
            bool candidate = lineOfSight && (central || peripheral) &&
                distance <= 20f;
            if (!candidate)
            {
                _evidence = Math.Max(0f, _evidence - 1.6f * dt);
                return Recognition.None;
            }

            float angleQuality = Math.Max(0f, Math.Min(1f,
                (forwardDot - 0.5f) / 0.5f));
            float proximity = Math.Max(0f, Math.Min(1f, 1f - distance / 20f));
            float motion = Math.Max(0f, Math.Min(1f, observedSpeed / 5f));
            float light = Math.Max(0.2f, Math.Min(1f, illumination));
            float rate = (0.32f + angleQuality * 0.85f +
                proximity * 0.75f + motion * 0.4f) * light;
            if (peripheral)
            {
                // Seeing movement in the corner of one's eye is a clue
                // only. Never let repeated lateral movement grant exact
                // target identity or attack permission.
                _evidence = Math.Min(0.44f, _evidence +
                    (0.20f + motion * 0.45f) * light * dt);
                return _evidence >= 0.22f
                    ? Recognition.Suspicious : Recognition.None;
            }
            _evidence = Math.Min(1.5f, _evidence + rate * dt);

            // Continue tracking a recently confirmed *visible* actor with
            // hysteresis. Loss of LOS always cancels direct recognition.
            if (_evidence >= 0.88f ||
                (_evidence >= 0.45f && now - _lastConfirmedAt <= 1.2f))
            {
                _lastConfirmedAt = now;
                return Recognition.Confirmed;
            }
            return _evidence >= 0.22f
                ? Recognition.Suspicious : Recognition.None;
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
