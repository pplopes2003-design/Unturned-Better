using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Real, small group campsite prototype. It places at most four actually
    /// OWNED native barricades into BarricadeManager's saved world. It is NOT
    /// a decorative GameObject and creates no artificial wall/loot.
    ///
    /// Avoids vanilla structures because their modern HousingConnections
    /// placement validation depends on a Player owner and physical snapping.
    /// No spoofed SteamID, group owner, housing link or free prefab is used.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcSettlementBuilder : MonoBehaviour
    {
        private const int MaxPieces = 4;
        private const int MaxPlacementHits = 48;
        private const float AttemptInterval = 11f;
        private readonly Collider[] _nearby = new Collider[MaxPlacementHits];
        private PrototypeActor _actor;
        private Line2NPCGroupMember _group;
        private Line2MobileNPCInventory _inventory;
        private Line2NPCCombatPerception _sight;
        private SteamNpcPrototypeRuntime _runtime;
        private float _nextAt;
        private float _nextPlacementAt;
        private float _nextLog;
        internal void Initialize(SteamNpcPrototypeRuntime runtime,
            PrototypeActor actor, Line2NPCCombatPerception sight)
        {
            _actor = actor;
            _runtime = runtime;
            _group = GetComponent<Line2NPCGroupMember>();
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _sight = sight;
            _nextAt = Time.time + UnityEngine.Random.Range(4f, 9f);
            _nextPlacementAt = Time.time + 25f;
        }

        private static bool Authorized()
        {
            return Provider.isServer && !Dedicator.IsDedicatedServer &&
                (Provider.clients == null || Provider.clients.Count <= 1) &&
                Provider.hasCheats && Level.isLoaded && Player.player != null &&
                BarricadeManager.instance != null;
        }

        private bool IsSafeRoutine()
        {
            return Authorized() && _actor != null && !_actor.IsDead &&
                !_actor.IsRecruited && !_actor.IsTacticallyRetreating &&
                _group != null && _group.Joined && _inventory != null &&
                (_sight == null ||
                 _sight.Kind == Line2NPCCombatPerception.TargetKind.None ||
                 _sight.VisibleTarget == null);
        }

        private bool HasOwnedBuildable(out ushort itemID,
            out ItemBarricadeAsset asset)
        {
            itemID = 0;
            asset = null;
            if (_inventory == null) return false;
            for (int slot = 0; slot < _inventory.UsedSlots; slot++)
            {
                Item item;
                if (!_inventory.TryGetItemSnapshot(slot, out item) ||
                    item == null || item.amount == 0) continue;
                ItemBarricadeAsset candidate = Assets.find(EAssetType.ITEM,
                    item.id) as ItemBarricadeAsset;
                if (candidate == null || candidate.barricade == null ||
                    candidate.health == 0 || !candidate.isSaveable ||
                    candidate.isPro)
                    continue;
                // Conservative native ground-camp categories. Doors,
                // storage, charges, traps, sentries and generators have
                // extra vanilla owner/placement requirements.
                if (candidate.build != EBuild.BARRICADE &&
                    candidate.build != EBuild.FORTIFICATION &&
                    candidate.build != EBuild.CAMPFIRE)
                    continue;
                itemID = item.id;
                asset = candidate;
                return true;
            }
            return false;
        }

        internal bool TryGetConstructionGoalServer(out Vector3 goal)
        {
            goal = Vector3.zero;
            ushort itemID;
            ItemBarricadeAsset asset;
            if (!IsSafeRoutine() || !HasOwnedBuildable(out itemID, out asset))
                return false;
            Vector3 site;
            int count;
            if (!Line2NpcSettlementLedger.TryGet(_group.GroupId,
                _actor.Disposition, out site, out count))
            {
                PrototypeActor leader;
                if (!Line2NPCGroupMember.TryGetLivingMember(
                    _group.GroupId, out leader) || leader != _actor)
                    return false; // One planner claims the first map site.
                if (!TryFindSafeCamp(out site) ||
                    !Line2NpcSettlementLedger.TryClaim(
                        _group.GroupId, _actor.Disposition, site))
                    return false;
                count = 0;
            }
            if (count >= MaxPieces) return false;
            // Keep the patrol goal anchored at the claimed campsite even
            // when already in range. Otherwise random patrol would walk the
            // worker away before its next bounded native build interval.
            goal = site;
            return true;
        }

        private bool TryFindSafeCamp(out Vector3 site)
        {
            site = Vector3.zero;
            Player player = Player.player;
            Vector3 center = _actor != null ? _actor.HomePosition : transform.position;
            for (int i = 0; i < 16; i++)
            {
                float theta = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float length = UnityEngine.Random.Range(9f, 20f);
                Vector3 candidate = center + new Vector3(
                    Mathf.Cos(theta) * length, 0f,
                    Mathf.Sin(theta) * length);
                Vector3 foot;
                if (!TryGround(candidate, out foot) ||
                    (player.transform.position - foot).sqrMagnitude < 20f * 20f ||
                    !ClearFootprint(foot, 2.2f))
                    continue;
                site = foot;
                return true;
            }
            return false;
        }

        private bool TryGround(Vector3 estimated, out Vector3 foot)
        {
            foot = Vector3.zero;
            if (!Level.checkSafeIncludingClipVolumes(estimated)) return false;
            RaycastHit ground;
            if (!Physics.Raycast(estimated + Vector3.up * 2.2f,
                Vector3.down, out ground, 5f, RayMasks.GROUND2,
                QueryTriggerInteraction.Ignore) ||
                ground.normal.y < 0.83f ||
                Mathf.Abs(ground.point.y - estimated.y) > 2f)
                return false;
            foot = ground.point + Vector3.up * 0.06f;
            return Level.checkSafeIncludingClipVolumes(foot);
        }

        private bool ClearFootprint(Vector3 point, float radius)
        {
            if (!Level.checkSafeIncludingClipVolumes(point)) return false;
            int count = Physics.OverlapBoxNonAlloc(
                point + Vector3.up * 0.78f,
                new Vector3(radius, 0.68f, radius),
                _nearby, Quaternion.identity, RayMasks.BLOCK_COLLISION,
                QueryTriggerInteraction.Ignore);
            if (count >= MaxPlacementHits) return false;
            // Player-placed objects, buildings, fences and other NPCs
            // cannot be bypassed; a blocked site is simply rejected.
            for (int i = 0; i < count; i++)
            {
                Collider obstacle = _nearby[i];
                _nearby[i] = null;
                if (obstacle == null || !obstacle.enabled) continue;
                if (obstacle.transform == transform ||
                    obstacle.transform.IsChildOf(transform)) continue;
                return false;
            }
            return true;
        }

        private void Update()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + AttemptInterval;
            if (!IsSafeRoutine() || Time.time < _nextPlacementAt) return;
            ushort ownedID;
            ItemBarricadeAsset asset;
            if (!HasOwnedBuildable(out ownedID, out asset)) return;
            Vector3 site;
            int pieces;
            if (!Line2NpcSettlementLedger.TryGet(_group.GroupId,
                _actor.Disposition, out site, out pieces) ||
                pieces >= MaxPieces) return;
            if ((transform.position - site).sqrMagnitude > 6f * 6f)
                return;
            Vector3[] offsets =
            {
                new Vector3(2.75f, 0f, 0f),
                new Vector3(-2.75f, 0f, 0f),
                new Vector3(0f, 0f, 2.75f),
                new Vector3(0f, 0f, -2.75f)
            };
            Vector3 requested = site + offsets[pieces];
            Vector3 position;
            if (!TryGround(requested, out position) ||
                Mathf.Abs(position.y - site.y) > 0.65f ||
                !ClearFootprint(position, 0.85f))
            {
                _nextPlacementAt = Time.time + 20f;
                return;
            }
            float yaw = Mathf.Atan2(offsets[pieces].x,
                offsets[pieces].z) * Mathf.Rad2Deg;
            // Debit exactly ONE real carried buildable before invoking
            // vanilla replication/persistence. On a declined placement
            // return that same item. The NPC has no spoofed Player/SteamID.
            Item consumed;
            if (!_inventory.TryConsumeOneItemByIdServer(ownedID,
                out consumed) || consumed == null) return;
            Transform placed = null;
            try
            {
                Barricade native = new Barricade(asset);
                placed = BarricadeManager.dropBarricade(native, null,
                    position, 0f, yaw, 0f, 0ul, 0ul);
                if (placed == null)
                {
                    if (!_inventory.TryStoreTransferredItem(consumed))
                        ProbeLogger.Log("NPC_BUILD_ROLLBACK_FAILED: " +
                            _actor.Identity + " item=" + ownedID);
                    _nextPlacementAt = Time.time + 30f;
                    return;
                }
                if (!Line2NpcSettlementLedger.CommitPlaced(
                    _group.GroupId, _actor.Disposition, ownedID,
                    placed.position, yaw))
                    ProbeLogger.Log("NPC_BUILD_LEDGER_MISMATCH: native object " +
                        "exists; inspect before further placement");
                _nextPlacementAt = Time.time + 95f;
                if (_runtime != null) _runtime.RequestPriorityCheckpoint();
                ProbeLogger.Log("NPC_NATIVE_BASE_PLACED: group=" +
                    _group.GroupId + " item=" + ownedID +
                    " slot=" + (pieces + 1) + "/" + MaxPieces +
                    " pos=" + placed.position.ToString("F1"));
            }
            catch (Exception error)
            {
                // Never recredit material after the native API returned a
                // real placed Transform: that would create duplication.
                if (placed == null &&
                    !_inventory.TryStoreTransferredItem(consumed))
                    ProbeLogger.Log("NPC_BUILD_ROLLBACK_FAILED: " +
                        _actor.Identity + " item=" + ownedID);
                _nextPlacementAt = Time.time + 40f;
                if (Time.time >= _nextLog)
                {
                    _nextLog = Time.time + 12f;
                    ProbeLogger.Log("NPC_BUILD_EXCEPTION: " +
                        error.GetType().Name + " actor=" + _actor.Identity);
                }
            }
        }
    }
}
