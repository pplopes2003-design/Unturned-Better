using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Tactical intentions only, independent of car physics and NPC AI.
    /// A 50% roll belongs only to eligible distant Combat Total spawns.
    /// Car pursuit must rely on directly observed driver/vehicle state.
    /// Heavy vehicles require a separate verified seat, turret and engine
    /// adapter; never infer tank capability from appearance or an asset ID.
    /// </summary>
    internal static class Line2NpcVehicleMissionPolicy
    {
        internal enum Mission { OnFoot, RoadDeployment, VehiclePursuit, ArmoredSupport }
        // 200 yards = 182.88 m. Round up to an intuitive 200m threshold.
        internal const float DistantDeploymentMeters = 200f;

        internal static bool RollDistantDeployment(bool combatTotal,
            bool hostile, bool newlySpawned, float playerDistance,
            float randomRoll)
        {
            return combatTotal && hostile && newlySpawned &&
                Valid(playerDistance) && playerDistance >= DistantDeploymentMeters &&
                Valid(randomRoll) && randomRoll >= 0f && randomRoll < 0.5f;
        }

        internal static bool MayPursueVehicle(bool combatTotal,
            bool hostile, bool verifiedPlayerVehicle,
            bool ownsRoadVehicle, bool hasSafeRoute,
            float separation, bool underFire)
        {
            return combatTotal && hostile && verifiedPlayerVehicle &&
                ownsRoadVehicle && hasSafeRoute && !underFire &&
                Valid(separation) && separation >= 25f &&
                separation <= 180f;
        }

        internal static bool MayDeployArmoredSupport(bool combatTotal,
            bool hostile, bool rareSpawnApproved,
            bool heavyEngineProfileSupported,
            bool validatedDriver, bool validatedTurret,
            bool traversableRoute)
        {
            return combatTotal && hostile && rareSpawnApproved &&
                heavyEngineProfileSupported && validatedDriver &&
                validatedTurret && traversableRoute;
        }

        internal static bool MayCreateVehicle(bool selectedByPolicy,
            bool supportedNativeDriver, bool routeVerified,
            bool safeGround, bool capacityAvailable)
        {
            // Fail closed: avoid spawning empty free vehicles, duplicate
            // assets or an uncontrollable tank during combat waves.
            return selectedByPolicy && supportedNativeDriver &&
                routeVerified && safeGround && capacityAvailable;
        }

        private static bool Valid(float number)
        {
            return !float.IsNaN(number) &&
                !float.IsInfinity(number) && number >= 0f;
        }
    }
}
