using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// NPC-local tactical vehicle intentions; the AI owns decisions and the
    /// independent controller owns reservation/approach. No vehicle or
    /// SteamPlayer is created here. Intent does not authorize a drive.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcVehicleMissionState : MonoBehaviour
    {
        private PrototypeActor _actor;
        private float _nextObserveAt;
        private bool _wantsRoadDeployment;
        private bool _wantsArmoredSupport;
        private readonly Line2NpcVehicleObservationMemory _escape =
            new Line2NpcVehicleObservationMemory();
        private bool _playerVehicleObserved;
        internal bool WantsRoadDeployment { get { return _wantsRoadDeployment; } }
        internal bool WantsArmoredSupport { get { return _wantsArmoredSupport; } }
        internal bool HasObservedVehicleEscape
        {
            get
            {
                uint vehicleId;
                float x, z;
                return _escape.TryRecall(Time.time, out vehicleId,
                    out x, out z);
            }
        }

        internal void Initialize(PrototypeActor actor, bool freshSpawn,
            float distanceFromPlayer)
        {
            _actor = actor;
            bool selected = Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                actor != null && actor.CombatMode == "total",
                actor != null && actor.Disposition == "hostile",
                freshSpawn, distanceFromPlayer, Random.value);
            _wantsRoadDeployment = selected;
            _wantsArmoredSupport =
                Line2NpcVehicleMissionPolicy.RollArmoredSupport(
                    actor != null && actor.CombatMode == "total",
                    actor != null && actor.Disposition == "hostile",
                    freshSpawn, distanceFromPlayer, Random.value);
            if (_wantsArmoredSupport)
            {
                // Heavy vehicle requires independent genuine driver,
                // turret and track/road adapters; absent on Steam today.
                ProbeLogger.Log("NPC_ARMORED_DEPLOYMENT_DEFERRED: npc=" +
                    actor.Identity + " distance=" +
                    distanceFromPlayer.ToString("F0") +
                    "m chance=2pct; native driver/turret unsupported");
                _wantsRoadDeployment = false;
            }
            if (_wantsRoadDeployment)
            {
                // NEVER create a free unmanned vehicle. Current Steam
                // driver path only works with SteamPlayer, so deployment
                // remains deferred until the native adapter passes checks.
                bool maySpawn = Line2NpcVehicleMissionPolicy.MayCreateVehicle(
                    true, false, false, false, false);
                if (!maySpawn)
                    ProbeLogger.Log("NPC_VEHICLE_DEPLOYMENT_DEFERRED: npc=" +
                        actor.Identity +
                        " roll=50pct selected, native driver unavailable");
            }
        }

        private void Update()
        {
            if (_actor == null || _actor.IsDead ||
                _actor.CombatMode != "total" ||
                Time.time < _nextObserveAt)
                return;
            _nextObserveAt = Time.time + 1.5f;
            if (!_actor.PlayerVisible || Player.player == null ||
                Player.player.movement == null)
            {
                _playerVehicleObserved = HasObservedVehicleEscape;
                return;
            }
            // No omniscient car-pursuit signal: the actor must currently
            // identify the actual local player before recording their car.
            InteractableVehicle observed =
                Player.player.movement.getVehicle();
            if (observed == null || observed.isDead ||
                observed.isExploded || observed.isEmpty)
                return;
            bool newEscape = !HasObservedVehicleEscape;
            if (!_escape.Observe(Time.time, observed.instanceID,
                    true, observed.transform.position.x,
                    observed.transform.position.z))
                return;
            _playerVehicleObserved = true;
            if (newEscape)
                ProbeLogger.Log("NPC_VEHICLE_ESCAPE_OBSERVED: npc=" +
                    _actor.Identity + " vehicle=" + observed.instanceID +
                    " (approximate 20m sector, expires after 8s out of sight)");
            // Actual high-speed pursuit requires a validated NPC-owned
            // operating car and a safe road route. This adapter cannot
            // invent either; no direct pursuit or vehicle spawn occurs.
            if (!Line2NpcVehicleMissionPolicy.MayPursueVehicle(
                    true, _actor.Disposition == "hostile", true,
                    false, false,
                    Vector3.Distance(_actor.transform.position,
                        observed.transform.position),
                    _actor.RecentlyDamaged))
                return;
        }
    }
}
