using System;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Native physical breaching of FIRSTHAND observed, enemy-owned NPC
    /// settlement barricades with a genuine CARRIED ItemMeleeAsset. This
    /// reuses BarricadeManager.damage, its server damage callbacks, armor,
    /// destruction and normal Unturned persistence. No synthetic explosion,
    /// no damage to arbitrary player buildings, no invisible structure hit.
    ///
    /// Explosive and launcher breaching are separate pending behaviors.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcRaidBreacher : MonoBehaviour
    {
        private const float MinimumHitInterval = 2.0f;
        private float _nextStrikeAt;
        private PrototypeActor _actor;
        private Line2MobileNPCInventory _inventory;
        private PrototypeAnimation _animation;
        private Line2NPCCombatPerception _perception;
        private Line2NPCGroupMember _group;

        internal void Initialize(PrototypeActor actor,
            Line2NPCCombatPerception perception)
        {
            _actor = actor;
            _perception = perception;
            _inventory = GetComponent<Line2MobileNPCInventory>();
            _animation = GetComponent<PrototypeAnimation>();
            _group = GetComponent<Line2NPCGroupMember>();
            _nextStrikeAt = Time.time + UnityEngine.Random.Range(0.4f, 2f);
        }

        internal bool TryStrikeVerifiedRivalServer(int rivalGroup,
            BarricadeDrop native, Transform physicalRoot)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer ||
                (Provider.clients != null && Provider.clients.Count > 1) ||
                !Provider.hasCheats || !Level.isLoaded ||
                Player.player == null || _actor == null ||
                _actor.IsDead || _actor.Disposition != "hostile" ||
                _actor.IsTacticallyRetreating ||
                _group == null || !_group.Joined ||
                _group.GroupId == rivalGroup || rivalGroup <= 0 ||
                _inventory == null || native == null ||
                native.asset == null || !native.asset.canBeDamaged ||
                native.GetServersideData() == null ||
                native.GetServersideData().owner != 0ul ||
                native.GetServersideData().group != 0ul ||
                physicalRoot == null || Time.time < _nextStrikeAt ||
                (_perception != null &&
                 _perception.VisibleTarget != null &&
                 _perception.Kind !=
                    Line2NPCCombatPerception.TargetKind.None))
                return false;

            byte x,y;
            ushort plant;
            BarricadeRegion region;
            if (!BarricadeManager.tryGetRegion(physicalRoot, out x,
                out y, out plant, out region) || region == null ||
                region.FindBarricadeByRootTransform(physicalRoot) != native)
                return false;
            Item meleeItem;
            ItemMeleeAsset melee;
            if (!_inventory.TryGetBestMeleeServer(out meleeItem, out melee) ||
                melee == null || meleeItem == null ||
                meleeItem.quality == 0 || melee.barricadeDamage <= 0f ||
                melee.isPro || melee.isRepair) return false;
            Vector3 eye = transform.position + Vector3.up * 1.4f;
            Vector3 aim = physicalRoot.position + Vector3.up * 0.65f;
            Vector3 ray = aim - eye;
            float reach = Mathf.Clamp(melee.range, 1.0f, 2.8f);
            if (ray.magnitude > reach + 0.5f || ray.magnitude < 0.12f)
                return false;
            Vector3 facing = physicalRoot.position - transform.position;
            facing.y = 0f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (facing.sqrMagnitude < 0.01f ||
                forward.sqrMagnitude < 0.01f ||
                Vector3.Dot(facing.normalized, forward.normalized) < 0.35f)
                return false;

            // Reject any wall, player structure, second barricade or window
            // in front of the verified target. No through-wall base attack.
            RaycastHit[] results = new RaycastHit[20];
            int n = Physics.RaycastNonAlloc(eye, ray.normalized,
                results, ray.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (n >= results.Length) return false;
            float nearest = float.PositiveInfinity;
            Transform closest = null;
            for (int i = 0; i < n; ++i)
            {
                Transform hit = results[i].transform;
                if (hit == null || hit == transform ||
                    hit.IsChildOf(transform)) continue;
                if (results[i].distance < nearest)
                {
                    nearest = results[i].distance;
                    closest = hit;
                }
            }
            if (closest != null && closest != physicalRoot &&
                !closest.IsChildOf(physicalRoot))
                return false;

            _nextStrikeAt = Time.time + MinimumHitInterval;
            float condition = Mathf.Clamp01(meleeItem.quality / 100f);
            float qualityFactor = Mathf.Clamp(0.5f + condition, 0.5f, 1f);
            float applied = Mathf.Clamp(
                melee.barricadeDamage * qualityFactor, 0.1f, 100f);
            try
            {
                BarricadeManager.damage(physicalRoot, applied, 1f, true);
                // Use the same finite wear policy as the existing NPC
                // melee executor when the hit is actually performed.
                _inventory.TryWearOwnedMeleeServer(meleeItem.id);
                if (_animation != null) _animation.PlayMeleeSwing();
                ProbeLogger.Log("NPC_RAID_NATIVE_MELEE: attacker=" +
                    _actor.Identity + " rivalGroup=" + rivalGroup +
                    " tool=" + meleeItem.id +
                    " damage=" + applied.ToString("F1") +
                    " (BarricadeManager owns hit and destruction)");
                return true;
            }
            catch (Exception error)
            {
                ProbeLogger.Log("NPC_RAID_MELEE_REJECTED: " +
                    error.GetType().Name);
                return false;
            }
        }
    }
}
