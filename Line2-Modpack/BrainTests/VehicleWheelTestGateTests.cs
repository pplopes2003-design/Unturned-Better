using System;
using Unturned2.Line2;

internal static class VehicleWheelTestGateTests
{
    internal static bool Run()
    {
        bool Safe(bool server = true, bool healthy = true, bool seat = true,
            bool car = true, bool dynamicBody = true, int wheels = 4,
            int powered = 2, float axles = 2f, float mass = 1400f, int fuel = 25)
        {
            return Line2NpcCarWheelTestGate.CanStart(server, healthy,
                seat, car, dynamicBody, wheels, powered, axles, mass, fuel);
        }
        if (!Safe() || Safe(server:false) || Safe(healthy:false) ||
            Safe(seat:false) || Safe(car:false) ||
            Safe(dynamicBody:false) || Safe(wheels:6) ||
            Safe(wheels:0) || Safe(powered:1) || Safe(axles:0.3f) ||
            Safe(mass:9000f) || Safe(fuel:0) ||
            Safe(axles:float.NaN))
            return Fail("unsupported/unowned car allowed to drive");
        Console.WriteLine("NPC_CAR_WHEEL_TEST_GATE_OK: controlled local four-wheel physics only");
        return true;
    }

    private static bool Fail(string message)
    {
        Console.Error.WriteLine("NPC_CAR_WHEEL_TEST_GATE_FAILED: " + message);
        return false;
    }
}
