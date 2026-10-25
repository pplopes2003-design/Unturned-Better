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
        private const int MaximumKnown = 8;
        // Cache the three nearest locally present candidates before optical checks.
        // The old single-nearest selection failed when a zombie was behind a wall
        // while a second, visible zombie was walking straight toward the NPC.
        private readonly Zombie[] nearestCandidates = new Zombie[3];
        private readonly float[] nearestDistances = new float[3];
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
            // Bounded personal hazard scan. Build a nearest-three shortlist
            // without allocating or raycasting every zombie in the map.
            // Close zombies behind a wall are NOT automatically known.
            List<Zombie> zombies = ZombieManager.AllZombies;
            if (zombies != null)
            {
                for (int k = 0; k < nearestCandidates.Length; ++k)
                {
                    nearestCandidates[k] = null;
                    nearestDistances[k] = 15f * 15f;
                }
                for (int i = 0; i < zombies.Count; ++i)
                {
                    Zombie candidate = zombies[i];
                    if (candidate == null || candidate.isDead
                        || !candidate.gameObject.activeInHierarchy)
                        continue;
                    Vector3 delta = candidate.transform.position - transform.position;
                    if (Mathf.Abs(delta.y) >= 3.0f)
                        continue;
                    float sq = delta.sqrMagnitude;
                    for (int j = 0; j < nearestCandidates.Length; ++j)
                    {
                        if (sq >= nearestDistances[j])
                            continue;
                        for (int k = nearestCandidates.Length - 1; k > j; --k)
                        {
                            nearestCandidates[k] = nearestCandidates[k - 1];
                            nearestDistances[k] = nearestDistances[k - 1];
                        }
                        nearestCandidates[j] = candidate;
                        nearestDistances[j] = sq;
                        break;
                    }
                }
                // The optical budget is spread globally; actors in a crowd
                // receive independent sightings, not a global zombie list.
                // Existing perception.VisibleZombie still updates without
                // consuming these supplementary checks.
                // An NPC surveying an empty stretch of road must NOT
                // consume the shared raycast slot needed by another actor
                // currently approaching a zombie group.
                if (nearestCandidates[0] != null && Time.time >= nextSharedOpticalAt)
                {
                    nextSharedOpticalAt = Time.time + 0.020f;
                    for (int i = 0; i < nearestCandidates.Length; ++i)
                    {
                        Zombie candidate = nearestCandidates[i];
                        if (candidate == null)
                            continue;
                        if (CanReallySeeServer(candidate))
                            RememberObservedServer(candidate.transform.position);
                    }
                }
            }

            // All decisions below depend on this NPC's own recent sightings
            // and close-range hearing; unknown hordes behind buildings are
            // never injected into its knowledge.
            float localRisk = GetKnownRiskAtServer(transform.position);
            bool hasEffectiveGun = ranged != null
                && ranged.SelectedGunAsset != null && ranged.RoundsRemaining > 0;
            // Combat Only means dangerous survivors, not suicidal knife rushes.
            // Treat a personally confirmed cluster as a reason to make space,
            // including when the rifle is loaded; keep all route tests native.
            float crowdThreshold = MobileNPCSettings.CombatOnly
                ? (hasEffectiveGun ? 1.15f : 0.55f)
                : (hasEffectiveGun ? 1.85f : 0.85f);
            bool overwhelmed = localRisk >= crowdThreshold
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
            expires[slot] = Time.time + 4.5f;
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
