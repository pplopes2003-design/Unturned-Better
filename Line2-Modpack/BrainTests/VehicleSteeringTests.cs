using System;
using Unturned2.Line2;

internal static class VehicleSteeringTests
{
    internal static bool Run()
    {
        var road = Line2NpcVehicleSteeringPolicy.Decide(
            true, 15f, 30f, 2f, 30f, 0f, false);
        if (road.Brake || road.Throttle <= 0f || road.Steer <= 0f)
            return Fail("normal steering and acceleration");
        var invalid = Line2NpcVehicleSteeringPolicy.Decide(
            false, 0f, 30f, 1f, 30f, 0f, false);
        if (!invalid.Brake || invalid.Throttle != 0f)
            return Fail("unsupported or disconnected route");
        var blocked = Line2NpcVehicleSteeringPolicy.Decide(
            true, 0f, 30f, 1f, 2f, 0f, false);
        if (!blocked.Brake || blocked.Throttle != 0f)
            return Fail("front obstruction emergency braking");
        var reverse = Line2NpcVehicleSteeringPolicy.Decide(
            true, 15f, 30f, 0f, 2f, 5f, true);
        if (!reverse.Reverse || reverse.Throttle >= 0f)
            return Fail("controlled reverse recovery");
        var unsafeReverse = Line2NpcVehicleSteeringPolicy.Decide(
            true, 15f, 30f, 0f, 2f, 5f, false);
        if (unsafeReverse.Reverse || !unsafeReverse.Brake)
            return Fail("reverse without rear clearance");
        var bad = Line2NpcVehicleSteeringPolicy.Decide(
            true, float.NaN, 30f, 2f, 30f, 0f, false);
        if (!bad.Brake || bad.Throttle != 0f)
            return Fail("nonfinite telemetry");
        Console.WriteLine("NPC_VEHICLE_STEERING_OK: corners, braking, reversing, invalid paths");
        return true;
    }

    private static bool Fail(string reason)
    {
        Console.Error.WriteLine("NPC_VEHICLE_STEERING_FAILED: " + reason);
        return false;
    }
}
