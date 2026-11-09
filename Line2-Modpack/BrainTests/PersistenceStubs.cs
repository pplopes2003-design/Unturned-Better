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
        internal static bool checkSafeIncludingClipVolumes(UnityEngine.Vector3 point)
        {
            return !(float.IsNaN(point.x) || float.IsNaN(point.y)
                || float.IsNaN(point.z));
        }
    }
}
// Test-only DTO shape matching the original Line1 injury snapshot.
namespace Unturned2.Line2
{
    // Runtime group roster is not linked into the isolated DTO policy test.
    internal static class Line2NPCGroupMember
    {
        internal static int LargestHistoricalGroupId;
        internal static void ReserveHistoricalGroupId(int groupId)
        {
            if (groupId > LargestHistoricalGroupId)
                LargestHistoricalGroupId = groupId;
        }
    }

    public sealed class Line2MobileNPCAdaptiveMemory
    {
        [System.Serializable] public sealed class ExperienceRecord
        {
            public int kind, sectorX, sectorZ, directObservations, heardObservations;
            public float positiveEvidence, negativeEvidence;
        }
    }
    public sealed class Line2MobileNPCTravelMemory
    {
        [System.Serializable] public sealed class LearnedRoute
        {
            public UnityEngine.Vector3 start, destination;
            public System.Collections.Generic.List<UnityEngine.Vector3> footsteps =
                new System.Collections.Generic.List<UnityEngine.Vector3>();
            public int successes, failedReuses;
        }
    }
    internal sealed class Line2MobileNPCInjurySystem
    {
        [System.Serializable]
        internal sealed class InjurySnapshot
        {
            public float[] condition = new float[6];
            public bool leftLegFractured;
            public bool rightLegFractured;
            public int bleedingSeverity;
            public int bleedingPart;
        }
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
