////////////////////////////////////////////////////////////////////////////////////////
// Unturned-Better project extension.
// Humanized firing decisions for mobile NPC weapon adapters.
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Produces humanized shot timing and aim decisions for a mobile NPC.
    /// A game-specific weapon adapter must execute the returned shot through the
    /// normal Unturned weapon, effects, ammunition, and damage systems.
    /// This component never applies damage by itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCCombatProfile : MonoBehaviour
    {
        [Header("Reaction")]
        [SerializeField, Min(0f)] private float minimumReactionDelay = 0.25f;
        [SerializeField, Min(0f)] private float maximumReactionDelay = 0.85f;

        [Header("Accuracy")]
        [SerializeField, Range(0.05f, 1f)] private float baseAccuracy = 0.72f;
        [SerializeField, Range(0f, 1f)] private float distancePenalty = 0.62f;
        [SerializeField, Range(0f, 1f)] private float movementPenalty = 0.24f;
        [SerializeField, Range(0f, 1f)] private float suppressionPenalty = 0.38f;
        [SerializeField, Min(0f)] private float aimHeight = 1.15f;
        [SerializeField, Min(0f)] private float missSpreadPerMeter = 0.035f;

        [Header("Fire cadence")]
        [SerializeField, Min(0.02f)] private float minimumShotInterval = 0.16f;
        [SerializeField, Min(0.02f)] private float maximumShotInterval = 0.28f;
        [SerializeField, Min(1)] private int minimumBurstSize = 2;
        [SerializeField, Min(1)] private int maximumBurstSize = 5;
        [SerializeField, Min(0f)] private float minimumBurstPause = 0.45f;
        [SerializeField, Min(0f)] private float maximumBurstPause = 1.1f;

        private Transform engagedTarget;
        private float nextShotTime;
        private int shotsRemainingInBurst;
        private bool waitingForBurstPause;

        public Transform EngagedTarget => engagedTarget;
        public bool IsReadyToFire => engagedTarget != null && Time.time >= nextShotTime;

        /// <summary>Starts a new engagement and adds a variable human reaction delay.</summary>
        public void BeginEngagement(Transform target)
        {
            if (target == null)
            {
                EndEngagement();
                return;
            }

            if (engagedTarget == target)
                return;

            engagedTarget = target;
            shotsRemainingInBurst = RollBurstSize();
            waitingForBurstPause = false;
            nextShotTime = Time.time + Random.Range(
                Mathf.Min(minimumReactionDelay, maximumReactionDelay),
                Mathf.Max(minimumReactionDelay, maximumReactionDelay));
        }

        public void EndEngagement()
        {
            engagedTarget = null;
            shotsRemainingInBurst = 0;
            waitingForBurstPause = false;
            nextShotTime = 0f;
        }

        /// <summary>
        /// Returns true when a shot may be attempted. hitTarget is probabilistic;
        /// aimPoint includes a miss offset when the NPC fails its accuracy roll.
        /// Call only from the authoritative server-side weapon adapter.
        /// </summary>
        public bool TryResolveShot(
            Transform target,
            Vector3 muzzlePosition,
            float effectiveRange,
            bool moving,
            bool underFire,
            out Vector3 aimPoint,
            out bool hitTarget)
        {
            aimPoint = Vector3.zero;
            hitTarget = false;

            if (target == null)
            {
                EndEngagement();
                return false;
            }

            if (engagedTarget != target)
                BeginEngagement(target);

            if (Time.time < nextShotTime)
                return false;

            if (waitingForBurstPause)
            {
                waitingForBurstPause = false;
                shotsRemainingInBurst = RollBurstSize();
            }

            Vector3 targetPoint = target.position + Vector3.up * aimHeight;
            float distance = Vector3.Distance(muzzlePosition, targetPoint);
            float normalizedDistance = effectiveRange > 0.01f
                ? Mathf.Clamp01(distance / effectiveRange)
                : 1f;

            float chance = baseAccuracy * (1f - normalizedDistance * distancePenalty);
            if (moving)
                chance *= 1f - movementPenalty;
            if (underFire)
                chance *= 1f - suppressionPenalty;
            chance = Mathf.Clamp(chance, 0.04f, 0.95f);

            hitTarget = Random.value <= chance;
            aimPoint = targetPoint;

            if (!hitTarget)
            {
                // Misses become more visible at range, while remaining bounded and
                // deterministic in meaning: the weapon adapter fires toward this point.
                float spread = Mathf.Max(0f, distance) * missSpreadPerMeter;
                Vector2 offset = Random.insideUnitCircle * spread;
                aimPoint += Vector3.right * offset.x + Vector3.up * offset.y;
            }

            shotsRemainingInBurst = Mathf.Max(0, shotsRemainingInBurst - 1);
            if (shotsRemainingInBurst == 0)
            {
                waitingForBurstPause = true;
                nextShotTime = Time.time + Random.Range(
                    Mathf.Min(minimumBurstPause, maximumBurstPause),
                    Mathf.Max(minimumBurstPause, maximumBurstPause));
            }
            else
            {
                nextShotTime = Time.time + Random.Range(
                    Mathf.Min(minimumShotInterval, maximumShotInterval),
                    Mathf.Max(minimumShotInterval, maximumShotInterval));
            }

            return true;
        }

        private int RollBurstSize()
        {
            int min = Mathf.Max(1, Mathf.Min(minimumBurstSize, maximumBurstSize));
            int max = Mathf.Max(min, Mathf.Max(minimumBurstSize, maximumBurstSize));
            return Random.Range(min, max + 1);
        }

        private void OnDisable()
        {
            EndEngagement();
        }
    }
}
