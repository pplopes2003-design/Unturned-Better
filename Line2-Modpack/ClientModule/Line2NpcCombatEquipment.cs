using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Steam module port of Line 1 MobileNPCEquipment for Combat Total.
    /// Uses only inventory-owned native clothing, never a decorative
    /// appearance that grants free armor. Does not change Normal mode.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcCombatEquipment : MonoBehaviour
    {
        private Line2MobileNPCInventory _inventory;
        private PrototypeNpcVitals _vitals;
        private HumanClothes _visual;
        private float _nextRefresh;
        private ushort _hat, _shirt, _pants, _vest, _backpack;
        private ushort _mask, _glasses;

        private void Awake()
        {
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _vitals = GetComponent<PrototypeNpcVitals>();
        }

        private void Update()
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                !Level.isLoaded || _inventory == null ||
                _vitals == null || _vitals.IsDead ||
                Time.time < _nextRefresh)
                return;
            _nextRefresh = Time.time + 1.5f;
            HumanClothes visual = GetComponentInChildren<HumanClothes>(true);
            if (visual == null) return;
            ushort h = Select(EItemType.HAT, out _, out _);
            ushort s = Select(EItemType.SHIRT, out _, out _);
            ushort p = Select(EItemType.PANTS, out _, out _);
            ushort v = Select(EItemType.VEST, out _, out _);
            ushort bp = Select(EItemType.BACKPACK, out _, out _);
            ushort m = Select(EItemType.MASK, out _, out _);
            ushort g = Select(EItemType.GLASSES, out _, out _);
            if (_visual == visual && h == _hat && s == _shirt &&
                p == _pants && v == _vest && bp == _backpack &&
                m == _mask && g == _glasses)
                return;
            _visual = visual;
            _hat = h; _shirt = s; _pants = p; _vest = v;
            _backpack = bp; _mask = m; _glasses = g;
            visual.hat = h; visual.shirt = s; visual.pants = p;
            visual.vest = v; visual.backpack = bp;
            visual.mask = m; visual.glasses = g;
            visual.apply();
        }

        internal float ApplyValidatedBodyArmorServer(
            Line2MobileNPCInjurySystem.BodyPart part)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                _inventory == null || _vitals == null || _vitals.IsDead)
                return 1f;
            switch (part)
            {
                case Line2MobileNPCInjurySystem.BodyPart.Head:
                    return OneArmor(EItemType.HAT);
                case Line2MobileNPCInjurySystem.BodyPart.LeftArm:
                case Line2MobileNPCInjurySystem.BodyPart.RightArm:
                    return OneArmor(EItemType.SHIRT);
                case Line2MobileNPCInjurySystem.BodyPart.LeftLeg:
                case Line2MobileNPCInjurySystem.BodyPart.RightLeg:
                    return OneArmor(EItemType.PANTS);
                case Line2MobileNPCInjurySystem.BodyPart.Torso:
                    return OneArmor(EItemType.SHIRT) *
                           OneArmor(EItemType.VEST);
                default:
                    return 1f;
            }
        }

        private float OneArmor(EItemType kind)
        {
            int slot;
            byte quality;
            ushort id = Select(kind, out slot, out quality);
            if (id == 0) return 1f;
            ItemClothingAsset asset =
                Assets.find(EAssetType.ITEM, id) as ItemClothingAsset;
            if (asset == null) return 1f;
            if (Provider.modeConfigData != null &&
                Provider.modeConfigData.Items.ShouldClothingTakeDamage &&
                quality > 0 &&
                _inventory.TryDegradeWornClothingQualityServer(slot, id))
                quality--;
            return Line2NpcCombatArmorPolicy.EffectiveMultiplier(
                asset.armor, quality);
        }

        private ushort Select(EItemType kind, out int slot, out byte quality)
        {
            slot = -1;
            quality = 0;
            if (!Provider.isServer || _inventory == null)
                return 0;
            float best = float.NegativeInfinity;
            ushort selected = 0;
            for (int i = 0; i < _inventory.UsedSlots; ++i)
            {
                Item owned;
                if (!_inventory.TryGetItemSnapshot(i, out owned) ||
                    owned == null) continue;
                ItemClothingAsset candidate =
                    Assets.find(EAssetType.ITEM, owned.id) as ItemClothingAsset;
                if (candidate == null || candidate.type != kind ||
                    candidate.isPro)
                    continue;
                float reduction = Line2NpcCombatArmorPolicy.EffectiveMultiplier(
                    candidate.armor, owned.quality);
                float score = (1f - reduction) * 100f +
                    Mathf.Clamp(owned.quality, 0, 100) * 0.01f;
                if (score <= best) continue;
                best = score;
                slot = i;
                selected = owned.id;
                quality = owned.quality;
            }
            return selected;
        }
    }
}
