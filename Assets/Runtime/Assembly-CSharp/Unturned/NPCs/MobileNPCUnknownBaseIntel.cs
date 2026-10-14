////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// First-hand reconnaissance of ACTUAL player-owned built structures,
    /// never static houses or a magically identified "enemy base". A player's
    /// ownership is not evidence the player is presently home or hostile.
    /// Unowned NPC-built structures intentionally remain unclassified because
    /// current settlement code has no persistent owner/group identity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCUnknownBaseIntel : MonoBehaviour
    {
        private const float ScanRange = 25f;
        private const float ScanEvery = 5f;
        private const float ReportRepeatAfter = 160f;
        private const float MaxRumorResponseRange = 120f;
        private static readonly Collider[] Nearby = new Collider[96];
        private static readonly RaycastHit[] Occlusion = new RaycastHit[16];
        private MobileNPCBrainDriver brain;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCGroupMember group;
        private MobileNPCFaction faction;
        private MobileNPCRadioOutbox radio;
        private Vector3 lastReported;
        private float lastReportTime = -10000f;
        private float nextScanAt;

        private bool hasReport;
        private bool isInvestigating;
        private int reportGroup;
        private MobileNPCFaction.Id reportFaction;
        private Vector3 observedSector;
        private Vector3 lookoutPoint;
        private float reportExpires;
        private float nextPlanAt;
        private float revisitAfter;
        private Vector3 lastInvestigated;

        private void Awake()
        {
            brain = GetComponent<MobileNPCBrainDriver>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            group = GetComponent<MobileNPCGroupMember>();
            faction = GetComponent<MobileNPCFaction>();
            radio = GetComponent<MobileNPCRadioOutbox>();
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded
                || vitals == null || vitals.IsDead)
                return;
            if (Time.time >= nextScanAt)
            {
                nextScanAt = Time.time + ScanEvery + Random.Range(0f, 1f);
                ScanPlayerBuiltStructuresServer();
            }
            if (!hasReport || Time.time < nextPlanAt)
                return;
            nextPlanAt = Time.time + 2f;
            if (Time.time >= reportExpires || brain == null
                || group == null || group.GroupId != reportGroup
                || faction == null || faction.CurrentFaction != reportFaction
                || brain.Target != null)
            {
                FinishIntel();
                return;
            }
            if (isInvestigating)
            {
                if (brain.CurrentGoal == MobileNPCTactics.Goal.Investigate
                    && (brain.Destination - lookoutPoint).sqrMagnitude < 4f)
                {
                    if ((transform.position - lookoutPoint).sqrMagnitude < 9f * 9f)
                        FinishIntel();
                    return;
                }
                FinishIntel();
                return;
            }
            if (brain.CurrentGoal != MobileNPCTactics.Goal.None
                || brain.CurrentState != MobileNPCBrain.State.Idle)
                return;
            if (brain.TryStartRememberedSupplySearch(lookoutPoint))
            {
                isInvestigating = true;
                Debug.Log("[MobileNPCUnknownBaseIntel] " + name
                    + " scouting reported possible constructed shelter from a standoff point, not attacking it.", this);
            }
        }

        private void ScanPlayerBuiltStructuresServer()
        {
            if (radio == null || brain == null || brain.Target != null
                || (brain.CurrentGoal != MobileNPCTactics.Goal.None
                    && brain.CurrentGoal != MobileNPCTactics.Goal.Investigate))
                return;
            int count = Physics.OverlapSphereNonAlloc(transform.position,
                ScanRange, Nearby, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (count >= Nearby.Length)
                return;
            // A single wall/floor can be placed for an unrelated purpose.
            // Require two distinct registered structures from same nonzero
            // native owner within ~14m, each currently in actual sight.
            StructureDrop first = null;
            ulong firstOwner = 0;
            Vector3 firstPosition = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                Collider coll = Nearby[i];
                if (coll == null)
                    continue;
                StructureRefComponent reference =
                    coll.GetComponentInParent<StructureRefComponent>();
                StructureDrop drop = reference != null
                    ? reference.tempNotSureIfStructureShouldBeAComponentYet : null;
                StructureData data = drop != null ? drop.GetServersideData() : null;
                if (drop == null || data == null || drop.model == null
                    || data.owner == 0UL || drop.instanceID == 0
                    || !drop.model.gameObject.activeInHierarchy
                    || !CanSee(drop.model.position, drop.model))
                    continue;

                Vector3 point = drop.model.position;
                if (first == null || data.owner != firstOwner
                    || (point - firstPosition).sqrMagnitude > 14f * 14f)
                {
                    first = drop;
                    firstOwner = data.owner;
                    firstPosition = point;
                    continue;
                }
                if (drop.instanceID == first.instanceID)
                    continue;
                Vector3 observed = (firstPosition + point) * 0.5f;
                if (Time.time < lastReportTime + ReportRepeatAfter
                    && (observed - lastReported).sqrMagnitude < 40f * 40f)
                    return;
                if (radio.TryQueueObservedUnknownBaseServer(observed))
                {
                    lastReported = observed;
                    lastReportTime = Time.time;
                }
                return;
            }
        }

        private bool CanSee(Vector3 position, Transform structure)
        {
            Vector3 start = transform.position + Vector3.up * 1.4f;
            Vector3 end = position + Vector3.up;
            Vector3 offset = end - start;
            float d = offset.magnitude;
            if (d < 0.2f || d > ScanRange + 2f)
                return false;
            int n = Physics.RaycastNonAlloc(start, offset / d, Occlusion, d,
                RayMasks.BLOCK_COLLISION, QueryTriggerInteraction.Ignore);
            if (n >= Occlusion.Length)
                return false;
            for (int i = 0; i < n; i++)
            {
                Collider collider = Occlusion[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform)
                    || collider.transform == structure
                    || collider.transform.IsChildOf(structure))
                    continue;
                return false;
            }
            return true;
        }

        public bool ReceiveUnknownBaseReportServer(MobileNPCRadioMessage message)
        {
            if (!Provider.isServer || message == null
                || message.Type != MobileNPCRadioMessage.Kind.UnknownBase
                || !message.IsFresh(Time.time) || message.Confidence < 0.4f
                || hasReport || brain == null || group == null
                || group.GroupId != message.SenderGroupId
                || faction == null || faction.CurrentFaction != message.Faction
                || vitals == null || vitals.IsDead)
                return false;

            Vector3 sector = new Vector3(
                Mathf.Round(message.ReportedPosition.x / 20f) * 20f,
                message.ReportedPosition.y,
                Mathf.Round(message.ReportedPosition.z / 20f) * 20f);
            if (Time.time < revisitAfter
                && (sector - lastInvestigated).sqrMagnitude < 40f * 40f)
                return false;
            Vector3 away = transform.position - sector;
            away.y = 0f;
            float distance = away.magnitude;
            if (distance < 32f || distance > MaxRumorResponseRange)
                return false;
            // Recon only: remain ~20m outside the reported structure,
            // not move to the doorway, loot or attack unknown owners.
            Vector2 random = Random.insideUnitCircle * 4f;
            Vector3 proposed = sector + away.normalized * 22f
                + new Vector3(random.x, 0f, random.y);
            if (!MobileNPCSpawnService.TryFindSafeGroundPosition(
                proposed, 12f, 8f, RayMasks.BLOCK_COLLISION, 38f,
                out Vector3 safeGround, out _)
                || !Level.checkSafeIncludingClipVolumes(safeGround)
                || (safeGround - sector).sqrMagnitude < 14f * 14f)
                return false;
            observedSector = sector;
            lookoutPoint = safeGround;
            reportExpires = Mathf.Min(message.ExpiresAt, Time.time + 85f);
            reportGroup = message.SenderGroupId;
            reportFaction = message.Faction;
            hasReport = true;
            isInvestigating = false;
            nextPlanAt = Time.time + Random.Range(1f, 4f);
            return true;
        }

        private void FinishIntel()
        {
            if (!hasReport)
                return;
            if (isInvestigating && brain != null
                && brain.CurrentGoal == MobileNPCTactics.Goal.Investigate
                && (brain.Destination - lookoutPoint).sqrMagnitude < 4f)
                brain.ReportDestinationReached();
            lastInvestigated = observedSector;
            revisitAfter = Time.time + 150f;
            hasReport = false;
            isInvestigating = false;
        }

        private void OnDisable()
        {
            hasReport = false;
            isInvestigating = false;
        }
    }
}
