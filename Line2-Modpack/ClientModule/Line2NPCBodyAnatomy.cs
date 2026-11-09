using System;
using System.Collections.Generic;
using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Reuses the actual six existing NPC_Client prefab BoxColliders,
    /// not invented character-shaped volumes. Vanilla prefab models them
    /// as triggers so physical motion remains CharacterController-owned.
    /// A separate, verified server raycast must hit one of these volumes
    /// before the original Line 1 DamageBridge applies any gun damage.
    /// </summary>
    internal static class Line2NPCBodyAnatomy
    {
        private struct Region
        {
            internal string Name;
            internal Line2MobileNPCInjurySystem.BodyPart Part;
            internal Region(string name, Line2MobileNPCInjurySystem.BodyPart part)
            {
                Name = name; Part = part;
            }
        }

        private static readonly Region[] Regions =
        {
            new Region("Skull", Line2MobileNPCInjurySystem.BodyPart.Head),
            new Region("Spine", Line2MobileNPCInjurySystem.BodyPart.Torso),
            new Region("Left_Arm", Line2MobileNPCInjurySystem.BodyPart.LeftArm),
            new Region("Right_Arm", Line2MobileNPCInjurySystem.BodyPart.RightArm),
            new Region("Left_Leg", Line2MobileNPCInjurySystem.BodyPart.LeftLeg),
            new Region("Right_Leg", Line2MobileNPCInjurySystem.BodyPart.RightLeg)
        };

        // Only select a layer the installed game explicitly puts in the
        // native ENTITY mask. Never guess that Default/Agent works for gun hits.
        private static int ResolveNativeEntityLayer()
        {
            int mask = RayMasks.ENTITY;
            int namedLayer = LayerMask.NameToLayer("Entity");
            if (namedLayer >= 0 && (mask & (1 << namedLayer)) != 0)
                return namedLayer;
            // Unturned versions using an unnamed single-bit ENTITY mask can
            // still be supported without inventing a layer assignment.
            if (mask == 0 || (mask & (mask - 1)) != 0)
                return -1;
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0) return i;
            return -1;
        }

        internal static bool TryBind(GameObject nativeVisual, out string error)
        {
            error = null;
            if (nativeVisual == null)
            {
                error = "native character visual missing";
                return false;
            }

            Transform[] bones = nativeVisual.GetComponentsInChildren<Transform>(true);
            var colliders = new BoxCollider[Regions.Length];
            for (int i = 0; i < Regions.Length; ++i)
            {
                Transform found = null;
                for (int k = 0; k < bones.Length; k++)
                {
                    if (bones[k] == null || bones[k].name != Regions[i].Name)
                        continue;
                    if (found != null)
                    {
                        error = "ambiguous bone " + Regions[i].Name;
                        return false;
                    }
                    found = bones[k];
                }
                if (found == null)
                {
                    error = "missing original bone " + Regions[i].Name;
                    return false;
                }

                BoxCollider nativeBox = found.GetComponent<BoxCollider>();
                if (nativeBox == null)
                {
                    error = "missing original BoxCollider on " + Regions[i].Name;
                    return false;
                }
                // NPC_Client is a visual prefab, not a server-side damage
                // actor. Its original bone layers are not guaranteed to be
                // included in RayMasks.ENTITY on the installed Steam build.
                // Verify structure first; layer only this spawned NPC's six
                // anatomical colliders after every bone passes validation.
                colliders[i] = nativeBox;
            }

            int entityLayer = ResolveNativeEntityLayer();
            if (entityLayer < 0)
            {
                error = "native ENTITY raycast mask has no unambiguous collision layer";
                return false;
            }

            // All anatomy was verified before enabling any hit volume.
            for (int i = 0; i < Regions.Length; i++)
            {
                BoxCollider collider = colliders[i];
                int originalLayer = collider.gameObject.layer;
                collider.gameObject.layer = entityLayer;
                if (originalLayer != entityLayer)
                    ProbeLogger.Log("NPC_ANATOMY_LAYER_ADAPTED: " +
                        Regions[i].Name + " original=" + originalLayer +
                        " entity=" + entityLayer);
                collider.isTrigger = true; // no extra physical movement owner
                var marker = collider.GetComponent<Line2MobileNPCBodyHitbox>();
                if (marker == null)
                    marker = collider.gameObject.AddComponent<Line2MobileNPCBodyHitbox>();
                marker.Configure(Regions[i].Part);
                collider.enabled = true;
            }
            ProbeLogger.Log("NPC_NATIVE_ANATOMY_OK: six original NPC_Client bone colliders enabled");
            return true;
        }
    }
}
