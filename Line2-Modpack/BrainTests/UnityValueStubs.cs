// Minimal value-only Unity stubs for compiling the imported tactical
// selector in BrainTests. This is NEVER included in the Steam module.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z)
        {
            this.x = x; this.y = y; this.z = z;
        }
    }

    public static class Mathf
    {
        public static float Clamp01(float x)
        {
            return x < 0f ? 0f : x > 1f ? 1f : x;
        }
        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }
    }
}
