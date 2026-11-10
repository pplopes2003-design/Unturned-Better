using System;
using Unturned2.Line2;

internal static class VehicleMissionTests
{
    internal static bool Run()
    {
        // 200 yards = 182.88 m; the selected game threshold rounds UP
        // to 200 m, independent of random chance.
        if (Line2NpcVehicleMissionPolicy.DistantDeploymentMeters != 200f ||
            !Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                true, true, true, 200f, 0.49f) ||
            Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                true, true, true, 199.9f, 0.10f) ||
            Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                true, true, true, 250f, 0.50f) ||
            Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                true, true, false, 250f, 0.10f) ||
            Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                false, true, true, 250f, 0.10f))
            return Fail("50pct roll beyond 200m only");

        if (!Line2NpcVehicleMissionPolicy.MayPursueVehicle(
                true, true, true, true, true, 80f, false) ||
            Line2NpcVehicleMissionPolicy.MayPursueVehicle(
                true, true, false, true, true, 80f, false) ||
            Line2NpcVehicleMissionPolicy.MayPursueVehicle(
                true, true, true, true, true, 80f, true))
            return Fail("pursuit requires observed car and safe maneuver");

        if (!Line2NpcVehicleMissionPolicy.RollArmoredSupport(
                true, true, true, 351f, 0.019f) ||
            Line2NpcVehicleMissionPolicy.RollArmoredSupport(
                true, true, true, 349f, 0.01f) ||
            Line2NpcVehicleMissionPolicy.RollArmoredSupport(
                true, true, true, 400f, 0.02f) ||
            Line2NpcVehicleMissionPolicy.RollArmoredSupport(
                false, true, true, 400f, 0.01f))
            return Fail("rare 2pct distant armored roll");
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
