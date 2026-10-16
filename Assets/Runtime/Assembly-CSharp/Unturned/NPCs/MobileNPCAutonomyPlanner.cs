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
        // Four finite first-hand scout outcomes per survivor. Group work-sector
        // leases expire independently; solo survivors also avoid repeating an
        // already visited or genuinely unreachable district every few seconds.
        private const int RecentScoutingSiteCapacity = 4;
        private readonly Vector3[] recentScoutingSites = new Vector3[RecentScoutingSiteCapacity];
        private readonly float[] recentScoutingSiteExpiresAt = new float[RecentScoutingSiteCapacity];
        private int nextRecentScoutingSiteSlot;
        private bool initialized;
        // Confirmed local scans, not mere arrival at candidate waypoints.
        private int consecutiveEmptyInspections;
        private int confirmedInspectionScans;
        private int confirmedWorldPickups;

        [System.Serializable]
        public sealed class RoutineSnapshot
        {
            public int job;
            public float exploreBias;
            public float guardBias;
            public float commitmentRemaining;
            public float guardShiftRemaining;
            public float guardCooldownRemaining;
            public float scavengeCooldownRemaining;
            public float recoveryCooldownRemaining;
            public int failedScavengeWaypoints;
            public int emptyInspections;
            public int inspectionScans;
            public int realPickups;
            public bool hasBlockedWaypoint;
            public Vector3 blockedWaypoint;
            public float blockedWaypointRemaining;
            // Optional for old save files. Time.time deadlines are NEVER saved.
            public Vector3[] recentScoutingSites;
            public float[] recentScoutingRemaining;
        }

        /// <summary>
        /// Save personal strategic intention and bounded remaining durations,
        /// not Time.time timestamps (which are invalid across Unity sessions).
        /// No travel/guard reservation is serialized as if it were still valid.
        /// </summary>
        public RoutineSnapshot ExportSnapshotServer()
        {
            if (!Provider.isServer || vitals != null && vitals.IsDead)
                return null;
            float now = Time.time;
            return new RoutineSnapshot
            {
                job = (int)currentJob,
                exploreBias = personalityExploreBias,
                guardBias = personalityGuardBias,
                commitmentRemaining = Mathf.Clamp(jobCommittedUntil - now, 0f, 90f),
                guardShiftRemaining = Mathf.Clamp(guardShiftEndsAt - now, 0f, 160f),
                guardCooldownRemaining = Mathf.Clamp(guardNotBefore - now, 0f, 120f),
                scavengeCooldownRemaining = Mathf.Clamp(scavengeNotBefore - now, 0f, 120f),
                recoveryCooldownRemaining = Mathf.Clamp(recoveryNotBefore - now, 0f, 120f),
                failedScavengeWaypoints = Mathf.Clamp(consecutiveFailedScavengeWaypoints, 0, 2),
                emptyInspections = Mathf.Clamp(consecutiveEmptyInspections, 0, 3),
                inspectionScans = Mathf.Clamp(confirmedInspectionScans, 0, 1000000),
                realPickups = Mathf.Clamp(confirmedWorldPickups, 0, 1000000),
                hasBlockedWaypoint = hasBlockedWaypoint && now < blockedWaypointExpiresAt,
                blockedWaypoint = lastBlockedWaypoint,
                blockedWaypointRemaining = Mathf.Clamp(blockedWaypointExpiresAt - now, 0f, 38f),
                recentScoutingSites = (Vector3[])recentScoutingSites.Clone(),
                recentScoutingRemaining = new float[]
                {
                    Remaining(recentScoutingSiteExpiresAt[0] - now, 120f),
                    Remaining(recentScoutingSiteExpiresAt[1] - now, 120f),
                    Remaining(recentScoutingSiteExpiresAt[2] - now, 120f),
                    Remaining(recentScoutingSiteExpiresAt[3] - now, 120f)
                }
            };
        }

        /// <summary>
        /// Rebuild the saved individual's utility context after inventory,
        /// needs and group were restored. A task is an intention, NOT an old
        /// tactical route: fresh world/physics checks are still obligatory.
        /// Old saves lacking RoutineSnapshot keep the new NPC defaults.
        /// </summary>
        public void RestoreSnapshotServer(RoutineSnapshot snapshot)
        {
            if (!Provider.isServer || snapshot == null || vitals != null && vitals.IsDead)
                return;
            if (snapshot.job < (int)Job.Patrol || snapshot.job > (int)Job.Recover)
                return;
            if (!IsFinite(snapshot.exploreBias) || !IsFinite(snapshot.guardBias))
                return;

            personalityExploreBias = Mathf.Clamp(snapshot.exploreBias, -7f, 8f);
            personalityGuardBias = Mathf.Clamp(snapshot.guardBias, -6f, 7f);
            currentJob = (Job)snapshot.job;
            // A guard job has no meaning when its group/base no longer exists.
            if (currentJob == Job.GuardSettlement
                && (group == null || !group.HasSettlement))
                currentJob = Job.Patrol;

            float now = Time.time;
            jobCommittedUntil = now + Remaining(snapshot.commitmentRemaining, 90f);
            guardNotBefore = now + Remaining(snapshot.guardCooldownRemaining, 120f);
            scavengeNotBefore = now + Remaining(snapshot.scavengeCooldownRemaining, 120f);
            recoveryNotBefore = now + Remaining(snapshot.recoveryCooldownRemaining, 120f);
            guardShiftEndsAt = currentJob == Job.GuardSettlement
                ? now + Mathf.Max(2f, Remaining(snapshot.guardShiftRemaining, 160f))
                : 0f;
            consecutiveFailedScavengeWaypoints = Mathf.Clamp(snapshot.failedScavengeWaypoints, 0, 2);
            consecutiveEmptyInspections = Mathf.Clamp(snapshot.emptyInspections, 0, 3);
            confirmedInspectionScans = Mathf.Clamp(snapshot.inspectionScans, 0, 1000000);
            confirmedWorldPickups = Mathf.Clamp(snapshot.realPickups, 0, 1000000);

            bool validBlockedPosition = IsFinite(snapshot.blockedWaypoint.x)
                && IsFinite(snapshot.blockedWaypoint.y)
                && IsFinite(snapshot.blockedWaypoint.z)
                && Level.checkSafeIncludingClipVolumes(snapshot.blockedWaypoint);
            hasBlockedWaypoint = snapshot.hasBlockedWaypoint && validBlockedPosition
                && Remaining(snapshot.blockedWaypointRemaining, 38f) > 0f;
            lastBlockedWaypoint = hasBlockedWaypoint ? snapshot.blockedWaypoint : Vector3.zero;
            blockedWaypointExpiresAt = hasBlockedWaypoint
                ? now + Remaining(snapshot.blockedWaypointRemaining, 38f) : 0f;
            // Old saves omit these optional arrays. Revalidate each remembered
            // physical place in this map, rather than trusting stale geometry
            // or old Time.time values. These are personal experiences only.
            for (int i = 0; i < RecentScoutingSiteCapacity; ++i)
                recentScoutingSiteExpiresAt[i] = 0f;
            if (snapshot.recentScoutingSites != null
                && snapshot.recentScoutingRemaining != null)
            {
                int count = Mathf.Min(RecentScoutingSiteCapacity,
                    Mathf.Min(snapshot.recentScoutingSites.Length,
                        snapshot.recentScoutingRemaining.Length));
                for (int i = 0; i < count; ++i)
                {
                    Vector3 site = snapshot.recentScoutingSites[i];
                    float remaining = Remaining(snapshot.recentScoutingRemaining[i], 120f);
                    if (remaining <= 0f || !IsFinite(site.x) || !IsFinite(site.y)
                        || !IsFinite(site.z) || !Level.checkSafeIncludingClipVolumes(site))
                        continue;
                    recentScoutingSites[i] = site;
                    recentScoutingSiteExpiresAt[i] = now + remaining;
                }
            }
            nextRecentScoutingSiteSlot = 0;
            // No old group sector/guard reservation, movement route, combat
            // target or pre-loading Time.time scheduling survives the reload.
            hasExplorationSectorLease = false;
            initialized = true;
            nextEvaluationTime = now + Random.Range(0.5f, 2.0f);
        }

        private static float Remaining(float value, float maximum)
        {
            return IsFinite(value) ? Mathf.Clamp(value, 0f, maximum) : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

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

            // Components are appended to the inactive runtime template in
            // stages. Never assume MedicalBehavior already existed at Awake;
            // resolve once after spawning so the shortage signal actually
            // reaches this strategic utility evaluator.
            if (medicalBehavior == null)
                medicalBehavior = GetComponent<MobileNPCMedicalBehavior>();
            if (injuries == null)
                injuries = GetComponent<MobileNPCInjurySystem>();

            float health = vitals != null ? vitals.HealthFraction : 1f;
            float thirst = needs != null ? needs.ThirstFraction : 1f;
            float hunger = needs != null ? needs.HungerFraction : 1f;
            bool hasSpace = inventory != null && inventory.HasSpace;
            bool hasGun = inventory != null && inventory.CountItemType(EItemType.GUN) > 0;
            MobileNPCHumanoidRangedCombat ranged = GetComponent<MobileNPCHumanoidRangedCombat>();
            bool combatMode = MobileNPCSettings.CombatMode;
            bool needsArms = combatMode && (!hasGun || ranged == null
                || ranged.SelectedGunAsset == null || ranged.RoundsRemaining <= 0);
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
            // Previously patrol (35+) beat normal scavenge (18+), so a
            // healthy, armed NPC born IN a town never chose to inspect
            // buildings. Investigation of REAL loot must be a standard
            // survival job, not only a starvation emergency.
            float scavenge = hasSpace && Time.time >= scavengeNotBefore
                ? 49f + supplyUrgency + (!hasGun ? 23f : 0f)
                    + (inGroup && !hasSettlement ? 5f : 0f)
                : -1000f;
            float guard = hasSettlement && Time.time >= guardNotBefore
                && group.HasAvailableGuardPostServer
                ? 53f + personalityGuardBias + (group.CanPlanSettlement ? 7f : 0f)
                    - (lacksWater || lacksFood || lacksMedical ? 35f : 0f)
                : -1000f;
            if (combatMode)
            {
                // No hunger/water utilities in this mode: reward real weapons
                // and compatible ammunition, then defend validated group posts
                // and patrol existing regional anchors. No imaginary invasions.
                patrol += 19f;
                if (hasSpace && Time.time >= scavengeNotBefore)
                    scavenge += needsArms ? 83f : 14f;
                if (guard > -100f)
                    guard += 30f;
            }
            float recover = Time.time >= recoveryNotBefore
                ? 8f + (1f - health) * 100f
                    + (vitals != null && Time.time - vitals.LastDamageTime < 9f ? 17f : 0f)
                : -1000f;
            // Resting while actively bleeding and without a dressing is a
            // deadly loop. Search personally known/visible REAL supplies
            // instead; no magical heal, loot or safe teleport is granted.
            if (lacksMedical && bleeding && hasSpace)
                recover = Mathf.Min(recover, 11f);

            // Profession is a durable strategic tendency, not a scripted
            // animation or a replacement for needs/actual world knowledge.
            // In particular, an unwounded urban survivor should search
            // buildings before selecting another unmotivated patrol loop.
            MobileNPCArchetypePolicy.Preset preset =
                MobileNPCArchetypePolicy.ForNPC(gameObject);
            switch (preset)
            {
                case MobileNPCArchetypePolicy.Preset.Survivor:
                    if (!combatMode) { scavenge += 36f; patrol -= 8f; guard -= 12f; }
                    break;
                case MobileNPCArchetypePolicy.Preset.Trader:
                    if (!combatMode)
                    {
                        // Traders are NOT all identical guards. The living
                        // group leader tends its real settlement, while
                        // ordinary members divide between patrol/security
                        // and expeditions according to their SAVED personal
                        // exploration bias. No fake shop/stock is created.
                        patrol -= 12f;
                        if (!hasSettlement)
                        {
                            scavenge += 37f; // earn supplies before a shop exists
                        }
                        else if (group != null && group.CanPlanSettlement)
                        {
                            guard += 62f; // seller/home steward
                            scavenge -= 16f;
                        }
                        else if (personalityExploreBias >= 0f)
                        {
                            scavenge += 49f; // traveling collector
                            guard -= 17f;
                        }
                        else
                        {
                            guard += 36f; // patrol/settlement security
                            scavenge -= 8f;
                        }
                    }
                    break;
                case MobileNPCArchetypePolicy.Preset.Raider:
                    patrol += LightingManager.isNighttime ? 24f : 12f;
                    scavenge += 9f;
                    guard -= 8f;
                    break;
                case MobileNPCArchetypePolicy.Preset.Military:
                    patrol += 27f;
                    guard += 22f;
                    if (!needsArms) scavenge -= 7f;
                    break;
            }

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
                    guardShiftEndsAt = Time.time + (combatMode
                        ? Random.Range(135f, 220f) : Random.Range(90f, 155f));
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
                if (group != null && group.HasSettlement && group.CanPlanSettlement
                    && MobileNPCArchetypePolicy.ForNPC(gameObject)
                        == MobileNPCArchetypePolicy.Preset.Trader)
                {
                    // Shop steward is NOT a perimeter guard: reserve no watch
                    // post. Stay by the group's real settlement anchor so
                    // collectors can reach the seller and transfer stock.
                    group.ReleaseGuardPostServer();
                    anchor = group.GroupAnchor;
                    minStep = 2f;
                    maxStep = 3.5f;
                    structurePreference = 0f;
                }
                else if (group != null && group.TryReserveGuardPostServer(out Vector3 post))
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
                // The motor still validates REAL architecture and reachable
                // floors. Presets change search distance, not loot spawning.
                MobileNPCArchetypePolicy.Preset preset =
                    MobileNPCArchetypePolicy.ForNPC(gameObject);
                minStep = preset == MobileNPCArchetypePolicy.Preset.Raider ? 7f : 5f;
                maxStep = preset == MobileNPCArchetypePolicy.Preset.Raider ? 32f
                    : preset == MobileNPCArchetypePolicy.Preset.Trader ? 28f : 22f;
                structurePreference = preset == MobileNPCArchetypePolicy.Preset.Raider
                    ? 0.8f : 1f;
            }
            else if (job == Job.Patrol)
            {
                MobileNPCArchetypePolicy.Preset preset =
                    MobileNPCArchetypePolicy.ForNPC(gameObject);
                switch (preset)
                {
                    case MobileNPCArchetypePolicy.Preset.Raider:
                        minStep = 20f; maxStep = 96f; structurePreference = 0.45f;
                        break;
                    case MobileNPCArchetypePolicy.Preset.Military:
                        minStep = 18f; maxStep = 88f; structurePreference = 0.55f;
                        break;
                    case MobileNPCArchetypePolicy.Preset.Trader:
                        if (group != null && group.HasSettlement
                            && (group.CanPlanSettlement
                                || inventory != null && inventory.FreeSlots <= 2))
                        {
                            // A seller remains near the REAL settlement, and
                            // a heavily loaded collector returns physically.
                            // Returning does not silently deposit items or
                            // grant them to teammates through group memory.
                            anchor = group.GroupAnchor;
                            minStep = 3f; maxStep = 9f;
                            structurePreference = 0f;
                        }
                        else
                        {
                            minStep = 6f; maxStep = 22f;
                            structurePreference = 0.85f;
                        }
                        break;
                    default:
                        minStep = 7f; maxStep = 30f; structurePreference = 0.9f;
                        break;
                }
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

        /// <summary>Reject recently experienced scout sectors, not arbitrary
        /// destinations of combat, radio, medicine or pursuit. Other floors do
        /// not become forbidden merely because XZ overlaps.</summary>
        public bool IsCandidateAllowedServer(Vector3 candidate)
        {
            if (!Provider.isServer || !IsFinite(candidate.x)
                || !IsFinite(candidate.y) || !IsFinite(candidate.z))
                return false;
            if (hasBlockedWaypoint && Time.time < blockedWaypointExpiresAt)
            {
                Vector3 blockedDifference = candidate - lastBlockedWaypoint;
                blockedDifference.y = 0f;
                if (blockedDifference.sqrMagnitude <= 12f * 12f)
                    return false;
            }
            for (int i = 0; i < RecentScoutingSiteCapacity; ++i)
            {
                if (Time.time >= recentScoutingSiteExpiresAt[i]
                    || Mathf.Abs(candidate.y - recentScoutingSites[i].y) > 2.5f)
                    continue;
                Vector3 difference = candidate - recentScoutingSites[i];
                difference.y = 0f;
                // A house can have several independent accessible rooms.
                // Scavenging records actual inspected positions with smaller
                // revisitation radii, while patrol still avoids whole sectors.
                float visitedRadius = currentJob == Job.Scavenge ? 5f : 18f;
                if (difference.sqrMagnitude <= visitedRadius * visitedRadius)
                    return false;
            }
            return true;
        }

        private void RememberScoutingOutcomeServer(Vector3 site, float cooldown)
        {
            if (!Provider.isServer || (currentJob != Job.Patrol && currentJob != Job.Scavenge)
                || !IsFinite(site.x) || !IsFinite(site.y) || !IsFinite(site.z)
                || !Level.checkSafeIncludingClipVolumes(site))
                return;
            // Refresh an already known district instead of flooding the
            // bounded history with adjacent waypoints from the same place.
            int slot = -1;
            for (int i = 0; i < RecentScoutingSiteCapacity; ++i)
            {
                if (Time.time >= recentScoutingSiteExpiresAt[i]
                    || Mathf.Abs(site.y - recentScoutingSites[i].y) > 2.5f)
                    continue;
                Vector3 difference = site - recentScoutingSites[i];
                difference.y = 0f;
                float sameSiteRadius = currentJob == Job.Scavenge ? 5f : 18f;
                if (difference.sqrMagnitude <= sameSiteRadius * sameSiteRadius)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                slot = nextRecentScoutingSiteSlot;
                nextRecentScoutingSiteSlot =
                    (nextRecentScoutingSiteSlot + 1) % RecentScoutingSiteCapacity;
            }
            recentScoutingSites[slot] = site;
            recentScoutingSiteExpiresAt[slot] = Time.time + cooldown;
        }

        /// <summary>
        /// Called ONLY after MobileNPCLooting actually enumerated nearby native
        /// world items and checked their real visibility. Finding no useful drop
        /// is normal; after three barren inspections, try a patrol/different
        /// district rather than repeating the same speculative loot objective.
        /// </summary>
        public void ReportLocalInspectionScannedServer(Vector3 inspectedPoint,
            bool startedRealLootGoal)
        {
            if (!Provider.isServer || (vitals != null && vitals.IsDead)
                || currentJob != Job.Scavenge)
                return;
            Vector3 difference = inspectedPoint - transform.position;
            if (difference.sqrMagnitude > 7f * 7f)
                return;

            confirmedInspectionScans = Mathf.Min(1000000, confirmedInspectionScans + 1);
            if (startedRealLootGoal)
            {
                consecutiveEmptyInspections = 0;
            }
            else
            {
                ++consecutiveEmptyInspections;
                // No 'loot acquired' claim is made for a sighting or scan.
                // Expiring this job temporarily lets familiar presets patrol
                // and discover new sectors rather than repeating blind loops.
                if (consecutiveEmptyInspections >= 3)
                {
                    consecutiveEmptyInspections = 0;
                    scavengeNotBefore = Time.time + 35f;
                    nextEvaluationTime = Time.time;
                }
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCUrbanScavenge] scan npc=" + name
                + " realLootGoal=" + startedRealLootGoal
                + " inspected=" + inspectedPoint
                + " totalScans=" + confirmedInspectionScans
                + " realTransfers=" + confirmedWorldPickups, this);
#endif
        }

        /// <summary>
        /// An authoritative ItemManager inventory transfer is the only source
        /// of a successful loot count; reaching a waypoint is never a pickup.
        /// </summary>
        public void ReportWorldPickupCompletedServer()
        {
            if (!Provider.isServer || (vitals != null && vitals.IsDead))
                return;
            consecutiveEmptyInspections = 0;
            confirmedWorldPickups = Mathf.Min(1000000, confirmedWorldPickups + 1);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCUrbanScavenge] real pickup npc=" + name
                + " count=" + confirmedWorldPickups, this);
#endif
        }

        public void ReportRoamBlockedServer(Vector3 attemptedWaypoint)
        {
            if (!Provider.isServer)
                return;
            ReleaseExplorationSectorServer(false, true);
            RememberScoutingOutcomeServer(attemptedWaypoint, 55f);
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

        public void ReportRoamReachedServer(Vector3 physicallyReachedWaypoint)
        {
            if (!Provider.isServer)
                return;
            ReleaseExplorationSectorServer(true, false);
            // Record a success only when the motor actually arrived.
            Vector3 difference = physicallyReachedWaypoint - transform.position;
            difference.y = 0f;
            if (difference.sqrMagnitude <= 2.5f * 2.5f
                && Mathf.Abs(physicallyReachedWaypoint.y - transform.position.y) <= 1.5f)
                RememberScoutingOutcomeServer(physicallyReachedWaypoint,
                    currentJob == Job.Scavenge ? 42f : 95f);
            consecutiveFailedScavengeWaypoints = 0;
            if (currentJob == Job.Recover)
                recoveryNotBefore = Time.time + 36f;
        }
    }
}
