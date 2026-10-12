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
            template.AddComponent<MobileNPCHumanoidMotor>();
            template.AddComponent<MobileNPCHumanoidPerception>();
            template.AddComponent<MobileNPCHumanoidDisposition>();
            template.AddComponent<MobileNPCFaction>();
            template.AddComponent<MobileNPCHumanoidVitals>();
            template.AddComponent<MobileNPCHumanoidAim>();
            template.AddComponent<MobileNPCHumanoidCombat>();
            template.AddComponent<MobileNPCHumanoidRangedCombat>();
            template.AddComponent<MobileNPCRangedPresentation>();
            template.AddComponent<MobileNPCVisualAnimation>();
            template.AddComponent<MobileNPCVisualGuard>();
            template.AddComponent<MobileNPCInventory>();
            template.AddComponent<MobileNPCSurvivalNeeds>();
            template.AddComponent<MobileNPCLooting>();
            template.AddComponent<MobileNPCMedicalUse>();
            template.AddComponent<MobileNPCSettlementPlanner>();
            template.AddComponent<MobileNPCSettlementBuilder>();
            template.AddComponent<MobileNPCGroupMember>();
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

            // The visual rig has no colliders. Add a separate trigger hit volume so
            // Unturned's normal aim/interact raycasts can resolve the NPC, without
            // adding a second solid body that interferes with CharacterController movement.
            GameObject hitboxObject = new GameObject("MobileNPCHitbox");
            hitboxObject.layer = visualRenderLayer;
            hitboxObject.transform.SetParent(template.transform, false);
            hitboxObject.transform.localPosition = Vector3.zero;
            hitboxObject.transform.localRotation = Quaternion.identity;
            CapsuleCollider hitbox = hitboxObject.AddComponent<CapsuleCollider>();
            hitbox.direction = 1;
            hitbox.center = new Vector3(0f, 0.9f, 0f);
            hitbox.height = 1.8f;
            hitbox.radius = 0.34f;
            hitbox.isTrigger = true;

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

            MonoBehaviour[] legacyBehaviours = visual.GetComponentsInChildren<MonoBehaviour>(true);
            int disabledLegacyBehaviourCount = 0;
            int preservedVisualBehaviourCount = 0;
            for (int i = 0; i < legacyBehaviours.Length; ++i)
            {
                MonoBehaviour behaviour = legacyBehaviours[i];
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

            directorObject = new GameObject("MobileNPCPopulationDirector_Runtime");
            MobileNPCPopulationDirector director = directorObject.AddComponent<MobileNPCPopulationDirector>();
            directorObject.AddComponent<MobileNPCInteractionController>();
            director.Configure(template);

            Debug.Log("[MobileNPCPopulationBootstrap] Single-player mobile NPC population initialized for loaded map "
                + level + " with root layer Agent (" + template.layer + ") and CharacterController overlap recovery disabled."
                + " Experimental local entities only; local NPC snapshots are persisted, but network replication is not implemented.");
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
