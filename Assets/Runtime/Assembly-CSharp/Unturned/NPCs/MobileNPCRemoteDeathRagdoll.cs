using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Client-only cosmetic death of a replicated humanoid.
    /// Copies the physical joint anatomy of Unturned's Ragdoll_Player to
    /// the ALREADY-VISIBLE NPC_Client bones, retaining its skin and clothing.
    /// No damage authority, loot, network traffic, or persistent colliders.
    /// </summary>
    internal static class MobileNPCRemoteDeathRagdoll
    {
        internal static bool TryRelease(GameObject root, Vector3 impulse)
        {
            if (root == null || Provider.isServer)
                return false;
            Transform visual = root.transform.Find("HumanoidVisual");
            Transform skeleton = visual != null
                ? visual.Find("Root/Skeleton") : null;
            GameObject native = Resources.Load<GameObject>("Characters/Ragdoll_Player");
            Transform source = native != null
                ? native.transform.Find("Skeleton") : null;
            if (skeleton == null || source == null)
                return false;
            Rigidbody[] sourceBodies = source.GetComponentsInChildren<Rigidbody>(true);
            CharacterJoint[] sourceJoints = source.GetComponentsInChildren<CharacterJoint>(true);
            if (sourceBodies.Length != 11 || sourceJoints.Length != 10)
                return false;

            Transform[] ownBones = skeleton.GetComponentsInChildren<Transform>(true);
            Transform[] targetBones = new Transform[11];
            // Validation first: never release a partially-matched physics
            // skeleton and launch it into the world.
            for (int i = 0; i < sourceBodies.Length; ++i)
            {
                Transform original = sourceBodies[i].transform;
                int matches = 0;
                for (int j = 0; j < ownBones.Length; ++j)
                {
                    if (ownBones[j].name == original.name)
                    {
                        targetBones[i] = ownBones[j];
                        ++matches;
                    }
                }
                if (matches != 1 || original.GetComponent<BoxCollider>() == null)
                    return false;
                if (original != source && (original.parent == null
                    || targetBones[i].parent == null
                    || original.parent.name != targetBones[i].parent.name))
                    return false;
            }
            foreach (CharacterJoint joint in sourceJoints)
            {
                if (joint == null || joint.connectedBody == null)
                    return false;
                bool from = false, to = false;
                for (int i = 0; i < sourceBodies.Length; ++i)
                {
                    from |= sourceBodies[i].transform == joint.transform;
                    to |= sourceBodies[i].transform == joint.connectedBody.transform;
                }
                if (!from || !to) return false;
            }

            // Remove living NPC's six shooting hitboxes, which must never
            // obstruct actual world gameplay after its server-confirmed death.
            foreach (MobileNPCBodyHitbox hitbox in
                root.GetComponentsInChildren<MobileNPCBodyHitbox>(true))
            {
                Collider collider = hitbox.GetComponent<Collider>();
                if (collider != null) collider.enabled = false;
            }

            Rigidbody visualBody = visual.GetComponent<Rigidbody>();
            if (visualBody != null)
            {
                visualBody.isKinematic = true;
                visualBody.detectCollisions = false;
            }
            Rigidbody[] bodies = new Rigidbody[11];
            for (int i = 0; i < sourceBodies.Length; ++i)
            {
                Rigidbody original = sourceBodies[i];
                Transform target = targetBones[i];
                BoxCollider originalBox = original.GetComponent<BoxCollider>();
                BoxCollider box = target.GetComponent<BoxCollider>();
                if (box == null) box = target.gameObject.AddComponent<BoxCollider>();
                box.center = originalBox.center;
                box.size = originalBox.size;
                box.sharedMaterial = originalBox.sharedMaterial;
                box.isTrigger = false;
                box.enabled = false;

                Rigidbody body = target.GetComponent<Rigidbody>();
                if (body == null) body = target.gameObject.AddComponent<Rigidbody>();
                body.mass = original.mass;
                body.drag = original.drag;
                body.angularDrag = original.angularDrag;
                body.useGravity = original.useGravity;
                body.interpolation = original.interpolation;
                body.collisionDetectionMode = original.collisionDetectionMode;
                body.isKinematic = true;
                body.detectCollisions = false;
                target.gameObject.layer = original.gameObject.layer;
                bodies[i] = body;
            }

            foreach (CharacterJoint original in sourceJoints)
            {
                int currentIndex = -1, connectedIndex = -1;
                for (int i = 0; i < sourceBodies.Length; ++i)
                {
                    if (sourceBodies[i].transform == original.transform)
                        currentIndex = i;
                    if (sourceBodies[i].transform == original.connectedBody.transform)
                        connectedIndex = i;
                }
                if (currentIndex < 0 || connectedIndex < 0)
                    return false;
                CharacterJoint joint =
                    targetBones[currentIndex].GetComponent<CharacterJoint>();
                if (joint == null)
                    joint = targetBones[currentIndex].gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = bodies[connectedIndex];
                joint.anchor = original.anchor;
                joint.axis = original.axis;
                joint.swingAxis = original.swingAxis;
                joint.autoConfigureConnectedAnchor = false;
                joint.connectedAnchor = bodies[connectedIndex].transform
                    .InverseTransformPoint(targetBones[currentIndex].TransformPoint(
                        original.anchor));
                joint.lowTwistLimit = original.lowTwistLimit;
                joint.highTwistLimit = original.highTwistLimit;
                joint.swing1Limit = original.swing1Limit;
                joint.swing2Limit = original.swing2Limit;
                joint.twistLimitSpring = original.twistLimitSpring;
                joint.swingLimitSpring = original.swingLimitSpring;
                joint.enableCollision = original.enableCollision;
                joint.enablePreprocessing = original.enablePreprocessing;
                joint.breakForce = original.breakForce;
                joint.breakTorque = original.breakTorque;
            }

            foreach (Animation animation in visual.GetComponentsInChildren<Animation>(true))
                animation.enabled = false;
            foreach (Animator animator in visual.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;

            Rigidbody central = null;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < bodies.Length; ++i)
            {
                BoxCollider box = bodies[i].GetComponent<BoxCollider>();
                if (box != null) box.enabled = true;
                bodies[i].detectCollisions = true;
                bodies[i].isKinematic = false;
                bodies[i].WakeUp();
                float dist = (bodies[i].worldCenterOfMass - visual.position).sqrMagnitude;
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    central = bodies[i];
                }
            }
            if (central != null && impulse.sqrMagnitude > 0.01f)
                central.AddForce(impulse.normalized * Mathf.Min(impulse.magnitude, 5f),
                    ForceMode.Impulse);
            // Visual corpses are strictly transient. Their server-side
            // lifetime/loot persists independently of this cosmetic view.
            Object.Destroy(root, 12f);
            return true;
        }
    }
}
