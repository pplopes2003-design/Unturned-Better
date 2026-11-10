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
        private bool _playerVehicleObserved;
        internal bool WantsRoadDeployment { get { return _wantsRoadDeployment; } }
        internal bool HasObservedVehicleEscape { get { return _playerVehicleObserved; } }

        internal void Initialize(PrototypeActor actor, bool freshSpawn,
            float distanceFromPlayer)
        {
            _actor = actor;
            bool selected = Line2NpcVehicleMissionPolicy.RollDistantDeployment(
                actor != null && actor.CombatMode == "total",
                actor != null && actor.Disposition == "hostile",
                freshSpawn, distanceFromPlayer, Random.value);
            _wantsRoadDeployment = selected;
            if (selected)
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
                return;
            // No omniscient car-pursuit signal: the actor must currently
            // identify the actual local player before recording their car.
            InteractableVehicle observed =
                Player.player.movement.getVehicle();
            if (observed == null || observed.isDead ||
                observed.isExploded || observed.isEmpty)
                return;
            if (!_playerVehicleObserved)
            {
                _playerVehicleObserved = true;
                ProbeLogger.Log("NPC_VEHICLE_ESCAPE_OBSERVED: npc=" +
                    _actor.Identity + " vehicle=" + observed.instanceID +
                    " (pursuit intent recorded; native driving unavailable)");
            }
        }
    }
}
