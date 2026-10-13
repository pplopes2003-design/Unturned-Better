using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Server-owned, collision-driven clamber over ONE low static parapet.
    /// This is not a shortcut across a building, automatic vault animation,
    /// inferred roof access, or a teleport. The motor alone calls Move.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class MobileNPCVaultTraversal : MonoBehaviour
    {
        private enum Phase { None, Rise, Across, Land }
        private Phase phase;
        private CharacterController controller;
        private Vector3 direction;
        private Vector3 landing;
        private Vector3 goalSnapshot;
        private float platformTop;
        private float startedAt;
        private float lastProgressAt;
        private float cooldownUntil;
        private static float sharedProbeUntil;
        private readonly RaycastHit[] hitBuffer = new RaycastHit[20];
        private readonly Collider[] overlapBuffer = new Collider[20];

        public bool IsTraversing => phase != Phase.None;
        public bool IsClambering => phase == Phase.Rise || phase == Phase.Across;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        public bool TryStartServer(Vector3 goal, Vector3 desired,
            LayerMask groundMask, float slopeLimit, int obstacleMask)
        {
            if (!Provider.isServer || IsTraversing || controller == null
                || !controller.enabled || Time.time < cooldownUntil
                || Time.time < sharedProbeUntil
                || Mathf.Abs(goal.y - transform.position.y) > 0.7f)
                return false;

            // Avoid per-frame expensive multi-cast checks in crowds.
            cooldownUntil = Time.time + 1.0f;
            sharedProbeUntil = Time.time + 0.12f;
            desired.y = 0f;
            if (desired.sqrMagnitude < 0.9f)
                return false;
            Vector3 forward = desired.normalized;
            Vector3 flatGoal = goal - transform.position;
            flatGoal.y = 0f;
            if (Vector3.Dot(flatGoal, forward) < 2.0f)
                return false;

            Vector3 root = transform.position;
            Vector3 frontRay = root + Vector3.up * 0.48f;
            if (!Physics.Raycast(frontRay, forward, out RaycastHit wall,
                0.95f, obstacleMask, QueryTriggerInteraction.Ignore)
                || wall.collider == null
                || wall.collider.attachedRigidbody != null
                || wall.collider.GetComponentInParent<Player>() != null
                || wall.collider.GetComponentInParent<Zombie>() != null
                || wall.collider.GetComponentInParent<MobileNPCHumanoidVitals>() != null
                || wall.collider.GetComponentInParent<InteractableVehicle>() != null
                || wall.collider.GetComponentInParent<InteractableDoor>() != null)
                return false;

            // Confirm a real horizontal obstacle TOP, not an arbitrary height
            // assigned to a wall or an object whose actual bounds are tall.
            Vector3 topProbe = wall.point + forward * 0.10f + Vector3.up * 1.75f;
            if (!Physics.Raycast(topProbe, Vector3.down, out RaycastHit top,
                1.95f, obstacleMask, QueryTriggerInteraction.Ignore)
                || top.collider != wall.collider
                || Vector3.Dot(top.normal, Vector3.up) < 0.84f)
                return false;
            float obstacleHeight = top.point.y - root.y;
            if (obstacleHeight < 0.70f || obstacleHeight > 1.10f)
                return false;

            Vector3 exitProbe = root + forward * 1.85f;
            if (!MobileNPCSpawnService.TryFindSafeGroundPosition(exitProbe,
                1.3f, 1.5f, groundMask, slopeLimit,
                out Vector3 realLanding, out _)
                || Mathf.Abs(realLanding.y - root.y) > 0.22f
                || !Level.checkSafeIncludingClipVolumes(realLanding))
                return false;

            float radius = Mathf.Clamp(controller.radius * 0.78f, 0.16f, 0.32f);
            float liftedY = top.point.y + 0.16f;
            Vector3 lifted = new Vector3(root.x, liftedY, root.z);
            Vector3 over = new Vector3(realLanding.x, liftedY, realLanding.z);

            if (!IsCapsuleClearAt(lifted, radius, obstacleMask)
                || !IsCapsuleClearAt(realLanding, radius, obstacleMask)
                || !IsCapsuleSweepClear(root, lifted, radius, obstacleMask)
                || !IsCapsuleSweepClear(lifted, over, radius, obstacleMask))
                return false;

            direction = forward;
            landing = realLanding;
            goalSnapshot = goal;
            platformTop = liftedY;
            startedAt = Time.time;
            lastProgressAt = Time.time;
            phase = Phase.Rise;
            return true;
        }

        private bool IsCapsuleClearAt(Vector3 position, float radius, int mask)
        {
            float height = Mathf.Max(controller.height, radius * 2.2f);
            Vector3 bottom = position + Vector3.up * (radius + 0.12f);
            Vector3 top = position + Vector3.up * (height - radius + 0.08f);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius,
                overlapBuffer, mask, QueryTriggerInteraction.Ignore);
            if (count >= overlapBuffer.Length)
                return false;
            for (int i = 0; i < count; ++i)
                if (overlapBuffer[i] != null && !IsOwn(overlapBuffer[i]))
                    return false;
            return true;
        }

        private bool IsCapsuleSweepClear(Vector3 from, Vector3 to, float radius, int mask)
        {
            Vector3 travel = to - from;
            if (travel.sqrMagnitude < 0.0001f)
                return true;
            float height = Mathf.Max(controller.height, radius * 2.2f);
            Vector3 bottom = from + Vector3.up * (radius + 0.12f);
            Vector3 top = from + Vector3.up * (height - radius + 0.08f);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius,
                travel.normalized, hitBuffer, travel.magnitude,
                mask, QueryTriggerInteraction.Ignore);
            if (count >= hitBuffer.Length)
                return false;
            for (int i = 0; i < count; ++i)
                if (hitBuffer[i].collider != null && !IsOwn(hitBuffer[i].collider))
                    return false;
            return true;
        }

        private bool IsOwn(Collider collider)
        {
            return collider.transform == transform ||
                collider.transform.IsChildOf(transform);
        }

        /// <summary>
        /// Returns velocity to be passed to CharacterController.Move by motor.
        /// Gravity is restored for final landing. No root-position writes.
        /// </summary>
        public Vector3 GetMotionServer(Vector3 goal, out bool applyGravity)
        {
            applyGravity = true;
            if (!Provider.isServer || !IsTraversing)
                return Vector3.zero;
            if (!controller.enabled || (goal - goalSnapshot).sqrMagnitude > 2.25f
                || Time.time - startedAt > 3.8f)
            {
                AbortServer();
                return Vector3.zero;
            }

            if (phase == Phase.Rise)
            {
                applyGravity = false;
                if (transform.position.y < platformTop - 0.08f)
                    return Vector3.up * 1.9f;
                phase = Phase.Across;
            }
            if (phase == Phase.Across)
            {
                applyGravity = false;
                Vector3 flat = landing - transform.position;
                flat.y = 0f;
                if (flat.magnitude > 0.40f)
                    return direction * 1.75f
                        + Vector3.up * Mathf.Clamp((platformTop - transform.position.y) * 3f,
                            -0.40f, 0.40f);
                phase = Phase.Land;
            }
            if (phase == Phase.Land)
            {
                Vector3 flat = landing - transform.position;
                flat.y = 0f;
                if (controller.isGrounded && flat.sqrMagnitude < 0.36f
                    && Mathf.Abs(transform.position.y - landing.y) < 0.42f)
                {
                    ResetServer();
                    return Vector3.zero;
                }
                return flat.sqrMagnitude < 0.03f ? Vector3.zero
                    : flat.normalized * Mathf.Min(flat.magnitude * 2f, 1.1f);
            }
            return Vector3.zero;
        }

        public void AfterMoveServer(Vector3 before)
        {
            if (!IsTraversing)
                return;
            if (Vector3.Distance(before, transform.position) > 0.012f)
                lastProgressAt = Time.time;
            if (Time.time - lastProgressAt > 0.55f)
                AbortServer();
        }

        public void AbortServer()
        {
            ResetServer();
            cooldownUntil = Time.time + 4.0f;
        }

        public void ResetServer()
        {
            phase = Phase.None;
        }
    }
}
