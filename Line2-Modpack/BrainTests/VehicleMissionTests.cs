using System;
using Unturned2.Line2;

internal static class VehicleMissionTests
{
    internal static bool Run()
    {
        if (!Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                true, true, true, 120f, 0.49f) ||
            Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                true, true, true, 120f, 0.50f) ||
            Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                true, true, true, 40f, 0.10f) ||
            Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                true, true, false, 120f, 0.10f))
            return Fail("50pct distant spawn assignment");

        if (!Line2NpcVehicleMissionPolicy.MayPursueVehicle(
                true, true, true, true, true, 80f, false) ||
            Line2NpcVehicleMissionPolicy.MayPursueVehicle(
                true, true, false, true, true, 80f, false) ||
            Line2NpcVehicleMissionPolicy.MayPursueVehicle(
                true, true, true, true, true, 80f, true))
            return Fail("pursuit requires observed car and safe maneuver");

        if (!Line2NpcVehicleMissionPolicy.MayDeployArmoredSupport(
                true, true, true, true, true, true, true) ||
            Line2NpcVehicleMissionPolicy.MayDeployArmoredSupport(
                true, true, true, true, false, true, true))
            return Fail("tank cannot deploy without driver and turret");
        if (Line2NpcVehicleMissionPolicy.MayCreateVehicle(
                true, false, true, true, true))
            return Fail("uncontrollable car spawned");
        Console.WriteLine("NPC_VEHICLE_MISSIONS_OK: 50% distant roll, pursuit, safe heavy adapter");
        return true;
    }

    private static bool Fail(string error)
    {
        Console.Error.WriteLine("NPC_VEHICLE_MISSIONS_FAILED: " + error);
        return false;
    }
}
