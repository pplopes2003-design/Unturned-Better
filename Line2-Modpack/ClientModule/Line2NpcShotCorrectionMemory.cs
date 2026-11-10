using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Bounded correction learned from the FIRST ACTUAL PHYSICS IMPACT
    /// of a committed rifle bullet near a currently visible target.
    /// It cannot learn from imagined misses, hidden targets, bullet
    /// trajectories not simulated by the engine, or unrelated weapons.
    /// Errors are world-space lateral/vertical offsets; range is measured
    /// from the shooter's true muzzle origin to the target.
    /// </summary>
    internal sealed class Line2NpcShotCorrectionMemory
    {
        private const float MaximumCorrectionMeters = 1.25f;
        private const float MaximumFeedbackError = 6f;
        private const float Expiry = 35f;
        private float _vertical;
        private float _lateral;
        private float _at = -1000f;
        private uint _gun;
        private int _target;
        private int _samples;

        internal float Vertical { get { return _vertical; } }
        internal float Lateral { get { return _lateral; } }
        internal int Samples { get { return _samples; } }

        internal void Reset()
        {
            _vertical = 0f;
            _lateral = 0f;
            _at = -1000f;
            _gun = 0;
            _target = 0;
            _samples = 0;
        }

        internal bool CanApply(float now, uint gun, int target)
        {
            return Valid(now) && gun != 0 && target != 0 &&
                _gun == gun && _target == target &&
                now >= _at && now - _at <= Expiry && _samples > 0;
        }

        internal bool Observe(float now, uint gun, int target,
            bool firsthandSight, bool actuallyCommitted,
            bool trustworthyImpact, bool struckTarget,
            float lateralMiss, float verticalMiss, float impactDistance)
        {
            if (!Valid(now) || gun == 0 || target == 0 ||
                !firsthandSight || !actuallyCommitted || !trustworthyImpact ||
                !Valid(lateralMiss) || !Valid(verticalMiss) ||
                !Valid(impactDistance) || impactDistance > MaximumFeedbackError ||
                Math.Abs(lateralMiss) > MaximumFeedbackError ||
                Math.Abs(verticalMiss) > MaximumFeedbackError)
                return false;
            if (_gun != gun || _target != target || now < _at ||
                now - _at > Expiry)
            {
                Reset();
                _gun = gun;
                _target = target;
            }
            _at = now;
            // Actual strikes confirm adequate calibration rather than
            // allowing infinite aim improvement.
            if (struckTarget)
            {
                _vertical *= 0.75f;
                _lateral *= 0.75f;
            }
            else
            {
                _vertical = Clamp(_vertical - verticalMiss * 0.25f,
                    -MaximumCorrectionMeters, MaximumCorrectionMeters);
                _lateral = Clamp(_lateral - lateralMiss * 0.25f,
                    -MaximumCorrectionMeters, MaximumCorrectionMeters);
            }
            _samples = Math.Min(10, _samples + 1);
            return true;
        }

        private static float Clamp(float n, float min, float max)
        {
            return Math.Max(min, Math.Min(max, n));
        }

        private static bool Valid(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
