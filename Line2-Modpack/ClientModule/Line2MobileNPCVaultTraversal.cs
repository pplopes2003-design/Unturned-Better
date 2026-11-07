using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Bounded, server-side PREFLIGHT for jumping across a low parapet.
    /// The NPC motor uses ordinary gravity and CharacterController.Move, just
    /// like a normal Unturned jump. No controlled Rise/Across/Land ascent,
    /// animation dependency, transform warp, or hovering over an obstacle.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class Line2MobileNPCVaultTraversal : MonoBehaviour
    {
        public struct JumpPlan
        {
            public Vector3 Direction;
            public Vector3 Landing;
            public float Impulse;
            public float HorizontalSpeed;
            public float ForwardDelay;
            public float TravelDistance;
            public float MaximumDuration;
            // Diagnostic only: a physical landing on a raised object, not
            // a new walkable navmesh link or an invented "climb" animation.
            public bool LandingOnObjectTop;
            // Exact physically measured support. This is NOT a new A* graph
            // edge; the motor must validate real grounded contact first.
            public Collider LandingSupportCollider;
        }

        // PlayerMovement uses JUMP=7 with up to a 25% Parkour skill bonus.
        // No player skill is granted to the NPC; this defines an upper bound
        // for a physically achievable Unturned-style optional higher hop.
        private const float NativeJumpImpulse = 7f;
        private const float ParkourJumpImpulse = 8.75f;
        private const float NativeRunSpeed = 4.5f;
        private const int MaxArcSamples = 24;
        private static float nextSharedProbeTime;

        private readonly RaycastHit[] arcHits = new RaycastHit[24];
        private readonly Collider[] clearanceHits = new Collider[24];
        private CharacterController controller;
        private float nextIndividualProbeTime;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        public bool TryPlanJumpServer(Vector3 goal, Vector3 desired,
            LayerMask groundMask, float maxSlope, int obstacleMask,
            float gravityAcceleration, out JumpPlan plan)
        {
            plan = default(JumpPlan);
            if (!Provider.isServer || controller == null || !controller.enabled
                || Time.time < nextIndividualProbeTime
                || Time.time < nextSharedProbeTime
                || gravityAcceleration < 9f
                || Mathf.Abs(goal.y - transform.position.y) > 1.15f)
                return false;

            // One expensive geometric proof globally per ~0.12 sec, one
            // per survivor per 0.9 sec. Normal movement is never throttled.
            nextIndividualProbeTime = Time.time + 0.9f;
            nextSharedProbeTime = Time.time + 0.12f;

            desired.y = 0f;
            if (desired.sqrMagnitude < 0.5f)
                return false;
            Vector3 direction = desired.normalized;
            Vector3 root = transform.position;

            // Shin-height observation also sees genuine short crates
            // whose flat tops are below the previous 0.48m ray.
            Vector3 wallProbe = root + Vector3.up * 0.28f;
            if (!Physics.Raycast(wallProbe, direction, out RaycastHit wall,
                0.95f, obstacleMask, QueryTriggerInteraction.Ignore)
                || wall.collider == null || IsSelf(wall.collider)
                || (wall.collider.attachedRigidbody != null
                    && !wall.collider.attachedRigidbody.isKinematic)
                || wall.collider.GetComponentInParent<Player>() != null
                || wall.collider.GetComponentInParent<Zombie>() != null
                || wall.collider.GetComponentInParent<PrototypeNpcVitals>() != null
                || wall.collider.GetComponentInParent<InteractableVehicle>() != null
                || wall.collider.GetComponentInParent<InteractableDoor>() != null)
                return false;

            // Only a real short, horizontal top. The old clamber artificially
            // raised the capsule up to 1.1m, even when the player jump could
            // not actually reach that height. Never do that.
            Vector3 topOrigin = wall.point + direction * 0.10f + Vector3.up * 1.65f;
            if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top,
                1.9f, obstacleMask, QueryTriggerInteraction.Ignore)
                || !IsSamePhysicalObstacle(top.collider, wall.collider)
                || Vector3.Dot(top.normal, Vector3.up) < 0.84f)
                return false;
            float wallHeight = top.point.y - root.y;
            if (wallHeight < 0.42f || wallHeight > 1.02f)
                return false;

            // Both "regular" and higher Parkour-style leaps are bounded by
            // real Unturned PlayerMovement constants, not arbitrary levitation.
            // Use Unturned's ordinary 7 impulse for small hops, and
            // its existing Parkour bound for taller but still feasible
            // object tops. Actual ballistic clearance remains decisive.
            float impulse = wallHeight < 0.60f
                ? NativeJumpImpulse : ParkourJumpImpulse;
            float apex = impulse * impulse / (2f * gravityAcceleration);
            float clearanceHeight = wallHeight + 0.13f;
            if (apex < clearanceHeight + 0.07f)
                return false;

            // A window sill may have a different thickness from a crate.
            // Probe several real landing surfaces rather than assuming all
            // openings are exactly 1.65m deep. All later ballistic sweeps
            // still have to clear the unchanged physical obstacle.
            float travelDistance = 1.65f;
            Vector3 landing = Vector3.zero;
            Collider groundCollider = null;
            bool hasGroundBeyond = false;
            for (int landingIndex = 0; landingIndex < 4; ++landingIndex)
            {
                float candidateDistance = 1.25f + landingIndex * 0.25f;
                Vector3 landingProbe = root + direction * candidateDistance;
                Vector3 candidateGround;
                Collider candidateSupport;
                if (!TryFindLandingGround(
                    landingProbe, 1.25f, 1.6f, groundMask, maxSlope,
                    out candidateGround, out candidateSupport)
                    || candidateSupport == null
                    || Mathf.Abs(candidateGround.y - root.y) > 0.22f
                    || !Level.checkSafeIncludingClipVolumes(candidateGround)
                    || IsSamePhysicalObstacle(candidateSupport, wall.collider)
                    || !CapsuleClearAt(candidateGround,
                        Mathf.Clamp(controller.radius * 0.78f, 0.15f, 0.29f),
                        obstacleMask))
                    continue;
                landing = candidateGround;
                groundCollider = candidateSupport;
                travelDistance = candidateDistance;
                hasGroundBeyond = true;
                break;
            }
            bool landingOnTop = false;
            Collider landingTopCollider = null;
            // A climbable LOW object may be a real intermediate footstep.
            // Use the exact observed collider and measured horizontal top.
            // No generic A* connection across objects is created.
            if (!hasGroundBeyond || goal.y - root.y > 0.35f)
            {
                bool foundTopLanding = false;
                float bestTopDistance = 0f;
                float firstDistance = Mathf.Clamp(wall.distance + 0.55f, 0.85f, 1.4f);
                for (int candidateIndex = 0; candidateIndex < 3; ++candidateIndex)
                {
                    float testDistance = firstDistance + candidateIndex * 0.2f;
                    Vector3 fromAbove = root + direction * testDistance + Vector3.up * 1.65f;
                    if (!Physics.Raycast(fromAbove, Vector3.down, out RaycastHit surface,
                        1.9f, obstacleMask, QueryTriggerInteraction.Ignore)
                        || !IsSamePhysicalObstacle(surface.collider, wall.collider)
                        || Vector3.Dot(surface.normal, Vector3.up) < 0.90f
                        || Mathf.Abs(surface.point.y - top.point.y) > 0.12f)
                        continue;
                    Vector3 candidateLanding = surface.point + Vector3.up * 0.05f;
                    if (!Level.checkSafeIncludingClipVolumes(candidateLanding))
                        continue;
                    float candidateHeight = candidateLanding.y - root.y;
                    if (candidateHeight < 0.42f || candidateHeight > 1.07f)
                        continue;
                    float capsuleRadius = Mathf.Clamp(controller.radius * 0.78f, 0.15f, 0.29f);
                    if (!HasStableObjectTopFootprint(candidateLanding,
                        wall.collider, direction, capsuleRadius, obstacleMask)
                        || !CapsuleClearAt(candidateLanding, capsuleRadius, obstacleMask))
                        continue;
                    foundTopLanding = true;
                    bestTopDistance = testDistance;
                    landing = candidateLanding;
                    landingTopCollider = surface.collider;
                    break;
                }
                if (!foundTopLanding)
                    return false;
                travelDistance = bestTopDistance;
                landingOnTop = true;
            }

            Vector3 flatGoal = goal - root;
            flatGoal.y = 0f;
            float forwardGoal = Vector3.Dot(flatGoal, direction);
            // A top landing is permitted either as an intermediate step
            // toward a more distant goal, or when the actual goal is
            // physically located on that SAME measured furniture surface.
            if (landingOnTop)
            {
                bool goalOnThisTop = Mathf.Abs(goal.y - landing.y) <= 0.4f
                    && Vector3.Distance(new Vector3(goal.x, 0f, goal.z),
                        new Vector3(landing.x, 0f, landing.z)) <= 0.85f;
                if (!goalOnThisTop && forwardGoal < travelDistance + 0.20f)
                    return false;
            }
            else if (forwardGoal < travelDistance + 0.15f)
                return false;

            float relativeHeight = landing.y - root.y;
            float discriminant = impulse * impulse -
                2f * gravityAcceleration * relativeHeight;
            if (discriminant <= 0f)
                return false;
            float duration = (impulse + Mathf.Sqrt(discriminant))
                / gravityAcceleration;

            // Hold forward motion until the body is high enough to clear
            // the obstruction, then move at standard running speed.
            float riseDiscriminant = impulse * impulse -
                2f * gravityAcceleration * clearanceHeight;
            if (riseDiscriminant <= 0f)
                return false;
            float forwardDelay = (impulse - Mathf.Sqrt(riseDiscriminant))
                / gravityAcceleration;
            float speed = NativeRunSpeed;
            if (forwardDelay + travelDistance / speed > duration - 0.055f)
                return false;
            // The chosen landing is a real top. This is only feasible when
            // feet stay above it until the body finishes traveling forward.
            if (landingOnTop && impulse * (duration - 0.05f)
                - 0.5f * gravityAcceleration * (duration - 0.05f)
                    * (duration - 0.05f) < bestTopClearance(wallHeight))
                return false;

            float radius = Mathf.Clamp(controller.radius * 0.78f, 0.15f, 0.29f);
            if (!CapsuleClearAt(landing, radius, obstacleMask))
                return false;

            // Sample real ballistic positions and sweep between samples.
            // Any hit other than our own collider rejects the maneuver.
            Vector3 previous = root;
            for (int i = 1; i <= MaxArcSamples; ++i)
            {
                float t = duration * i / (MaxArcSamples + 1f);
                float along = Mathf.Min(travelDistance,
                    Mathf.Max(0f, t - forwardDelay) * speed);
                Vector3 point = root + direction * along
                    + Vector3.up * (impulse * t
                        - 0.5f * gravityAcceleration * t * t);
                if (!Level.checkSafeIncludingClipVolumes(point)
                    || !CapsuleSweepClear(previous, point, radius, obstacleMask))
                    return false;
                previous = point;
            }

            plan.Direction = direction;
            plan.Landing = landing;
            plan.Impulse = impulse;
            plan.HorizontalSpeed = speed;
            plan.ForwardDelay = forwardDelay;
            plan.TravelDistance = travelDistance;
            plan.MaximumDuration = duration + 0.20f;
            plan.LandingOnObjectTop = landingOnTop;
            plan.LandingSupportCollider = landingOnTop ? landingTopCollider : null;
            return true;
        }

        /// <summary>
        /// A furniture prefab may use separate box colliders for its side and
        /// top. Accept only the SAME registered interactive or the same
        /// kinematic rigidbody, not everything sharing a building root.
        /// </summary>
        /// <summary>
        /// Steam-only alternative to Line 1 MobileNPCSpawnService's ground
        /// helper. Uses the same real Raycast support and slope checks.
        /// Never treats moving vehicles or furniture as architectural floor.
        /// </summary>
        private static bool TryFindLandingGround(Vector3 candidate,
            float height, float drop, LayerMask mask, float slope,
            out Vector3 foot, out Collider support)
        {
            foot = Vector3.zero;
            support = null;
            RaycastHit hit;
            if (!Physics.Raycast(candidate + Vector3.up * height,
                Vector3.down, out hit, height + drop, mask,
                QueryTriggerInteraction.Ignore) || hit.collider == null ||
                Vector3.Angle(hit.normal, Vector3.up) > slope ||
                !Level.checkSafeIncludingClipVolumes(hit.point))
                return false;
            if (hit.collider.attachedRigidbody != null &&
                !hit.collider.attachedRigidbody.isKinematic)
                return false;
            Interactable furniture =
                hit.collider.GetComponentInParent<Interactable>();
            if (furniture is InteractableBed ||
                furniture is InteractableStorage ||
                furniture is InteractableMannequin ||
                furniture is InteractableSentry)
                return false;
            foot = hit.point + Vector3.up * 0.05f;
            support = hit.collider;
            return true;
        }

        public static bool IsSamePhysicalObstacle(Collider a, Collider b)
        {
            if (a == null || b == null)
                return false;
            if (a == b)
                return true;
            Interactable furniture = a.GetComponentInParent<Interactable>();
            if (furniture != null && !(furniture is InteractableDoor)
                && furniture == b.GetComponentInParent<Interactable>())
                return true;
            return a.attachedRigidbody != null
                && a.attachedRigidbody == b.attachedRigidbody
                && a.attachedRigidbody.isKinematic;
        }

        private static bool HasStableObjectTopFootprint(Vector3 landing,
            Collider observed, Vector3 direction, float radius, int mask)
        {
            // A narrow chair back or shelf lip might have a flat top ray
            // but no room for even the NPC's feet. Ground both lateral
            // support samples against the SAME physically observed object.
            Vector3 lateral = new Vector3(-direction.z, 0f, direction.x).normalized;
            float spread = Mathf.Clamp(radius * 0.65f, 0.09f, 0.20f);
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 rayStart = landing + lateral * (spread * i)
                    + Vector3.up * 0.65f;
                if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit,
                    0.95f, mask, QueryTriggerInteraction.Ignore)
                    || !IsSamePhysicalObstacle(hit.collider, observed)
                    || Vector3.Dot(hit.normal, Vector3.up) < 0.90f
                    || Mathf.Abs(hit.point.y + 0.05f - landing.y) > 0.12f)
                    return false;
            }
            return true;
        }

        private static float bestTopClearance(float topHeight)
        {
            return Mathf.Max(0.45f, topHeight - 0.10f);
        }

        private bool CapsuleClearAt(Vector3 origin, float radius, int mask)
        {
            float height = Mathf.Max(controller.height, radius * 2.2f);
            Vector3 bottom = origin + Vector3.up * (radius + 0.12f);
            Vector3 top = origin + Vector3.up * (height - radius + 0.08f);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius,
                clearanceHits, mask, QueryTriggerInteraction.Ignore);
            if (count >= clearanceHits.Length)
                return false;
            for (int i = 0; i < count; ++i)
                if (clearanceHits[i] != null && !IsSelf(clearanceHits[i]))
                    return false;
            return true;
        }

        private bool CapsuleSweepClear(Vector3 from, Vector3 to,
            float radius, int mask)
        {
            Vector3 movement = to - from;
            if (movement.sqrMagnitude <= 0.000001f)
                return true;
            float height = Mathf.Max(controller.height, radius * 2.2f);
            Vector3 bottom = from + Vector3.up * (radius + 0.12f);
            Vector3 top = from + Vector3.up * (height - radius + 0.08f);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius,
                movement.normalized, arcHits, movement.magnitude, mask,
                QueryTriggerInteraction.Ignore);
            if (count >= arcHits.Length)
                return false;
            for (int i = 0; i < count; ++i)
                if (arcHits[i].collider != null && !IsSelf(arcHits[i].collider))
                    return false;
            return true;
        }

        private bool IsSelf(Collider candidate)
        {
            return candidate.transform == transform ||
                candidate.transform.IsChildOf(transform);
        }
    }
}
