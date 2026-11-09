using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Read-only discovery of existing Steam vehicles. This deliberately
    /// never calls the Player-only seat API or changes Rigidbody state.
    /// A future NPC-specific occupancy + native simulation adapter must
    /// be implemented before actors can actually drive.
    /// </summary>
    internal sealed class Line2NpcVehicleDiscovery : MonoBehaviour
    {
        private readonly List<InteractableVehicle> _nearby =
            new List<InteractableVehicle>(32);

        internal bool TryFindParkedCar(Vector3 origin,
            out InteractableVehicle result, out Vector3 approach)
        {
            result = null;
            approach = Vector3.zero;
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || VehicleManager.vehicles == null)
                return false;
            _nearby.Clear();
            VehicleManager.getVehiclesInRadius(origin, 55f * 55f, _nearby);
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < _nearby.Count; ++i)
            {
                InteractableVehicle vehicle = _nearby[i];
                if (vehicle == null || vehicle.asset == null ||
                    vehicle.asset.engine != EEngine.CAR ||
                    vehicle.isDead || vehicle.isExploded ||
                    vehicle.isLocked || vehicle.isDriven ||
                    vehicle.fuel == 0)
                    continue;
                Passenger seat = vehicle.GetSeatByIndex(0);
                if (seat == null || seat.seat == null ||
                    seat.player != null) continue;
                // The real seat can lie inside the chassis. Return only
                // a rough exterior candidate, NOT a guaranteed route.
                Vector3 point = seat.seat.position -
                    vehicle.transform.right * 1.7f;
                float sq = (point - origin).sqrMagnitude;
                if (sq >= bestDistance) continue;
                bestDistance = sq;
                result = vehicle;
                approach = point;
            }
            _nearby.Clear();
            return result != null;
        }

        private void OnDisable()
        {
            _nearby.Clear();
        }
    }
}
