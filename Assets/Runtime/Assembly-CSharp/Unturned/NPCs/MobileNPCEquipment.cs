////////////////////////////////////////////////////////////////////////////////////////
// U3 SDK mobile NPC integration. See project LICENSE.
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Real owned clothing only. The native HumanClothes presents visuals and
    /// ItemClothingAsset supplies quality-dependent armor for each native limb.
    /// Equipment stays in NPCInventory for real saving, crafting and death loot.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCInventory))]
    public sealed class MobileNPCEquipment : MonoBehaviour
    {
        private MobileNPCInventory inventory;
        private MobileNPCHumanoidVitals vitals;
        private HumanClothes visual;
        private float nextRefresh;
        private ushort hatID, shirtID, pantsID, vestID, backpackID, maskID, glassesID;

        private void Awake()
        {
            inventory = GetComponent<MobileNPCInventory>();
            vitals = GetComponent<MobileNPCHumanoidVitals>();
        }

        private void Update()
        {
            if (!Provider.isServer || !Level.isLoaded || inventory == null
                || vitals != null && vitals.IsDead || Time.time < nextRefresh)
                return;
            nextRefresh = Time.time + 1.5f;
            HumanClothes current = GetComponentInChildren<HumanClothes>(true);
            if (current == null)
                return;
            ushort h = SelectServer(EItemType.HAT, out _, out _);
            ushort s = SelectServer(EItemType.SHIRT, out _, out _);
            ushort p = SelectServer(EItemType.PANTS, out _, out _);
            ushort v = SelectServer(EItemType.VEST, out _, out _);
            ushort bp = SelectServer(EItemType.BACKPACK, out _, out _);
            ushort m = SelectServer(EItemType.MASK, out _, out _);
            ushort g = SelectServer(EItemType.GLASSES, out _, out _);
            if (current == visual && h == hatID && s == shirtID && p == pantsID
                && v == vestID && bp == backpackID && m == maskID && g == glassesID)
                return;
            visual = current;
            hatID = h; shirtID = s; pantsID = p; vestID = v;
            backpackID = bp; maskID = m; glassesID = g;
            current.hat = h; current.shirt = s; current.pants = p;
            current.vest = v; current.backpack = bp;
            current.mask = m; current.glasses = g;
            current.apply();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[MobileNPCEquipment] Real inventory clothing on " + name
                + ": hat=" + h + ", shirt=" + s + ", pants=" + p + ", vest=" + v, this);
#endif
        }

        /// <summary>Native Earpiece is a MASK asset. A worn, owned
        /// Earpiece receives on the selected channel, never transmits.</summary>
        public bool HasWornEarpieceServer
        {
            get
            {
                if (!Provider.isServer || vitals != null && vitals.IsDead
                    || maskID == 0 || inventory == null)
                    return false;
                ItemMaskAsset mask = Assets.find(EAssetType.ITEM, maskID) as ItemMaskAsset;
                return mask != null && mask.isEarpiece
                    && inventory.ContainsItemID(maskID);
            }
        }

        /// <summary>
        /// Mirrors DamageTool.getPlayerArmor's anatomical allocation. Return
        /// 1 when unarmored. Native clothing quality is debited before protection.
        /// </summary>
        public float ApplyValidatedBodyArmorServer(MobileNPCInjurySystem.BodyPart part)
        {
            if (!Provider.isServer || inventory == null)
                return 1f;
            switch (part)
            {
                case MobileNPCInjurySystem.BodyPart.Head:
                    return OneArmorServer(EItemType.HAT);
                case MobileNPCInjurySystem.BodyPart.LeftArm:
                case MobileNPCInjurySystem.BodyPart.RightArm:
                    return OneArmorServer(EItemType.SHIRT);
                case MobileNPCInjurySystem.BodyPart.LeftLeg:
                case MobileNPCInjurySystem.BodyPart.RightLeg:
                    return OneArmorServer(EItemType.PANTS);
                case MobileNPCInjurySystem.BodyPart.Torso:
                    return OneArmorServer(EItemType.SHIRT) * OneArmorServer(EItemType.VEST);
                default:
                    return 1f;
            }
        }

        private float OneArmorServer(EItemType kind)
        {
            ushort id = SelectServer(kind, out int slot, out byte quality);
            if (id == 0)
                return 1f;
            ItemClothingAsset asset = Assets.find(EAssetType.ITEM, id) as ItemClothingAsset;
            if (asset == null)
                return 1f;
            if (Provider.modeConfigData.Items.ShouldClothingTakeDamage && quality > 0
                && inventory.TryDegradeWornClothingQualityServer(slot, id))
                --quality;
            float effective = asset.armor + (1f - asset.armor) * (1f - quality / 100f);
            return Mathf.Clamp(effective, 0.01f, 1f);
        }

        private ushort SelectServer(EItemType kind, out int index, out byte quality)
        {
            index = -1;
            quality = 0;
            if (!Provider.isServer || inventory == null)
                return 0;
            ushort id = 0;
            float best = float.NegativeInfinity;
            for (int slot = 0; slot < inventory.UsedSlots; ++slot)
            {
                if (!inventory.TryGetItemSnapshot(slot, out Item item) || item == null)
                    continue;
                ItemClothingAsset asset = Assets.find(EAssetType.ITEM, item.id) as ItemClothingAsset;
                if (asset == null || asset.type != kind || asset.isPro)
                    continue;
                float multiplier = asset.armor + (1f - asset.armor) * (1f - item.quality / 100f);
                float score = (1f - Mathf.Clamp01(multiplier)) * 100f + item.quality * 0.01f;
                if (score <= best)
                    continue;
                best = score;
                index = slot;
                id = item.id;
                quality = item.quality;
            }
            return id;
        }
    }
}
