using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Last first-hand observed player vehicle sector. A driver may pursue
    /// only a real, currently observed escape. The short-lived memory is
    /// suitable for searching, NEVER a continuous target position feed.
    /// </summary>
    internal sealed class Line2NpcVehicleObservationMemory
    {
        private const float LifetimeSeconds = 8f;
        private uint _vehicle;
        private float _lastSeen = -10000f;
        private float _sectorX, _sectorZ;

        internal bool Observe(float now, uint vehicleId,
            bool visuallyConfirmed, float worldX, float worldZ)
        {
            if (vehicleId == 0 || !visuallyConfirmed ||
                !Finite(now) || !Finite(worldX) || !Finite(worldZ))
                return false;
            _vehicle = vehicleId;
            _lastSeen = now;
            // Deliberate 20m uncertainty: avoid tracking the car by a
            // perfect position after LOS is broken.
            _sectorX = (float)Math.Floor(worldX / 20f) * 20f + 10f;
            _sectorZ = (float)Math.Floor(worldZ / 20f) * 20f + 10f;
            return true;
        }

        internal bool TryRecall(float now, out uint vehicleId,
            out float sectorX, out float sectorZ)
        {
            vehicleId = 0;
            sectorX = sectorZ = 0f;
            if (!Finite(now) || now < _lastSeen ||
                now - _lastSeen > LifetimeSeconds || _vehicle == 0)
                return false;
            vehicleId = _vehicle;
            sectorX = _sectorX;
            sectorZ = _sectorZ;
            return true;
        }

        internal void Forget()
        {
            _vehicle = 0;
            _lastSeen = -10000f;
            _sectorX = _sectorZ = 0f;
        }

        private static bool Finite(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }
    }
}
