////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Limited server-side door interaction for the mobile humanoid motor.
    /// Only a physically blocking, nearby, UNLOCKED native barricade door may
    /// be opened. This never fakes a Player, ignores an owner lock, forces
    /// physics colliders off, or grants teleportation through a doorway.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCDoorNavigation : MonoBehaviour
    {
        private readonly RaycastHit[] blockingHits = new RaycastHit[16];
        private float nextProbeTime;
        private float nextActualOpenAttemptTime;
        private float waitingForDoorAnimationUntil;
        private InteractableDoor lastOpenedDoor;
        private MobileNPCHumanoidVitals vitals;

        private void Awake()
        {
            vitals = GetComponent<MobileNPCHumanoidVitals>();
        }

        /// <summary>
        /// Returns true when an actual door was opened (or is temporarily
        /// animating). The motor may wait for collision to clear rather than
        /// prematurely deleting a valid planned corridor through that door.
        /// Returns false on locked doors, unrelated objects or missing regions,
        /// so ordinary A*/obstacle avoidance remains in force.
        /// </summary>
        public bool TryHandleBlockingDoorServer(Vector3 origin, Vector3 direction,
            float probeRadius, float probeDistance, int collisionMask)
        {
            if (!Provider.isServer || !Level.isLoaded || vitals == null || vitals.IsDead
                || Time.time < nextProbeTime || !IsFinite(origin)
                || !IsFinite(direction) || direction.sqrMagnitude < 0.2f
                || probeDistance <= 0.1f || probeDistance > 4f)
                return false;

            nextProbeTime = Time.time + 0.30f;
            direction.Normalize();
            // A saturated hit buffer could hide a closer wall. Fail closed.
            int count = Physics.SphereCastNonAlloc(origin, Mathf.Clamp(probeRadius, 0.05f, 0.35f),
                direction, blockingHits, probeDistance, collisionMask,
                QueryTriggerInteraction.Ignore);
            if (count <= 0 || count >= blockingHits.Length)
                return false;

            Collider nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                Collider hit = blockingHits[i].collider;
                if (hit == null || hit.transform == transform
                    || hit.transform.IsChildOf(transform))
                    continue;
                if (blockingHits[i].distance < nearestDistance)
                {
                    nearestDistance = blockingHits[i].distance;
                    nearest = hit;
                }
            }
            if (nearest == null || nearestDistance > 2.4f)
                return false;

            // A wall closer than the door is not a door the NPC can reach.
            InteractableDoor door = nearest.GetComponentInParent<InteractableDoor>();
            if (door == null || !door.isActiveAndEnabled || door.IsChildOfVehicle
                || door.IsLockedForMobileNPCNavigation
                || (door.transform.position - transform.position).sqrMagnitude > 9f
                || !Level.checkSafeIncludingClipVolumes(door.transform.position))
                return false;

            if (door.isOpen)
            {
                // Keep a planned route while animated colliders settle, but
                // don't freeze forever if a stale collider remains obstructing.
                return door == lastOpenedDoor && Time.time < waitingForDoorAnimationUntil;
            }

            // The native animated door may still be within its 0.75s toggle
            // debounce. Briefly wait rather than repeatedly deleting the path.
            if (!door.isOpenable)
                return true;
            if (Time.time < nextActualOpenAttemptTime)
                return true;
            nextActualOpenAttemptTime = Time.time + 1.5f;

            // This is the game's own server-side mutation and replication path.
            // It checks a real BarricadeRegion and updates actual stored state.
            // NEVER call updateToggle directly or send a spoofed player request.
            if (!BarricadeManager.ServerSetDoorOpen(door, true))
                return false;

            lastOpenedDoor = door;
            waitingForDoorAnimationUntil = Time.time + 1.25f;
            return true;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }
    }
}
