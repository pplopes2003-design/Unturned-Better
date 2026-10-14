////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;

namespace SDG.Unturned
{
    /// <summary>
    /// Lightweight world-space name label for mobile NPCs. Uses Unity's built-in
    /// TextMesh to avoid a dependency on a custom Canvas or TMP prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MobileNPCHumanoidDisposition))]
    public sealed class MobileNPCNameplate : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float height = 2.15f;
        [SerializeField, Min(0.001f)] private float characterSize = 0.018f;
        [SerializeField, Min(1f)] private float visibleDistance = 9f;

        private TextMesh label;
        private Transform labelTransform;
        private MobileNPCHumanoidDisposition disposition;

        private void Awake()
        {
            disposition = GetComponent<MobileNPCHumanoidDisposition>();
            GameObject labelObject = new GameObject("MobileNPCNameplateText");
            // Use the visible NPC model layer, rather than Default which may be
            // absent from the gameplay camera culling mask.
            Transform visualRoot = transform.Find("HumanoidVisual");
            labelObject.layer = visualRoot != null ? visualRoot.gameObject.layer : gameObject.layer;
            labelTransform = labelObject.transform;
            labelTransform.SetParent(transform, false);
            labelTransform.localPosition = Vector3.up * height;
            label = labelObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 48;
            label.characterSize = characterSize;
            label.color = Color.white;
            label.gameObject.SetActive(false);
            label.richText = false;
            label.text = disposition != null ? disposition.DisplayName : "Sobrevivente";
            MeshRenderer renderer = labelObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = 20;
            }
        }

        private void LateUpdate()
        {
            if (label == null || labelTransform == null)
                return;

            if (disposition != null && label.text != disposition.DisplayName)
                label.text = disposition.DisplayName;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            Vector3 view = labelTransform.position - camera.transform.position;
            float distance = view.magnitude;
            // World-space labels are local identification cues, not wallhacks:
            // require close proximity, the NPC roughly in front of the
            // player, and unobstructed scene geometry.
            bool canRead = distance > 0.1f && distance <= visibleDistance
                && Vector3.Dot(camera.transform.forward, view / Mathf.Max(distance, 0.01f)) >= 0.92f
                && !Physics.Linecast(camera.transform.position,
                    labelTransform.position, RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore);
            if (label.gameObject.activeSelf != canRead)
                label.gameObject.SetActive(canRead);
            if (canRead)
            {
                // Copy the camera rotation: TextMesh faces the viewer with
                // readable glyph orientation; LookRotation(viewer - text)
                // inverted the authored text mesh in this prefab.
                labelTransform.rotation = camera.transform.rotation;
            }
        }
    }
}
