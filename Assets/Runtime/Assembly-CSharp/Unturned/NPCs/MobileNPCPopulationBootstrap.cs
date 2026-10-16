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

            // Player-exact six hitboxes are attached AFTER disabling the
            // NPC_Client's native physics colliders, to follow animation
            // without duplicating physical movement or corpse colliders.

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

        /// <summary>
        /// Player_Server and Player_Client use exactly SIX BoxColliders:
        /// Skull, Spine, Left_Arm, Right_Arm, Left_Leg, Right_Leg.
        /// These are the exact same player collider centers/sizes read
        /// from checked-in Player_Server.prefab, NOT inferred from NPC
        /// proportions and NOT added to the native ragdoll physics bones.
        /// A single child on each existing NPC_Client animated bone
        /// owns an Entity-layer hit box; the six original NPC_Client
        /// bone colliders stay disabled until ragdoll authoring at death.
        /// </summary>
        private static bool CreateAnimatedAnatomicalHitboxes(
            Transform visual, CharacterController controller)
        {
            Transform skeleton = visual != null
                ? visual.Find("Root/Skeleton") : null;
            Transform spine = skeleton != null
                ? skeleton.Find("Spine") : null;
            Transform skull = spine != null ? spine.Find("Skull") : null;
            Transform leftShoulder = spine != null
                ? spine.Find("Left_Shoulder") : null;
            Transform rightShoulder = spine != null
                ? spine.Find("Right_Shoulder") : null;
            Transform leftArm = leftShoulder != null
                ? leftShoulder.Find("Left_Arm") : null;
            Transform rightArm = rightShoulder != null
                ? rightShoulder.Find("Right_Arm") : null;
            Transform leftHip = skeleton != null
                ? skeleton.Find("Left_Hip") : null;
            Transform rightHip = skeleton != null
                ? skeleton.Find("Right_Hip") : null;
            Transform leftLeg = leftHip != null
                ? leftHip.Find("Left_Leg") : null;
            Transform rightLeg = rightHip != null
                ? rightHip.Find("Right_Leg") : null;

            if (controller == null || spine == null || skull == null
                || leftArm == null || rightArm == null
                || leftLeg == null || rightLeg == null)
            {
                Debug.LogError("[MobileNPCPopulationBootstrap] Cannot attach exact "
                    + "Player_Server anatomy: missing a required NPC_Client bone.");
                return false;
            }

            // The checked-in Player_Server prefab is the source of these
            // EXACT bone-local BoxCollider dimensions. No extra anatomy:
            // Left/Right arms and legs remain ONE hitbox each.
            CreateNativePlayerDamageBox(skull, controller, "Head",
                MobileNPCInjurySystem.BodyPart.Head,
                new Vector3(-0.45f, 0f, 0f),
                new Vector3(0.7f, 0.7f, 0.7f));
            CreateNativePlayerDamageBox(spine, controller, "Torso",
                MobileNPCInjurySystem.BodyPart.Torso,
                new Vector3(-0.4f, 0f, 0f),
                new Vector3(0.9f, 1f, 0.6f));
            CreateNativePlayerDamageBox(leftArm, controller, "LeftArm",
                MobileNPCInjurySystem.BodyPart.LeftArm,
                new Vector3(-0.4f, 0f, 0f),
                new Vector3(0.8f, 0.4f, 0.4f));
            CreateNativePlayerDamageBox(rightArm, controller, "RightArm",
                MobileNPCInjurySystem.BodyPart.RightArm,
                new Vector3(-0.4f, 0f, 0f),
                new Vector3(0.8f, 0.4f, 0.4f));
            CreateNativePlayerDamageBox(leftLeg, controller, "LeftLeg",
                MobileNPCInjurySystem.BodyPart.LeftLeg,
                new Vector3(-0.4f, -0.05f, 0f),
                new Vector3(0.8f, 0.4f, 0.4f));
            CreateNativePlayerDamageBox(rightLeg, controller, "RightLeg",
                MobileNPCInjurySystem.BodyPart.RightLeg,
                new Vector3(-0.4f, 0.05f, 0f),
                new Vector3(0.8f, 0.4f, 0.4f));

            int count = visual.GetComponentsInChildren<MobileNPCBodyHitbox>(true).Length;
            Debug.Log("[MobileNPCPopulationBootstrap] Player-exact six animated "
                + "damage boxes built: Skull, Spine, Left_Arm, Right_Arm, "
                + "Left_Leg, Right_Leg. Native Player_Server centers and sizes; "
                + "separate from the ragdoll's bone colliders.");
            return count == MobileNPCBodyHitbox.ExpectedAnatomicalVolumes;
        }

        private static void CreateNativePlayerDamageBox(Transform ownerBone,
            CharacterController controller, string regionName,
            MobileNPCInjurySystem.BodyPart part,
            Vector3 nativeCenter, Vector3 nativeSize)
        {
            GameObject region = new GameObject("Hit_" + regionName);
            region.layer = 23; // Native ENTITY raycast layer.
            region.transform.SetParent(ownerBone, false);
            region.transform.localPosition = Vector3.zero;
            region.transform.localRotation = Quaternion.identity;
            region.transform.localScale = Vector3.one;

            BoxCollider box = region.AddComponent<BoxCollider>();
            box.center = nativeCenter;
            box.size = nativeSize;
            box.isTrigger = false;
            region.AddComponent<MobileNPCBodyHitbox>().Configure(part);
            // Keep the NPC_Client original colliders disabled during
            // movement. They are activated ONLY as ragdoll after death.
            Physics.IgnoreCollision(box, controller, true);
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
