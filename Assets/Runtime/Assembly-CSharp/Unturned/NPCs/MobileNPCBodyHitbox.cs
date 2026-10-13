////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Associates one physical Entity-layer hit collider with an anatomical region.
    /// This is a marker, not a second health owner: all damage goes through the
    /// parent MobileNPCHumanoidVitals exactly once after server ray validation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class MobileNPCBodyHitbox : MonoBehaviour
    {
        [SerializeField] private MobileNPCInjurySystem.BodyPart bodyPart
            = MobileNPCInjurySystem.BodyPart.Torso;

        public MobileNPCInjurySystem.BodyPart BodyPart => bodyPart;

        private void OnEnable()
        {
            // Physics.IgnoreCollision is not persistent across deactivation.
            // The prefab is assembled while INACTIVE, so its earlier bootstrap
            // ignore requests may be discarded by Unity. Reapply on each
            // actually enabled NPC instance; never let anatomical damage
            // colliders push the movement CharacterController around.
            Collider ownCollider = GetComponent<Collider>();
            CharacterController mover = GetComponentInParent<CharacterController>();
            if (ownCollider != null && mover != null && ownCollider.enabled && mover.enabled)
                Physics.IgnoreCollision(ownCollider, mover, true);
        }

        /// <summary>
        /// Called when constructing the inactive runtime template. Avoids relying
        /// on prefab-edited components or inaccurate inferred limb from ELimb.
        /// </summary>
        public void Configure(MobileNPCInjurySystem.BodyPart part)
        {
            bodyPart = part;
        }

        public static bool TryResolve(Transform hitTransform, out MobileNPCInjurySystem.BodyPart part)
        {
            part = MobileNPCInjurySystem.BodyPart.Torso;
            if (hitTransform == null)
                return false;

            // GetComponentInParent lets the ray hit a collider child of a
            // labeled body region without depending on layer/tag assumptions.
            MobileNPCBodyHitbox marker = hitTransform.GetComponentInParent<MobileNPCBodyHitbox>();
            if (marker == null)
                return false;
            MobileNPCHumanoidVitals vitals = marker.GetComponentInParent<MobileNPCHumanoidVitals>();
            if (vitals == null || vitals.IsDead)
                return false;
            part = marker.bodyPart;
            return true;
        }
    }
}
