////////////////////////////////////////////////////////////////////////////////////////
// Unturned-Better project extension.
// Mobile NPC AI foundation. Kept separate from the vanilla Zombie implementation.
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Server-side state and perception foundation for humanoid mobile NPCs.
    /// Target acquisition and combat are deliberately delegated to adapters so this
    /// component does not change the behaviour of existing vanilla zombies or sentries.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCBrain : MonoBehaviour
    {
        public enum BrainState
        {
            Idle,
            Investigating,
            Pursuing,
            Returning
        }

        [Header("Perception")]
        [SerializeField, Min(1f)] private float sightRange = 32f;
        [SerializeField, Range(1f, 180f)] private float fieldOfView = 110f;
        [SerializeField, Min(0f)] private float hearingRange = 48f;
        [SerializeField, Min(0f)] private float memoryDuration = 8f;
        [SerializeField, Min(0.1f)] private float targetScanInterval = 0.5f;
        [SerializeField] private bool acquirePlayersAutomatically = true;
        [SerializeField] private Transform eyePoint;
        [SerializeField] private LayerMask obstructionMask = ~0;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float walkSpeed = 2.2f;
        [SerializeField, Min(0f)] private float sprintSpeed = 4.1f;
        [SerializeField, Min(0f)] private float turnSpeed = 240f;
        [SerializeField, Min(0.1f)] private float stoppingDistance = 1.8f;
        [SerializeField, Min(0f)] private float returnDistance = 2.5f;

        private CharacterController characterController;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private Vector3 lastKnownTargetPosition;
        private float lastTargetSeenAt = float.NegativeInfinity;
        private float verticalVelocity;
        private float nextTargetScanTime;
        private bool initialized;

        public BrainState State { get; private set; } = BrainState.Idle;
        public Transform CurrentTarget { get; private set; }
        public Vector3 LastKnownTargetPosition => lastKnownTargetPosition;
        public Vector3 SpawnPosition => spawnPosition;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
            initialized = true;
        }

        private void Update()
        {
            // AI decisions and movement must be authoritative on the server.
            if (!Provider.isServer || !initialized || !isActiveAndEnabled)
                return;

            if (acquirePlayersAutomatically && CurrentTarget == null && Time.time >= nextTargetScanTime)
            {
                nextTargetScanTime = Time.time + targetScanInterval;
                AcquireNearestVisiblePlayer();
            }

            if (CurrentTarget != null && CanSee(CurrentTarget))
            {
                lastKnownTargetPosition = CurrentTarget.position;
                lastTargetSeenAt = Time.time;
                State = BrainState.Pursuing;
            }
            else if (CurrentTarget != null && Time.time - lastTargetSeenAt > memoryDuration)
            {
                CurrentTarget = null;
                State = BrainState.Investigating;
            }

            switch (State)
            {
                case BrainState.Pursuing:
                    MoveTowards(CurrentTarget != null ? CurrentTarget.position : lastKnownTargetPosition,
                        CurrentTarget != null ? sprintSpeed : walkSpeed);
                    if (CurrentTarget == null && HorizontalDistance(transform.position, lastKnownTargetPosition) <= stoppingDistance)
                        State = BrainState.Idle;
                    break;

                case BrainState.Investigating:
                    MoveTowards(lastKnownTargetPosition, walkSpeed);
                    if (HorizontalDistance(transform.position, lastKnownTargetPosition) <= stoppingDistance)
                        State = BrainState.Idle;
                    break;

                case BrainState.Returning:
                    MoveTowards(spawnPosition, walkSpeed);
                    if (HorizontalDistance(transform.position, spawnPosition) <= returnDistance)
                    {
                        State = BrainState.Idle;
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, spawnRotation, turnSpeed * Time.deltaTime);
                    }
                    break;

                case BrainState.Idle:
                    ApplyGravityOnly();
                    break;
            }
        }

        private void AcquireNearestVisiblePlayer()
        {
            Player[] players = FindObjectsOfType<Player>();
            Transform bestTarget = null;
            float bestDistance = sightRange * sightRange;

            for (int i = 0; i < players.Length; i++)
            {
                Player player = players[i];
                if (player == null || !player.gameObject.activeInHierarchy)
                    continue;

                Transform candidate = player.transform;
                if (candidate == transform || candidate.IsChildOf(transform) || !CanSee(candidate))
                    continue;

                float distance = (candidate.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = candidate;
                }
            }

            if (bestTarget != null)
                TrySetTarget(bestTarget);
        }

        /// <summary>Assign a candidate target from the game-specific player/NPC adapter.</summary>
        public bool TrySetTarget(Transform candidate)
        {
            if (!Provider.isServer || candidate == null || !CanSee(candidate))
                return false;

            CurrentTarget = candidate;
            lastKnownTargetPosition = candidate.position;
            lastTargetSeenAt = Time.time;
            State = BrainState.Pursuing;
            return true;
        }

        /// <summary>Notify this NPC of a noise; hearing does not require line of sight.</summary>
        public void NotifyNoise(Vector3 worldPosition, float loudnessRadius)
        {
            if (!Provider.isServer || CurrentTarget != null)
                return;

            float effectiveRange = Mathf.Min(hearingRange, Mathf.Max(0f, loudnessRadius));
            if ((worldPosition - transform.position).sqrMagnitude > effectiveRange * effectiveRange)
                return;

            lastKnownTargetPosition = worldPosition;
            lastTargetSeenAt = Time.time;
            State = BrainState.Investigating;
        }

        public void ClearTarget(bool investigateLastPosition = true)
        {
            if (CurrentTarget != null)
                lastKnownTargetPosition = CurrentTarget.position;

            CurrentTarget = null;
            State = investigateLastPosition ? BrainState.Investigating : BrainState.Returning;
        }

        public void ReturnToSpawn()
        {
            CurrentTarget = null;
            State = BrainState.Returning;
        }

        public bool CanSee(Transform candidate)
        {
            if (candidate == null)
                return false;

            Vector3 origin = eyePoint != null ? eyePoint.position : transform.position + Vector3.up * 1.5f;
            Vector3 targetPoint = candidate.position + Vector3.up;
            Vector3 offset = targetPoint - origin;
            float distance = offset.magnitude;

            if (distance > sightRange || distance <= 0.001f)
                return false;

            if (Vector3.Angle(transform.forward, offset) > fieldOfView * 0.5f)
                return false;

            if (Physics.Raycast(origin, offset / distance, out RaycastHit hit, distance, obstructionMask,
                QueryTriggerInteraction.Ignore))
            {
                return hit.transform == candidate || hit.transform.IsChildOf(candidate);
            }

            return true;
        }

        private void MoveTowards(Vector3 destination, float speed)
        {
            Vector3 offset = destination - transform.position;
            Vector3 horizontal = Vector3.ProjectOnPlane(offset, Vector3.up);
            if (horizontal.sqrMagnitude > 0.01f)
            {
                Quaternion desiredRotation = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRotation, turnSpeed * Time.deltaTime);
            }

            if (characterController == null)
                return;

            Vector3 movement = Vector3.zero;
            if (horizontal.magnitude > stoppingDistance)
                movement = horizontal.normalized * speed;

            if (characterController.isGrounded)
                verticalVelocity = -1f;
            else
                verticalVelocity += Physics.gravity.y * Time.deltaTime;

            movement.y = verticalVelocity;
            characterController.Move(movement * Time.deltaTime);
        }

        private void ApplyGravityOnly()
        {
            if (characterController == null)
                return;

            if (characterController.isGrounded)
                verticalVelocity = -1f;
            else
                verticalVelocity += Physics.gravity.y * Time.deltaTime;

            characterController.Move(Vector3.up * (verticalVelocity * Time.deltaTime));
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private void OnDisable()
        {
            // Avoid retaining a stale player Transform across pooling/despawn.
            CurrentTarget = null;
        }
    }
}