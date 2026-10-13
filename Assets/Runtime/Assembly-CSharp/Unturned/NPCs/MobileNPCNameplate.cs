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
        [SerializeField, Min(1f)] private float visibleDistance = 32f;

        private TextMesh label;
        private Transform labelTransform;
        private MobileNPCHumanoidDisposition disposition;

        private void Awake()
        {
            disposition = GetComponent<MobileNPCHumanoidDisposition>();
            GameObject labelObject = new GameObject("MobileNPCNameplateText");
            labelObject.layer = 0;
            labelTransform = labelObject.transform;
            labelTransform.SetParent(transform, false);
            labelTransform.localPosition = Vector3.up * height;
            label = labelObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 48;
            label.characterSize = characterSize;
            label.color = Color.white;
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

            float distance = Vector3.Distance(camera.transform.position, labelTransform.position);
            label.gameObject.SetActive(distance <= visibleDistance);
            if (!label.gameObject.activeSelf)
                return;

            Vector3 direction = camera.transform.position - labelTransform.position;
            if (direction.sqrMagnitude > 0.001f)
                labelTransform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }
}
