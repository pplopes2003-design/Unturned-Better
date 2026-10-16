////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// First A-Life-inspired *online* job arbitration layer. Unlike a random roaming
    /// roll, the job is selected from facts actually owned by THIS NPC (health,
    /// carried items, needs and group settlement). No omniscient world knowledge,
    /// background teleportation, simulated pickup or hidden player information.
    ///
    /// Combat, ordered tactical travel and confirmed loot remain owned by existing
    /// components and take priority over the low-urgency routine job.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCBrainDriver))]
    public sealed class MobileNPCAutonomyPlanner : MonoBehaviour
    {
        public enum Job { Patrol, Scavenge, GuardSettlement, Recover }

        [SerializeField, Range(2f, 12f)] private float evaluationInterval = 5f;
        [SerializeField, Range(10f, 90f)] private float minimumJobCommitment = 27f;
        [SerializeField, Range(4f, 28f)] private float switchAdvantage = 16f;

        private MobileNPCBrainDriver brain;
        private MobileNPCInventory inventory;
        private MobileNPCSurvivalNeeds needs;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCGroupMember group;
        private MobileNPCMedicalBehavior medicalBehavior;
        private MobileNPCInjurySystem injuries;

        private Job currentJob = Job.Patrol;
        private float nextEvaluationTime;
        private float jobCommittedUntil;
        private float recoveryNotBefore;
        private float scavengeNotBefore;
        private float guardNotBefore;
        private float guardShiftEndsAt;
        private float personalityExploreBias;
        private float personalityGuardBias;
        private int consecutiveFailedScavengeWaypoints;
        private Vector3 lastBlockedWaypoint;
        private float blockedWaypointExpiresAt;
        private bool hasBlockedWaypoint;
        private bool hasExplorationSectorLease;
        private bool initialized;

        public Job CurrentJob => currentJob;

        private void Awake()
        {
            brain = GetComponent<MobileNPCBrainDriver>();
            inventory = GetComponent<MobileNPCInventory>();
            needs = GetComponent<MobileNPCSurvivalNeeds>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            group = GetComponent<MobileNPCGroupMember>();
            medicalBehavior = GetComponent<MobileNPCMedicalBehavior>();
            injuries = GetComponent<MobileNPCInjurySystem>();
            // Small independent preferences. These are not permanent identity traits.
            personalityExploreBias = Random.Range(-7f, 8f);
            personalityGuardBias = Random.Range(-6f, 7f);
            nextEvaluationTime = Time.time + Random.Range(0.2f, 2.5f);
        }

        /// <summary>Only called by the motor while it is free of a brain/tactical goal.</summary>
        public Job EvaluateRoutineServer()
        {
            if (!Provider.isServer || !Level.isLoaded || (vitals != null && vitals.IsDead))
                return currentJob;
            if (Time.time < nextEvaluationTime)
                return currentJob;
            nextEvaluationTime = Time.time + Mathf.Max(2f, evaluationInterval)
                + Random.Range(0f, 1.5f);

            // Finish a timed recovery BEFORE recomputing the health utility.
            // Otherwise a wounded NPC can immediately renew Recover forever,
            // despite the intended short rest and its cooldown.
            if (currentJob == Job.Recover && initialized
                && Time.time >= jobCommittedUntil)
            {
                recoveryNotBefore = Time.time + 36f;
                currentJob = Job.Patrol;
                jobCommittedUntil = Time.time + 12f;
            }

            // Threat perception and tactical travel exclusively own movement.
            // Never rotate group jobs while a confirmed danger/sensory task
            // is still in progress.
            if (brain == null || brain.Target != null
                || brain.CurrentGoal != MobileNPCTactics.Goal.None
                || brain.CurrentState != MobileNPCBrain.State.Idle)
                return currentJob;

            // A-Life duty shifts: a guard must occasionally hand over its
            // physical reserved post, permitting another living squadmate to
            // guard while this individual scouts/restocks. A tactical threat
            // above would defer the rotation rather than abandon defense.
            if (currentJob == Job.GuardSettlement && initialized
                && Time.time >= guardShiftEndsAt && guardShiftEndsAt > 0f)
            {
                if (group != null) group.ReleaseGuardPostServer();
                guardNotBefore = Time.time + Random.Range(35f, 65f);
                currentJob = Job.Patrol;
                jobCommittedUntil = Time.time + 10f;
                guardShiftEndsAt = 0f;
            }

            float health = vitals != null ? vitals.HealthFraction : 1f;
            float thirst = needs != null ? needs.ThirstFraction : 1f;
            float hunger = needs != null ? needs.HungerFraction : 1f;
            bool hasSpace = inventory != null && inventory.HasSpace;
            bool hasGun = inventory != null && inventory.CountItemType(EItemType.GUN) > 0;
            bool lacksFood = needs != null && needs.NeedsFood
                && inventory != null && inventory.CountItemType(EItemType.FOOD) == 0;
            bool lacksWater = needs != null && needs.NeedsWater
                && inventory != null && inventory.CountItemType(EItemType.WATER) == 0;
            bool lacksMedical = medicalBehavior != null && medicalBehavior.NeedsMedicalSupplies;
            bool bleeding = injuries != null && injuries.CurrentBleeding
                != MobileNPCInjurySystem.BleedingSeverity.None;
            bool inGroup = group != null && group.GroupId > 0;
            bool hasSettlement = inGroup && group.HasSettlement;

            // A-Life needs utility is tied to items the survivor ACTUALLY
            // carries. Being thirsty WITH water is not a reason to abandon a
            // base guard post: SurvivalNeeds will drink it directly.
            float supplyUrgency = (lacksWater
                ? 38f + Mathf.Max(0f, 0.70f - thirst) * 135f : 0f)
                + (lacksFood ? 25f + Mathf.Max(0f, 0.70f - hunger) * 110f : 0f)
                + (lacksMedical ? (bleeding ? 118f : 85f) : 0f);
            float patrol = 35f + personalityExploreBias;
            float scavenge = hasSpace && Time.time >= scavengeNotBefore
                ? 18f + supplyUrgency + (!hasGun ? 16f : 0f)
                : -1000f;
            float guard = hasSettlement && Time.time >= guardNotBefore
                && group.HasAvailableGuardPostServer
                ? 53f + personalityGuardBias + (group.CanPlanSettlement ? 7f : 0f)
                    - (lacksWater || lacksFood || lacksMedical ? 35f : 0f)
                : -1000f;
            float recover = Time.time >= recoveryNotBefore
                ? 8f + (1f - health) * 100f
                    + (vitals != null && Time.time - vitals.LastDamageTime < 9f ? 17f : 0f)
                : -1000f;
            // Resting while actively bleeding and without a dressing is a
            // deadly loop. Search personally known/visible REAL supplies
            // instead; no magical heal, loot or safe teleport is granted.
            if (lacksMedical && bleeding && hasSpace)
                recover = Mathf.Min(recover, 11f);

            Job best = Job.Patrol;
            float bestScore = patrol;
            if (scavenge > bestScore) { best = Job.Scavenge; bestScore = scavenge; }
            if (guard > bestScore) { best = Job.GuardSettlement; bestScore = guard; }
            if (recover > bestScore) { best = Job.Recover; bestScore = recover; }

            float currentScore = currentJob == Job.Scavenge ? scavenge
                : currentJob == Job.GuardSettlement ? guard
                : currentJob == Job.Recover ? recover : patrol;
            bool emergency = health < 0.30f && best == Job.Recover
                || best == Job.Scavenge && (lacksMedical && bleeding
                    || lacksWater && thirst < 0.27f
                    || lacksFood && hunger < 0.25f);
            if (initialized && best != currentJob && Time.time < jobCommittedUntil
                && !emergency && bestScore < currentScore + switchAdvantage)
                return currentJob;

            if (!initialized || best != currentJob)
            {
                if (group != null)
                {
                    if (currentJob == Job.GuardSettlement)
                        group.ReleaseGuardPostServer();
                    // Switching strategic routine never owns the previous
                    // scout's location or an interrupted supply-search lease.
                    ReleaseExplorationSectorServer(false, false);
                }
                currentJob = best;
                initialized = true;
                jobCommittedUntil = Time.time + Mathf.Max(10f, minimumJobCommitment);
                if (currentJob == Job.Recover)
                {
                    // Recover is a short pause, not permanent AI paralysis.
                    jobCommittedUntil = Time.time + Random.Range(6f, 11f);
                }
                else if (currentJob == Job.GuardSettlement)
                {
                    // Duty shift ends only while idle, not in the middle
                    // of defending the settlement against a known enemy.
                    guardShiftEndsAt = Time.time + Random.Range(90f, 155f);
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[MobileNPCAutonomyPlanner] " + name + " routine=" + currentJob
                    + " hp=" + health.ToString("F2") + " food=" + hunger.ToString("F2")
                    + " water=" + thirst.ToString("F2") + " pack="
                    + (inventory != null ? inventory.UsedSlots : 0));
#endif
            }

            return currentJob;
        }

        /// <summary>Provide a job profile, not a magical or unvalidated destination.</summary>
        public void GetRoamingProfileServer(Vector3 home, out Vector3 anchor,
            out float minStep, out float maxStep, out float structurePreference)
        {
            anchor = home;
            minStep = 24f;
            maxStep = 72f;
            structurePreference = 0.55f;
            Job job = EvaluateRoutineServer();
            if (job == Job.GuardSettlement)
            {
                if (group != null && group.TryReserveGuardPostServer(out Vector3 post))
                {
                    anchor = post;
                    minStep = 3f;
                    maxStep = 8f;
                    structurePreference = 0f;
                }
                else
                {
                    // No reachable, physically verified or free guard job.
                    // Return to ordinary patrol; another member may claim a
                    // post later when a guard dies or changes duties.
                    if (group != null)
                        group.ReleaseGuardPostServer();
                    currentJob = Job.Patrol;
                    guardNotBefore = Time.time + 20f;
                    jobCommittedUntil = Time.time + 12f;
                    nextEvaluationTime = Time.time + 1f;
                }
            }
            else if (job == Job.Scavenge)
            {
                minStep = 15f;
                maxStep = 48f;
                structurePreference = 0.88f;
            }
        }


        private void OnDisable()
        {
            if (group != null)
            {
                group.ReleaseGuardPostServer();
                ReleaseExplorationSectorServer(false, false);
            }
        }

        private void OnDestroy()
        {
            if (group != null)
            {
                group.ReleaseGuardPostServer();
                ReleaseExplorationSectorServer(false, false);
            }
        }

        /// <summary>
        /// The motor first validates an authentic structure/ground destination.
        /// This method only arbitrates whether a teammate is already checking
        /// the same location; it never discovers or manufactures POIs.
        /// </summary>
        public bool TryClaimObservedExplorationSectorServer(Vector3 site)
        {
            if (!Provider.isServer || vitals != null && vitals.IsDead)
                return false;
            if (currentJob != Job.Patrol && currentJob != Job.Scavenge)
                return false;
            bool claimed = group == null || group.TryClaimActivitySectorServer(site, 75f);
            hasExplorationSectorLease = claimed;
            return claimed;
        }

        public void ReleaseExplorationSectorServer(bool visited, bool blocked)
        {
            if (!hasExplorationSectorLease)
                return;
            hasExplorationSectorLease = false;
            if (group != null)
                group.ReleaseActivitySectorServer(visited, blocked);
        }

        /// <summary>Failed routes are learned only from actual physical stalls.</summary>
        public bool IsCandidateAllowedServer(Vector3 candidate)
        {
            if (!Provider.isServer || !hasBlockedWaypoint
                || Time.time >= blockedWaypointExpiresAt)
                return true;
            Vector3 difference = candidate - lastBlockedWaypoint;
            difference.y = 0f;
            return difference.sqrMagnitude > 12f * 12f;
        }

        public void ReportRoamBlockedServer(Vector3 attemptedWaypoint)
        {
            if (!Provider.isServer)
                return;
            ReleaseExplorationSectorServer(false, true);
            lastBlockedWaypoint = attemptedWaypoint;
            blockedWaypointExpiresAt = Time.time + 35f;
            hasBlockedWaypoint = true;
            if (currentJob == Job.Scavenge && ++consecutiveFailedScavengeWaypoints >= 3)
            {
                // No endless re-selection of the same inaccessible loot district.
                scavengeNotBefore = Time.time + 42f;
                consecutiveFailedScavengeWaypoints = 0;
                nextEvaluationTime = Time.time;
            }
        }

        public void ReportRoamReachedServer()
        {
            if (!Provider.isServer)
                return;
            ReleaseExplorationSectorServer(true, false);
            consecutiveFailedScavengeWaypoints = 0;
            if (currentJob == Job.Recover)
                recoveryNotBefore = Time.time + 36f;
        }
    }
}
