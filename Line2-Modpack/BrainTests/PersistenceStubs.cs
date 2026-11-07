// Test-only Unity/Unturned stand-ins. NEVER ship in the Steam module.
using System.Text.Json;

namespace SDG.Unturned
{
    internal static class Provider
    {
        internal static bool isServer = true;
    }
    internal static class Dedicator
    {
        internal static bool IsDedicatedServer;
    }
    internal sealed class LevelInfo { internal string name = "BrainTests"; }
    internal static class Level
    {
        internal static bool isLoaded = true;
        internal static LevelInfo info = new LevelInfo();
    }
}
namespace UnityEngine
{
    internal static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options =
            new JsonSerializerOptions { IncludeFields = true };
        internal static string ToJson(object value)
        {
            return JsonSerializer.Serialize(value, value.GetType(), Options);
        }
        internal static T FromJson<T>(string value)
        {
            return JsonSerializer.Deserialize<T>(value, Options);
        }
    }
}
