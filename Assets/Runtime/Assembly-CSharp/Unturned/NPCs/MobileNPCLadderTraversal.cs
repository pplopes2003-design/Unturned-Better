using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Optional server-only physical traversal of genuine Unturned Ladder-tagged
    /// surfaces. Never calls PlayerStance.ReceiveClimbRequest/teleportToLocation:
    /// the humanoid motor remains the sole CharacterController.Move owner.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class MobileNPCLadderTraversal : MonoBehaviour
    {
        private enum Phase { None, Approach, Climb, Exit }
        private Phase phase;
        private CharacterController controller;
        private Collider ladder;
        private Vector3 normal;
        private Vector3 entry;
        private Vector3 exit;
        private Vector3 requestedGoal;
        private float directionY;
        private float started;
        private float nextAttempt;
        private float nextContactProbe;
        private float lastProgress;
        private static float nextSharedAttempt;
        private readonly Collider[] clearanceBuffer = new Collider[24];
        private readonly RaycastHit[] approachBuffer = new RaycastHit[12];
        // LADDER_INTERACT includes structures/terrain besides ladder colliders;
        // provide enough fixed room for cluttered interiors before fail-closed.
        private readonly Collider[] surveyedLadders = new Collider[128];
        private float nextSurvey;
        private static float nextSharedSurvey;

        /// <summary>
        /// Find a physically real, directed off-mesh ladder opportunity whose
        /// first endpoint is on OUR current floor and second endpoint is on
        /// the requested floor. This does not commit movement or assert the
        /// approach is reachable: the motor must plan a true path to entry.
        /// </summary>
        public bool TryFindApproachServer(Vector3 goal, LayerMask groundMask,
            float maximumSlope, out Vector3 entrance)
        {
            entrance = Vector3.zero;
            if (!Provider.isServer || IsTraversing || controller == null
                || !controller.enabled || Time.time < nextSurvey
                || Time.time < nextSharedSurvey
                || Mathf.Abs(goal.y - transform.position.y) < 1.4f)
                return false;
            nextSurvey = Time.time + 1.5f;
            nextSharedSurvey = Time.time + 0.18f;

            // A bounded local discovery, not an unbounded map-wide authoring pass.
            int count = Physics.OverlapSphereNonAlloc(transform.position, 12f,
                surveyedLadders, RayMasks.LADDER_INTERACT,
                QueryTriggerInteraction.Collide);
            if (count >= surveyedLadders.Length)
                return false; // saturated discovery cannot prove completeness

            float directionY = Mathf.Sign(goal.y - transform.position.y);
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; ++i)
            {
                Collider candidate = surveyedLadders[i];
                if (candidate == null || !candidate.enabled
                    || !candidate.gameObject.activeInHierarchy
                    || !candidate.CompareTag("Ladder"))
                    continue;
                Vector3 ladderFace = candidate.transform.up;
                if (Mathf.Abs(Vector3.Dot(ladderFace, Vector3.up)) > 0.1f)
                    continue;
                ladderFace.y = 0f;
                if (ladderFace.sqrMagnitude < 0.9f)
                    continue;
                ladderFace.Normalize();

                Bounds bounds = candidate.bounds;
                float entryY = directionY > 0f ? bounds.min.y : bounds.max.y;
                if (Mathf.Abs(entryY - transform.position.y) > 0.9f)
                    continue;

                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 outward = ladderFace * side;
                    Vector3 approx = candidate.transform.position
                        + outward * Mathf.Clamp(controller.radius + 0.15f, 0.45f, 0.65f);
                    approx.y = entryY + 0.4f;
                    if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
                        approx, 1f, 1.3f, groundMask, maximumSlope,
                        out Vector3 entryGround, out _)
                        || Mathf.Abs(entryGround.y - transform.position.y) > 0.45f
                        || !Level.checkSafeIncludingClipVolumes(entryGround)
                        || !IsClearStandingSpace(entryGround))
                        continue;
                    float approachDistance = Vector3.Distance(transform.position, entryGround);
                    if (approachDistance < 2.0f || approachDistance > 11.0f)
                        continue; // existing direct ladder entry handles short range

                    // Prove an authentic Ladder collider faces the chosen
                    // entry; a floor/wall between them rejects this offmesh link.
                    Vector3 eye = entryGround + Vector3.up * 0.55f;
                    if (!Physics.Raycast(eye, -outward, out RaycastHit faceHit,
                        1.1f, RayMasks.LADDER_INTERACT,
                        QueryTriggerInteraction.Collide)
                        || faceHit.collider != candidate
                        || Mathf.Abs(Vector3.Dot(faceHit.normal, candidate.transform.up)) <= 0.9f
                        || !FindRealExit(bounds, entryGround, outward, directionY,
                            goal, groundMask, maximumSlope, out Vector3 groundedExit))
                        continue;

                    float score = approachDistance + Vector3.Distance(goal, groundedExit) * 0.35f;
                    if (score < best)
                    {
                        best = score;
                        entrance = entryGround;
                    }
                }
            }
            return !float.IsPositiveInfinity(best);
        }


        public bool IsTraversing => phase != Phase.None;
        public bool IsClimbing => phase == Phase.Climb || phase == Phase.Exit;
        public bool IsMovingOnLadder => phase == Phase.Climb || phase == Phase.Exit;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        public void ResetTraversal()
        {
            phase = Phase.None;
            ladder = null;
        }

        public bool TryStartServer(Vector3 goal, LayerMask groundMask, float maximumSlope)
        {
            if (!Provider.isServer || phase != Phase.None
                || controller == null || !controller.enabled
                || Time.time < nextAttempt || Time.time < nextSharedAttempt
                || Mathf.Abs(goal.y - transform.position.y) < 1.4f)
                return false;

            nextAttempt = Time.time + 1.2f;
            nextSharedAttempt = Time.time + 0.18f;
            float sign = Mathf.Sign(goal.y - transform.position.y);
            Vector3 baseDirection = goal - transform.position;
            baseDirection.y = 0f;
            if (baseDirection.sqrMagnitude < 0.01f)
                baseDirection = transform.forward;
            baseDirection.y = 0f;
            baseDirection.Normalize();
            float bestScore = float.PositiveInfinity;
            Collider bestLadder = null;
            Vector3 bestNormal = Vector3.zero;
            Vector3 bestEntry = Vector3.zero;
            Vector3 bestExit = Vector3.zero;

            for (int i = 0; i < 8; ++i)
            {
                Vector3 direction = Quaternion.AngleAxis(i * 45f, Vector3.up) * baseDirection;
                // Descending from an upper lip can put the player's
                // 0.8m eye origin above the ladder collider. Probe lower too.
                Vector3 origin = transform.position + Vector3.up * 0.55f;
                bool foundHit = Physics.Raycast(origin, direction,
                    out RaycastHit hit, 1.7f, RayMasks.LADDER_INTERACT,
                    QueryTriggerInteraction.Collide);
                if (!foundHit)
                {
                    origin = transform.position - Vector3.up * 0.22f;
                    foundHit = Physics.Raycast(origin, direction,
                        out hit, 1.7f, RayMasks.LADDER_INTERACT,
                        QueryTriggerInteraction.Collide);
                }
                if (!foundHit
                    || hit.collider == null || !hit.collider.CompareTag("Ladder")
                    || Mathf.Abs(Vector3.Dot(hit.normal, hit.collider.transform.up)) <= 0.9f
                    || Mathf.Abs(Vector3.Dot(Vector3.up, hit.collider.transform.up)) > 0.1f)
                    continue;

                Bounds bounds = hit.collider.bounds;
                float currentY = transform.position.y;
                // Only enter from an observed end, never from a wall halfway up.
                if (sign > 0f ? Mathf.Abs(currentY - bounds.min.y) > 1.25f
                    : Mathf.Abs(currentY - bounds.max.y) > 1.25f)
                    continue;
                Vector3 outward = hit.normal;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.9f)
                    continue;
                outward.Normalize();
                Vector3 anchor = hit.point + outward *
                    Mathf.Clamp(controller.radius + 0.15f, 0.45f, 0.65f);
                anchor.y = currentY;
                if (Vector3.Distance(transform.position, anchor) > 1.5f
                    || !Level.checkSafeIncludingClipVolumes(anchor))
                    continue;
                if (!MobileNPCSpawnService.TryFindSafeGroundPosition(anchor,
                    0.9f, 0.9f, groundMask, maximumSlope,
                    out Vector3 entryGround, out _)
                    || Mathf.Abs(entryGround.y - currentY) > 0.35f
                    || !IsClearStandingSpace(entryGround)
                    || !HasClearApproach(transform.position, entryGround,
                        groundMask, maximumSlope))
                    continue;
                anchor = entryGround;

                // Find an ACTUAL upper/lower walkable platform, not a guessed
                // coordinate above the ladder or an arbitrary neighboring roof.
                if (!FindRealExit(bounds, anchor, outward, sign,
                    goal, groundMask, maximumSlope, out Vector3 exitPoint))
                    continue;
                Vector3 eyeAtEntry = anchor
                    + Vector3.up * Mathf.Min(1.0f, controller.height * 0.6f);
                if (Physics.Linecast(hit.point, eyeAtEntry,
                    RayMasks.BLOCK_STANCE, QueryTriggerInteraction.Ignore))
                    continue;
                float score = Vector3.Distance(transform.position, anchor)
                    + Vector3.Distance(goal, exitPoint) * 0.4f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestLadder = hit.collider;
                    bestNormal = outward;
                    bestEntry = anchor;
                    bestExit = exitPoint;
                }
            }
            if (bestLadder == null)
                return false;

            ladder = bestLadder;
            normal = bestNormal;
            entry = bestEntry;
            exit = bestExit;
            directionY = sign;
            requestedGoal = goal;
            started = Time.time;
            lastProgress = started;
            nextContactProbe = 0f;
            phase = Phase.Approach;
            return true;
        }

        private bool HasClearApproach(Vector3 from, Vector3 to,
            LayerMask groundMask, float maximumSlope)
        {
            Vector3 displacement = to - from;
            displacement.y = 0f;
            float length = displacement.magnitude;
            if (length < 0.10f)
                return true;
            Vector3 midway = (from + to) * 0.5f;
            if (!MobileNPCSpawnService.TryFindSafeGroundPosition(midway,
                0.7f, 0.7f, groundMask, maximumSlope,
                out Vector3 middleGround, out _)
                || Mathf.Abs(middleGround.y - midway.y) > 0.35f)
                return false;
            float radius = Mathf.Max(0.12f, controller.radius * 0.75f);
            Vector3 a = from + Vector3.up * (radius + 0.12f);
            Vector3 b = from + Vector3.up * (controller.height - radius + 0.08f);
            int count = Physics.CapsuleCastNonAlloc(a, b, radius,
                displacement / length, approachBuffer, length,
                RayMasks.BLOCK_LADDER, QueryTriggerInteraction.Ignore);
            if (count >= approachBuffer.Length)
                return false;
            for (int i = 0; i < count; ++i)
                if (approachBuffer[i].collider != null
                    && approachBuffer[i].collider.transform != transform
                    && !approachBuffer[i].collider.transform.IsChildOf(transform))
                    return false;
            return true;
        }

        private bool FindRealExit(Bounds bounds, Vector3 entryPoint,
            Vector3 outward, float sign, Vector3 goal, LayerMask groundMask,
            float maximumSlope, out Vector3 result)
        {
            result = Vector3.zero;
            float expectedY = sign > 0f ? bounds.max.y : bounds.min.y;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < 3; ++i)
            {
                float signedOffset = i == 0 ? -0.95f : i == 1 ? 0.55f : -0.45f;
                Vector3 candidate = entryPoint + outward * signedOffset;
                candidate.y = expectedY + 0.8f;
                if (!MobileNPCSpawnService.TryFindSafeGroundPosition(candidate,
                    1.2f, 1.9f, groundMask, maximumSlope,
                    out Vector3 ground, out _)
                    || Mathf.Abs(ground.y - expectedY) > 0.95f
                    || Mathf.Abs(ground.y - goal.y) > 1.0f
                    || !Level.checkSafeIncludingClipVolumes(ground)
                    || !IsClearStandingSpace(ground))
                    continue;
                float score = Vector3.Distance(goal, ground)
                    + (signedOffset > 0f ? 0.25f : 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    result = ground;
                }
            }
            return !float.IsPositiveInfinity(bestScore);
        }

        private bool IsClearStandingSpace(Vector3 position)
        {
            float radius = Mathf.Max(0.10f, controller.radius * 0.85f);
            float height = Mathf.Max(controller.height, radius * 2.1f);
            Vector3 bottom = position + Vector3.up * (radius + 0.12f);
            Vector3 top = position + Vector3.up * (height - radius + 0.08f);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius,
                clearanceBuffer, RayMasks.BLOCK_LADDER, QueryTriggerInteraction.Ignore);
            if (count >= clearanceBuffer.Length)
                return false;
            for (int i = 0; i < count; ++i)
                if (clearanceBuffer[i] != null
                    && clearanceBuffer[i].transform != transform
                    && !clearanceBuffer[i].transform.IsChildOf(transform))
                    return false;
            return true;
        }

        public Vector3 GetMotionServer(Vector3 goal, float deltaTime)
        {
            if (!Provider.isServer || phase == Phase.None)
                return Vector3.zero;
            if (ladder == null || !ladder.enabled
                || !ladder.gameObject.activeInHierarchy || !ladder.CompareTag("Ladder")
                || !controller.enabled
                // Once the body is actually on a verified ladder, finish the
                // physical climb/exit before reconsidering a moving enemy.
                // A two-metre target shift must not drop the NPC mid-rung.
                || (phase == Phase.Approach
                    && (goal - requestedGoal).sqrMagnitude > 4f)
                || Time.time - started > 9f)
            {
                AbortServer();
                return Vector3.zero;
            }

            if (phase == Phase.Approach)
            {
                Vector3 horizontal = entry - transform.position;
                horizontal.y = 0f;
                if (horizontal.sqrMagnitude < 0.06f)
                {
                    phase = Phase.Climb;
                    lastProgress = Time.time;
                        }
                else
                {
                    return horizontal.normalized * Mathf.Min(1.4f,
                        horizontal.magnitude / Mathf.Max(0.02f, deltaTime));
                }
            }

            if (phase == Phase.Climb)
            {
                // Reaching the top/bottom ends contact with the ladder; begin
                // the physically checked exit BEFORE probing rung contact.
                float ladderEndY = directionY > 0f
                    ? ladder.bounds.max.y : ladder.bounds.min.y;
                if (Mathf.Abs(transform.position.y - exit.y) <= 0.55f
                    || Mathf.Abs(transform.position.y - ladderEndY) <= 0.42f)
                    phase = Phase.Exit;
            }

            if (phase == Phase.Climb)
            {
                if (Time.time >= nextContactProbe)
                {
                    nextContactProbe = Time.time + 0.18f;
                    // Same Ladder-tagged COLLIDER must remain in front of the
                    // NPC during ascent/descent. No surface = no climbing.
                    Vector3 origin = transform.position + Vector3.up * 0.5f;
                    if (!Physics.Raycast(origin, -normal, out RaycastHit contact,
                        0.95f, RayMasks.LADDER_INTERACT, QueryTriggerInteraction.Collide)
                        || contact.collider != ladder
                        || !contact.collider.CompareTag("Ladder"))
                    {
                        AbortServer();
                        return Vector3.zero;
                    }
                }
                return Vector3.up * directionY * 1.65f;
            }

            if (phase == Phase.Exit)
            {
                Vector3 offset = exit - transform.position;
                float vertical = Mathf.Clamp(offset.y * 2f, -0.9f, 0.9f);
                offset.y = 0f;
                // A path end is not a landing until the real controller
                // reports floor contact; never acknowledge a mid-air arrival.
                if (offset.sqrMagnitude < 0.16f
                    && Mathf.Abs(exit.y - transform.position.y) < 0.50f
                    && controller.isGrounded)
                {
                    ResetTraversal();
                    return Vector3.zero;
                }
                return offset.normalized * Mathf.Min(1.15f,
                    offset.magnitude / Mathf.Max(0.02f, deltaTime))
                    + Vector3.up * vertical;
            }
            return Vector3.zero;
        }

        public void AfterMoveServer(Vector3 before, CollisionFlags collision)
        {
            if (phase == Phase.None)
                return;
            if (Vector3.Distance(transform.position, before) >= 0.009f)
            {
                lastProgress = Time.time;
                }
            else if (Time.time - lastProgress > 0.75f)
                AbortServer();
            if (phase == Phase.Exit
                && (collision & CollisionFlags.Sides) != 0
                && Time.time - lastProgress > 0.3f)
                AbortServer();
        }

        public void AbortServer()
        {
            ResetTraversal();
            nextAttempt = Time.time + 5f;
        }
    }
}
