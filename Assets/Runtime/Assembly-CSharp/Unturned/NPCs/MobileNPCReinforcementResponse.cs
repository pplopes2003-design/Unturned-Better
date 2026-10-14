////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt file for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// One living, armed group member can VOLUNTARILY answer a genuine radio
    /// distress message. It travels to an imprecise ally rendezvous via the
    /// existing tactical motor; it does not spawn reinforcements, transfer
    /// hostile target Transforms or ignore cover/combat priorities.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCBrainDriver))]
    [RequireComponent(typeof(MobileNPCGroupMember))]
    public sealed class MobileNPCReinforcementResponse : MonoBehaviour
    {
        private const float MaximumResponseRange = 90f;
        private const float MinimumUsefulResponseRange = 12f;
        private const float MinimumHealth = 0.55f;
        private const float ArrivalRadius = 9f;
        private const float ThinkInterval = 1.5f;
        private const float ReconsiderCooldown = 80f;
        private MobileNPCBrainDriver brain;
        private MobileNPCGroupMember group;
        private MobileNPCFaction faction;
        private MobileNPCHumanoidDisposition disposition;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCHumanoidRangedCombat weapon;
        private Vector3 plannedRendezvous;
        private int originalGroup;
        private MobileNPCFaction.Id originalFaction;
        private float deadline;
        private float nextThinkAt;
        private float nextEligibleAt;
        private float delayUntil;
        private float onSceneSince;
        private bool hasAssignment;
        private bool movingToHelp;

        private void Awake()
        {
            brain = GetComponent<MobileNPCBrainDriver>();
            group = GetComponent<MobileNPCGroupMember>();
            faction = GetComponent<MobileNPCFaction>();
            disposition = GetComponent<MobileNPCHumanoidDisposition>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            weapon = GetComponent<MobileNPCHumanoidRangedCombat>();
        }

        /// <summary>
        /// Score must be computed only AFTER the outbox has authenticated real
        /// 1445/base radio equipment, channel, faction and group. Only one
        /// listener per distress call receives the optional assignment.
        /// </summary>
        public float GetResponseScoreServer(MobileNPCRadioMessage message)
        {
            if (!Provider.isServer || !Level.isLoaded || message == null
                || message.Type != MobileNPCRadioMessage.Kind.NeedReinforcements
                || !message.IsFresh(Time.time) || message.Confidence < 0.45f
                || Time.time < nextEligibleAt || hasAssignment
                || brain == null || group == null || group.GroupId <= 0
                || group.GroupId != message.SenderGroupId
                || faction == null || faction.CurrentFaction != message.Faction
                || disposition == null
                || disposition.CurrentRole == MobileNPCHumanoidDisposition.Role.Neutral
                || vitals == null || vitals.IsDead
                || vitals.HealthFraction < MinimumHealth
                || weapon == null || weapon.SelectedGunAsset == null
                || weapon.RoundsRemaining < 3 || weapon.IsReloading
                || brain.Target != null
                || brain.CurrentGoal != MobileNPCTactics.Goal.None
                || brain.CurrentState != MobileNPCBrain.State.Idle)
                return -1f;

            Vector3 direction = message.ReportedPosition - transform.position;
            direction.y = 0f;
            float sqrDistance = direction.sqrMagnitude;
            if (sqrDistance < MinimumUsefulResponseRange * MinimumUsefulResponseRange
                || sqrDistance > MaximumResponseRange * MaximumResponseRange)
                return -1f;

            // Healthy, armed, nearby responders are preferred, but an unarmed,
            // dying or engaged squadmate is not ordered to charge a firefight.
            return 1000f - Mathf.Sqrt(sqrDistance) * 4f
                + vitals.HealthFraction * 80f
                + Mathf.Min(weapon.RoundsRemaining, 60);
        }

        public bool ReceiveReinforcementRequestServer(MobileNPCRadioMessage message)
        {
            if (GetResponseScoreServer(message) < 0f)
                return false;

            // The caller's location is itself slightly aged. Convert it into
            // a coarse sector and offset the intended rendezvous; this cannot
            // identify, target or track the original enemy.
            Vector3 coarse = new Vector3(
                Mathf.Round(message.ReportedPosition.x / 15f) * 15f,
                message.ReportedPosition.y,
                Mathf.Round(message.ReportedPosition.z / 15f) * 15f);
            Vector2 error = Random.insideUnitCircle * 6f;
            Vector3 requested = coarse + new Vector3(error.x, 0f, error.y);
            if (!MobileNPCSpawnService.TryFindSafeGroundPosition(requested,
                12f, 10f, RayMasks.BLOCK_COLLISION, 38f,
                out Vector3 safeGround, out _)
                || !Level.checkSafeIncludingClipVolumes(safeGround)
                || (safeGround - transform.position).sqrMagnitude
                    > MaximumResponseRange * MaximumResponseRange)
                return false;

            plannedRendezvous = safeGround;
            originalGroup = message.SenderGroupId;
            originalFaction = message.Faction;
            deadline = Mathf.Min(message.ExpiresAt, Time.time + 48f);
            delayUntil = Time.time + Random.Range(1.25f, 3.25f);
            nextEligibleAt = Time.time + ReconsiderCooldown;
            nextThinkAt = delayUntil;
            onSceneSince = 0f;
            hasAssignment = true;
            movingToHelp = false;
            return true;
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || !hasAssignment
                || Time.time < nextThinkAt)
                return;
            nextThinkAt = Time.time + ThinkInterval;

            if (Time.time >= deadline || group == null || group.GroupId != originalGroup
                || faction == null || faction.CurrentFaction != originalFaction
                || vitals == null || vitals.IsDead
                || vitals.HealthFraction < MinimumHealth
                || weapon == null || weapon.SelectedGunAsset == null
                || weapon.RoundsRemaining == 0 || brain == null
                || brain.Target != null)
            {
                FinishAssignmentServer();
                return;
            }

            if (!movingToHelp)
            {
                if (Time.time < delayUntil)
                    return;
                if (brain.CurrentGoal != MobileNPCTactics.Goal.None
                    || brain.CurrentState != MobileNPCBrain.State.Idle)
                {
                    // Do not interrupt an ally's active survival, patrol, or
                    // companion objective to satisfy a remote radio request.
                    FinishAssignmentServer();
                    return;
                }
                if (!brain.TryStartRadioReinforcementServer(plannedRendezvous))
                {
                    FinishAssignmentServer();
                    return;
                }
                movingToHelp = true;
                Debug.Log("[MobileNPCReinforcementResponse] " + name
                    + " volunteered for a real group radio distress call; traveling by normal navigation.", this);
                return;
            }

            if (brain.CurrentGoal != MobileNPCTactics.Goal.Reinforce
                || (brain.Destination - plannedRendezvous).sqrMagnitude > 4f)
            {
                FinishAssignmentServer();
                return;
            }

            if ((transform.position - plannedRendezvous).sqrMagnitude
                <= ArrivalRadius * ArrivalRadius)
            {
                if (onSceneSince == 0f)
                    onSceneSince = Time.time;
                // Stay a moment to allow the regular perception/AI to search
                // for a *newly visible* threat. Never grant a target by radio.
                if (Time.time - onSceneSince >= 4f)
                    FinishAssignmentServer();
            }
        }

        private void FinishAssignmentServer()
        {
            if (hasAssignment && movingToHelp && brain != null
                && brain.CurrentGoal == MobileNPCTactics.Goal.Reinforce
                && (brain.Destination - plannedRendezvous).sqrMagnitude <= 4f)
                brain.ReportDestinationReached();
            hasAssignment = false;
            movingToHelp = false;
            onSceneSince = 0f;
        }

        private void OnDisable()
        {
            hasAssignment = false;
            movingToHelp = false;
            onSceneSince = 0f;
        }
    }
}
