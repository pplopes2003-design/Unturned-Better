using SDG.Unturned;

namespace Unturned2.Line2
{
    /// <summary>
    /// Read-only inspection of actual loaded Steam/Workshop vehicle assets.
    /// Weaponized != tank: tracks, armor, turret ownership and native
    /// locomotion must each be validated before heavy deployment.
    /// </summary>
    internal static class Line2NpcVehicleCapabilityAudit
    {
        internal static string Describe(InteractableVehicle vehicle)
        {
            if (vehicle == null || vehicle.asset == null)
                return "no native vehicle";
            Passenger driver = vehicle.GetSeatByIndex(0);
            bool hasRealDriverSeat = driver != null && driver.seat != null;
            bool hasNativePhysicsProfile =
                vehicle.asset.physicsProfileRef.Find() != null;
            int equippedTurrets = 0;
            if (vehicle.passengers != null)
                foreach (Passenger passenger in vehicle.passengers)
                    if (passenger != null && passenger.turret != null &&
                        passenger.turretAim != null)
                        equippedTurrets++;
            return "vehicle=" + vehicle.instanceID +
                " asset=" + vehicle.asset.id +
                " engine=" + vehicle.asset.engine +
                " realSeat=" + hasRealDriverSeat +
                " nativePhysics=" + hasNativePhysicsProfile +
                " wheels=" + (vehicle.tires != null ? vehicle.tires.Length : 0) +
                " armedSeats=" + equippedTurrets +
                " locked=" + vehicle.isLocked +
                " occupied=" + vehicle.isDriven +
                " fuel=" + vehicle.fuel +
                " NPC-driver-supported=False";
        }
    }
}
