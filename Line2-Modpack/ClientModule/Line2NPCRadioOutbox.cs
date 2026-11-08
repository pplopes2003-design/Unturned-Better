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
        internal const ushort NativeWalkieId = Line2NPCRadioRules.NativeWalkieId;
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
                _sight.VisibleTarget == null ||
                !_inventory.ContainsItemID(NativeWalkieId) ||
                !_membership.Joined || Time.time < _nextSendAt ||
                Time.time < nextChannelAvailableAt ||
                !Provider.isServer || Dedicator.IsDedicatedServer ||
                (Provider.clients != null && Provider.clients.Count > 1))
                return;

            bool playerContact =
                _sight.Kind == Line2NPCCombatPerception.TargetKind.Player;
            bool combatTotalContact =
                Line2NpcCombatTotalPolicy.Enabled(_actor.CombatMode) &&
                (_sight.Kind == Line2NPCCombatPerception.TargetKind.Npc ||
                 _sight.Kind == Line2NPCCombatPerception.TargetKind.Zombie);
            if (!playerContact && !combatTotalContact) return;
            if (playerContact)
            {
                Player player =
                    _sight.VisibleTarget.GetComponentInParent<Player>();
                if (!_actor.PlayerVisible || player == null ||
                    player.life == null || player.life.isDead ||
                    (player.transform.position - transform.position).sqrMagnitude >
                        400f)
                    return;
            }
            else
            {
                // A rival/zombie report is only allowed from a CURRENT,
                // personally verified, living and hostile contact.
                bool livingEnemy = false;
                if (_sight.Kind == Line2NPCCombatPerception.TargetKind.Npc)
                {
                    PrototypeActor rival = _sight.VisibleTarget
                        .GetComponentInParent<PrototypeActor>();
                    livingEnemy = rival != null && !rival.IsDead &&
                        _sight.CanEngageNpcTarget(rival);
                }
                else if (_sight.Kind ==
                    Line2NPCCombatPerception.TargetKind.Zombie)
                {
                    Zombie zombie = _sight.VisibleTarget
                        .GetComponentInParent<Zombie>();
                    livingEnemy = zombie != null && !zombie.isDead;
                }
                float contactDistance = Vector3.Distance(
                    transform.position, _sight.VisibleTarget.position);
                bool firsthand = _sight.HasVerifiedSight(
                    _sight.VisibleTarget, _sight.Kind,
                    Line2NpcCombatTotalSquadPolicy.MaxContactRange);
                if (!Line2NpcCombatTotalSquadPolicy.CanBroadcast(
                    true, firsthand, livingEnemy,
                    _inventory.ContainsItemID(NativeWalkieId),
                    contactDistance))
                    return;
            }

            // Do not radio the exact Player position: spatial grid preserves
            // an approximate 20m sector from one confirmed observation.
            Vector3 seen = _sight.LastVisiblePosition;
            Vector3 sector = new Vector3(
                Line2NPCRadioRules.QuantizeSector(seen.x), seen.y,
                Line2NPCRadioRules.QuantizeSector(seen.z));
            if (float.IsNaN(sector.x) || float.IsNaN(sector.z))
                return;
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
                if (otherGroup == null || otherInventory == null ||
                    !Line2NPCRadioRules.MayRelay(
                        _membership.GroupId, otherGroup.GroupId,
                        _membership.Faction, otherGroup.Faction,
                        !_actor.IsDead, !other.IsDead,
                        _inventory.ContainsItemID(NativeWalkieId),
                        otherInventory.ContainsItemID(NativeWalkieId),
                        Vector3.Distance(other.transform.position,
                            transform.position)))
                    continue;

                if (combatTotalContact)
                {
                    if (other.TryReceiveVerifiedCombatSquadContact(
                            _actor, sector, now))
                        recipients++;
                }
                else
                {
                    other.ReceiveVerifiedRadioReport(sector, now);
                    recipients++;
                }
            }
            _candidates.Clear();
            _nextSendAt = now + SendCooldown;
            nextChannelAvailableAt = now + AirTime;
            if (Time.time >= _nextLogAt)
            {
                ProbeLogger.Log(
                    (combatTotalContact ? "NPC_COMBAT_TOTAL_RADIO_CONTACT" :
                     "NPC_RADIO_CONTACT") +
                    ": group=" + _membership.GroupId +
                    " faction=" + _membership.Faction +
                    " sender=" + _actor.Identity +
                    " threatKind=" + _sight.Kind +
                    " recipients=" + recipients +
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
