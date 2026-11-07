using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Inspects the ACTUAL collider contacted by the native humanoid motor,
    /// including layer, primitive collider, map LevelObject asset, door type
    /// and floor geometry. Does not infer doors/windows from arbitrary names
    /// or change physics. Emits a bounded diagnostic only after observed
    /// failure to make progress on an actual movement goal.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class Line2NpcObstacleAudit : MonoBehaviour
    {
        private PrototypeActor _actor;
        private Collider _lastCollider;
        private Vector3 _lastCollisionPoint;
        private Vector3 _lastLocation;
        private float _lastCollisionAt;
        private float _lastProgressAt;
        private float _nextAuditAt;
        private float _nextLogAt;
        private readonly List<RegionCoordinate> _regions =
            new List<RegionCoordinate>(9);
        internal void Initialize(PrototypeActor actor)
        {
            _actor = actor;
            _lastLocation = transform.position;
            _lastProgressAt = Time.time;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit == null || hit.collider == null ||
                hit.collider.transform == transform ||
                hit.collider.transform.IsChildOf(transform))
                return;
            _lastCollider = hit.collider;
            _lastCollisionPoint = hit.point;
            _lastCollisionAt = Time.time;
        }

        private void Update()
        {
            if (Time.time < _nextAuditAt) return;
            _nextAuditAt = Time.time + 0.5f;
            if (_actor == null || _actor.IsDead || !Provider.isServer ||
                Dedicator.IsDedicatedServer || !Level.isLoaded) return;
            if ((transform.position - _lastLocation).sqrMagnitude > 0.40f * 0.40f)
            {
                _lastProgressAt = Time.time;
                _lastLocation = transform.position;
                return;
            }
            if (Time.time - _lastProgressAt < 3f ||
                Time.time - _lastCollisionAt > 2f ||
                Time.time < _nextLogAt ||
                (_actor.CurrentDestination - transform.position).sqrMagnitude < 9f)
                return;
            _nextLogAt = Time.time + 12f;
            Collider collider = _lastCollider;
            if (collider == null) return;
            InteractableDoor barricadeDoor =
                collider.GetComponentInParent<InteractableDoor>();
            InteractableObjectBinaryState mapDoor =
                collider.GetComponentInParent<InteractableObjectBinaryState>();
            string authored = FindNativeObjectAsset(collider.transform,
                _lastCollisionPoint);
            ProbeLogger.Log("NPC_COLLIDER_BLOCKED: actor=" + _actor.Identity +
                " object=" + collider.gameObject.name +
                " type=" + collider.GetType().Name +
                " layer=" + LayerMask.LayerToName(collider.gameObject.layer) +
                "(" + collider.gameObject.layer + ")" +
                " trigger=" + collider.isTrigger +
                " nativeDoor=" + (barricadeDoor != null) +
                " mapBinaryDoor=" + (mapDoor != null) +
                " authored=" + authored +
                " at=" + _lastCollisionPoint.ToString("F1") +
                " goal=" + _actor.CurrentDestination.ToString("F1"));
        }

        private string FindNativeObjectAsset(Transform touched, Vector3 point)
        {
            if (LevelObjects.objects == null || touched == null)
                return "no-LevelObject-registry";
            _regions.Clear();
            Regions.getRegionsInRadius(point, 5f, _regions);
            for (int r = 0; r < _regions.Count; ++r)
            {
                RegionCoordinate region = _regions[r];
                if (region.x >= LevelObjects.objects.GetLength(0) ||
                    region.y >= LevelObjects.objects.GetLength(1))
                    continue;
                List<LevelObject> objects = LevelObjects.objects[region.x, region.y];
                if (objects == null) continue;
                int stride = Mathf.Max(1, Mathf.CeilToInt(objects.Count / 96f));
                int inspected = 0;
                for (int i = 0; i < objects.Count && inspected < 96; i += stride)
                {
                    inspected++;
                    LevelObject obj = objects[i];
                    if (obj == null || obj.transform == null || obj.asset == null)
                        continue;
                    Transform root = obj.transform;
                    if (touched == root || touched.IsChildOf(root))
                        return obj.asset.name + "/" + obj.asset.type;
                }
            }
            return "not-matched (may be barricade/vehicle/terrain)";
        }
    }
}
