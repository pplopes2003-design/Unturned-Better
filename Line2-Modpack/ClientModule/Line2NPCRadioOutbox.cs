using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Steam-side packet-less adaptation of Line 1 MobileNPCRadioOutbox:
    /// truthful first-hand player observations, finite inventory walkie,
    /// faction + group + range authorization, decaying approximate sector.
    /// This is NPC-to-NPC world intelligence, NOT player voice transport,
    /// walkie audio interception or network replication.
    /// </summary>
    internal sealed class Line2NPCRadioOutbox : MonoBehaviour
    {
        internal const ushort NativeWalkieId = 1445;
        private const float CommunicationRange = 90f;
        private const float SendCooldown = 20f;
        private const float AirTime = 1.25f;
        private const int MaxRecipients = 32;
        private static float nextChannelAvailableAt;
        private readonly List<PrototypeActor> _candidates =
            new List<PrototypeActor>(MaxRecipients);
        private SteamNpcPrototypeRuntime _runtime;
        private PrototypeActor _actor;
        private Line2NPCCombatPerception _sight;
        private Line2MobileNPCInventory _inventory;
        private Line2NPCGroupMember _membership;
        private float _nextSampleAt;
        private float _nextSendAt;
        private float _nextLogAt;

        internal void Initialize(SteamNpcPrototypeRuntime runtime,
                                 PrototypeActor actor,
                                 Line2NPCCombatPerception sight,
                                 Line2MobileNPCInventory inventory)
        {
            _runtime = runtime;
            _actor = actor;
            _sight = sight;
            _inventory = inventory;
            _membership = GetComponent<Line2NPCGroupMember>();
            _nextSampleAt = Time.time + UnityEngine.Random.Range(0.4f, 1.1f);
        }

        private void Update()
        {
            if (Time.time < _nextSampleAt) return;
            _nextSampleAt = Time.time + 0.55f;
            if (_actor == null || _inventory == null || _sight == null ||
                _runtime == null || _membership == null ||
                !_actor.IsCombatEnabled || _actor.IsDead ||
                _actor.Disposition != "hostile" ||
                _sight.Kind != Line2NPCCombatPerception.TargetKind.Player ||
                _sight.VisibleTarget == null ||
                !_actor.PlayerVisible ||
                !_inventory.ContainsItemID(NativeWalkieId) ||
                !_membership.Joined || Time.time < _nextSendAt ||
                Time.time < nextChannelAvailableAt ||
                !Provider.isServer || Dedicator.IsDedicatedServer ||
                (Provider.clients != null && Provider.clients.Count > 1))
                return;

            Player player = _sight.VisibleTarget.GetComponentInParent<Player>();
            if (player == null || player.life == null || player.life.isDead)
                return;
            if ((player.transform.position - transform.position).sqrMagnitude > 400f)
                return;

            // Do not radio the exact Player position: spatial grid preserves
            // an approximate 20m sector from one confirmed observation.
            Vector3 seen = _sight.LastVisiblePosition;
            Vector3 sector = new Vector3(
                Mathf.Floor(seen.x / 20f) * 20f + 10f, seen.y,
                Mathf.Floor(seen.z / 20f) * 20f + 10f);
            float now = Time.time;
            _runtime.CopyLiveActors(_candidates, MaxRecipients);
            int recipients = 0;
            foreach (PrototypeActor other in _candidates)
            {
                if (other == null || other == _actor || other.IsDead ||
                    !other.IsCombatEnabled) continue;
                Line2NPCGroupMember otherGroup =
                    other.GetComponent<Line2NPCGroupMember>();
                Line2MobileNPCInventory otherInventory = other.Inventory;
                if (otherGroup == null || !otherGroup.Joined ||
                    otherGroup.GroupId != _membership.GroupId ||
                    otherGroup.Faction != _membership.Faction ||
                    otherInventory == null ||
                    !otherInventory.ContainsItemID(NativeWalkieId) ||
                    (other.transform.position - transform.position).sqrMagnitude >
                       CommunicationRange * CommunicationRange)
                    continue;

                other.ReceiveVerifiedRadioReport(sector, now);
                recipients++;
            }
            _candidates.Clear();
            _nextSendAt = now + SendCooldown;
            nextChannelAvailableAt = now + AirTime;
            if (Time.time >= _nextLogAt)
            {
                ProbeLogger.Log("NPC_RADIO_CONTACT: group=" + _membership.GroupId +
                    " faction=" + _membership.Faction +
                    " sender=" + _actor.Identity + " recipients=" + recipients +
                    " sector20m=" + (int)sector.x + "," + (int)sector.z);
                _nextLogAt = Time.time + 12f;
            }
        }

        private void OnDisable()
        {
            _candidates.Clear();
        }

        internal static void ResetSession()
        {
            nextChannelAvailableAt = 0f;
        }
    }
}
