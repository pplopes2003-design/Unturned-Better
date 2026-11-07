using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Finite local scavenging from ACTUALLY spawned native ItemData/ItemDrop.
    /// Unlike the Line 1 SDK's engine extension, this only requests the
    /// singleplayer transfer in Line2MobileNPCInventory. Never invents a
    /// world item from a map spawn hint or remote radio report.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcWorldLootCollector : MonoBehaviour
    {
        private const float Radius = 3.0f;
        private const int MaximumChecked = 100;
        private const float TickSeconds = 3.8f;
        private readonly List<RegionCoordinate> _regions =
            new List<RegionCoordinate>(9);
        private PrototypeActor _actor;
        private Line2MobileNPCInventory _inventory;
        private Line2NpcNeeds _needs;
        private Line2MobileNPCInjurySystem _injuries;
        private Line2NPCCombatPerception _perception;
        private Line2NpcWorldSurvey _survey;
        private float _nextAt;
        private uint _lastDeniedID;
        private float _lastDeniedUntil;

        internal void Initialize(PrototypeActor actor,
            Line2NPCCombatPerception perception)
        {
            _actor = actor;
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _needs = GetComponent<Line2NpcNeeds>();
            _injuries = GetComponent<Line2MobileNPCInjurySystem>();
            _perception = perception;
            _survey = GetComponent<Line2NpcWorldSurvey>();
            _nextAt = Time.time + UnityEngine.Random.Range(1.0f, TickSeconds);
        }

        private static bool LocalAuthority()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                (Provider.clients == null || Provider.clients.Count <= 1) &&
                Level.isLoaded && ItemManager.instance != null &&
                ItemManager.regions != null;
        }

        private int Score(ItemAsset asset)
        {
            if (asset == null || _inventory == null) return -1000;
            bool combatOnly = _actor.CombatMode == "total";
            switch (asset.type)
            {
                case EItemType.GUN:
                    Item gun;
                    ItemGunAsset best;
                    return !_inventory.TryGetBestGun(out gun, out best) ? 115 : 24;
                case EItemType.MAGAZINE:
                    {
                        Item ownedGun;
                        ItemGunAsset gunAsset;
                        ItemMagazineAsset magazine = asset as ItemMagazineAsset;
                        if (magazine == null) return -1000;
                        bool ordinaryCompatible =
                            _inventory.TryGetBestGun(out ownedGun, out gunAsset) &&
                            gunAsset != null &&
                            magazine.CalibersContainAnyOfIds(gunAsset.magazineCalibers);
                        return ordinaryCompatible ||
                            _inventory.IsCompatibleRaidMagazineServer(magazine)
                            ? 110 : 20;
                    }
                case EItemType.MELEE:
                    Item existing;
                    ItemMeleeAsset melee;
                    return !_inventory.TryGetBestMeleeServer(out existing,
                        out melee) ? 80 : 18;
                case EItemType.FOOD:
                    if (combatOnly) return -1000;
                    return _needs != null && _needs.Hunger < 40f ? 130
                        : _needs != null && _needs.Hunger < 70f ? 85 : 32;
                case EItemType.WATER:
                    if (combatOnly) return -1000;
                    return _needs != null && _needs.Thirst < 40f ? 135
                        : _needs != null && _needs.Thirst < 70f ? 90 : 33;
                case EItemType.MEDICAL:
                    return _injuries != null && _injuries.NeedsMedicalAttention
                        ? 125 : 65;
                case EItemType.THROWABLE:
                    return _actor.CombatMode != "passive" ? 53 : -1000;
                case EItemType.BARRICADE:
                case EItemType.STORAGE:
                case EItemType.STRUCTURE:
                    return combatOnly ? -1000 :
                        _inventory.CountItemType(asset.type) < 3 ? 58 : 20;
                case EItemType.BACKPACK:
                case EItemType.VEST:
                    return _inventory.CountItemType(asset.type) == 0 ? 55 : 12;
                case EItemType.SUPPLY:
                    return combatOnly ? -1000 : 28;
                default:
                    return -1000; // Do not fill capacity with arbitrary junk.
            }
        }

        private void Update()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + TickSeconds;
            if (!LocalAuthority() || _actor == null || _actor.IsDead ||
                _inventory == null || !_inventory.HasSpace ||
                _actor.IsTacticallyRetreating ||
                (_perception != null &&
                 _perception.Kind != Line2NPCCombatPerception.TargetKind.None &&
                 _perception.VisibleTarget != null))
                return;
            Vector3 here = transform.position;
            _regions.Clear();
            Regions.getRegionsInRadius(here, Radius + 1.0f, _regions);

            // Score the same authoritative ItemData used by ItemManager's
            // native server pickup. The receiving inventory will revalidate
            // the precise instance, physical drop, LOS and ownership.
            uint bestID = 0;
            float bestScore = 48f;
            int checkedCount = 0;
            for (int i = 0; i < _regions.Count && checkedCount < MaximumChecked; i++)
            {
                RegionCoordinate c = _regions[i];
                if (c.x >= ItemManager.regions.GetLength(0) ||
                    c.y >= ItemManager.regions.GetLength(1)) continue;
                ItemRegion region = ItemManager.regions[c.x, c.y];
                if (region == null || region.items == null) continue;
                int stride = Mathf.Max(1,
                    Mathf.CeilToInt(region.items.Count / 35f));
                for (int j = 0; j < region.items.Count &&
                     checkedCount < MaximumChecked; j += stride)
                {
                    ++checkedCount;
                    ItemData data = region.items[j];
                    if (data == null || data.item == null ||
                        data.instanceID == 0 ||
                        (data.instanceID == _lastDeniedID &&
                         Time.time < _lastDeniedUntil))
                        continue;
                    Vector3 gap = data.point - here;
                    if (gap.sqrMagnitude > Radius * Radius ||
                        Mathf.Abs(gap.y) > 1.3f) continue;
                    ItemAsset asset = Assets.find(EAssetType.ITEM,
                        data.item.id) as ItemAsset;
                    if (asset == null || asset.isPro) continue;
                    int score = Score(asset);
                    float adjusted = score - gap.magnitude * 2.1f;
                    if (adjusted <= bestScore) continue;
                    bestID = data.instanceID;
                    bestScore = adjusted;
                }
            }
            if (bestID == 0) return;
            string reason;
            if (_inventory.TryPickupWorldItemServer(bestID, here,
                Radius + 0.35f, out reason))
            {
                if (_survey != null) _survey.RecordVerifiedWorldPickup();
                ProbeLogger.Log("NPC_WORLD_PICKUP_COMMITTED: actor=" +
                    _actor.Identity + " instance=" + bestID +
                    " importance=" + bestScore.ToString("F1"));
            }
            else
            {
                _lastDeniedID = bestID;
                _lastDeniedUntil = Time.time + 12f;
                if (reason != "world-item-occluded")
                    ProbeLogger.Log("NPC_WORLD_PICKUP_DEFERRED: instance=" +
                        bestID + " reason=" + reason);
            }
        }
    }
}
