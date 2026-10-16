////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Single-player-first runtime bootstrap for the experimental mobile NPC population.
    /// Builds an inactive template from the game's humanoid NPC visual asset, then enables
    /// population only after a local server map has loaded. This is not network replication.
    /// </summary>
    internal static class MobileNPCPopulationBootstrap
    {
        private const string VisualResourcePath = "Characters/NPC_Client";
        private static bool subscribed;
        private static GameObject template;
        private static GameObject directorObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!subscribed)
            {
                Level.onPostLevelLoaded += OnPostLevelLoaded;
                subscribed = true;
            }

            // Depending on startup order, the hook can be installed after a map is already loaded.
            if (Level.isLoaded)
            {
                CreateForLoadedMap(0);
            }
        }

        private static void OnPostLevelLoaded(int level)
        {
            CreateForLoadedMap(level);
        }

        private static void CreateForLoadedMap(int level)
        {
            if (!Provider.isServer || Dedicator.IsDedicatedServer)
            {
                return;
            }

            // Flush the outgoing map before group state and the persistence queue are reset.
            // This is especially important when changing maps before the 30-second autosave.
            MobileNPCPersistence.PrepareForMapTransitionServer();
            MobileNPCGroupMember.ResetAllServerGroups();
            MobileNPCRadioOutbox.ResetRadioSessionServer();
            MobileNPCPersistence.ResetForLoadedMapServer();
            MobileNPCAirdropContest.ResetServer();
            DestroyExistingRuntimeObjects();

            GameObject visualPrefab = Resources.Load<GameObject>(VisualResourcePath);
            if (visualPrefab == null)
            {
                Debug.LogWarning("[MobileNPCPopulationBootstrap] Could not load Resources/" + VisualResourcePath
                    + ".prefab; mobile NPC population remains disabled.");
                return;
            }

            template = new GameObject("MobileHumanNPC_RuntimeTemplate");
            // Unturned's zombie CharacterController runs on Agent (layer 24), not
            // Default. The game's layer collision matrix gives Agent physical
            // collision with the world; Default does not. Keep the physical root on
            // Agent and preserve NPC_Client's authored render layer on its visual child.
            template.layer = LayerMasks.AGENT;
            template.SetActive(false);

            CharacterController controller = template.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 38f;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.08f;
            // Match Zombie.start(): zombie CharacterControllers do not use Unity's
            // overlap recovery, which can push agents unpredictably around map geometry.
            controller.enableOverlapRecovery = false;

            template.AddComponent<MobileNPCBrainDriver>();
            // Strategic routine arbitration runs only when tactical AI is idle.
            template.AddComponent<MobileNPCAutonomyPlanner>();
            template.AddComponent<MobileNPCHumanoidMotor>();
            template.AddComponent<MobileNPCDoorNavigation>();
            template.AddComponent<MobileNPCHumanoidPerception>();
            template.AddComponent<MobileNPCHumanoidDisposition>();
            template.AddComponent<MobileNPCFaction>();
            template.AddComponent<MobileNPCHumanoidVitals>();
            template.AddComponent<MobileNPCAdaptiveMemory>();
            template.AddComponent<MobileNPCTravelMemory>();
            template.AddComponent<MobileNPCSupplyKnowledge>();
            template.AddComponent<MobileNPCHumanoidAim>();
            template.AddComponent<MobileNPCHumanoidCombat>();
            template.AddComponent<MobileNPCHumanoidRangedCombat>();
            template.AddComponent<MobileNPCRangedPresentation>();
            template.AddComponent<MobileNPCVisualAnimation>();
            template.AddComponent<MobileNPCNameplate>();
            template.AddComponent<MobileNPCVisualGuard>();
            template.AddComponent<MobileNPCAppearance>();
            // RequireComponent on combat/medical systems may already have
            // inserted Inventory on the inactive template. Do not add it
            // twice (confirmed twice-initialized components in Player.log).
            if (template.GetComponent<MobileNPCInventory>() == null)
                template.AddComponent<MobileNPCInventory>();
            template.AddComponent<MobileNPCDeathLoot>();
            template.AddComponent<MobileNPCSurvivalNeeds>();
            template.AddComponent<MobileNPCLooting>();
            template.AddComponent<MobileNPCMedicalUse>();
            template.AddComponent<MobileNPCSettlementPlanner>();
            template.AddComponent<MobileNPCSettlementBuilder>();
            template.AddComponent<MobileNPCBaseGeneratorOperator>();
            if (template.GetComponent<MobileNPCGroupMember>() == null)
                template.AddComponent<MobileNPCGroupMember>();
            template.AddComponent<MobileNPCRadioOutbox>();
            template.AddComponent<MobileNPCAirdropRumor>();
            template.AddComponent<MobileNPCUsefulLootRumor>();
            template.AddComponent<MobileNPCReinforcementResponse>();
            template.AddComponent<MobileNPCUnknownBaseIntel>();
            template.AddComponent<MobileNPCGrenadeThrower>();
            template.AddComponent<MobileNPCCompanionContract>();

            // Reuse the game's existing humanoid visual hierarchy without modifying its
            // legacy prefab or retaining its player/NPC-specific gameplay behaviours.
            GameObject visual = Object.Instantiate(visualPrefab, template.transform, false);
            visual.name = "HumanoidVisual";
            // Keep the prefab-authored local scale: NPC_Client may rely on its
            // original scale to match the game's humanoid rig. Overwriting it with
            // Vector3.one can make the mesh incorrectly sized or move its bounds
            // outside the expected camera framing.
            visual.transform.localPosition = Vector3.zero;
            // NPC_Client's skin is authored in a rig coordinate frame whose long axis
            // is currently mapped onto world Z when parented directly to the world root.
            // The player presentation normally supplies the missing -90 degree X-axis
            // correction. This standalone mobile visual has no Player presentation object,
            // so apply that correction at the visual root to stand the humanoid upright.
            visual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // Keep the native NPC_Client render layer. The checked-in prefab is authored
            // on layer 16 (Medium), which Unturned's character rendering expects. Default
            // (0) is not a safe substitute because the gameplay camera may cull it.
            int visualRenderLayer = visualPrefab.layer;
            Transform[] visualTransforms = visual.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < visualTransforms.Length; ++i)
            {
                visualTransforms[i].gameObject.layer = visualRenderLayer;
            }

            // Anatomical damage volumes are attached AFTER legacy visual
            // colliders/scripts are disabled. They follow live animated
            // bones (not a static six-volume upright approximation).

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                bool cameraIncludesLayer = (mainCamera.cullingMask & (1 << visualRenderLayer)) != 0;
                Debug.Log("[MobileNPCPopulationBootstrap] Render camera audit: camera=" + mainCamera.name
                    + ", cameraMask=0x" + mainCamera.cullingMask.ToString("X8")
                    + ", NPC_Client renderLayer=" + visualRenderLayer
                    + ", cameraIncludesRenderLayer=" + cameraIncludesLayer + ".");
                if (!cameraIncludesLayer)
                {
                    Debug.LogError("[MobileNPCPopulationBootstrap] Main camera culls the NPC_Client render layer. "
                        + "The model and diagnostic marker cannot render until this camera/layer mismatch is fixed.");
                }
            }
            else
            {
                Debug.LogWarning("[MobileNPCPopulationBootstrap] Camera.main is null during visual setup; "
                    + "the NPC visual layer remains the native NPC_Client layer " + visualRenderLayer + ".");
            }

            // The source asset is only a visual template. Explicitly retain its mesh
            // renderers while removing legacy gameplay scripts and collision bodies.
            Renderer[] visualRenderers = visual.GetComponentsInChildren<Renderer>(true);
            int usableRendererCount = 0;
            int activatedRendererObjects = 0;
            for (int i = 0; i < visualRenderers.Length; ++i)
            {
                Renderer renderer = visualRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.allowOcclusionWhenDynamic = false;

                // The source NPC visual can contain renderer objects under inactive
                // hierarchy nodes that are normally enabled by legacy gameplay scripts.
                // Those scripts are intentionally stripped from this mobile template,
                // so activate the renderer's path up to (but not including) the template.
                Transform node = renderer.transform;
                while (node != null && node != template.transform)
                {
                    if (!node.gameObject.activeSelf)
                    {
                        node.gameObject.SetActive(true);
                    }
                    node = node.parent;
                }
                if (renderer.gameObject.activeSelf)
                {
                    ++activatedRendererObjects;
                }

                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                {
                    // Keep the rig rendering even if its initial bind-pose bounds are
                    // outside the camera frustum before the first animation update.
                    skinnedRenderer.updateWhenOffscreen = true;
                    if (skinnedRenderer.sharedMesh != null && skinnedRenderer.sharedMesh.vertexCount > 0)
                    {
                        ++usableRendererCount;
                    }
                }
                else if (renderer is MeshRenderer meshRenderer)
                {
                    MeshFilter meshFilter = meshRenderer.GetComponent<MeshFilter>();
                    if (meshFilter != null && meshFilter.sharedMesh != null && meshFilter.sharedMesh.vertexCount > 0)
                    {
                        ++usableRendererCount;
                    }
                }
            }

            Debug.Log("[MobileNPCPopulationBootstrap] NPC_Client visual audit: scale="
                + visual.transform.localScale + ", renderers=" + visualRenderers.Length
                + ", rendererObjectsActiveSelf=" + activatedRendererObjects
                + ", usableMeshRenderers=" + usableRendererCount
                + ", localPosition=" + visual.transform.localPosition + ".");

            // Renderer count alone cannot prove that the humanoid is upright, textured,
            // or animated. Emit enough asset-level detail to distinguish those failures
            // from a camera/layer issue in the next player log.
            for (int i = 0; i < visualRenderers.Length; ++i)
            {
                Renderer renderer = visualRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                string materialSummary = "";
                Material[] materials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; ++materialIndex)
                {
                    Material material = materials[materialIndex];
                    if (materialIndex > 0)
                    {
                        materialSummary += ";";
                    }

                    materialSummary += material == null
                        ? "null"
                        : material.name + "/shader=" + (material.shader != null ? material.shader.name : "null");
                }

                string meshSummary = "none";
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    meshSummary = skinned.sharedMesh != null
                        ? skinned.sharedMesh.name + "/localBounds=" + skinned.localBounds
                        : "missing-skinned-mesh";
                }
                else
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        meshSummary = filter.sharedMesh.name + "/localBounds=" + filter.sharedMesh.bounds;
                    }
                }

                Debug.Log("[MobileNPCPopulationBootstrap] Visual renderer[" + i + "]: path="
                    + GetHierarchyPath(renderer.transform, visual.transform)
                    + ", type=" + renderer.GetType().Name
                    + ", active=" + renderer.gameObject.activeInHierarchy
                    + ", enabled=" + renderer.enabled
                    + ", layer=" + renderer.gameObject.layer
                    + ", worldBounds=" + renderer.bounds
                    + ", mesh=" + meshSummary
                    + ", materials=[" + materialSummary + "].");
            }

            Animation[] legacyAnimations = visual.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; ++i)
            {
                Animation animation = legacyAnimations[i];
                string clips = "";
                foreach (AnimationState state in animation)
                {
                    if (state == null)
                    {
                        continue;
                    }

                    if (clips.Length > 0)
                    {
                        clips += ",";
                    }
                    clips += state.name;
                }

                Debug.Log("[MobileNPCPopulationBootstrap] Visual legacy Animation: path="
                    + GetHierarchyPath(animation.transform, visual.transform)
                    + ", active=" + animation.gameObject.activeInHierarchy
                    + ", enabled=" + animation.enabled
                    + ", clipCount=" + animation.GetClipCount()
                    + ", clips=[" + clips + "].");
            }

            Animator[] animators = visual.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; ++i)
            {
                Animator animator = animators[i];
                Debug.Log("[MobileNPCPopulationBootstrap] Visual Animator: path="
                    + GetHierarchyPath(animator.transform, visual.transform)
                    + ", active=" + animator.gameObject.activeInHierarchy
                    + ", enabled=" + animator.enabled
                    + ", controller=" + (animator.runtimeAnimatorController != null
                        ? animator.runtimeAnimatorController.name : "none") + ".");
            }

            if (visualRenderers.Length == 0 || usableRendererCount == 0)
            {
                Debug.LogError("[MobileNPCPopulationBootstrap] NPC_Client has no usable mesh renderer (renderer count="
                    + visualRenderers.Length + ", usable meshes=" + usableRendererCount
                    + "); refusing to initialize an invisible mobile NPC population.");
                DestroyExistingRuntimeObjects();
                return;
            }

            // The root CharacterController is the mobile NPC's physical body. Keep it
            // enabled independently of the legacy colliders removed from the visual rig.
            controller.enabled = true;
            controller.detectCollisions = true;

            Rigidbody[] rigidbodies = visual.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; ++i)
            {
                rigidbodies[i].isKinematic = true;
                rigidbodies[i].detectCollisions = false;
            }

            Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
            {
                colliders[i].enabled = false;
            }

            Behaviour[] legacyBehaviours = visual.GetComponentsInChildren<Behaviour>(true);
            int disabledLegacyBehaviourCount = 0;
            int preservedVisualBehaviourCount = 0;
            for (int i = 0; i < legacyBehaviours.Length; ++i)
            {
                Behaviour behaviour = legacyBehaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                // The native humanoid rig needs Animation/Animator, while HumanClothes creates
                // and applies the game's skin/clothing material in Awake. Disabling
                // HumanClothes before the inactive template is activated leaves both
                // meshes white. Preserve these visual systems and strip gameplay scripts.
                if (behaviour is Animation || behaviour is Animator || behaviour is HumanClothes)
                {
                    behaviour.enabled = true;
                    ++preservedVisualBehaviourCount;
                    continue;
                }

                behaviour.enabled = false;
                ++disabledLegacyBehaviourCount;
            }

            Debug.Log("[MobileNPCPopulationBootstrap] Visual behaviour audit: disabledLegacy="
                + disabledLegacyBehaviourCount + ", preservedVisualSystems="
                + preservedVisualBehaviourCount + ".");

            if (!CreateAnimatedAnatomicalHitboxes(visual.transform, controller))
            {
                Debug.LogError("[MobileNPCPopulationBootstrap] Missing/invalid humanoid "
                    + "skeleton or hit volumes. Refusing to initialize NPCs that "
                    + "might appear alive but be impossible to hit.");
                DestroyExistingRuntimeObjects();
                return;
            }

            directorObject = new GameObject("MobileNPCPopulationDirector_Runtime");
            MobileNPCPopulationDirector director = directorObject.AddComponent<MobileNPCPopulationDirector>();
            // World-level pacing advisor shares the *existing* population
            // lifecycle; it never spawns agents, chooses hidden target
            // coordinates, or replaces each agent's sensory/tactical brain.
            directorObject.AddComponent<MobileNPCEncounterDirector>();
            directorObject.AddComponent<MobileNPCInteractionController>();
            directorObject.AddComponent<MobileNPCRadioInterceptHUD>();
            // The HUD only renders a radio report when the native local
            // receiver gear and PlayerQuests.radioFrequency actually match.
            // Server-side motion sampling generates audible footstep evidence.
            // It does not expose the player's live transform to NPC decisions.
            directorObject.AddComponent<MobileNPCPlayerFootstepSensor>();
            director.Configure(template);

            Debug.Log("[MobileNPCPopulationBootstrap] Single-player mobile NPC population initialized for loaded map "
                + level + " with root layer Agent (" + template.layer + ") and CharacterController overlap recovery disabled."
                + " Experimental local entities only; local NPC snapshots are persisted, but network replication is not implemented.");
        }

        private static bool CreateAnimatedAnatomicalHitboxes(
            Transform visual, CharacterController controller)
        {
            Transform skeleton = visual.Find("Root/Skeleton");
            Transform spine = skeleton != null ? skeleton.Find("Spine") : null;
            Transform skull = spine != null ? spine.Find("Skull") : null;
            Transform[] shoulders = new Transform[2];
            Transform[] elbows = new Transform[2];
            Transform[] wrists = new Transform[2];
            Transform[] hips = new Transform[2];
            Transform[] knees = new Transform[2];
            Transform[] ankles = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                shoulders[i] = spine != null ? spine.Find(side + "_Shoulder") : null;
                elbows[i] = shoulders[i] != null
                    ? shoulders[i].Find(side + "_Arm") : null;
                wrists[i] = elbows[i] != null
                    ? elbows[i].Find(side + "_Hand") : null;
                hips[i] = skeleton != null ? skeleton.Find(side + "_Hip") : null;
                knees[i] = hips[i] != null ? hips[i].Find(side + "_Leg") : null;
                ankles[i] = knees[i] != null
                    ? knees[i].Find(side + "_Foot") : null;
            }
            if (controller == null || spine == null || skull == null)
                return false;
            for (int i = 0; i < 2; i++)
                if (shoulders[i] == null || elbows[i] == null
                    || wrists[i] == null || hips[i] == null
                    || knees[i] == null || ankles[i] == null)
                    return false;

            // All positions are measured from the NPC_Client's actual
            // bind-pose bones. Parenting to the upstream animated joint
            // makes body/limb hitboxes follow locomotion, aiming, crouching
            // and jumping without any additional Update/GC/physics casts.
            // Root CharacterController owns walking; Entity layer owns
            // non-trigger DAMAGE raycasts, never ragdoll rigidbodies.
            // The NPC_Client bind-pose has Leg/Hand joints in the MIDDLE
            // of their authored boxes, not at the toes/fingertips. Follow
            // the real native BoxCollider envelopes so extremities are
            // not silently missed by rays.
            BoxCollider spineEnvelope = spine.GetComponent<BoxCollider>();
            BoxCollider skullEnvelope = skull.GetComponent<BoxCollider>();
            if (spineEnvelope == null || skullEnvelope == null
                || spineEnvelope.size.x < 0.6f || skullEnvelope.size.x < 0.3f)
                return false;
            Vector3 torsoBase = spine.position;
            Vector3 neckBase = skull.position;
            Vector3 up = (neckBase - torsoBase).normalized;
            if ((neckBase - torsoBase).sqrMagnitude < 0.18f)
                return false;
            CreateAnatomicalCapsule(spine, controller, "Abdomen",
                MobileNPCInjurySystem.BodyPart.Torso,
                Vector3.Lerp(torsoBase, neckBase, 0.04f),
                Vector3.Lerp(torsoBase, neckBase, 0.52f),
                Mathf.Min(0.37f, spineEnvelope.size.y * 0.43f));
            CreateAnatomicalCapsule(spine, controller, "Chest",
                MobileNPCInjurySystem.BodyPart.Torso,
                Vector3.Lerp(torsoBase, neckBase, 0.48f),
                Vector3.Lerp(torsoBase, neckBase, 0.96f),
                Mathf.Min(0.38f, spineEnvelope.size.y * 0.46f));
            CreateAnatomicalSphere(spine, controller, "Pelvis",
                MobileNPCInjurySystem.BodyPart.Torso,
                torsoBase - up * 0.07f, 0.32f);
            CreateAnatomicalSphere(skull, controller, "Neck",
                MobileNPCInjurySystem.BodyPart.Torso,
                neckBase + up * 0.035f, 0.125f);
            CreateAnatomicalSphere(skull, controller, "Head",
                MobileNPCInjurySystem.BodyPart.Head,
                skull.TransformPoint(skullEnvelope.center),
                Mathf.Max(0.235f, skullEnvelope.size.x * 0.60f));

            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                MobileNPCInjurySystem.BodyPart armPart = i == 0
                    ? MobileNPCInjurySystem.BodyPart.LeftArm
                    : MobileNPCInjurySystem.BodyPart.RightArm;
                MobileNPCInjurySystem.BodyPart legPart = i == 0
                    ? MobileNPCInjurySystem.BodyPart.LeftLeg
                    : MobileNPCInjurySystem.BodyPart.RightLeg;
                Vector3 upperArm = elbows[i].position - shoulders[i].position;
                Vector3 forearm = wrists[i].position - elbows[i].position;
                BoxCollider armEnvelope = elbows[i].GetComponent<BoxCollider>();
                BoxCollider legEnvelope = knees[i].GetComponent<BoxCollider>();
                if (upperArm.sqrMagnitude < 0.035f || forearm.sqrMagnitude < 0.035f
                    || (knees[i].position - hips[i].position).sqrMagnitude < 0.03f
                    || (ankles[i].position - knees[i].position).sqrMagnitude < 0.035f
                    || armEnvelope == null || armEnvelope.size.x < 0.6f
                    || legEnvelope == null || legEnvelope.size.x < 0.6f)
                    return false;
                Vector3 armTip = elbows[i].TransformPoint(
                    armEnvelope.center + Vector3.left
                    * (armEnvelope.size.x * 0.5f - 0.01f));
                Vector3 legTip = knees[i].TransformPoint(
                    legEnvelope.center + Vector3.left
                    * (legEnvelope.size.x * 0.5f - 0.01f));
                CreateAnatomicalSphere(shoulders[i], controller, side + "Shoulder",
                    armPart, shoulders[i].position + upperArm * 0.16f, 0.16f);
                CreateAnatomicalCapsule(shoulders[i], controller, side + "UpperArm",
                    armPart, shoulders[i].position, elbows[i].position, 0.165f);
                CreateAnatomicalCapsule(elbows[i], controller, side + "Forearm",
                    armPart, elbows[i].position,
                    Vector3.Lerp(wrists[i].position, armTip, 0.70f), 0.17f);
                CreateAnatomicalSphere(wrists[i], controller, side + "Hand",
                    armPart, armTip, 0.16f);

                CreateAnatomicalCapsule(hips[i], controller, side + "Thigh",
                    legPart, hips[i].position, ankles[i].position, 0.17f);
                CreateAnatomicalCapsule(knees[i], controller, side + "Shin",
                    legPart, ankles[i].position, legTip, 0.165f);
                CreateAnatomicalSphere(ankles[i], controller, side + "Foot",
                    legPart, legTip, 0.17f);
            }

            int count = visual.GetComponentsInChildren<MobileNPCBodyHitbox>(true).Length;
            Debug.Log("[MobileNPCPopulationBootstrap] Bone-attached anatomical "
                + "hitboxes=" + count + " (head, neck, chest, abdomen, pelvis, "
                + "shoulders, arms, forearms, hands, thighs, shins, feet). "
                + "All follow NPC_Client's original animated skeleton; physics "
                + "controller remains on Agent and hits use Entity.");
            return count == MobileNPCBodyHitbox.ExpectedAnatomicalVolumes;
        }

        private static void CreateAnatomicalCapsule(Transform ownerBone,
            CharacterController controller, string name,
            MobileNPCInjurySystem.BodyPart part,
            Vector3 fromWorld, Vector3 toWorld, float radius)
        {
            GameObject hit = new GameObject("Hit_" + name);
            hit.layer = 23; // Unturned ENTITY, separate from skin/ragdoll layer.
            hit.transform.SetParent(ownerBone, false);
            Vector3 direction = toWorld - fromWorld;
            hit.transform.localPosition =
                ownerBone.InverseTransformPoint((fromWorld + toWorld) * 0.5f);
            hit.transform.localRotation = Quaternion.FromToRotation(
                Vector3.up, ownerBone.InverseTransformDirection(direction.normalized));
            CapsuleCollider collider = hit.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.center = Vector3.zero;
            collider.radius = radius;
            collider.height = direction.magnitude + radius * 2f;
            collider.isTrigger = false;
            hit.AddComponent<MobileNPCBodyHitbox>().Configure(part);
            Physics.IgnoreCollision(collider, controller, true);
        }

        private static void CreateAnatomicalSphere(Transform ownerBone,
            CharacterController controller, string name,
            MobileNPCInjurySystem.BodyPart part, Vector3 centerWorld, float radius)
        {
            GameObject hit = new GameObject("Hit_" + name);
            hit.layer = 23;
            hit.transform.SetParent(ownerBone, false);
            hit.transform.localPosition = ownerBone.InverseTransformPoint(centerWorld);
            SphereCollider collider = hit.AddComponent<SphereCollider>();
            collider.center = Vector3.zero;
            collider.radius = radius;
            collider.isTrigger = false;
            hit.AddComponent<MobileNPCBodyHitbox>().Configure(part);
            Physics.IgnoreCollision(collider, controller, true);
        }

        private static string GetHierarchyPath(Transform node, Transform root)
        {
            string path = node != null ? node.name : "<null>";
            Transform parent = node != null ? node.parent : null;
            while (parent != null && parent != root)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private static void DestroyExistingRuntimeObjects()
        {
            if (directorObject != null)
            {
                Object.Destroy(directorObject);
                directorObject = null;
            }

            if (template != null)
            {
                Object.Destroy(template);
                template = null;
            }
        }
    }
}
