using System;
using Unturned2.Line2;

internal static class VehicleReservationTests
{
    internal static bool Run()
    {
        var ledger = new Line2NpcVehicleReservation();
        if (ledger.TryClaim(0, 7, 1f, 30f) ||
            ledger.TryClaim(123, 0, 1f, 30f) ||
            ledger.TryClaim(123, 7, float.NaN, 30f) ||
            ledger.TryClaim(123, 7, 1f, 70f))
            return Fail("invalid reservation accepted");
        if (!ledger.TryClaim(123, 7, 1f, 30f) ||
            ledger.TryClaim(123, 9, 2f, 30f) ||
            !ledger.Owns(123, 7, 3f))
            return Fail("multiple NPCs acquired one car");
        ledger.Release(123, 9);
        if (!ledger.Owns(123, 7, 3f))
            return Fail("nonowner cleared reservation");
        if (!ledger.TryClaim(123, 9, 32f, 20f) ||
            ledger.Owns(123, 7, 32f) ||
            !ledger.Owns(123, 9, 33f))
            return Fail("expired reservation not reclaimed");
        ledger.ReleaseAll(9);
        if (ledger.Owns(123, 9, 33f))
            return Fail("despawn did not release claim");
        Console.WriteLine("NPC_VEHICLE_RESERVATION_OK: exclusivity, timeout, release");
        return true;
    }

    private static bool Fail(string why)
    {
        Console.Error.WriteLine("NPC_VEHICLE_RESERVATION_FAILED: " + why);
        return false;
    }
}
