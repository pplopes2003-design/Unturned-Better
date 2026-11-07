// Value-only stand-ins to compile original Line 1 policies in the isolated
// .NET test runner. NEVER included in Steam's Unity/Unturned module.
using System;

namespace UnityEngine
{
    public sealed class Transform
    {
        public Vector3 position;
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x; this.y = y; this.z = z;
        }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public float sqrMagnitude => x * x + y * y + z * z;

        public static Vector3 operator +(Vector3 a, Vector3 b) =>
            new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) =>
            new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) =>
            new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator /(Vector3 a, float b) =>
            new Vector3(a.x / b, a.y / b, a.z / b);

        public static Vector3 ClampMagnitude(Vector3 v, float maxLength)
        {
            float limit = maxLength * maxLength;
            float lengthSq = v.sqrMagnitude;
            return lengthSq <= limit || lengthSq < 0.00001f
                ? v : v * (maxLength / (float)Math.Sqrt(lengthSq));
        }
    }

    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float PI = (float)Math.PI;

        public static float Clamp01(float x) =>
            x < 0f ? 0f : x > 1f ? 1f : x;
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Cos(float a) => (float)Math.Cos(a);
        public static float Sin(float a) => (float)Math.Sin(a);
        public static float Sqrt(float a) => (float)Math.Sqrt(a);
        public static float Lerp(float a, float b, float f) => a + (b - a) * f;
    }

    public static class Random
    {
        private static readonly System.Random generator = new System.Random(12345);
        public static float value => (float)generator.NextDouble();
        public static float Range(float min, float max) =>
            min + (max - min) * value;
    }
}
