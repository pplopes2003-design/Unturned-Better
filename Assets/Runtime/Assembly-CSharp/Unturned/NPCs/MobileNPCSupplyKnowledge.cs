////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Individual, bounded associative memory of ACTUAL world-item pickups:
    /// medicine, food, water, equipment, compatible magazines and construction
    /// supplies. Current demand determines which memory is useful. Site visits
    /// never guarantee loot has respawned, and no map loot database is revealed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCSupplyKnowledge : MonoBehaviour
    {
        [Serializable]
        public sealed class SiteRecord
        {
            public int magazineID; // v4 legacy serialized magazine record; v5 reads this as itemID
            public int itemID;
            public float x;
            public float y;
            public float z;
            public float finds;
            public float failedVisits;
        }

        public struct SpokenSiteLesson
        {
            public ushort ItemID;
            public Vector3 ApproximateLocation;
            public int Sequence;
        }

        private const int MaximumSites = 32;
        private const float SameSiteRadiusSqr = 18f * 18f;
        private readonly List<SiteRecord> sites = new List<SiteRecord>(MaximumSites);
        private MobileNPCHumanoidVitals vitals;
        private bool hasPendingLesson;
        private SpokenSiteLesson pendingLesson;
        private int nextLessonSequence;

        public int KnownSiteCount => sites.Count;

        private void Awake()
        {
            vitals = GetComponent<MobileNPCHumanoidVitals>();
        }

        /// <summary>
        /// Only call AFTER ItemManager confirms an actual transfer. Never learn by
        /// seeing nearby drops, inspecting unloaded buildings or guessing loot.
        /// </summary>
        public bool RecordSuccessfulPickupServer(ushort itemID, byte loadedRounds, Vector3 worldPosition)
        {
            if (!IsAliveServer() || loadedRounds == 0 || itemID == 0 || !ValidPosition(worldPosition))
                return false;
            ItemAsset itemAsset = Assets.find(EAssetType.ITEM, itemID) as ItemAsset;
            if (itemAsset == null)
                return false;
            // An empty magazine isn't proof the site yields useful ammunition.
            if (itemAsset is ItemMagazineAsset magazine && magazine.MaxAmountAsByte == 0)
                return false;

            SiteRecord site = FindSite(itemID, worldPosition);
            if (site == null)
            {
                if (sites.Count >= MaximumSites)
                    EvictLeastUsefulSite();
                site = new SiteRecord
                {
                    itemID = itemID,
                    x = worldPosition.x,
                    y = worldPosition.y,
                    z = worldPosition.z
                };
                sites.Add(site);
            }
            // Evidence saturates; an NPC cannot become certain that a static spawn
            // forever contains ammunition just because it looted it multiple times.
            site.finds = Mathf.Min(12f, site.finds + 1f);
            site.failedVisits *= 0.75f;

            if (++nextLessonSequence <= 0)
                nextLessonSequence = 1;
            hasPendingLesson = true;
            pendingLesson = new SpokenSiteLesson
            {
                ItemID = itemID,
                ApproximateLocation = new Vector3(site.x, site.y, site.z),
                Sequence = nextLessonSequence
            };
            return true;
        }

        /// <summary>One failed physical visit reduces confidence, not erases the site.</summary>
        public void RecordUnsuccessfulVisitServer(ushort itemID, Vector3 sitePosition)
        {
            if (!IsAliveServer())
                return;
            SiteRecord site = FindSite(itemID, sitePosition);
            if (site != null)
                site.failedVisits = Mathf.Min(12f, site.failedVisits + 1f);
        }

        /// <summary>
        /// Returns a remembered place for a magazine compatible with the currently
        /// owned gun, not a currently guaranteed drop. Never returns an unobserved
        /// location or an invalid/unloaded item asset.
        /// </summary>
        public bool TrySuggestCompatibleMagazineSiteServer(ItemGunAsset gun, Vector3 from,
            out Vector3 suggestedPosition, out ushort magazineID)
        {
            suggestedPosition = Vector3.zero;
            magazineID = 0;
            if (!IsAliveServer() || gun == null || gun.magazineCalibers == null
                || gun.magazineCalibers.Length == 0 || !ValidPosition(from))
                return false;

            float bestUtility = float.NegativeInfinity;
            for (int i = 0; i < sites.Count; ++i)
            {
                SiteRecord site = sites[i];
                int siteItemID = ResolveItemID(site);
                if (siteItemID <= 0 || siteItemID > ushort.MaxValue)
                    continue;
                ItemMagazineAsset magazine = Assets.find(EAssetType.ITEM, (ushort)siteItemID) as ItemMagazineAsset;
                if (magazine == null || magazine.MaxAmountAsByte == 0
                    || magazine.calibers == null || magazine.calibers.Length == 0
                    || !magazine.CalibersContainAnyOfIds(gun.magazineCalibers))
                    continue;

                Vector3 knownPlace = new Vector3(site.x, site.y, site.z);
                float distance = Vector3.Distance(from, knownPlace);
                if (distance < 12f || distance > 240f)
                    continue;
                float confidence = (site.finds + 1f) / (site.finds + site.failedVisits + 2f);
                if (confidence < 0.28f)
                    continue;
                // Reward credible past finds, penalize long/uncertain expeditions.
                float utility = confidence * 100f - distance * 0.22f;
                if (utility > bestUtility)
                {
                    bestUtility = utility;
                    suggestedPosition = knownPlace;
                    magazineID = (ushort)siteItemID;
                }
            }
            return magazineID != 0;
        }

        public bool TryGetUnspokenSiteLessonServer(out SpokenSiteLesson lesson)
        {
            lesson = default(SpokenSiteLesson);
            if (!IsAliveServer() || !hasPendingLesson)
                return false;
            lesson = pendingLesson;
            return true;
        }

        public void ConfirmSpokenSiteLessonServer(int sequence)
        {
            if (IsAliveServer() && hasPendingLesson && pendingLesson.Sequence == sequence)
                hasPendingLesson = false;
        }

        /// <summary>
        /// Listener gains weaker, secondhand confidence and never re-broadcasts it
        /// automatically. The caller must first verify local audibility and membership.
        /// </summary>
        public bool ReceiveSpokenSiteLessonServer(SpokenSiteLesson lesson)
        {
            if (!IsAliveServer() || lesson.ItemID == 0 || !ValidPosition(lesson.ApproximateLocation)
                || !(Assets.find(EAssetType.ITEM, lesson.ItemID) is ItemAsset))
                return false;

            SiteRecord site = FindSite(lesson.ItemID, lesson.ApproximateLocation);
            if (site == null)
            {
                if (sites.Count >= MaximumSites)
                    EvictLeastUsefulSite();
                site = new SiteRecord
                {
                    itemID = lesson.ItemID,
                    x = lesson.ApproximateLocation.x,
                    y = lesson.ApproximateLocation.y,
                    z = lesson.ApproximateLocation.z
                };
                sites.Add(site);
            }
            site.finds = Mathf.Min(12f, site.finds + 0.3f);
            return true;
        }

        public List<SiteRecord> ExportSnapshotServer()
        {
            List<SiteRecord> snapshot = new List<SiteRecord>(MaximumSites);
            if (!IsAliveServer())
                return snapshot;
            for (int i = 0; i < sites.Count; ++i)
                snapshot.Add(Clone(sites[i]));
            return snapshot;
        }

        public void RestoreSnapshotServer(List<SiteRecord> snapshot)
        {
            if (!IsAliveServer())
                return;
            sites.Clear();
            hasPendingLesson = false;
            nextLessonSequence = 0;
            if (snapshot == null)
                return;
            for (int i = 0; i < snapshot.Count && sites.Count < MaximumSites; ++i)
            {
                SiteRecord site = snapshot[i];
                if (!ValidRecord(site)
                    || !(Assets.find(EAssetType.ITEM, (ushort)ResolveItemID(site)) is ItemAsset)
                    || FindSite((ushort)ResolveItemID(site), new Vector3(site.x, site.y, site.z)) != null)
                    continue;
                sites.Add(Clone(site));
            }
        }

        public void ForgetOnDeathServer()
        {
            if (!Provider.isServer)
                return;
            sites.Clear();
            hasPendingLesson = false;
        }

        private bool IsAliveServer()
        {
            return Provider.isServer && vitals != null && !vitals.IsDead;
        }

        private SiteRecord FindSite(ushort itemID, Vector3 position)
        {
            for (int i = 0; i < sites.Count; ++i)
            {
                SiteRecord site = sites[i];
                if (site == null || ResolveItemID(site) != itemID)
                    continue;
                Vector3 knownPlace = new Vector3(site.x, site.y, site.z);
                Vector3 delta = knownPlace - position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= SameSiteRadiusSqr)
                    return site;
            }
            return null;
        }

        /// <summary>
        /// Demand-weighted memory search for supplies the actor can actually use
        /// now. Native item category and magazine caliber constrain usefulness.
        /// Future systems can add more needs without teaching hidden spawn tables.
        /// </summary>
        public bool TrySuggestNeededSupplySiteServer(MobileNPCInventory inventory,
            MobileNPCHumanoidVitals health, MobileNPCInjurySystem injuries,
            MobileNPCSurvivalNeeds survival, Vector3 from,
            out Vector3 position, out ushort itemID)
        {
            position = Vector3.zero;
            itemID = 0;
            if (!IsAliveServer() || inventory == null || !ValidPosition(from))
                return false;

            ItemGunAsset ownedGun = null;
            Item gunSnapshot = null;
            inventory.TryGetBestGun(out gunSnapshot, out ownedGun);
            float bestUtility = float.NegativeInfinity;
            for (int i = 0; i < sites.Count; ++i)
            {
                SiteRecord site = sites[i];
                int candidateID = ResolveItemID(site);
                if (candidateID <= 0 || candidateID > ushort.MaxValue)
                    continue;
                ItemAsset asset = Assets.find(EAssetType.ITEM, (ushort)candidateID) as ItemAsset;
                if (asset == null)
                    continue;
                float need = GetCurrentItemNeed(asset, inventory, health, injuries,
                    survival, gunSnapshot, ownedGun);
                if (need <= 0f)
                    continue;

                Vector3 remembered = new Vector3(site.x, site.y, site.z);
                float distance = Vector3.Distance(from, remembered);
                if (distance < 12f || distance > 240f)
                    continue;
                // Avoid fatal long expeditions for active bleeding. Emergency
                // supplies or salvage garments should be nearby, or a higher
                // priority survival/treatment decision should take over.
                bool activelyBleeding = injuries != null
                    && injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None;
                if (activelyBleeding && distance > 75f)
                    continue;
                float confidence = (site.finds + 1f) /
                    (site.finds + site.failedVisits + 2f);
                if (confidence < 0.28f)
                    continue;
                float utility = need * confidence - distance * 0.2f;
                if (utility > bestUtility)
                {
                    bestUtility = utility;
                    position = remembered;
                    itemID = (ushort)candidateID;
                }
            }
            return itemID != 0;
        }

        /// <summary>
        /// Strictly need-driven utility. Never schedule a trip merely because an
        /// object is known. Medical care, hydration and hunger outrank nonessential
        /// spare equipment; compatible magazines use the vanilla caliber list.
        /// </summary>
        private static float GetCurrentItemNeed(ItemAsset asset,
            MobileNPCInventory inventory, MobileNPCHumanoidVitals health,
            MobileNPCInjurySystem injuries, MobileNPCSurvivalNeeds survival,
            Item heldGun, ItemGunAsset gun)
        {
            switch (asset.type)
            {
                case EItemType.MEDICAL:
                {
                    bool bleeding = injuries != null
                        && injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None;
                    bool fractured = injuries != null && injuries.HasFracturedLeg;
                    bool hurt = health != null && health.HealthFraction < 0.68f;
                    bool hasWoundDressing = inventory.ContainsItemID(393)
                        || inventory.ContainsItemID(95) || inventory.ContainsItemID(394);
                    // An arbitrary medicine is not a dressing. If bleeding, first
                    // seek rag, bandage or dressing; if fractured, seek a splint.
                    if (bleeding && !hasWoundDressing
                        && (asset.id == 393 || asset.id == 95 || asset.id == 394))
                        return 165f;
                    if (fractured && !inventory.ContainsItemID(96) && asset.id == 96)
                        return 145f;
                    if (hurt && inventory.CountItemType(EItemType.MEDICAL) == 0)
                        return 112f;
                    return 0f;
                }
                case EItemType.FOOD:
                    return survival != null && survival.NeedsFood
                        && inventory.CountItemType(EItemType.FOOD) == 0 ? 92f : 0f;
                case EItemType.WATER:
                    return survival != null && survival.NeedsWater
                        && inventory.CountItemType(EItemType.WATER) == 0 ? 110f : 0f;
                case EItemType.MAGAZINE:
                {
                    ItemMagazineAsset magazine = asset as ItemMagazineAsset;
                    if (gun == null || heldGun == null || heldGun.state == null
                        || heldGun.state.Length < 11 || heldGun.state[10] > 8
                        || magazine == null || magazine.MaxAmountAsByte == 0
                        || magazine.calibers == null || gun.magazineCalibers == null
                        || !magazine.CalibersContainAnyOfIds(gun.magazineCalibers)
                        || inventory.TryFindCompatibleMagazine(gun,
                            out int spareSlot, out ItemMagazineAsset spareAsset))
                        return 0f;
                    return 90f;
                }
                case EItemType.GUN:
                    return inventory.CountItemType(EItemType.GUN) == 0 ? 98f : 0f;
                case EItemType.MELEE:
                    return inventory.CountItemType(EItemType.GUN) == 0
                        && inventory.CountItemType(EItemType.MELEE) == 0 ? 80f : 0f;
                case EItemType.BACKPACK:
                case EItemType.VEST:
                    return inventory.CountItemType(asset.type) == 0 ? 45f : 0f;
                case EItemType.SHIRT:
                case EItemType.PANTS:
                {
                    if (inventory.CountItemType(asset.type) > 0)
                        return 0f;
                    bool requiresDressing = injuries != null
                        && injuries.CurrentBleeding != MobileNPCInjurySystem.BleedingSeverity.None
                        && !inventory.ContainsItemID(393)
                        && !inventory.ContainsItemID(95)
                        && !inventory.ContainsItemID(394);
                    // Garments are *potential* ingredients. The actual conversion
                    // only happens if the vanilla blueprint chain exists and all
                    // supplies are in inventory. Never hand out free bandages.
                    return requiresDressing ? 70f : 25f;
                }
                case EItemType.BARRICADE:
                case EItemType.STORAGE:
                case EItemType.STRUCTURE:
                    return inventory.CountItemType(asset.type) == 0 ? 35f : 0f;
                default:
                    return 0f;
            }
        }

        private static int ResolveItemID(SiteRecord site)
        {
            return site == null ? 0 : site.itemID > 0 ? site.itemID : site.magazineID;
        }

        private void EvictLeastUsefulSite()
        {
            int weakest = 0;
            float weakestUtility = float.PositiveInfinity;
            for (int i = 0; i < sites.Count; ++i)
            {
                SiteRecord site = sites[i];
                float utility = site.finds - site.failedVisits;
                if (utility < weakestUtility)
                {
                    weakest = i;
                    weakestUtility = utility;
                }
            }
            sites.RemoveAt(weakest);
        }

        private static SiteRecord Clone(SiteRecord source)
        {
            return new SiteRecord
            {
                magazineID = source.magazineID,
                itemID = source.itemID,
                x = source.x, y = source.y, z = source.z,
                finds = source.finds, failedVisits = source.failedVisits
            };
        }

        private static bool ValidRecord(SiteRecord site)
        {
            return site != null && ResolveItemID(site) > 0 && ResolveItemID(site) <= ushort.MaxValue
                && ValidPosition(new Vector3(site.x, site.y, site.z))
                && !float.IsNaN(site.finds) && !float.IsInfinity(site.finds)
                && !float.IsNaN(site.failedVisits) && !float.IsInfinity(site.failedVisits)
                && site.finds >= 0f && site.finds <= 12f
                && site.failedVisits >= 0f && site.failedVisits <= 12f;
        }

        private static bool ValidPosition(Vector3 position)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x)
                && !float.IsNaN(position.y) && !float.IsInfinity(position.y)
                && !float.IsNaN(position.z) && !float.IsInfinity(position.z)
                && Mathf.Abs(position.x) < 1000000f
                && Mathf.Abs(position.z) < 1000000f;
        }
    }
}
