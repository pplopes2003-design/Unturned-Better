using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// EXPLICIT SINGLEPLAYER PHYSICS EXPERIMENT, not the native Steam
    /// SteamPlayer-driven seat/ignition/network system. Applies bounded
    /// WheelCollider forces to a four-wheel parked native CAR only.
    /// Started solely via admin "car drive-test" after a genuine A* approach.
    /// It does not grant motorized combat, multiplayer replication or
    /// Workshop/tank compatibility. Actual fuel is debited server-side.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcCarWheelDriveTest : MonoBehaviour
    {
        private const float TripMeters = 18f;
        private const float MaximumSeconds = 25f;
        private const float MaxSafeSpeed = 9f;
        private readonly RaycastHit[] _obstacles = new RaycastHit[12];
        private readonly RaycastHit[] _groundHits = new RaycastHit[16];
        private readonly WheelCollider[] _wheels = new WheelCollider[4];
        private readonly bool[] _powered = new bool[4];
        private readonly bool[] _front = new bool[4];
        private readonly float[] _initialMotor = new float[4];
        private readonly float[] _initialBrake = new float[4];
        private readonly float[] _initialSteer = new float[4];
        private InteractableVehicle _car;
        private PrototypeActor _driver;
        private CharacterController _character;
        private Vector3 _destination;
        private Vector3 _initialSafePosition;
        private float _endAt;
        private float _nextFuelAt;
        private float _nextLogAt;
        private float _stuckSince;
        private bool _active;

        internal bool IsRunning { get { return _active; } }

        internal bool TryStart(InteractableVehicle car,
            PrototypeActor driver, out string reason)
        {
            reason = "unsupported";
            if (_active || !Authorized() || car == null ||
                driver == null || driver.IsDead ||
                car.asset == null || car.asset.engine != EEngine.CAR ||
                car.isDead || car.isExploded || car.isLocked ||
                car.isDriven || !car.isEmpty || car.fuel < 2 ||
                car.rootRigidbody == null ||
                car.rootRigidbody.isKinematic ||
                car.rootRigidbody.mass < 50f)
                return false;
            Passenger seat = car.GetSeatByIndex(0);
            _character = driver.GetComponent<CharacterController>();
            if (seat == null || seat.seat == null || seat.player != null ||
                _character == null || !_character.enabled)
                return false;
            // Only 4 independently validated WheelColliders, not tanks,
            // crawlers, carboats, custom Workshop suspension, or trains.
            Wheel[] nativeWheels = car.tires;
            if (nativeWheels == null || nativeWheels.Length != 4)
            {
                reason = "only 4-wheel native cars supported";
                return false;
            }
            float frontMost = float.NegativeInfinity;
            float backMost = float.PositiveInfinity;
            int powered = 0;
            for (int i = 0; i < 4; ++i)
            {
                Wheel wheel = nativeWheels[i];
                if (wheel == null || wheel.IsDead || wheel.wheel == null)
                {
                    reason = "missing/unusable native wheel";
                    return false;
                }
                _wheels[i] = wheel.wheel;
                _initialMotor[i] = wheel.wheel.motorTorque;
                _initialBrake[i] = wheel.wheel.brakeTorque;
                _initialSteer[i] = wheel.wheel.steerAngle;
                _powered[i] = wheel.isPowered;
                if (_powered[i]) powered++;
                float z = car.transform.InverseTransformPoint(
                    wheel.wheel.transform.position).z;
                frontMost = Mathf.Max(frontMost, z);
                backMost = Mathf.Min(backMost, z);
            }
            if (!Line2NpcCarWheelTestGate.CanStart(
                    Authorized(), !car.isDead && !car.isExploded,
                    seat.player == null, car.asset.engine == EEngine.CAR,
                    !car.rootRigidbody.isKinematic,
                    nativeWheels.Length, powered, frontMost - backMost,
                    car.rootRigidbody.mass, car.fuel))
            {
                reason = "unsupported four-wheel test topology/mass/authority";
                return false;
            }
            float mid = (frontMost + backMost) * 0.5f;
            for (int i = 0; i < 4; ++i)
                _front[i] = car.transform.InverseTransformPoint(
                    _wheels[i].transform.position).z > mid;
            Vector3 target = car.transform.position +
                car.transform.forward * TripMeters;
            RaycastHit ground;
            if (!Physics.Raycast(target + Vector3.up * 5f,
                    Vector3.down, out ground, 12f,
                    RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore) ||
                ground.normal.y < 0.72f ||
                !Level.checkSafeIncludingClipVolumes(
                    ground.point + Vector3.up * 0.1f))
            {
                reason = "no safe physical test waypoint";
                return false;
            }
            _car = car;
            _driver = driver;
            _initialSafePosition = driver.transform.position;
            _destination = ground.point;
            _endAt = Time.time + MaximumSeconds;
            _nextFuelAt = Time.time + 8f;
            _stuckSince = Time.time;
            _character.enabled = false;
            driver.transform.SetParent(seat.seat, false);
            driver.transform.localPosition = Vector3.zero;
            driver.transform.localRotation = Quaternion.identity;
            _active = true;
            VehicleManager.onEnterVehicleRequested += OnEnterRequested;
            VehicleManager.OnPreDestroyVehicle += OnVehicleDestroyed;
            enabled = true;
            reason = "controlled 18m wheel-physics test begun";
            ProbeLogger.Log("NPC_CAR_WHEEL_TEST_BEGIN: npc=" + driver.Identity +
                " nativeCar=" + car.instanceID +
                " physical wheels=4; no fake SteamPlayer");
            return true;
        }

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                Provider.hasCheats && Level.isLoaded &&
                (Provider.clients == null || Provider.clients.Count <= 1);
        }

        private void OnEnterRequested(Player player,
            InteractableVehicle vehicle, ref bool allow)
        {
            if (_active && vehicle == _car) allow = false;
        }

        private void OnVehicleDestroyed(InteractableVehicle vehicle)
        {
            if (_active && vehicle == _car) Stop("native car destroyed");
        }

        private float ObstacleDistance(bool behind)
        {
            if (_car == null) return 0f;
            Vector3 forward = behind
                ? -_car.transform.forward : _car.transform.forward;
            Vector3 origin = _car.transform.position +
                forward * 2.1f + Vector3.up * 0.95f;
            int count = Physics.SphereCastNonAlloc(
                origin, 0.65f, forward, _obstacles, 11f,
                RayMasks.BLOCK_COLLISION | RayMasks.PLAYER |
                    RayMasks.ENTITY, QueryTriggerInteraction.Ignore);
            if (count >= _obstacles.Length) return 0f;
            float nearest = 11f;
            for (int i = 0; i < count; i++)
            {
                Collider hit = _obstacles[i].collider;
                if (hit == null || hit.transform == _car.transform ||
                    hit.transform.IsChildOf(_car.transform) ||
                    (_driver != null &&
                     (hit.transform == _driver.transform ||
                      hit.transform.IsChildOf(_driver.transform))))
                    continue;
                nearest = Mathf.Min(nearest, _obstacles[i].distance);
            }
            return nearest;
        }

        private void FixedUpdate()
        {
            if (!_active) return;
            if (!Authorized() || _car == null || _driver == null ||
                _driver.IsDead || _car.isDead || _car.isExploded ||
                _car.isDriven || _car.fuel == 0 ||
                _car.rootRigidbody == null ||
                _car.rootRigidbody.isKinematic ||
                Time.time >= _endAt)
            {
                Stop("authority/vehicle invalid or test timed out");
                return;
            }
            Vector3 delta = _destination - _car.transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 2.6f)
            {
                Stop("test waypoint reached");
                return;
            }
            float speed = _car.rootRigidbody.velocity.magnitude;
            if (speed > MaxSafeSpeed + 3f)
            {
                Stop("overspeed safety guard");
                return;
            }
            float heading = Vector3.SignedAngle(
                _car.transform.forward, delta, Vector3.up);
            float frontal = ObstacleDistance(false);
            bool rearClear = ObstacleDistance(true) >= 5f;
            if (speed < 0.5f && frontal < 3.5f)
            {
                if (_stuckSince <= 0f) _stuckSince = Time.time;
            }
            else
                _stuckSince = 0f;
            float stalled = _stuckSince > 0f
                ? Time.time - _stuckSince : 0f;
            Line2NpcVehicleSteeringPolicy.Command drive =
                Line2NpcVehicleSteeringPolicy.Decide(
                    true, heading, distance, speed, frontal,
                    stalled, rearClear);
            if (speed > MaxSafeSpeed)
            {
                drive.Throttle = 0f;
                drive.Brake = true;
            }
            float torque = Mathf.Min(650f,
                Mathf.Max(150f, _car.rootRigidbody.mass * 0.25f));
            float braking = Mathf.Min(4500f,
                Mathf.Max(800f, _car.rootRigidbody.mass * 1.2f));
            for (int i = 0; i < _wheels.Length; ++i)
            {
                WheelCollider wheel = _wheels[i];
                if (wheel == null)
                {
                    Stop("physical wheel disappeared");
                    return;
                }
                wheel.motorTorque = _powered[i]
                    ? drive.Throttle * torque : 0f;
                wheel.brakeTorque = drive.Brake ? braking : 0f;
                wheel.steerAngle = _front[i] ? drive.Steer * 22f : 0f;
            }
            if (Time.time >= _nextFuelAt)
            {
                _nextFuelAt = Time.time + 8f;
                _car.askBurnFuel(1); // Native server fuel, never free.
                VehicleManager.sendVehicleFuel(_car, _car.fuel);
            }
            if (Time.time >= _nextLogAt)
            {
                _nextLogAt = Time.time + 5f;
                ProbeLogger.Log("NPC_CAR_WHEEL_TEST_PROGRESS: npc=" +
                    _driver.Identity + " vehicle=" + _car.instanceID +
                    " speed=" + speed.ToString("F1") +
                    " distance=" + distance.ToString("F1") +
                    " obstacle=" + frontal.ToString("F1"));
            }
        }

        private bool TryFindSafeExit(out Vector3 exit)
        {
            exit = Vector3.zero;
            if (_car == null) return false;
            Vector3 basePoint = _car.transform.position;
            for (int i = 0; i < 8; i++)
            {
                // Try both vehicle sides and front/rear corners, at least
                // 3m from the body, before resorting to test-only recovery.
                float side = (i & 1) == 0 ? -1f : 1f;
                float fore = (i & 2) == 0 ? -1f : 1f;
                float radius = i < 4 ? 3.0f : 4.5f;
                Vector3 sample = basePoint +
                    _car.transform.right * side * radius +
                    _car.transform.forward * fore * 1.3f +
                    Vector3.up * 3.5f;
                int count = Physics.RaycastNonAlloc(sample,
                    Vector3.down, _groundHits, 8f,
                    RayMasks.BLOCK_COLLISION | RayMasks.ENTITY,
                    QueryTriggerInteraction.Ignore);
                if (count == 0 || count >= _groundHits.Length) continue;
                float nearest = float.PositiveInfinity;
                Vector3 candidate = Vector3.zero;
                for (int n = 0; n < count; n++)
                {
                    Collider collider = _groundHits[n].collider;
                    if (collider == null || collider.transform == _car.transform ||
                        collider.transform.IsChildOf(_car.transform) ||
                        _groundHits[n].normal.y < 0.72f ||
                        _groundHits[n].distance >= nearest)
                        continue;
                    nearest = _groundHits[n].distance;
                    candidate = _groundHits[n].point + Vector3.up * 0.08f;
                }
                if (nearest == float.PositiveInfinity ||
                    Mathf.Abs(candidate.y - basePoint.y) > 3f ||
                    !Level.checkSafeIncludingClipVolumes(candidate) ||
                    Physics.CheckCapsule(candidate + Vector3.up * 0.50f,
                        candidate + Vector3.up * 1.65f, 0.33f,
                        RayMasks.BLOCK_COLLISION,
                        QueryTriggerInteraction.Ignore))
                    continue;
                exit = candidate;
                return true;
            }
            return false;
        }

        internal void Stop(string reason)
        {
            if (!_active) return;
            _active = false;
            VehicleManager.onEnterVehicleRequested -= OnEnterRequested;
            VehicleManager.OnPreDestroyVehicle -= OnVehicleDestroyed;
            for (int i = 0; i < _wheels.Length; i++)
            {
                WheelCollider wheel = _wheels[i];
                if (wheel == null) continue;
                // Never leave permanent steering/brake/torque changes
                // on a car after the experiment is stopped or interrupted.
                wheel.motorTorque = _initialMotor[i];
                wheel.steerAngle = _initialSteer[i];
                wheel.brakeTorque = _initialBrake[i];
            }
            // Stop the small controlled 18m test without leaving an
            // unoccupied, coasting vehicle to collide with the dismount.
            if (_car != null && _car.rootRigidbody != null &&
                !_car.rootRigidbody.isKinematic)
            {
                _car.rootRigidbody.velocity = Vector3.zero;
                _car.rootRigidbody.angularVelocity = Vector3.zero;
            }
            if (_driver != null)
            {
                Vector3 exit;
                bool safeExit = TryFindSafeExit(out exit);
                if (!safeExit)
                {
                    // Last-resort TEST-ONLY recovery to the original
                    // already verified A* boarding position. Never spawn
                    // a new occupant inside an unverified collider.
                    exit = _initialSafePosition;
                    ProbeLogger.Log("NPC_CAR_TEST_EXIT_FALLBACK: no safe " +
                        "current dismount; restored original boarding position");
                }
                _driver.transform.SetParent(null, true);
                _driver.transform.position = exit;
                if (_character != null && !_driver.IsDead)
                    _character.enabled = true;
            }
            ProbeLogger.Log("NPC_CAR_WHEEL_TEST_STOP: reason=" + reason);
            _driver = null;
            _car = null;
            _character = null;
            enabled = false;
        }

        private void OnDisable() { Stop("physics test disabled"); }
        private void OnDestroy() { Stop("physics test destroyed"); }
    }
}
