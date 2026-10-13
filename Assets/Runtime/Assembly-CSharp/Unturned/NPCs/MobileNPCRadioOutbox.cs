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
        private readonly Queue<MobileNPCRadioMessage> pending = new Queue<MobileNPCRadioMessage>();
        private MobileNPCInventory inventory;
        private MobileNPCGroupMember group;
        private MobileNPCFaction faction;
        private MobileNPCHumanoidVitals vitals;
        private MobileNPCHumanoidDisposition disposition;
        private float nextPrepareAt;

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
            pending.Clear();
        }
    }
}
