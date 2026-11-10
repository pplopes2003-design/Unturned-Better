using SDG.Unturned;
using UnityEngine;

namespace Unturned2.Line2
{
    /// <summary>
    /// Coarse local light exposure for firsthand visual recognition.
    /// A single shared snapshot is refreshed at most every 18 seconds,
    /// never per NPC/frame. Only the nearest plausible point/spot light
    /// triggers an extra world-occlusion ray. No renderer readbacks or
    /// GPU queries. This is NOT physically precise interior lighting.
    /// </summary>
    internal static class Line2NpcLocalLightSensor
    {
        private const int MaxCachedLights = 128;
        private const float CacheSeconds = 18f;
        private static Light[] _lights = new Light[0];
        private static float _nextCacheAt = -1f;

        internal static float Sample(Vector3 target, bool night)
        {
            if (!night) return 1f;
            if (!Level.isLoaded || Dedicator.IsDedicatedServer)
                return 0.45f;
            if (Time.time >= _nextCacheAt || Time.time < 0f)
                Refresh();
            Light best = null;
            float strongest = 0f;
            int inspected = Mathf.Min(_lights.Length, MaxCachedLights);
            for (int i = 0; i < inspected; ++i)
            {
                Light source = _lights[i];
                if (source == null || !source.isActiveAndEnabled ||
                    (source.type != LightType.Point && source.type != LightType.Spot) ||
                    source.intensity <= 0f || source.range <= 0f)
                    continue;
                Vector3 offset = target - source.transform.position;
                float distanceSq = offset.sqrMagnitude;
                if (distanceSq >= source.range * source.range) continue;
                if (source.type == LightType.Spot && distanceSq > 0.01f)
                {
                    float cosine = Vector3.Dot(source.transform.forward,
                        offset.normalized);
                    if (cosine < Mathf.Cos(source.spotAngle * 0.5f *
                        Mathf.Deg2Rad)) continue;
                }
                float contribution = source.intensity *
                    Mathf.Clamp01(1f - Mathf.Sqrt(distanceSq) /
                        source.range);
                if (contribution > strongest)
                {
                    strongest = contribution;
                    best = source;
                }
            }
            if (best == null || strongest < 0.10f) return 0.45f;
            Vector3 displacement = target - best.transform.position;
            float distance = displacement.magnitude;
            if (distance > 0.1f &&
                Physics.Raycast(best.transform.position,
                    displacement / distance, distance - 0.05f,
                    RayMasks.BLOCK_COLLISION,
                    QueryTriggerInteraction.Ignore))
                return 0.45f;
            return Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(strongest));
        }

        private static void Refresh()
        {
            _nextCacheAt = Time.time + CacheSeconds;
            // Shared snapshot avoids per-NPC scene-wide Light scanning.
            // Disabled/missing lights are filtered when sampled.
            _lights = UnityEngine.Object.FindObjectsOfType<Light>();
            if (_lights == null) _lights = new Light[0];
        }
    }
}
