using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Finite survival needs in Normal/Passive modes. Uses only actual food
    /// and water already carried by the NPC; never invents or collects loot.
    /// Combat Total bypasses needs. Entirely local-authoritative.
    /// </summary>
    internal sealed class Line2NpcNeeds : MonoBehaviour
    {
        private const float TickSeconds = 12f;
        private const float HungerLossPerSecond = 0.012f;
        private const float ThirstLossPerSecond = 0.018f;
        private PrototypeActor _actor;
        private Line2MobileNPCInventory _inventory;
        private PrototypeNpcVitals _vitals;
        private float _nextTickAt;
        private float _lastSampleAt;
        internal float Hunger { get; private set; } = 100f;
        internal float Thirst { get; private set; } = 100f;
        internal bool IsUrgent { get { return Hunger < 18f || Thirst < 18f; } }

        private void Awake()
        {
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _vitals = GetComponent<PrototypeNpcVitals>();
            _lastSampleAt = Time.time;
            _nextTickAt = Time.time + TickSeconds;
        }

        internal void Initialize(PrototypeActor actor)
        {
            _actor = actor;
            _lastSampleAt = Time.time;
            _nextTickAt = Time.time + TickSeconds;
        }

        internal bool TryRestore(float hunger, float thirst)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                float.IsNaN(hunger) || float.IsInfinity(hunger) ||
                float.IsNaN(thirst) || float.IsInfinity(thirst) ||
                hunger < 0f || hunger > 100f || thirst < 0f || thirst > 100f)
                return false;
            Hunger = hunger;
            Thirst = thirst;
            _lastSampleAt = Time.time;
            return true;
        }

        private void Update()
        {
            if (Time.time < _nextTickAt) return;
            _nextTickAt = Time.time + TickSeconds;
            if (_actor == null || _actor.IsDead || _vitals == null ||
                _inventory == null || !Provider.isServer ||
                Dedicator.IsDedicatedServer || !Level.isLoaded)
            {
                _lastSampleAt = Time.time;
                return;
            }
            float elapsed = Mathf.Clamp(Time.time - _lastSampleAt, 0f, 60f);
            _lastSampleAt = Time.time;
            if (_actor.CombatMode == "total") return;
            Hunger = Mathf.Max(0f, Hunger - elapsed * HungerLossPerSecond);
            Thirst = Mathf.Max(0f, Thirst - elapsed * ThirstLossPerSecond);
            if (Hunger < 40f) TryConsumeOne(true);
            if (Thirst < 40f) TryConsumeOne(false);
            if (Hunger <= 0f || Thirst <= 0f)
                _vitals.ApplyServerDamage(1.25f, Vector3.zero);
        }

        private bool TryConsumeOne(bool food)
        {
            for (int index = 0; index < _inventory.UsedSlots; index++)
            {
                Item snapshot;
                if (!_inventory.TryGetItemSnapshot(index, out snapshot) ||
                    snapshot.amount == 0) continue;
                ItemConsumeableAsset consumable =
                    Assets.find(EAssetType.ITEM, snapshot.id) as ItemConsumeableAsset;
                if (consumable == null ||
                    (food && !(consumable is ItemFoodAsset)) ||
                    (!food && !(consumable is ItemWaterAsset))) continue;
                float value = food ? consumable.food : consumable.water;
                if (value <= 0f) continue;
                Item consumed;
                // Consume exactly one actual unit, preserving the other
                // contents of stacked food/water and their native item state.
                if (!_inventory.TryConsumeOneItemByIdServer(
                    snapshot.id, out consumed)) return false;
                if (food) Hunger = Mathf.Min(100f, Hunger + value);
                else Thirst = Mathf.Min(100f, Thirst + value);
                ProbeLogger.Log("NPC_SURVIVAL_CONSUMED: npc=" + _actor.Identity +
                    " item=" + consumed.id + " kind=" + (food ? "food" : "water"));
                return true;
            }
            return false;
        }
    }
}
