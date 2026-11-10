using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Independent NPC-owned approach/reservation adapter for native cars.
    /// All locomotion decisions belong to PrototypeActor/A*; the native
    /// seat and engine remain untouched until an actual supported nonplayer
    /// seat/physics/network adapter exists. A parked car is NOT "drivable"
    /// merely because an NPC reached its door.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcVehicleController : MonoBehaviour
    {
        internal enum State { Idle, Approaching, AtDoor, Invalid }
        private static readonly Line2NpcVehicleReservation Reservations =
            new Line2NpcVehicleReservation();
        private Line2NpcVehicleDiscovery _discovery;
        private InteractableVehicle _vehicle;
        private Vector3 _approach;
        private float _expiresAt;
        private int _owner;
        private uint _id;
        private State _state;

        internal State CurrentState { get { return _state; } }
        internal Vector3 ApproachDestination { get { return _approach; } }
        internal uint VehicleInstanceId { get { return _id; } }

        private void Awake()
        {
            _owner = GetInstanceID();
            _discovery = GetComponent<Line2NpcVehicleDiscovery>();
            if (_discovery == null)
                _discovery = gameObject.AddComponent<Line2NpcVehicleDiscovery>();
        }

        internal bool TryBeginApproach(Vector3 npcPosition)
        {
            if (_state != State.Idle || !Authorized() ||
                _discovery == null) return false;
            InteractableVehicle car;
            Vector3 door;
            if (!_discovery.TryFindParkedCar(npcPosition, out car, out door) ||
                !Valid(car)) return false;
            uint instance = car.instanceID;
            if (!Reservations.TryClaim(instance, _owner, Time.time, 50f))
                return false;
            _vehicle = car;
            _id = instance;
            _approach = door;
            _expiresAt = Time.time + 50f;
            _state = State.Approaching;
            ProbeLogger.Log("NPC_VEHICLE_APPROACH_RESERVED: npc=" + name +
                " vehicle=" + _id + " door=" + door.ToString("F1"));
            return true;
        }

        internal void ReportMovement(Vector3 npcPosition, bool nativeRouteValid)
        {
            if (_state == State.AtDoor)
            {
                if (Time.time >= _expiresAt ||
                    !Authorized() || !Valid(_vehicle))
                    Cancel("at-door reservation timed out or car vanished");
                return;
            }
            if (_state != State.Approaching) return;
            if (!Authorized() || !Valid(_vehicle) ||
                !Reservations.Owns(_id, _owner, Time.time) ||
                Time.time >= _expiresAt || !nativeRouteValid)
            {
                Cancel("vehicle invalid, unowned, expired or no native route");
                return;
            }
            if ((npcPosition - _approach).sqrMagnitude > 2.25f)
                return;
            _state = State.AtDoor;
            // A missing native driver adapter should not hold a seat
            // reservation for the original 50-second approach budget.
            _expiresAt = Mathf.Min(_expiresAt, Time.time + 4f);
            // IMPORTANT: no fake SteamPlayer can be inserted in Passenger.
            // No isEngineOn mutation, Rigidbody forces, seat teleport or
            // simulated driving commands are issued. The next adapter must
            // expose a native, authoritative NPC occupant before enabling it.
            ProbeLogger.Log("NPC_VEHICLE_AT_DOOR: npc=" + name +
                " vehicle=" + _id +
                " native NPC driver seat adapter NOT AVAILABLE");
        }

        internal void Cancel(string reason)
        {
            if (_id != 0) Reservations.Release(_id, _owner);
            if (_state != State.Idle)
                ProbeLogger.Log("NPC_VEHICLE_RELEASE: npc=" + name +
                    " vehicle=" + _id + " reason=" + reason);
            _id = 0;
            _vehicle = null;
            _approach = Vector3.zero;
            _expiresAt = 0f;
            _state = State.Idle;
        }

        private static bool Valid(InteractableVehicle vehicle)
        {
            if (vehicle == null || vehicle.asset == null ||
                vehicle.asset.engine != EEngine.CAR ||
                vehicle.isDead || vehicle.isExploded ||
                vehicle.isLocked || vehicle.isDriven || vehicle.fuel == 0)
                return false;
            Passenger driver = vehicle.GetSeatByIndex(0);
            return driver != null && driver.seat != null &&
                driver.player == null && vehicle.rootRigidbody != null;
        }

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                Level.isLoaded &&
                (Provider.clients == null || Provider.clients.Count <= 1);
        }

        private void OnDisable()
        {
            Cancel("NPC disabled/despawned");
        }

        private void OnDestroy()
        {
            Cancel("NPC destroyed");
        }
    }
}
