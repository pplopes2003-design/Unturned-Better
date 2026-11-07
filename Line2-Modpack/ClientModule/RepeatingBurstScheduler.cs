using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Repeating 3-round bursts when the trigger remains held.
    /// Pure scheduling state machine: does NOT directly fire bullets, consume
    /// ammo, animate a gun, or register any Unturned input callback. A future
    /// validated, authoritative firearm adapter must do those things.
    /// </summary>
    internal sealed class RepeatingBurstScheduler
    {
        internal const int ShotsPerBurst = 3;
        internal const float MinimumGapSeconds = 0.16f;
        private readonly float _shotIntervalSeconds;
        private readonly float _interBurstGapSeconds;

        private bool _active;
        private int _remainingInBurst;
        private float _nextShotAt;

        internal RepeatingBurstScheduler(float shotIntervalSeconds,
                                         float interBurstGapSeconds)
        {
            if (float.IsNaN(shotIntervalSeconds) ||
                float.IsInfinity(shotIntervalSeconds) ||
                shotIntervalSeconds < 0.02f)
                throw new ArgumentOutOfRangeException("shotIntervalSeconds");
            if (float.IsNaN(interBurstGapSeconds) ||
                float.IsInfinity(interBurstGapSeconds) ||
                interBurstGapSeconds < MinimumGapSeconds)
                throw new ArgumentOutOfRangeException("interBurstGapSeconds");
            _shotIntervalSeconds = shotIntervalSeconds;
            _interBurstGapSeconds = interBurstGapSeconds;
        }

        internal void Reset()
        {
            _active = false;
            _remainingInBurst = 0;
            _nextShotAt = 0f;
        }

        /// <summary>
        /// Return TRUE only when the adapter may attempt one *native*
        /// authoritative shot. The adapter must check ammo, reload, equipped
        /// item, player state and actual Unturned fire cooldown itself.
        /// If the shot fails, Reset to prevent phantom burst progression.
        /// Call once per normal Unity Update, never a tight catch-up loop.
        /// </summary>
        internal bool TrySchedule(float now, bool held, bool canFire)
        {
            if (float.IsNaN(now) || float.IsInfinity(now))
            {
                Reset();
                return false;
            }
            if (!held || !canFire)
            {
                Reset();
                return false;
            }
            if (!_active)
            {
                _active = true;
                _remainingInBurst = ShotsPerBurst;
                _nextShotAt = now;
            }
            if (now < _nextShotAt) return false;

            _remainingInBurst--;
            if (_remainingInBurst == 0)
            {
                _remainingInBurst = ShotsPerBurst;
                _nextShotAt = now + _interBurstGapSeconds;
            }
            else
                _nextShotAt = now + _shotIntervalSeconds;
            return true;
        }
    }
}
