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
    /// Sends reports among living same-group radio owners and to the host-only
    /// channel/equipment-gated intercept HUD. No native voice packets, physical
    /// NPC hand equipment, remote client messages or powered base relays.
    /// A real, owned vanilla Walkie Talkie is required for each NPC endpoint.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCInventory))]
    public sealed class MobileNPCRadioOutbox : MonoBehaviour
    {
        public const ushort VanillaWalkieTalkieID = 1445;
        private const int MaximumPendingReports = 4;
        private const float SendPreparationCooldown = 20f;
        // Vanilla walkie/earpiece channel is PlayerQuests.radioFrequency:uint.
        // NPCs have no PlayerQuests or PlayerVoice component; choose one
        // deterministic tuneable native-frequency value for each living group.
        // Runtime GroupId is unique within a map session; do not modulo the
        // identity because unrelated groups would eventually share a channel.
        // This is simulated text traffic, NOT player microphone voice.
        private static readonly HashSet<MobileNPCRadioOutbox> ActiveRadios
            = new HashSet<MobileNPCRadioOutbox>();
        private static float nextGlobalTransmitAt;
        private static float nextRadioDiagnosticsAt;
        private const float TransmissionAirtime = 1.25f;
        private const float PerNpcTransmitInterval = 8f;
        private readonly Queue<MobileNPCRadioMessage> pending = new Queue<MobileNPCRadioMessage>();
        private MobileNPCInventory inventory;
        private MobileNPCGroupMember group;
        private MobileNPCFaction faction;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCHumanoidDisposition disposition;
        private float nextPrepareAt;
        private float nextAttemptTransmitAt;
        private float nextPersonalTransmitAt;

        public uint TunedFrequency => group != null && group.GroupId > 0
            ? PlayerQuests.DEFAULT_RADIO_FREQUENCY
                + (uint)group.GroupId
            : 0u;

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
            if (!Provider.isServer || !HasOwnedPortableRadio
                || Time.time < nextAttemptTransmitAt
                || Time.time < nextGlobalTransmitAt
                || Time.time < nextPersonalTransmitAt)
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

            // Consume airtime only on a real source-owned portable radio.
            // The NPC has no PlayerEquipment hand slot; operating a known
            // owned device is a bounded simulation, not a claimed native voice
            // or visual equip animation.
            nextGlobalTransmitAt = Time.time + TransmissionAirtime;
            nextPersonalTransmitAt = Time.time + PerNpcTransmitInterval;
            int delivered = 0;
            foreach (MobileNPCRadioOutbox receiver in ActiveRadios)
            {
                if (receiver == null || receiver == this
                    || !receiver.HasOwnedPortableRadio
                    || receiver.TunedFrequency != frequency
                    || receiver.group == null
                    || receiver.group.GroupId != group.GroupId
                    || receiver.faction == null
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
                delivered++;
            }
            // Same frequency and physically worn earpiece/equipped walkie
            // criteria as native PlayerVoice radio culling. No chat broadcast.
            bool intercepted = MobileNPCRadioInterceptHUD.CanLocalPlayerIntercept(frequency);
            MobileNPCRadioInterceptHUD.PresentServerTransmission(frequency, message);
            if (Time.time >= nextRadioDiagnosticsAt)
            {
                nextRadioDiagnosticsAt = Time.time + 10f;
                Debug.Log("[MobileNPCRadioOutbox] Portable radio text broadcast: group="
                    + group.GroupId + ", frequency=" + frequency
                    + ", NPC receivers=" + delivered
                    + ", local player intercepted=" + intercepted
                    + ". No microphone audio or remote-client replication.", this);
            }
            ConfirmTransmittedReport(message);
        }

        private void Awake()
        {
            inventory = GetComponent<MobileNPCInventory>();
            group = GetComponent<MobileNPCGroupMember>();
            faction = GetComponent<MobileNPCFaction>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
            disposition = GetComponent<MobileNPCHumanoidDisposition>();
        }

        /// <summary>
        /// Item ownership permits SIMULATED NPC radio operation; NPCs do not
        /// have native PlayerEquipment/PlayerVoice, or a hand-held radio prop.
        /// Keep the visual/native voice bridge as a separate COM-02 task.
        /// </summary>
        public bool HasOwnedPortableRadio => Provider.isServer && inventory != null
            && inventory.ContainsItemID(VanillaWalkieTalkieID)
            && (vitals == null || !vitals.IsDead);

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
        }
    }
}
