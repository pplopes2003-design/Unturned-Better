using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Strict eligibility for the opt-in offline 18m wheel-physics trial.
    /// Not production vehicle authority. Tanks/tracks and custom topology
    /// are not permitted until tested separately against Steam physics.
    /// </summary>
    internal static class Line2NpcCarWheelTestGate
    {
        internal static bool CanStart(bool localServer, bool vehicleHealthy,
            bool driverSeatFree, bool engineIsCar, bool dynamicRigidbody,
            int wheelCount, int poweredWheelCount,
            float axleSeparation, float massKg, int fuel)
        {
            return localServer && vehicleHealthy && driverSeatFree &&
                engineIsCar && dynamicRigidbody && wheelCount == 4 &&
                poweredWheelCount >= 2 && poweredWheelCount <= 4 &&
                Valid(axleSeparation) && axleSeparation >= 0.7f &&
                Valid(massKg) && massKg >= 50f && massKg <= 6500f &&
                fuel >= 2;
        }

        private static bool Valid(float x)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x);
        }
    }
}
