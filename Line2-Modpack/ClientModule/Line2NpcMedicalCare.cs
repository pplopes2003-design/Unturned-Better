using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Adapts Line 1's delayed MedicalBehavior / MedicalUse priorities to
    /// the Steam prototype. Only authentic rag (393), bandage (95),
    /// dressing (394) and splint (96) can be spent. An item remains in
    /// inventory until treatment finishes, ensuring death during treatment
    /// cannot hide or duplicate a reserved item.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcMedicalCare : MonoBehaviour
    {
        private enum Treatment { None, Bandage, SplintLeft, SplintRight, Heal }
        private PrototypeActor _actor;
        private PrototypeNpcVitals _vitals;
        private Line2MobileNPCInjurySystem _injuries;
        private Line2MobileNPCInventory _inventory;
        private Line2NPCCombatPerception _sight;
        private float _nextAttempt;
        private float _finishesAt;
        private Treatment _pending;
        private ushort _itemId;
        private float _healing;
        private float _nextNoSupplyLog;

        internal void Initialize(PrototypeActor actor, Line2NPCCombatPerception sight)
        {
            _actor = actor;
            _vitals = GetComponent<PrototypeNpcVitals>();
            _injuries = GetComponent<Line2MobileNPCInjurySystem>();
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _sight = sight;
            _nextAttempt = Time.time + UnityEngine.Random.Range(2f, 5f);
        }

        private static float DressingHeal(ushort itemID)
        {
            switch (itemID)
            {
                case 393: return 5f;
                case 95: return 15f;
                case 394: return 35f;
                default: return 0f;
            }
        }

        private bool Carries(ushort id)
        {
            if (_inventory == null) return false;
            for (int slot = 0; slot < _inventory.UsedSlots; slot++)
            {
                Item item;
                if (_inventory.TryGetItemSnapshot(slot, out item) &&
                    item != null && item.id == id && item.amount > 0)
                    return true;
            }
            return false;
        }

        private ushort ChooseDressing()
        {
            // Prefer a small native dressing and preserve stronger medicine.
            return Carries(393) ? (ushort)393 :
                Carries(95) ? (ushort)95 :
                Carries(394) ? (ushort)394 : (ushort)0;
        }

        private bool IsActiveDanger()
        {
            // Never stop to bandage while the NPC is fleeing, being shot at,
            // or has a verified enemy in sight. In particular critical health
            // is a reason to seek safety, not permission to heal in the open.
            if (_actor != null && (_actor.IsTacticallyRetreating ||
                _actor.RecentlyDamaged || _actor.UnderVerifiedSuppression))
                return true;
            return _sight != null &&
                _sight.Kind != Line2NPCCombatPerception.TargetKind.None &&
                _sight.VisibleTarget != null;
        }

        private void Update()
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || _actor == null || _vitals == null ||
                _inventory == null || _injuries == null)
                return;
            if (_actor.IsDead)
            {
                // Item was never debited, so death-loot owns it.
                _pending = Treatment.None;
                return;
            }
            if (_pending != Treatment.None)
            {
                if (IsActiveDanger())
                {
                    _pending = Treatment.None;
                    _nextAttempt = Time.time + 3f;
                    return;
                }
                if (Time.time >= _finishesAt)
                    CommitTreatment();
                return;
            }
            if (Time.time < _nextAttempt || IsActiveDanger()) return;
            _nextAttempt = Time.time + 4f;
            ushort dressing = ChooseDressing();
            if (_injuries.CurrentBleeding !=
                Line2MobileNPCInjurySystem.BleedingSeverity.None)
            {
                if (dressing == 0)
                {
                    // Uses only actual carried resources and validated
                    // vanilla recipes; never free-spawns a bandage.
                    _inventory.TryCraftNativeWoundDressingServer();
                    return;
                }
                Begin(Treatment.Bandage, dressing, DressingHeal(dressing));
                return;
            }
            if (_injuries.HasFracturedLeg && Carries(96))
            {
                Treatment fracture = _injuries.LeftLeg.IsFractured
                    ? Treatment.SplintLeft : Treatment.SplintRight;
                Begin(fracture, 96, 0f);
                return;
            }
            if (_vitals.HealthFraction < 0.55f && dressing != 0)
                Begin(Treatment.Heal, dressing, DressingHeal(dressing));
            else if (Time.time >= _nextNoSupplyLog &&
                     (_vitals.HealthFraction < 0.55f ||
                      _injuries.HasFracturedLeg ||
                      _injuries.CurrentBleeding !=
                          Line2MobileNPCInjurySystem.BleedingSeverity.None))
            {
                _nextNoSupplyLog = Time.time + 30f;
                ProbeLogger.Log("NPC_MEDICAL_NO_SUPPLIES: actor=" +
                    _actor.Identity + " health=" +
                    _vitals.Health.ToString("F1") +
                    " dressing=" + dressing +
                    " splint=" + Carries(96));
            }
        }

        private void Begin(Treatment type, ushort itemID, float healing)
        {
            _pending = type;
            _itemId = itemID;
            _healing = healing;
            _finishesAt = Time.time + 2f;
            // The medical action is presentation-only. The consumable is
            // still debited exclusively by CommitTreatment after 2 seconds.
            PrototypeAnimation visual = GetComponent<PrototypeAnimation>();
            if (visual != null) visual.PlayMedicalTreatment(2f);
            ProbeLogger.Log("NPC_MEDICAL_BEGIN: actor=" + _actor.Identity +
                " item=" + itemID + " care=" + type);
        }

        private void CommitTreatment()
        {
            Treatment type = _pending;
            ushort itemId = _itemId;
            float healing = _healing;
            _pending = Treatment.None;
            _nextAttempt = Time.time + 12f;
            // Check the need AGAIN after the 2s animation delay. An unrelated
            // healer might have already treated this NPC in the meantime.
            bool needed = type == Treatment.Bandage
                ? _injuries.CurrentBleeding !=
                  Line2MobileNPCInjurySystem.BleedingSeverity.None
                : type == Treatment.SplintLeft
                    ? _injuries.LeftLeg.IsFractured
                    : type == Treatment.SplintRight
                        ? _injuries.RightLeg.IsFractured
                        : _vitals.HealthFraction < 0.99f;
            if (!needed) return;
            Item consumed;
            if (!_inventory.TryConsumeOneItemByIdServer(itemId, out consumed) ||
                consumed == null)
                return;
            bool used = false;
            switch (type)
            {
                case Treatment.Bandage:
                    used = _injuries.ApplyBandageServer(healing);
                    break;
                case Treatment.SplintLeft:
                    used = _injuries.ApplySplintServer(
                        Line2MobileNPCInjurySystem.BodyPart.LeftLeg);
                    break;
                case Treatment.SplintRight:
                    used = _injuries.ApplySplintServer(
                        Line2MobileNPCInjurySystem.BodyPart.RightLeg);
                    break;
                case Treatment.Heal:
                    used = _vitals.Heal(healing);
                    break;
            }
            if (!used)
            {
                // Return the same unit if treatment could not be applied.
                if (!_inventory.TryStoreTransferredItem(consumed))
                    ProbeLogger.Log("NPC_MEDICAL_ROLLBACK_FAILED: id=" +
                        consumed.id + " actor=" + _actor.Identity);
                return;
            }
            ProbeLogger.Log("NPC_MEDICAL_COMPLETED: actor=" + _actor.Identity +
                " care=" + type + " consumed=" + consumed.id +
                " hp=" + _vitals.Health.ToString("F1"));
        }
    }
}
