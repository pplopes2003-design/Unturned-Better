using System;
using Unturned2.Line2;

internal static class VehicleObservationTests
{
    internal static bool Run()
    {
        var memory = new Line2NpcVehicleObservationMemory();
        uint id; float x, z;
        if (memory.Observe(1f, 44u, false, 13f, 26f) ||
            memory.TryRecall(1f, out id, out x, out z))
            return Fail("invisible car detected");
        if (!memory.Observe(2f, 44u, true, 13f, 26f) ||
            !memory.TryRecall(3f, out id, out x, out z) ||
            id != 44u || x != 10f || z != 30f)
            return Fail("coarse observed sector");
        if (memory.TryRecall(10.01f, out id, out x, out z))
            return Fail("car pursuit memory stayed active past 8 seconds");
        memory.Observe(12f, 45u, true, 66f, -36f);
        if (!memory.TryRecall(13f, out id, out x, out z) ||
            id != 45u || x != 70f || z != -30f)
            return Fail("updated vehicle location became precise or stale");
        memory.Forget();
        if (memory.TryRecall(13f, out id, out x, out z))
            return Fail("forgotten car still trackable");
        Console.WriteLine("NPC_VEHICLE_SIGHT_MEMORY_OK: firsthand, 20m sector, 8s expiry");
        return true;
    }

    private static bool Fail(string reason)
    {
        Console.Error.WriteLine("NPC_VEHICLE_SIGHT_MEMORY_FAILED: " + reason);
        return false;
    }
}
