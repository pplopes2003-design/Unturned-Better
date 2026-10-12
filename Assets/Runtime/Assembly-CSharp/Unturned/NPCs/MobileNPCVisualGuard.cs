////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Runtime safety net for the mobile NPC visual hierarchy. Reasserts renderable
    /// layers/state after prefab cloning and provides a temporary visible capsule only
    /// when the NPC is in the active camera's view but none of its authored renderers render.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MobileNPCVisualGuard : MonoBehaviour
    {
        private const float RefreshInterval = 0.35f;
        private static readonly Vector3 FallbackBoundsSize = new Vector3(0.7f, 1.9f, 0.7f);

        private Transform visualRoot;
        private Renderer[] visualRenderers = new Renderer[0];
        private GameObject fallbackObject;
        private Renderer fallbackRenderer;
        private Material fallbackMaterial;
        private float nextRefreshTime;
        private bool warnedAboutFallback;

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
            UpdateVisibilityFallback();
        }

        private void RefreshHierarchyReferences()
        {
            if (visualRoot == null)
            {
                Transform found = transform.Find("HumanoidVisual");
                if (found != null)
                {
                    visualRoot = found;
                }
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

            Transform[] nodes = visualRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < nodes.Length; ++i)
            {
                if (nodes[i] == null)
                {
                    continue;
                }

                // Preserve the render layer authored by NPC_Client. The game uses
                // named layers such as Medium (16); moving the mesh to Default (0)
                // can place it outside Unturned's gameplay camera culling mask.
                int renderLayer = visualRoot.gameObject.layer;
                if (nodes[i].gameObject.layer != renderLayer)
                {
                    nodes[i].gameObject.layer = renderLayer;
                }

                if (!nodes[i].gameObject.activeSelf)
                {
                    nodes[i].gameObject.SetActive(true);
                }
            }

            for (int i = 0; i < visualRenderers.Length; ++i)
            {
                Renderer renderer = visualRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!renderer.enabled)
                {
                    renderer.enabled = true;
                }

                if (renderer.forceRenderingOff)
                {
                    renderer.forceRenderingOff = false;
                }

                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                }
            }
        }

        private void UpdateVisibilityFallback()
        {
            Camera camera = Camera.main;
            if (camera == null || visualRoot == null)
            {
                SetFallbackActive(false);
                return;
            }

            bool authoredRendererVisible = false;
            bool hasUsableMesh = false;
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

                authoredRendererVisible |= renderer.isVisible;
            }

            Vector3 bodyCenter = transform.position + Vector3.up * 0.95f;
            Bounds bodyBounds = new Bounds(bodyCenter, FallbackBoundsSize);
            bool bodyInCameraView = GeometryUtility.TestPlanesAABB(
                GeometryUtility.CalculateFrustumPlanes(camera), bodyBounds);
            int renderLayer = visualRoot.gameObject.layer;
            bool cameraIncludesRenderLayer = (camera.cullingMask & (1 << renderLayer)) != 0;

            // This is deliberately a diagnostic fallback, not the NPC's normal model.
            // If the real humanoid is visible it stays disabled; if it is not, the capsule
            // gives the developer a visible position marker while the log records the fault.
            bool needsFallback = bodyInCameraView && hasUsableMesh && !authoredRendererVisible;
            SetFallbackActive(needsFallback);
            if (needsFallback && !warnedAboutFallback)
            {
                warnedAboutFallback = true;
                Debug.LogWarning("[MobileNPCVisualGuard] NPC body is in the main camera frustum, "
                    + "but NPC_Client renderers are not visible. camera=" + camera.name
                    + ", cameraMask=0x" + camera.cullingMask.ToString("X8")
                    + ", renderLayer=" + renderLayer + ", cameraIncludesLayer=" + cameraIncludesRenderLayer
                    + ", rendererCount=" + visualRenderers.Length
                    + ", bounds=" + bodyBounds + ". A temporary capsule was requested; inspect this "
                    + "camera/layer audit if it is still invisible.", this);
            }
        }

        private void SetFallbackActive(bool shouldBeActive)
        {
            if (!shouldBeActive)
            {
                if (fallbackObject != null && fallbackObject.activeSelf)
                {
                    fallbackObject.SetActive(false);
                }
                return;
            }

            if (fallbackObject == null)
            {
                fallbackObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallbackObject.name = "NPC_VISUAL_DIAGNOSTIC_FALLBACK";
                // Use the same layer as the native NPC_Client prefab, not Default.
                fallbackObject.layer = visualRoot != null ? visualRoot.gameObject.layer : gameObject.layer;
                fallbackObject.transform.SetParent(transform, false);
                fallbackObject.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                fallbackObject.transform.localRotation = Quaternion.identity;
                fallbackObject.transform.localScale = new Vector3(0.45f, 0.9f, 0.45f);

                Collider collider = fallbackObject.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                fallbackRenderer = fallbackObject.GetComponent<Renderer>();
                // Prefer an unlit diagnostic material so lighting/shadow settings cannot
                // make the marker look black. Standard remains a fallback for stripped variants.
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                if (fallbackRenderer != null && shader != null)
                {
                    fallbackMaterial = new Material(shader);
                    fallbackMaterial.color = new Color(1f, 0.1f, 0.8f, 1f);
                    fallbackRenderer.sharedMaterial = fallbackMaterial;
                    fallbackRenderer.enabled = true;
                    fallbackRenderer.forceRenderingOff = false;
                }
            }

            if (fallbackObject != null && !fallbackObject.activeSelf)
            {
                fallbackObject.SetActive(true);
            }
        }

        private void OnDestroy()
        {
            if (fallbackMaterial != null)
            {
                Destroy(fallbackMaterial);
                fallbackMaterial = null;
            }
        }
    }
}
