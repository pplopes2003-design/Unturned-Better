using System;
using System.Collections.Generic;
using Unturned2.Line2;

namespace Unturned2.Line2
{
    internal static class ProbeLogger
    {
        internal static readonly List<string> Entries = new List<string>();
        public static void Log(string message)
        {
            Entries.Add(message);
            Console.WriteLine(message);
        }
    }
}

internal static class Program
{
    private static int Main()
    {
        NpcDecisionBrainSelfTest.Run();
        bool success = ProbeLogger.Entries.Exists(
            s => s.StartsWith("NPC_BRAIN_SELFTEST_OK:", StringComparison.Ordinal));
        bool failed = ProbeLogger.Entries.Exists(
            s => s.StartsWith("NPC_BRAIN_SELFTEST_FAILED:", StringComparison.Ordinal));
        if (failed || !success)
        {
            Console.Error.WriteLine("NPC brain deterministic test FAILED.");
            return 1;
        }

        var brain = new NpcDecisionBrain();
        var noVision = new NpcDecisionInput
        {
            IsHostile = true,
            RemoteSuggestion = "engage",
            HasRecentMemory = false,
            ThreatVisible = false
        };
        if (brain.Evaluate(2f, noVision) != NpcIntent.Patrol)
        {
            Console.Error.WriteLine("NPC brain accepted remote targeting without sight.");
            return 2;
        }

        if (NpcExplosionMath.Calculate(100f, 10f, 0f) != 100f ||
            NpcExplosionMath.Calculate(100f, 10f, 5f) != 50f ||
            NpcExplosionMath.Calculate(100f, 10f, 10f) != 0f ||
            NpcExplosionMath.Calculate(100f, 10f, 12f) != 0f ||
            NpcExplosionMath.Calculate(100f, 10f, float.NaN) != 0f ||
            NpcExplosionMath.Calculate(float.PositiveInfinity, 10f, 2f) != 0f ||
            NpcExplosionMath.Calculate(100f, 0f, 2f) != 0f)
        {
            Console.Error.WriteLine("NPC radial explosion damage math FAILED.");
            return 3;
        }
        Console.WriteLine("EXPLOSION_MATH_OK: center, half-radius, edge, out of range and invalid numbers.");
        Console.WriteLine("BrainTests PASSED: source C# state machine and perception safety.");
        Console.WriteLine("Not a Steam game compilation or NPC combat test.");
        return 0;
    }
}
