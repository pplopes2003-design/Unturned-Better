////////////////////////////////////////////////////////////////////////////////////////
// Verify the exact shipped NPC and player ragdoll anatomy before a Dev Build.         //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

internal sealed class MobileNPCRagdollPrefabTests
{
    [Test]
    public void NativeRagdollBonesMatchRuntimeNpcClientSkeleton()
    {
        GameObject native = Resources.Load<GameObject>("Characters/Ragdoll_Player");
        GameObject npc = Resources.Load<GameObject>("Characters/MobileNPC_Client");
        Assert.IsNotNull(native, "Missing original Ragdoll_Player Resources prefab.");
        Assert.IsNotNull(npc, "Missing actual MobileNPC_Client Resources prefab.");

        Transform sourceRoot = native.transform.Find("Skeleton");
        Transform targetRoot = npc.transform.Find("Root/Skeleton");
        Assert.IsNotNull(sourceRoot, "Native Skeleton path changed.");
        Assert.IsNotNull(targetRoot, "NPC_Client Root/Skeleton path changed.");

        Rigidbody[] source = sourceRoot.GetComponentsInChildren<Rigidbody>(true);
        CharacterJoint[] links = sourceRoot.GetComponentsInChildren<CharacterJoint>(true);
        Assert.AreEqual(11, source.Length, "Native ragdoll expected 11 physics bones.");
        Assert.AreEqual(10, links.Length, "Native ragdoll expected 10 joints.");

        Transform[] npcBones = targetRoot.GetComponentsInChildren<Transform>(true);
        var matched = new Dictionary<Transform, Transform>();
        for (int i = 0; i < source.Length; ++i)
        {
            Transform bone = source[i].transform;
            Transform destination = null;
            int count = 0;
            for (int j = 0; j < npcBones.Length; ++j)
            {
                if (npcBones[j].name != bone.name) continue;
                destination = npcBones[j];
                count++;
            }
            Assert.AreEqual(1, count, "Missing or ambiguous NPC skin bone " + bone.name);
            Assert.IsNotNull(bone.GetComponent<BoxCollider>(),
                "Native rigid bone has no collider: " + bone.name);
            if (bone == sourceRoot)
            {
                Assert.AreSame(targetRoot, destination,
                    "Skeleton root is not the NPC skin skeleton root.");
                // Deliberately do NOT compare parent names here:
                // Ragdoll_Player/Skeleton and MobileNPC_Client/Root/Skeleton
                // are distinct prefab wrappers, not an anatomy mismatch.
            }
            else
            {
                Assert.IsNotNull(bone.parent, bone.name);
                Assert.IsNotNull(destination.parent, bone.name);
                Assert.AreEqual(bone.parent.name, destination.parent.name,
                    "Physical bone parent changed: " + bone.name);
            }
            matched.Add(bone, destination);
        }

        foreach (CharacterJoint joint in links)
        {
            Assert.IsNotNull(joint.connectedBody, "Unconnected joint: " + joint.name);
            Assert.IsTrue(matched.ContainsKey(joint.transform),
                "Joint itself has no mapped NPC bone: " + joint.name);
            Assert.IsTrue(matched.ContainsKey(joint.connectedBody.transform),
                "Joint's connected rigidbody has no mapped NPC bone: " + joint.name);
        }

        Rigidbody npcRootBody = npc.GetComponent<Rigidbody>();
        Assert.IsNotNull(npcRootBody, "NPC_Client root rigidbody changed.");
        Assert.IsTrue(npcRootBody.isKinematic,
            "An animated NPC root rigidbody must not simulate independently.");
    }
}
