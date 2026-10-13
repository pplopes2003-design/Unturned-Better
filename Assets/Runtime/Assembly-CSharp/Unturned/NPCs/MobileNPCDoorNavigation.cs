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
        private readonly Collider[] occupancyHits = new Collider[32];
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
                || !IsFinite(origin) || !IsFinite(direction)
                || direction.sqrMagnitude < 0.2f || probeDistance <= 0.1f
                || probeDistance > 4f)
                return false;
            if (Time.time < nextProbeTime)
            {
                // Retain the active doorway's brief collision-animation wait
                // between scans; returning false here could erase the planned
                // corridor before the native hinge animation has cleared.
                return lastOpenedDoor != null && lastOpenedDoor.isOpen
                    && Time.time < waitingForDoorAnimationUntil
                    && (lastOpenedDoor.transform.position - transform.position).sqrMagnitude <= 9f;
            }

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

            // Check the exact registered barricade before calling the native
            // door mutation. Its internal implementation expects the root
            // BarricadeDrop to exist, so a stale prefab must never enter it.
            if (!BarricadeManager.tryGetRegion(door.transform,
                out byte regionX, out byte regionY, out ushort plant,
                out BarricadeRegion region) || region == null
                || region.FindBarricadeByRootFast(door.transform) == null)
                return false;

            // Game-owned reliable replication and persisted door state;
            // never spoof a Player, force local animation or ignore locks.
            if (!BarricadeManager.ServerSetDoorOpen(door, true))
                return false;

            lastOpenedDoor = door;
            waitingForDoorAnimationUntil = Time.time + 1.25f;
            return true;
        }

        /// <summary>
        /// Side-effect-free edge classifier for local A*: a CLOSED but unlocked
        /// native door is potentially traversable after a future physical
        /// interaction. This method does not open anything or modify the NPC's
        /// active plan. Fail closed if the edge also intersects a wall, vehicle,
        /// another door, or if hit buffering is saturated.
        /// </summary>
        public bool IsTraversableUnlockedDoorEdgeServer(Vector3 origin, Vector3 direction,
            float probeRadius, float distance, int collisionMask)
        {
            if (!Provider.isServer || !Level.isLoaded || vitals == null || vitals.IsDead
                || !IsFinite(origin) || !IsFinite(direction)
                || direction.sqrMagnitude < 0.2f || distance < 0.10f || distance > 2.6f)
                return false;

            int count = Physics.SphereCastNonAlloc(origin,
                Mathf.Clamp(probeRadius, 0.05f, 0.35f),
                direction.normalized, blockingHits, distance, collisionMask,
                QueryTriggerInteraction.Ignore);
            if (count <= 0 || count >= blockingHits.Length)
                return false;

            InteractableDoor onlyDoor = null;
            bool foundDoor = false;
            for (int i = 0; i < count; ++i)
            {
                Collider hit = blockingHits[i].collider;
                if (hit == null || hit.transform == transform
                    || hit.transform.IsChildOf(transform))
                    continue;

                InteractableDoor door = hit.GetComponentInParent<InteractableDoor>();
                if (door == null || !door.isActiveAndEnabled
                    || door.IsChildOfVehicle
                    || door.IsLockedForMobileNPCNavigation
                    || !Level.checkSafeIncludingClipVolumes(door.transform.position))
                    return false;

                // Every real hit must belong to the SAME single door.
                if (onlyDoor != null && onlyDoor != door)
                    return false;
                onlyDoor = door;
                foundDoor = true;
            }
            return foundDoor && IsRegisteredNativeDoor(onlyDoor);
        }

        /// <summary>
        /// Closed unlocked door volume may occupy a local A* node even if the
        /// space behind it is otherwise traversable. Every non-self overlap
        /// must belong to one and the same genuine unlocked door. We cannot
        /// assume a room/wall is passable just because a door is nearby.
        /// </summary>
        public bool IsOnlyUnlockedDoorOccupyingNodeServer(Vector3 bottom, Vector3 top,
            float radius, int collisionMask)
        {
            if (!Provider.isServer || vitals == null || vitals.IsDead
                || !IsFinite(bottom) || !IsFinite(top))
                return false;

            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius,
                occupancyHits, collisionMask, QueryTriggerInteraction.Ignore);
            if (count <= 0 || count >= occupancyHits.Length)
                return false;

            InteractableDoor onlyDoor = null;
            bool sawDoor = false;
            for (int i = 0; i < count; ++i)
            {
                Collider collider = occupancyHits[i];
                if (collider == null || collider.transform == transform
                    || collider.transform.IsChildOf(transform))
                    continue;
                InteractableDoor door = collider.GetComponentInParent<InteractableDoor>();
                if (door == null || !door.isActiveAndEnabled
                    || door.IsChildOfVehicle || door.IsLockedForMobileNPCNavigation
                    || !Level.checkSafeIncludingClipVolumes(door.transform.position))
                    return false;
                if (onlyDoor != null && onlyDoor != door)
                    return false;
                onlyDoor = door;
                sawDoor = true;
            }
            return sawDoor && IsRegisteredNativeDoor(onlyDoor);
        }

        private static bool IsRegisteredNativeDoor(InteractableDoor door)
        {
            // Local A* must not plan through a loose/stale scene object which
            // cannot actually be opened through native server state.
            return door != null
                && BarricadeManager.tryGetRegion(door.transform,
                    out byte x, out byte y, out ushort plant, out BarricadeRegion region)
                && region != null
                && region.FindBarricadeByRootFast(door.transform) != null;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x)
                && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
                && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }
    }
}
