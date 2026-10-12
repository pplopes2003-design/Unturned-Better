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
            template.SetActive(false);

            CharacterController controller = template.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 38f;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.08f;

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
            template.AddComponent<MobileNPCInventory>();
            template.AddComponent<MobileNPCLooting>();
            template.AddComponent<MobileNPCMedicalUse>();
            template.AddComponent<MobileNPCSettlementPlanner>();
            template.AddComponent<MobileNPCSettlementBuilder>();
            template.AddComponent<MobileNPCGroupMember>();

            // Reuse the game's existing humanoid visual hierarchy without modifying its
            // legacy prefab or retaining its player/NPC-specific gameplay behaviours.
            GameObject visual = Object.Instantiate(visualPrefab, template.transform, false);
            visual.name = "HumanoidVisual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

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
            for (int i = 0; i < legacyBehaviours.Length; ++i)
            {
                legacyBehaviours[i].enabled = false;
            }

            directorObject = new GameObject("MobileNPCPopulationDirector_Runtime");
            MobileNPCPopulationDirector director = directorObject.AddComponent<MobileNPCPopulationDirector>();
            director.Configure(template);

            Debug.Log("[MobileNPCPopulationBootstrap] Single-player mobile NPC population initialized for loaded map "
                + level + ". Experimental local entities only; local NPC snapshots are persisted, but network replication is not implemented.");
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
