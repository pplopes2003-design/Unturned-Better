////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Server-local preparation queue for NPC intel. Does NOT transmit over a
    /// channel or show subtitles. Native channel, equipment state of player
    /// receivers and base/vehicle radio infrastructure still need integration.
    /// A real, owned vanilla Walkie Talkie is required even to queue a report.
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
                + (uint)(group.GroupId % 100000)
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
        /// The item's presence is only a *minimum* condition. A future native
        /// bridge must separately check that the NPC operates/tunes it.
        /// </summary>
        public bool HasOwnedPortableRadio => Provider.isServer && inventory != null
            && inventory.ContainsItemID(VanillaWalkieTalkieID)
            && (vitals == null || !vitals.IsDead);

        public int PendingCount => pending.Count;

        public bool TryQueueObservedContact(Vector3 lastObservedPlayerPosition)
        {
            if (!Provider.isServer || !HasOwnedPortableRadio || Time.time < nextPrepareAt
                || group == null || group.GroupId <= 0 || disposition == null
                || !disposition.CanEngagePlayers)
                return false;

            if (!MobileNPCRadioMessage.TryCreate(MobileNPCRadioMessage.Kind.EnemyContact,
                lastObservedPlayerPosition, Time.time, 24f, 0.78f,
                faction != null ? faction.CurrentFaction : MobileNPCFaction.Id.Unaffiliated,
                group.GroupId, disposition.DisplayName,
                out MobileNPCRadioMessage message))
                return false;

            nextPrepareAt = Time.time + SendPreparationCooldown;
            while (pending.Count >= MaximumPendingReports)
                pending.Dequeue();
            pending.Enqueue(message);
            // No transmission until the native voice/channel bridge is verified.
            return true;
        }

        /// <summary>
        /// Pure handoff for a future transport bridge. Intentionally does not
        /// expose an unchecked send event or leak messages to client UI.
        /// </summary>
        public bool TryPeekFreshReport(out MobileNPCRadioMessage message)
        {
            message = null;
            if (!HasOwnedPortableRadio)
                return false;

            while (pending.Count > 0 && !pending.Peek().IsFresh(Time.time))
                pending.Dequeue();
            if (pending.Count == 0)
                return false;
            message = pending.Peek();
            return true;
        }

        public void ConfirmTransmittedReport(MobileNPCRadioMessage message)
        {
            // Reserved for verified native transport: the object must match the
            // first queued record, preventing removal of another report.
            if (Provider.isServer && message != null && pending.Count > 0
                && object.ReferenceEquals(pending.Peek(), message))
                pending.Dequeue();
        }

        private void OnDisable()
        {
            ActiveRadios.Remove(this);
            pending.Clear();
            nextPrepareAt = 0f;
            nextPersonalTransmitAt = 0f;
        }
    }
}
