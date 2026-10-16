////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Server-local NPC radio queue and single-player text transmitter.
    /// Sends reports among living same-group radio endpoints and to the host-only
    /// channel/equipment-gated intercept HUD. No native voice packets.
    /// ONLY an owned walkie transmits; an ACTUALLY worn earpiece listens.
    /// No fictional generator-powered storage/vehicle radio endpoint.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCInventory))]
    public sealed class MobileNPCRadioOutbox : MonoBehaviour
    {
        public const ushort VanillaWalkieTalkieID = 1445;
        private const int MaximumPendingReports = 4;
        private const float SendPreparationCooldown = 20f;
        // Native player tuning uses PlayerQuests.radioFrequency:uint.
        // NPCs cannot equip PlayerVoice; transmit carefully simulated text.
        // Organized faction radios deliberately share a stable channel across
        // their surviving groups. Unaffiliated survivors keep a separate
        // short-lived group channel to avoid accidental cross-group leakage.
        // Player can intercept by manually tuning to that exact uint channel.
        public const uint FactionChannelStep = 1000u;
        private static readonly HashSet<MobileNPCRadioOutbox> ActiveRadios
            = new HashSet<MobileNPCRadioOutbox>();
        private static float nextGlobalTransmitAt;
        private static float nextRadioDiagnosticsAt;
        private const float TransmissionAirtime = 1.25f;
        private const float PerNpcTransmitInterval = 8f;
        private readonly Queue<MobileNPCRadioMessage> pending = new Queue<MobileNPCRadioMessage>();
        private MobileNPCInventory inventory;
        private MobileNPCEquipment equipment;
        private MobileNPCGroupMember group;
        private MobileNPCFaction faction;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCHumanoidDisposition disposition;
        private float nextPrepareAt;
        private float nextAttemptTransmitAt;
        private float nextPersonalTransmitAt;
        private float nextReinforcementRequestAt;
        private float lastUsefulLootReportAt = -1000f;
        private Vector3 lastUsefulLootReportPosition;
        private ushort lastUsefulLootReportItemID;

        public uint TunedFrequency
        {
            get
            {
                if (group == null || group.GroupId <= 0 || faction == null)
                    return 0u;
                uint baseFrequency = PlayerQuests.DEFAULT_RADIO_FREQUENCY;
                MobileNPCFaction.Id current = faction.CurrentFaction;
                if (current == MobileNPCFaction.Id.Unaffiliated)
                    return baseFrequency + 10000u + (uint)group.GroupId;
                return baseFrequency + (uint)current * FactionChannelStep;
            }
        }

        public static void ResetRadioSessionServer()
        {
            ActiveRadios.Clear();
            nextGlobalTransmitAt = 0f;
            nextRadioDiagnosticsAt = 0f;
        }

        private void OnEnable()
        {
            ActiveRadios.Add(this);
        }

        private void Update()
        {
            if (!Provider.isServer || pending.Count == 0
                || Time.time < nextAttemptTransmitAt
                || Time.time < nextGlobalTransmitAt
                || Time.time < nextPersonalTransmitAt
                || !HasOwnedPortableRadio)
                return;
            nextAttemptTransmitAt = Time.time + 0.35f;
            if (!TryPeekFreshReport(out MobileNPCRadioMessage message)
                || group == null || group.GroupId <= 0
                || faction == null || message.SenderGroupId != group.GroupId
                || message.Faction != faction.CurrentFaction
                || !message.IsFresh(Time.time))
                return;

            uint frequency = TunedFrequency;
            if (frequency == 0u)
                return;

            // No native NPC PlayerVoice microphone: reports are simulated,
            // and only an NPC with a genuinely owned walkie can transmit.
            nextGlobalTransmitAt = Time.time + TransmissionAirtime;
            nextPersonalTransmitAt = Time.time + PerNpcTransmitInterval;
            int delivered = 0;
            int earpieceReceivers = 0;
            MobileNPCReinforcementResponse bestResponder = null;
            float bestResponderScore = -1f;
            foreach (MobileNPCRadioOutbox receiver in ActiveRadios)
            {
                if (receiver == null || receiver == this
                    || !receiver.HasRadioReceiverServer
                    || receiver.TunedFrequency != frequency
                    || receiver.group == null || receiver.group.GroupId <= 0
                    || receiver.faction == null
                    // Unaffiliated strangers must not be told other groups'
                    // secrets just because they lack an assigned faction.
                    || (message.Faction == MobileNPCFaction.Id.Unaffiliated
                        && receiver.group.GroupId != message.SenderGroupId)
                    || receiver.faction.CurrentFaction != message.Faction
                    || receiver.vitals != null && receiver.vitals.IsDead)
                    continue;

                MobileNPCBrainDriver brain = receiver.GetComponent<MobileNPCBrainDriver>();
                if (brain == null)
                    continue;

                // Radio reports are unverified distant hearsay, not target
                // acquisition. Only actual first-hand sightings in sender
                // have been allowed into this outbox.
                if (message.Type == MobileNPCRadioMessage.Kind.EnemyContact)
                {
                    float age = Mathf.Max(0f, Time.time - message.ObservedAt);
                    float confidence = Mathf.Clamp(
                        message.Confidence * (1f - age / 45f), 0.18f, 0.65f);
                    Vector2 error = Random.insideUnitCircle
                        * Mathf.Lerp(8f, 3f, confidence);
                    Vector3 uncertainLocation = message.ReportedPosition
                        + new Vector3(error.x, 0f, error.y);
                    brain.ReportGroupPlayerContactClue(uncertainLocation, confidence);
                }
                else if (message.Type == MobileNPCRadioMessage.Kind.Airdrop)
                {
                    // Actual crate lookup and loot validation stay local.
                    MobileNPCAirdropRumor rumor =
                        receiver.GetComponent<MobileNPCAirdropRumor>();
                    rumor?.ReceiveAirdropReportServer(message);
                }
                else if (message.Type == MobileNPCRadioMessage.Kind.UnknownBase)
                {
                    MobileNPCUnknownBaseIntel intel =
                        receiver.GetComponent<MobileNPCUnknownBaseIntel>();
                    intel?.ReceiveUnknownBaseReportServer(message);
                }
                else if (message.Type == MobileNPCRadioMessage.Kind.UsefulLoot)
                {
                    // A valid radio conveys the item TYPE and hearsay region,
                    // not an ItemManager world instance or remote pickup right.
                    MobileNPCUsefulLootRumor rumor =
                        receiver.GetComponent<MobileNPCUsefulLootRumor>();
                    rumor?.ReceiveUsefulLootReportServer(message);
                }
                else if (message.Type == MobileNPCRadioMessage.Kind.NeedReinforcements)
                {
                    // Choose AT MOST one genuinely available ally. Radio does
                    // not create more NPCs or grant live hostile identity.
                    MobileNPCReinforcementResponse responder =
                        receiver.GetComponent<MobileNPCReinforcementResponse>();
                    float score = responder != null
                        ? responder.GetResponseScoreServer(message) : -1f;
                    if (score > bestResponderScore)
                    {
                        bestResponderScore = score;
                        bestResponder = responder;
                    }
                }
                delivered++;
                if (!receiver.HasOwnedPortableRadio)
                    earpieceReceivers++;
            }
            if (bestResponder != null)
                bestResponder.ReceiveReinforcementRequestServer(message);

            // Players still need actual native receiving equipment and manual
            // tuning. This message is never relayed as microphone voice.
            bool intercepted = MobileNPCRadioInterceptHUD.CanLocalPlayerIntercept(frequency);
            MobileNPCRadioInterceptHUD.PresentServerTransmission(frequency, message);
            if (Time.time >= nextRadioDiagnosticsAt)
            {
                nextRadioDiagnosticsAt = Time.time + 10f;
                Debug.Log("[MobileNPCRadioOutbox] Physical radio text broadcast: group="
                    + group.GroupId + ", frequency=" + frequency
                    + ", source=owned-walkie"
                    + ", NPC receivers=" + delivered
                    + ", worn-earpiece receivers=" + earpieceReceivers
                    + ", local player intercepted=" + intercepted
                    + ". No microphone audio or remote-client replication.", this);
            }
            ConfirmTransmittedReport(message);
        }

        private void Awake()
        {
            inventory = GetComponent<MobileNPCInventory>();
            equipment = GetComponent<MobileNPCEquipment>();
            group = GetComponent<MobileNPCGroupMember>();
            faction = GetComponent<MobileNPCFaction>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            disposition = GetComponent<MobileNPCHumanoidDisposition>();
        }

        /// <summary>
        /// An owned vanilla Walkie Talkie may transmit simulated NPC text.
        /// Vanilla PlayerVoice itself cannot be attached to our custom NPC.
        /// </summary>
        public bool HasOwnedPortableRadio => Provider.isServer && inventory != null
            && inventory.ContainsItemID(VanillaWalkieTalkieID)
            && (vitals == null || !vitals.IsDead);

        /// <summary>Matches PlayerVoice.canHearRadio: an owned walkie or an
        /// actually worn native Earpiece mask. An inventoried mask is not enough.</summary>
        public bool HasRadioReceiverServer => HasOwnedPortableRadio
            || equipment != null && equipment.HasWornEarpieceServer;

        public int PendingCount => pending.Count;

        public bool TryQueueObservedContact(Vector3 lastObservedPlayerPosition)
        {
            if (!Provider.isServer || !HasOwnedPortableRadio || Time.time < nextPrepareAt
                || group == null || group.GroupId <= 0 || faction == null
                || disposition == null
                || !disposition.CanEngagePlayers)
                return false;

            if (!MobileNPCRadioMessage.TryCreate(MobileNPCRadioMessage.Kind.EnemyContact,
                lastObservedPlayerPosition, Time.time, 24f, 0.78f,
                faction.CurrentFaction,
                group.GroupId, disposition.DisplayName,
                out MobileNPCRadioMessage message))
                return false;

            nextPrepareAt = Time.time + SendPreparationCooldown;
            while (pending.Count >= MaximumPendingReports)
                pending.Dequeue();
            pending.Enqueue(message);
            // Update sends simulated text only, never native voice packets.
            return true;
        }

        /// <summary>
        /// Reports a physically verified landed carepackage only when the NPC is
        /// close enough to observe it. Does not manufacture loot or reveal
        /// unseen airdrops to distant squads without an owned walkie.
        /// </summary>
        public bool TryQueueObservedAirdrop(Vector3 observedPosition)
        {
            if (!Provider.isServer || !HasOwnedPortableRadio || Time.time < nextPrepareAt
                || group == null || group.GroupId <= 0
                || faction == null || disposition == null
                || Vector3.SqrMagnitude(observedPosition - transform.position) > 28f * 28f)
                return false;

            if (!MobileNPCRadioMessage.TryCreate(MobileNPCRadioMessage.Kind.Airdrop,
                observedPosition, Time.time, 45f, 0.8f, faction.CurrentFaction,
                group.GroupId, disposition.DisplayName, out MobileNPCRadioMessage message))
                return false;

            nextPrepareAt = Time.time + SendPreparationCooldown;
            while (pending.Count >= MaximumPendingReports)
                pending.Dequeue();
            pending.Enqueue(message);
            return true;
        }

        /// <summary>Called only by the native ownership/LOS scanner after
        /// confirming two genuine nonzero-owner player structures.</summary>
        public bool TryQueueObservedUnknownBaseServer(Vector3 observedSite)
        {
            if (!Provider.isServer || Time.time < nextPrepareAt
                || group == null || group.GroupId <= 0 || faction == null
                || disposition == null || !HasOwnedPortableRadio
                || (observedSite - transform.position).sqrMagnitude > 30f * 30f)
                return false;
            if (!MobileNPCRadioMessage.TryCreate(
                MobileNPCRadioMessage.Kind.UnknownBase,
                observedSite, Time.time, 85f, 0.61f,
                faction.CurrentFaction, group.GroupId,
                disposition.DisplayName, out MobileNPCRadioMessage message))
                return false;
            nextPrepareAt = Time.time + SendPreparationCooldown;
            while (pending.Count >= MaximumPendingReports)
                pending.Dequeue();
            pending.Enqueue(message);
            return true;
        }

        /// <summary>
        /// Send only an ACTUAL first-hand, short-range, unobstructed and
        /// registered world drop that can be valuable to a team member.
        /// The sender must have seen the drop while doing ordinary local
        /// ItemManager scans; the receiver must revalidate need and later
        /// independently find the drop before it can take anything.
        /// </summary>
        public bool TryQueueObservedUsefulLootServer(InteractableItem observed)
        {
            if (!Provider.isServer || Time.time < nextPrepareAt
                || group == null || group.GroupId <= 0
                || faction == null || disposition == null
                || observed == null || observed.asset == null
                || observed.jar == null || observed.jar.item == null
                || observed.jar.item.id == 0
                || MobileNPCUsefulLootVision.IntrinsicImportance(observed.asset) < 60f
                || !HasOwnedPortableRadio
                || !MobileNPCUsefulLootVision.CanSeeWorldDropServer(
                    observed, transform, 17f))
                return false;

            ushort itemID = observed.jar.item.id;
            Vector3 location = observed.transform.position;
            if (itemID == lastUsefulLootReportItemID
                && (location - lastUsefulLootReportPosition).sqrMagnitude
                    < 25f * 25f && Time.time - lastUsefulLootReportAt < 95f)
                return false;
            if (!MobileNPCRadioMessage.TryCreateUsefulLoot(
                location, itemID, Time.time, 62f, 0.76f,
                faction.CurrentFaction, group.GroupId, disposition.DisplayName,
                out MobileNPCRadioMessage message))
                return false;

            lastUsefulLootReportAt = Time.time;
            lastUsefulLootReportPosition = location;
            lastUsefulLootReportItemID = itemID;
            nextPrepareAt = Time.time + SendPreparationCooldown;
            while (pending.Count >= MaximumPendingReports)
                pending.Dequeue();
            pending.Enqueue(message);
            return true;
        }

        /// <summary>
        /// A real visible threat AND accepted damage within the last 7s justify
        /// a short, infrequent help request. Location is CALLER's own position,
        /// not the shooter's live position. No calls from rumor, noise, status
        /// alone or a non-hostile entity; no free reinforcements created.
        /// </summary>
        public bool TryQueueCombatReinforcementsServer(Transform seenThreat)
        {
            if (!Provider.isServer || Time.time < nextReinforcementRequestAt
                || Time.time < nextPrepareAt || seenThreat == null
                || group == null || group.GroupId <= 0 || faction == null
                || vitals == null || vitals.IsDead
                || vitals.LastDamageTime < 0f
                || Time.time - vitals.LastDamageTime > 7f
                || vitals.HealthFraction > 0.9f || disposition == null
                || !HasOwnedPortableRadio)
                return false;

            Player player = seenThreat.GetComponentInParent<Player>();
            MobileNPCHumanoidDisposition enemyNPC =
                seenThreat.GetComponentInParent<MobileNPCHumanoidDisposition>();
            Zombie zombie = seenThreat.GetComponentInParent<Zombie>();
            MobileNPCHumanoidVitals enemyVitals = enemyNPC != null
                ? enemyNPC.GetComponent<MobileNPCHumanoidVitals>() : null;
            bool verifiedEnemy = player != null && player.life != null
                && !player.life.isDead && disposition.CanEngagePlayers
                || enemyNPC != null && enemyNPC != disposition
                && MobileNPCHumanoidDisposition.AreHostile(disposition, enemyNPC)
                && enemyVitals != null && !enemyVitals.IsDead
                || zombie != null && !zombie.isDead;
            if (!verifiedEnemy)
                return false;

            if (!MobileNPCRadioMessage.TryCreate(
                MobileNPCRadioMessage.Kind.NeedReinforcements,
                transform.position, Time.time, 48f, 0.78f,
                faction.CurrentFaction, group.GroupId,
                disposition.DisplayName, out MobileNPCRadioMessage message))
                return false;

            nextReinforcementRequestAt = Time.time + 80f;
            nextPrepareAt = Time.time + SendPreparationCooldown;
            while (pending.Count >= MaximumPendingReports)
                pending.Dequeue();
            pending.Enqueue(message);
            return true;
        }

        /// <summary>
        /// Return the next fresh report for local simulated text transport.
        /// A report created before a group/faction change must not block fresh
        /// information or be forwarded using a different group's new channel.
        /// </summary>
        public bool TryPeekFreshReport(out MobileNPCRadioMessage message)
        {
            message = null;
            if (!HasOwnedPortableRadio)
                return false;

            while (pending.Count > 0)
            {
                MobileNPCRadioMessage queued = pending.Peek();
                if (queued.IsFresh(Time.time) && group != null && group.GroupId > 0
                    && faction != null && queued.SenderGroupId == group.GroupId
                    && queued.Faction == faction.CurrentFaction)
                    break;
                // Expired or former-group intelligence cannot be transmitted
                // under a new identity, even when a fresher report is waiting.
                pending.Dequeue();
            }
            if (pending.Count == 0)
                return false;
            message = pending.Peek();
            return true;
        }

        public void ConfirmTransmittedReport(MobileNPCRadioMessage message)
        {
            // Consume only the record actually sent; a different pending
            // message must never be removed by an unrelated send.
            if (Provider.isServer && message != null && pending.Count > 0
                && object.ReferenceEquals(pending.Peek(), message))
                pending.Dequeue();
        }

        private void OnDisable()
        {
            ActiveRadios.Remove(this);
            pending.Clear();
            nextPrepareAt = 0f;
            nextAttemptTransmitAt = 0f;
            nextPersonalTransmitAt = 0f;
            nextReinforcementRequestAt = 0f;
            lastUsefulLootReportAt = -1000f;
            lastUsefulLootReportItemID = 0;
            lastUsefulLootReportPosition = Vector3.zero;
        }
    }
}
