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
    /// Individual, bounded associative memory of where an ACTUAL server-validated
    /// magazine was picked up. Compatibility with an owned gun is checked against
    /// native ItemGunAsset / ItemMagazineAsset calibers, never a guessed item name.
    /// A memory is a search hypothesis, not proof that loot currently respawned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCSupplyKnowledge : MonoBehaviour
    {
        [Serializable]
        public sealed class SiteRecord
        {
            public int magazineID;
            public float x;
            public float y;
            public float z;
            public float finds;
            public float failedVisits;
        }

        public struct SpokenSiteLesson
        {
            public ushort MagazineID;
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
        /// Only call AFTER ItemManager confirms an actual transfer and the NPC owns
        /// the transferred item. Nearby visible items are NOT memorized as finds.
        /// </summary>
        public bool RecordSuccessfulPickupServer(ushort itemID, Vector3 worldPosition)
        {
            if (!IsAliveServer() || !ValidPosition(worldPosition))
                return false;
            ItemMagazineAsset magazine = Assets.find(EAssetType.ITEM, itemID) as ItemMagazineAsset;
            if (magazine == null || magazine.MaxAmountAsByte == 0)
                return false;

            SiteRecord site = FindSite(itemID, worldPosition);
            if (site == null)
            {
                if (sites.Count >= MaximumSites)
                    EvictLeastUsefulSite();
                site = new SiteRecord
                {
                    magazineID = itemID,
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
                MagazineID = itemID,
                ApproximateLocation = new Vector3(site.x, site.y, site.z),
                Sequence = nextLessonSequence
            };
            return true;
        }

        /// <summary>One failed physical visit reduces confidence, not erases the site.</summary>
        public void RecordUnsuccessfulVisitServer(ushort magazineID, Vector3 sitePosition)
        {
            if (!IsAliveServer())
                return;
            SiteRecord site = FindSite(magazineID, sitePosition);
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
                if (site == null || site.magazineID <= 0 || site.magazineID > ushort.MaxValue)
                    continue;
                ItemMagazineAsset magazine = Assets.find(EAssetType.ITEM, (ushort)site.magazineID) as ItemMagazineAsset;
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
                    magazineID = (ushort)site.magazineID;
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
            if (!IsAliveServer() || lesson.MagazineID == 0 || !ValidPosition(lesson.ApproximateLocation)
                || !(Assets.find(EAssetType.ITEM, lesson.MagazineID) is ItemMagazineAsset))
                return false;

            SiteRecord site = FindSite(lesson.MagazineID, lesson.ApproximateLocation);
            if (site == null)
            {
                if (sites.Count >= MaximumSites)
                    EvictLeastUsefulSite();
                site = new SiteRecord
                {
                    magazineID = lesson.MagazineID,
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
                    || !(Assets.find(EAssetType.ITEM, (ushort)site.magazineID) is ItemMagazineAsset)
                    || FindSite((ushort)site.magazineID, new Vector3(site.x, site.y, site.z)) != null)
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
                if (site == null || site.magazineID != itemID)
                    continue;
                Vector3 knownPlace = new Vector3(site.x, site.y, site.z);
                Vector3 delta = knownPlace - position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= SameSiteRadiusSqr)
                    return site;
            }
            return null;
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
                x = source.x, y = source.y, z = source.z,
                finds = source.finds, failedVisits = source.failedVisits
            };
        }

        private static bool ValidRecord(SiteRecord site)
        {
            return site != null && site.magazineID > 0 && site.magazineID <= ushort.MaxValue
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
