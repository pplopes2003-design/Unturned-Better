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
        private const int MaxPieces = 5; // 1 central storage + 4 perimeter barricades
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
        private float _nextNativeReconcileAt;
        private readonly Line2NpcNativeCampRegistry _nativeCamp =
            new Line2NpcNativeCampRegistry();
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
            // Give native BarricadeManager ample time to instantiate all
            // saved pieces after map load before trying to prune old entries.
            _nextNativeReconcileAt = Time.time + 45f;
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
            out ItemBarricadeAsset asset, bool preferStorage)
        {
            itemID = 0;
            asset = null;
            if (_inventory == null) return false;
            ushort perimeterId = 0;
            ItemBarricadeAsset perimeter = null;
            for (int slot = 0; slot < _inventory.UsedSlots; slot++)
            {
                Item item;
                if (!_inventory.TryGetItemSnapshot(slot, out item) ||
                    item == null || item.amount == 0) continue;
                ItemBarricadeAsset candidate = Assets.find(EAssetType.ITEM,
                    item.id) as ItemBarricadeAsset;
                if (candidate == null || candidate.barricade == null ||
                    candidate.health == 0 || !candidate.isSaveable ||
                    candidate.isPro) continue;

                // A native, actually-carried STORAGE is the first group
                // construction goal. After one has been registered, do not
                // spend another container on the defensive perimeter.
                if (candidate.build == EBuild.STORAGE && preferStorage)
                {
                    itemID = item.id;
                    asset = candidate;
                    return true;
                }
                if (candidate.build != EBuild.BARRICADE &&
                    candidate.build != EBuild.FORTIFICATION &&
                    candidate.build != EBuild.CAMPFIRE)
                    continue;
                if (perimeter != null) continue;
                perimeterId = item.id;
                perimeter = candidate;
            }
            itemID = perimeterId;
            asset = perimeter;
            return asset != null;
        }

        internal bool TryGetConstructionGoalServer(out Vector3 goal)
        {
            goal = Vector3.zero;
            if (!IsSafeRoutine()) return false;
            Vector3 site;
            int count;
            if (!Line2NpcSettlementLedger.TryGet(_group.GroupId,
                _actor.Disposition, out site, out count))
            {
                PrototypeActor leader;
                if (!Line2NPCGroupMember.TryGetLivingMember(
                    _group.GroupId, out leader) || leader != _actor)
                    return false; // One planner claims the first map site.
                ushort provisionalID;
                ItemBarricadeAsset provisionalAsset;
                if (!HasOwnedBuildable(out provisionalID, out provisionalAsset,
                    true) || !TryFindSafeCamp(out site) ||
                    !Line2NpcSettlementLedger.TryClaim(
                        _group.GroupId, _actor.Disposition, site))
                    return false;
                count = 0;
            }
            if (count >= MaxPieces) return false;
            ushort carriedID;
            ItemBarricadeAsset carriedAsset;
            if (!HasOwnedBuildable(out carriedID, out carriedAsset,
                !Line2NpcSettlementLedger.HasCentralStorage(
                    _group.GroupId, _actor.Disposition)))
                return false;
            // Keep the patrol goal anchored at the claimed campsite even
            // when already in range. Otherwise random patrol would walk the
            // worker away before its next bounded native build interval.
            goal = site;
            return true;
        }

        private bool IsLivingGroupLeader()
        {
            PrototypeActor leader;
            return _group != null && _group.Joined &&
                Line2NPCGroupMember.TryGetLivingMember(
                    _group.GroupId, out leader) && leader == _actor;
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
            if (!IsSafeRoutine()) return;
            Vector3 site;
            int pieces;
            bool hasSite = Line2NpcSettlementLedger.TryGet(
                _group.GroupId, _actor.Disposition, out site, out pieces);
            if (hasSite && Time.time >= _nextNativeReconcileAt &&
                (transform.position - site).sqrMagnitude < 12f * 12f &&
                IsLivingGroupLeader())
            {
                _nextNativeReconcileAt = Time.time + 40f;
                if (Line2NpcSettlementLedger.ReconcileObservedPieces(
                    _group.GroupId, _actor.Disposition,
                    part =>
                    {
                        BarricadeDrop actual;
                        return _nativeCamp.TryFindSavedPiece(part, out actual);
                    }))
                {
                    ProbeLogger.Log("NPC_CAMP_RECONCILED: group=" +
                        _group.GroupId +
                        " missing destroyed native pieces removed");
                    if (_runtime != null) _runtime.RequestPriorityCheckpoint();
                    Line2NpcSettlementLedger.TryGet(_group.GroupId,
                        _actor.Disposition, out site, out pieces);
                }
            }
            if (Time.time < _nextPlacementAt) return;
            bool needsStorage = !Line2NpcSettlementLedger.HasCentralStorage(
                _group.GroupId, _actor.Disposition);
            ushort ownedID;
            ItemBarricadeAsset asset;
            if (!HasOwnedBuildable(out ownedID, out asset, needsStorage))
            {
                // This tries ONE real native blueprint per shared cooldown;
                // never spawns a barricade without owned ingredients.
                if (_inventory.TryCraftNativeCampBarricadeServer() &&
                    _runtime != null)
                    _runtime.RequestPriorityCheckpoint();
                return;
            }
            if (!hasSite || pieces >= MaxPieces ||
                (transform.position - site).sqrMagnitude > 6f * 6f)
                return;
            Vector3[] offsets =
            {
                new Vector3(2.75f, 0f, 0f),
                new Vector3(-2.75f, 0f, 0f),
                new Vector3(0f, 0f, 2.75f),
                new Vector3(0f, 0f, -2.75f)
            };
            bool placingStorage = asset.build == EBuild.STORAGE;
            Vector3 requested = site;
            float yaw = 0f;
            if (!placingStorage)
            {
                // A destroyed perimeter piece leaves a HOLE, not a new
                // sequential slot. Probe real manifest positions so the
                // next item repairs an empty cardinal location instead of
                // rebuilding on top of an existing native barricade.
                bool foundEmpty = false;
                for (int i = 0; i < offsets.Length; i++)
                {
                    Vector3 slot = site + offsets[i];
                    if (Line2NpcSettlementLedger.IsPositionOccupied(
                        _group.GroupId, _actor.Disposition, slot, 1.05f))
                        continue;
                    requested = slot;
                    yaw = Mathf.Atan2(offsets[i].x,
                        offsets[i].z) * Mathf.Rad2Deg;
                    foundEmpty = true;
                    break;
                }
                if (!foundEmpty) return;
            }
            Vector3 position;
            if (!TryGround(requested, out position) ||
                Mathf.Abs(position.y - site.y) > 0.65f ||
                !ClearFootprint(position, placingStorage ? 1.1f : 0.85f))
            {
                _nextPlacementAt = Time.time + 20f;
                return;
            }
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
                uint nativeID;
                if (!_nativeCamp.TryIdentifyPlaced(placed, ownedID,
                    out nativeID) ||
                    !Line2NpcSettlementLedger.CommitPlaced(
                        _group.GroupId, _actor.Disposition, ownedID,
                        placed.position, yaw, nativeID, placingStorage))
                    ProbeLogger.Log("NPC_BUILD_LEDGER_MISMATCH: native object " +
                        "exists but was not committed; inspect before placement");
                _nextPlacementAt = Time.time + 95f;
                if (_runtime != null) _runtime.RequestPriorityCheckpoint();
                ProbeLogger.Log("NPC_NATIVE_BASE_PLACED: group=" +
                    _group.GroupId + " item=" + ownedID +
                    " isStorage=" + placingStorage +
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
