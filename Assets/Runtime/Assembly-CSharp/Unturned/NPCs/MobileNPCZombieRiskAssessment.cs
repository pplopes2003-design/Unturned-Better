////////////////////////////////////////////////////////////////////////////////////////
// Local zombie risk based on short-range personal sight/contact, not a director feed. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    [DisallowMultipleComponent]
    public sealed class MobileNPCZombieRiskAssessment : MonoBehaviour
    {
        private const int MaximumKnown = 6;
        private readonly Vector3[] observed = new Vector3[MaximumKnown];
        private readonly float[] expires = new float[MaximumKnown];
        private readonly RaycastHit[] rayHits = new RaycastHit[12];
        private MobileNPCBrainDriver brain;
        private MobileNPCHumanoidPerception perception;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCHumanoidRangedCombat ranged;
        private float nextScanAt;
        private float nextRetreatAt;
        private static float nextSharedOpticalAt;

        private void Awake()
        {
            brain = GetComponent<MobileNPCBrainDriver>();
            perception = GetComponent<MobileNPCHumanoidPerception>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            ranged = GetComponent<MobileNPCHumanoidRangedCombat>();
            nextScanAt = Time.time + Random.Range(0.2f, 0.9f);
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded
                || vitals == null || vitals.IsDead || perception == null
                || Time.time < nextScanAt)
                return;
            nextScanAt = Time.time + Random.Range(0.55f, 0.85f);

            Zombie visible = perception.VisibleZombie;
            if (visible != null && !visible.isDead)
                RememberObservedServer(visible.transform.position);
            // Other nearby zombies can be heard at striking distance; farther
            // ones require an actual unobstructed short local line of sight.
            List<Zombie> zombies = ZombieManager.AllZombies;
            if (zombies != null)
            {
                Zombie nearest = null;
                float nearestSqr = 15f * 15f;
                for (int i = 0; i < zombies.Count; ++i)
                {
                    Zombie candidate = zombies[i];
                    if (candidate == null || candidate.isDead
                        || !candidate.gameObject.activeInHierarchy)
                        continue;
                    Vector3 delta = candidate.transform.position - transform.position;
                    float sq = delta.sqrMagnitude;
                    if (sq < nearestSqr)
                    {
                        nearest = candidate;
                        nearestSqr = sq;
                    }
                    // Contact-range awareness does not require scanning
                    // through distant walls or using enemies outside hearing.
                    if (sq <= 3.7f * 3.7f && Mathf.Abs(delta.y) < 2.5f)
                        RememberObservedServer(candidate.transform.position);
                }
                if (nearest != null && nearestSqr > 3.7f * 3.7f
                    && Time.time >= nextSharedOpticalAt)
                {
                    nextSharedOpticalAt = Time.time + 0.075f;
                    if (CanReallySeeServer(nearest))
                        RememberObservedServer(nearest.transform.position);
                }
            }

            // All decisions below depend on this NPC's own recent sightings
            // and close-range hearing; unknown hordes behind buildings are
            // never injected into its knowledge.
            float localRisk = GetKnownRiskAtServer(transform.position);
            bool hasEffectiveGun = ranged != null
                && ranged.SelectedGunAsset != null && ranged.RoundsRemaining > 0;
            bool overwhelmed = localRisk >= (hasEffectiveGun ? 1.85f : 0.85f)
                || localRisk >= 0.7f && vitals.HealthFraction < 0.45f;
            if (!overwhelmed || brain == null || Time.time < nextRetreatAt
                || brain.CurrentGoal == MobileNPCTactics.Goal.Retreat
                || brain.CurrentGoal == MobileNPCTactics.Goal.SeekCover)
                return;
            Vector3 danger = GetNearestKnownThreatServer();
            if (danger == Vector3.zero)
                return;
            nextRetreatAt = Time.time + 5f;
            // An attempted withdrawal must still pass the motor's real
            // local path test. If it fails, existing combat remains active.
            if (brain.TryStartLocalZombieWithdrawalServer(danger, this))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MobileNPCZombieRisk] RETREAT npc=" + name
                    + " risk=" + localRisk.ToString("F2")
                    + " armed=" + hasEffectiveGun, this);
#endif
            }
        }

        private bool CanReallySeeServer(Zombie zombie)
        {
            Vector3 eye = transform.position + Vector3.up * 1.3f;
            Vector3 target = zombie.transform.position + Vector3.up * 1.05f;
            Vector3 delta = target - eye;
            float distance = delta.magnitude;
            if (distance < 0.01f || distance > 15f)
                return false;
            int count = Physics.RaycastNonAlloc(eye, delta / distance,
                rayHits, distance, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (count >= rayHits.Length)
                return false; // saturation is uncertain, never clairvoyant
            float nearest = float.PositiveInfinity;
            Collider closest = null;
            for (int i = 0; i < count; ++i)
            {
                Collider col = rayHits[i].collider;
                if (col == null || col.transform.IsChildOf(transform))
                    continue;
                if (rayHits[i].distance < nearest)
                {
                    closest = col;
                    nearest = rayHits[i].distance;
                }
            }
            // World collisions before the zombie block the sighting. A clear
            // ray also counts if the zombie has no collider in this mask.
            return closest == null || closest.GetComponentInParent<Zombie>() == zombie;
        }

        private void RememberObservedServer(Vector3 pos)
        {
            if (!Provider.isServer || !Level.checkSafeIncludingClipVolumes(pos))
                return;
            int slot = -1;
            float soonestExpiry = float.PositiveInfinity;
            for (int i = 0; i < MaximumKnown; ++i)
            {
                if (Time.time < expires[i]
                    && (observed[i] - pos).sqrMagnitude <= 2.5f * 2.5f)
                {
                    slot = i;
                    break;
                }
                if (expires[i] < soonestExpiry)
                {
                    soonestExpiry = expires[i];
                    slot = i;
                }
            }
            if (slot < 0) return;
            observed[slot] = pos;
            expires[slot] = Time.time + 3.0f;
        }

        public float GetKnownRiskAtServer(Vector3 point)
        {
            if (!Provider.isServer || !Level.isLoaded)
                return 0f;
            float threat = 0f;
            for (int i = 0; i < MaximumKnown; ++i)
            {
                if (Time.time >= expires[i] || Mathf.Abs(point.y - observed[i].y) > 3f)
                    continue;
                Vector3 delta = observed[i] - point;
                delta.y = 0f;
                float dist = delta.magnitude;
                if (dist > 11f) continue;
                threat += Mathf.Lerp(1.2f, 0f, dist / 11f);
                if (dist < 3.5f)
                    threat += 0.25f;
            }
            return threat;
        }

        public Vector3 GetNearestKnownThreatServer()
        {
            float nearest = float.PositiveInfinity;
            Vector3 result = Vector3.zero;
            for (int i = 0; i < MaximumKnown; ++i)
            {
                if (Time.time >= expires[i]) continue;
                float sq = (observed[i] - transform.position).sqrMagnitude;
                if (sq < nearest)
                {
                    nearest = sq;
                    result = observed[i];
                }
            }
            return result;
        }
    }
}
