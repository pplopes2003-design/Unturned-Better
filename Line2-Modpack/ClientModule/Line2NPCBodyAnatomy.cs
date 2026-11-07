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
                // Verify actual ENTITY collision layer, do not guess a new
                // mask or mutate unrelated object layers.
                int index = nativeBox.gameObject.layer;
                if (index < 0 || index > 31 ||
                    (RayMasks.ENTITY & (1 << index)) == 0)
                {
                    error = "non-ENTITY collider layer on " + Regions[i].Name;
                    return false;
                }
                colliders[i] = nativeBox;
            }

            // All anatomy was verified before enabling any hit volume.
            for (int i = 0; i < Regions.Length; i++)
            {
                BoxCollider collider = colliders[i];
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
