using System;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Ported from Line 1 MobileNPCHumanoidVitals. Only the already-dead
    /// NPC_Client clone receives native Ragdoll_Player authored joints.
    /// Falls back to a physically falling single rigid corpse when assets
    /// are missing/incompatible. Never touches the vanilla prefab asset.
    /// </summary>
    internal sealed class Line2NpcCorpseRagdoll : MonoBehaviour
    {
        private bool _activated;
        private static bool Finite(float number)
        {
            return !float.IsNaN(number) && !float.IsInfinity(number);
        }
        internal void OnAuthoritativeDeath(Vector3 hitImpulse)
        {
            if (_activated) return;
            _activated = true;
            try { ActivateAuthoredRagdoll(hitImpulse); }
            catch (Exception error)
            {
                ProbeLogger.Log("NPC_RAGDOLL_FAILED: " + error);
            }
        }
		private void ActivateRigidCorpseFallback(Transform visualRoot, Vector3 hitImpulse,
			Rigidbody[] incompleteBodies, Collider[] incompleteColliders)
		{
			for (int i = 0; i < incompleteColliders.Length; ++i)
			{
				if (incompleteColliders[i] != null)
					incompleteColliders[i].enabled = false;
			}
			for (int i = 0; i < incompleteBodies.Length; ++i)
			{
				if (incompleteBodies[i] == null)
					continue;
				incompleteBodies[i].isKinematic = true;
				incompleteBodies[i].detectCollisions = false;
			}

			

			// The proxy uses the world-upright NPC capsule frame, rather than the
			// -90 degree rotated visual rig frame. It stays inside the NPC hierarchy
			// so the existing corpse cleanup removes it with the dead instance.
			GameObject corpse = new GameObject("MobileNPCCorpsePhysics");
			corpse.layer = LayerMask.NameToLayer("Agent");
			corpse.transform.SetParent(transform, false);
			corpse.transform.localPosition = Vector3.zero;
			corpse.transform.localRotation = Quaternion.identity;
			visualRoot.SetParent(corpse.transform, true);

			CapsuleCollider corpseCollider = corpse.AddComponent<CapsuleCollider>();
			corpseCollider.direction = 1;
			corpseCollider.center = new Vector3(0f, 0.9f, 0f);
			corpseCollider.height = 1.8f;
			corpseCollider.radius = 0.34f;
			Rigidbody corpseBody = corpse.AddComponent<Rigidbody>();
			corpseBody.mass = 70f;
			corpseBody.useGravity = true;
			corpseBody.isKinematic = false;
			corpseBody.interpolation = RigidbodyInterpolation.Interpolate;
			corpseBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

			Vector3 direction = hitImpulse.sqrMagnitude > 0.001f
				? hitImpulse.normalized : transform.forward;
			direction.y = 0f;
			if (direction.sqrMagnitude < 0.001f)
				direction = transform.forward;
			direction.Normalize();
			// An off-centre push causes the corpse to tip naturally instead of
			// remaining an upright, static-looking capsule after death.
			// A 70kg capsule needs a small horizontal off-centre nudge, not
			// an unbounded impulse that catapults the corpse through scenery.
			corpseBody.AddForceAtPosition(direction * Mathf.Clamp(
				hitImpulse.magnitude * 1.3f + 4f, 4f, 13f),
				corpse.transform.position + Vector3.up * 1.5f, ForceMode.Impulse);
			corpseBody.AddTorque(Vector3.Cross(Vector3.up, direction) * 7f, ForceMode.Impulse);
			Debug.LogWarning("[MobileNPCHumanoidVitals] Incomplete authored skeletal ragdoll; "
				+ "activated physical capsule corpse fallback for " + name
				+ ". This is a rigid-body fall, not articulated bone ragdoll.", this);
		}

		private bool TryAuthorNativeBoneRagdoll(Transform visualRoot)
		{
			Transform destinationSkeleton = visualRoot != null
				? visualRoot.Find("Root/Skeleton") : null;
			GameObject nativePrefab = Resources.Load<GameObject>(
				"Characters/Ragdoll_Player");
			Transform sourceSkeleton = nativePrefab != null
				? nativePrefab.transform.Find("Skeleton") : null;
			if (destinationSkeleton == null || sourceSkeleton == null)
			{
				Debug.LogWarning("[MobileNPCHumanoidVitals] Cannot author articulated NPC "
					+ "ragdoll: missing NPC_Client Root/Skeleton or Ragdoll_Player "
					+ "Skeleton native resource for " + name + ".", this);
				return false;
			}

			Rigidbody[] sourceBodies = sourceSkeleton.GetComponentsInChildren<Rigidbody>(true);
			CharacterJoint[] sourceJoints =
				sourceSkeleton.GetComponentsInChildren<CharacterJoint>(true);
			if (sourceBodies.Length != 11 || sourceJoints.Length != 10)
			{
				Debug.LogError("[MobileNPCHumanoidVitals] Unexpected native "
					+ "Ragdoll_Player physics layout: bodies=" + sourceBodies.Length
					+ ", joints=" + sourceJoints.Length + ". Not guessing bone links.", this);
				return false;
			}

			Transform[] ownBones = destinationSkeleton.GetComponentsInChildren<Transform>(true);
			Transform[] sourceTransforms = new Transform[sourceBodies.Length];
			Transform[] destinationTransforms = new Transform[sourceBodies.Length];
			// Validate ALL 11 correspondences and native box volumes before
			// changing any gameplay object. Human skin bind poses are the
			// original NPC_Client rig, not the ragdoll prefab mesh.
			for (int i = 0; i < sourceBodies.Length; ++i)
			{
				Transform source = sourceBodies[i].transform;
				Transform match = null;
				int matches = 0;
				for (int b = 0; b < ownBones.Length; ++b)
				{
					if (ownBones[b].name != source.name)
						continue;
					match = ownBones[b];
					++matches;
				}
				if (matches != 1 || source.GetComponent<BoxCollider>() == null)
				{
					Debug.LogError("[MobileNPCHumanoidVitals] Missing/ambiguous "
						+ "bone or collider for native ragdoll bone " + source.name
						+ ", count=" + matches + ".", this);
					return false;
				}
				// Do not compare this LIVE animated bone pose against the rest-pose
				// Ragdoll_Player prefab: during walking, crouching and combat the
				// limbs must differ. This false negative previously dropped dead NPCs
				// into a rolling capsule instead of releasing their articulated rig.
				// Validate the stable bone hierarchy and native physical anatomy,
				// while preserving the exact pose at the moment of death.
				// The native Ragdoll_Player/Skeleton is parented under
				// Ragdoll_Player, whereas the rendered NPC_Client/Skeleton is
				// parented under Root. Their root parents MUST differ. Compare
				// actual descendants by their immediate anatomical parent;
				// preserve the runtime death pose without demanding identical
				// unrelated prefab wrappers.
				bool isSkeletonRoot = source == sourceSkeleton
					&& match == destinationSkeleton;
				bool validAnatomy = isSkeletonRoot
					|| match.parent != null && source.parent != null
						&& match.parent.name == source.parent.name;
				Vector3 scale = match.lossyScale;
				if (!validAnatomy
					|| !Finite(scale.x) || !Finite(scale.y) || !Finite(scale.z)
					|| Mathf.Abs(scale.x) < 0.0001f
					|| Mathf.Abs(scale.y) < 0.0001f
					|| Mathf.Abs(scale.z) < 0.0001f)
				{
					Debug.LogError("[MobileNPCHumanoidVitals] Invalid authored ragdoll "
						+ "bone parent or scale at " + source.name
						+ ", sourceParent=" + (source.parent != null ? source.parent.name : "<null>")
						+ ", targetParent=" + (match.parent != null ? match.parent.name : "<null>")
						+ ", scale=" + scale + ".", this);
					return false;
				}
				sourceTransforms[i] = source;
				destinationTransforms[i] = match;
			}
			for (int j = 0; j < sourceJoints.Length; ++j)
			{
				if (sourceJoints[j] == null || sourceJoints[j].connectedBody == null)
					return false;
				bool foundCurrent = false, foundConnected = false;
				for (int i = 0; i < sourceTransforms.Length; ++i)
				{
					foundCurrent |= sourceTransforms[i] == sourceJoints[j].transform;
					foundConnected |= sourceTransforms[i]
						== sourceJoints[j].connectedBody.transform;
				}
				if (!foundCurrent || !foundConnected)
				{
					Debug.LogError("[MobileNPCHumanoidVitals] Broken native "
						+ "ragdoll joint link at " + sourceJoints[j].name + ".", this);
					return false;
				}
			}

			// Native NPC_Client contains an unused root Rigidbody. Keep that
			// root inert: only skeleton bones may be released on death.
			Rigidbody oldVisualBody = visualRoot.GetComponent<Rigidbody>();
			if (oldVisualBody != null)
			{
				oldVisualBody.isKinematic = true;
				oldVisualBody.detectCollisions = false;
			}
			Rigidbody[] createdBodies = new Rigidbody[sourceBodies.Length];
			for (int i = 0; i < sourceBodies.Length; ++i)
			{
				Transform target = destinationTransforms[i];
				Rigidbody sourceBody = sourceBodies[i];
				// Six NPC_Client bone BoxColliders already exist, disabled
				// for normal gameplay. Reuse them rather than duplicating
				// capsule contacts on the same bone.
				BoxCollider sourceBox = sourceBody.GetComponent<BoxCollider>();
				BoxCollider box = target.GetComponent<BoxCollider>();
				if (box == null)
					box = target.gameObject.AddComponent<BoxCollider>();
				box.center = sourceBox.center;
				box.size = sourceBox.size;
				box.sharedMaterial = sourceBox.sharedMaterial;
				box.isTrigger = false;
				box.enabled = false;

				Rigidbody body = target.GetComponent<Rigidbody>();
				if (body == null)
					body = target.gameObject.AddComponent<Rigidbody>();
				body.mass = sourceBody.mass;
				body.drag = sourceBody.drag;
				body.angularDrag = sourceBody.angularDrag;
				body.useGravity = sourceBody.useGravity;
				body.interpolation = sourceBody.interpolation;
				body.collisionDetectionMode = sourceBody.collisionDetectionMode;
				body.constraints = sourceBody.constraints;
				body.isKinematic = true;
				body.detectCollisions = false;
				// Ragdoll_Player uses native Ragdoll physics layer 12,
				// whereas NPC_Client render bones use Medium layer 16.
				// Bone renderers are on separate Model_0 / Model_1 objects.
				target.gameObject.layer = sourceBody.gameObject.layer;
				createdBodies[i] = body;
			}
			for (int j = 0; j < sourceJoints.Length; ++j)
			{
				CharacterJoint template = sourceJoints[j];
				int own = -1, connected = -1;
				for (int i = 0; i < sourceTransforms.Length; ++i)
				{
					if (sourceTransforms[i] == template.transform) own = i;
					if (sourceTransforms[i] == template.connectedBody.transform) connected = i;
				}
				CharacterJoint joint =
					destinationTransforms[own].GetComponent<CharacterJoint>();
				if (joint == null)
					joint = destinationTransforms[own].gameObject.AddComponent<CharacterJoint>();
				joint.connectedBody = createdBodies[connected];
				joint.anchor = template.anchor;
				joint.axis = template.axis;
				joint.swingAxis = template.swingAxis;
				// Native authored anchors are defined in the prefab REST pose.
				// The NPC dies in an arbitrary animation frame. Configure the
				// connected anchor from that ACTUAL world pose, otherwise the
				// physics solver pulls the limbs toward distant rest-pose
				// anchors and launches the corpse like a rolling ball.
				joint.autoConfigureConnectedAnchor = false;
				joint.connectedAnchor = createdBodies[connected].transform
					.InverseTransformPoint(destinationTransforms[own].TransformPoint(template.anchor));
				joint.lowTwistLimit = template.lowTwistLimit;
				joint.highTwistLimit = template.highTwistLimit;
				joint.swing1Limit = template.swing1Limit;
				joint.swing2Limit = template.swing2Limit;
				joint.twistLimitSpring = template.twistLimitSpring;
				joint.swingLimitSpring = template.swingLimitSpring;
				joint.enableCollision = template.enableCollision;
				joint.enablePreprocessing = template.enablePreprocessing;
				joint.breakForce = template.breakForce;
				joint.breakTorque = template.breakTorque;
				// CharacterJoint derives from Joint/Component, NOT
				// Behaviour; the physics link is active when added.
			}
			Debug.Log("[MobileNPCHumanoidVitals] Authored native articulated NPC "
				+ "ragdoll on original skin skeleton: bones=" + createdBodies.Length
				+ ", joints=" + sourceJoints.Length + ", npc=" + name + ".", this);
			return true;
		}

		private void ActivateAuthoredRagdoll(Vector3 hitImpulse)
		{
			Transform visualRoot = transform.Find("NPC_ClientVisual");
			if (visualRoot == null)
			{
				Debug.LogError("[MobileNPCHumanoidVitals] Death presentation failed: NPC_ClientVisual root is missing.", this);
				return;
			}

			// NPC_Client itself has NO articulated ragdoll. Author it from
			// the SAME skeleton of the native Ragdoll_Player asset before
			// disabling the live pose writers. Fall back only on missing
			// incompatible resources/physics.
			bool articulatedAuthored = false;
			try { articulatedAuthored = TryAuthorNativeBoneRagdoll(visualRoot); }
            catch (System.Exception error)
            {
                Debug.LogError("[Line2NpcCorpseRagdoll] Native authoring failed: " + error, this);
            }
			// Stop all animation writers before releasing the authored bone rigidbodies.
			Animation[] animations = visualRoot.GetComponentsInChildren<Animation>(true);
			for (int i = 0; i < animations.Length; ++i)
			{
				if (animations[i] != null)
					animations[i].enabled = false;
			}
			Animator[] animators = visualRoot.GetComponentsInChildren<Animator>(true);
			for (int i = 0; i < animators.Length; ++i)
			{
				if (animators[i] != null)
					animators[i].enabled = false;
			}

			Transform actualSkeleton = visualRoot.Find("Root/Skeleton");
			Collider[] colliders = actualSkeleton != null
				? actualSkeleton.GetComponentsInChildren<Collider>(true)
				: visualRoot.GetComponentsInChildren<Collider>(true);
			Rigidbody[] authoredBodies = actualSkeleton != null
				? actualSkeleton.GetComponentsInChildren<Rigidbody>(true)
				: visualRoot.GetComponentsInChildren<Rigidbody>(true);
			Joint[] authoredJoints = actualSkeleton != null
				? actualSkeleton.GetComponentsInChildren<Joint>(true)
				: visualRoot.GetComponentsInChildren<Joint>(true);
			// The SIX live anatomical hitboxes occupy the SAME bones and
			// BoxColliders as six of the eleven native ragdoll volumes. Their
			// MobileNPCBodyHitbox components were disabled on death, so excluding
			// colliders with that marker makes a complete 11-bone ragdoll look
			// incomplete (5/11) and forces the rigid capsule fallback every time.
			// Validate the authored bone bodies themselves, not the gameplay marker.
			int usableColliders = 0;
			for (int i = 0; i < authoredBodies.Length; ++i)
			{
				Rigidbody bone = authoredBodies[i];
				BoxCollider box = bone != null ? bone.GetComponent<BoxCollider>() : null;
				if (bone != null && box != null && !box.isTrigger
					&& box.size.x > 0f && box.size.y > 0f && box.size.z > 0f
					&& actualSkeleton != null && bone.transform.IsChildOf(actualSkeleton))
					++usableColliders;
			}
			// Never enable a partially authored linked skeleton. NPC_Client
			// has none originally; Ragdoll_Player carries exactly 11 rigid
			// bodies, 11 colliders and 10 connected joints. A half-created
			// ragdoll is less safe than the explicit rigid-corpse fallback.
			if (!articulatedAuthored || authoredBodies.Length != 11
				|| authoredJoints.Length != 10 || usableColliders < 11)
			{
				Debug.LogError("[MobileNPCHumanoidVitals] Native articulated ragdoll "
					+ "validation failed: authored=" + articulatedAuthored
					+ ", bodies=" + authoredBodies.Length
					+ ", colliders=" + usableColliders
					+ ", joints=" + authoredJoints.Length + ", npc=" + name
					+ ". Using visibly falling rigid-body fallback; NOT a bone ragdoll.", this);
				// The previous return froze the last animation frame until
				// despawn. A failed bone rig now produces visible physical
				// death while keeping all six live hitboxes disabled.
				ActivateRigidCorpseFallback(visualRoot, hitImpulse,
					authoredBodies, colliders);
				return;
			}
			int enabledColliders = 0;
			for (int i = 0; i < authoredBodies.Length; ++i)
			{
				Rigidbody bone = authoredBodies[i];
				BoxCollider bodyCollider = bone != null ? bone.GetComponent<BoxCollider>() : null;
				if (bodyCollider == null)
					continue;
				// The gameplay hitbox MARKER stays disabled; only its native
				// physical collider is reactivated to articulate the corpse.
				bodyCollider.enabled = true;
				++enabledColliders;
			}

			Rigidbody[] bodies = authoredBodies;
			Joint[] joints = authoredJoints;
			int releasedBodies = 0;
			Rigidbody impulseBody = null;
			float nearestSqrDistance = float.PositiveInfinity;
			Vector3 hitPoint = transform.position;
			if (hitImpulse.sqrMagnitude > 0.0001f)
			{
				// The hit impulse carries direction/magnitude, not a hit point. Use the
				// nearest authored body to the NPC root as a stable fallback application point.
				hitPoint = visualRoot.position;
			}
			for (int i = 0; i < bodies.Length; ++i)
			{
				Rigidbody body = bodies[i];
				if (body == null)
					continue;
				body.detectCollisions = true;
				body.isKinematic = false;
				body.WakeUp();
				++releasedBodies;
				float sqrDistance = (body.worldCenterOfMass - hitPoint).sqrMagnitude;
				if (sqrDistance < nearestSqrDistance)
				{
					nearestSqrDistance = sqrDistance;
					impulseBody = body;
				}
			}

			if (impulseBody != null && hitImpulse.sqrMagnitude > 0.0001f)
			{
				// Weapon damage is not physical impulse measured in N*s.
				// An uncapped 100-point hit applied to a light ragdoll bone
				// can spin the entire corpse across the road.
				Vector3 physicalImpulse = hitImpulse.normalized
					* Mathf.Min(hitImpulse.magnitude, 6f);
				impulseBody.AddForce(physicalImpulse, ForceMode.Impulse);
			}

			if (releasedBodies == 0 || enabledColliders == 0 || joints.Length == 0)
			{
				Debug.LogError("[MobileNPCHumanoidVitals] NPC_Client death ragdoll may be incomplete: released rigidbodies="
					+ releasedBodies + ", enabled colliders=" + enabledColliders + ", joints=" + joints.Length
					+ ". Check the NPC_Client prefab's authored ragdoll hierarchy.", this);
			}
			else
			{
				Debug.Log("[MobileNPCHumanoidVitals] Death ragdoll activated: rigidbodies="
					+ releasedBodies + ", colliders=" + enabledColliders + ", joints=" + joints.Length + ".", this);
			}
		}

    }
}
