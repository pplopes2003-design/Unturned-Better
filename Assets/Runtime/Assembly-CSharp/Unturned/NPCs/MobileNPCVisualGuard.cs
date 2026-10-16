////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Keeps the NPC_Client visual hierarchy renderable. This component intentionally
    /// creates no fallback geometry: the humanoid mesh is the only visible body.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCVisualGuard : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;
        private Transform visualRoot;
        private Renderer[] visualRenderers = new Renderer[0];
        private float nextRefreshTime;
        private bool warnedAboutInvisibleRenderers;

        private void Awake()
        {
            RefreshHierarchyReferences();
            EnforceRenderableState();
        }

        private void LateUpdate()
        {
            if (Time.time < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.time + RefreshInterval;
            RefreshHierarchyReferences();
            EnforceRenderableState();
            DiagnoseVisibility();
        }

        private void RefreshHierarchyReferences()
        {
            if (visualRoot == null)
            {
                visualRoot = transform.Find("HumanoidVisual");
            }

            if (visualRoot != null && (visualRenderers == null || visualRenderers.Length == 0))
            {
                visualRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            }
        }

        private void EnforceRenderableState()
        {
            if (visualRoot == null)
            {
                return;
            }

            int renderLayer = visualRoot.gameObject.layer;
            // Only touch renderers and the ancestors needed to display them.
            // The full NPC_Client hierarchy also contains six animated
            // MobileNPCBodyHitbox children on ENTITY (23). Repainting every
            // descendant to the visual MEDIUM (16) layer made all NPC spawns
            // fail the mandatory damage-mask invariant. Activating unrelated
            // rig nodes here can also undo gameplay/corpse setup.
            for (int i = 0; i < visualRenderers.Length; ++i)
            {
                Renderer renderer = visualRenderers[i];
                if (renderer == null)
                    continue;

                Transform node = renderer.transform;
                while (node != null && node != transform)
                {
                    if (node.gameObject.layer != renderLayer)
                        node.gameObject.layer = renderLayer;
                    if (!node.gameObject.activeSelf)
                        node.gameObject.SetActive(true);
                    if (node == visualRoot)
                        break;
                    node = node.parent;
                }
            }

            for (int i = 0; i < visualRenderers.Length; ++i)
            {
                Renderer renderer = visualRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                }
            }
        }

        private void DiagnoseVisibility()
        {
            if (warnedAboutInvisibleRenderers || visualRoot == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            bool hasUsableMesh = false;
            bool anyVisible = false;
            for (int i = 0; i < visualRenderers.Length; ++i)
            {
                Renderer renderer = visualRenderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (renderer is SkinnedMeshRenderer skinned)
                {
                    hasUsableMesh |= skinned.sharedMesh != null && skinned.sharedMesh.vertexCount > 0;
                }
                else if (renderer is MeshRenderer meshRenderer)
                {
                    MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                    hasUsableMesh |= filter != null && filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0;
                }

                anyVisible |= renderer.isVisible;
            }

            if (!hasUsableMesh || anyVisible)
            {
                return;
            }

            Bounds bodyBounds = new Bounds(transform.position + Vector3.up * 0.95f, new Vector3(0.7f, 1.9f, 0.7f));
            if (!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bodyBounds))
            {
                return;
            }

            warnedAboutInvisibleRenderers = true;
            int renderLayer = visualRoot.gameObject.layer;
            bool cameraIncludesLayer = (camera.cullingMask & (1 << renderLayer)) != 0;
            Debug.LogWarning("[MobileNPCVisualGuard] NPC_Client mesh is still invisible; no diagnostic capsule will be created. "
                + "camera=" + camera.name + ", cameraMask=0x" + camera.cullingMask.ToString("X8")
                + ", renderLayer=" + renderLayer + ", cameraIncludesLayer=" + cameraIncludesLayer
                + ", rendererCount=" + visualRenderers.Length + ", bounds=" + bodyBounds + ".", this);
        }
    }
}
