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
        var tactical = new Line2MobileNPCTactics();
        tactical.ReportDamage(20f, 100f, 1f);
        bool defensive = tactical.TryChooseDefensiveGoal(
            new UnityEngine.Vector3(0f, 0f, 0f),
            null, new UnityEngine.Vector3(-5f, 0f, 0f), 1.1f, false);
        if (!defensive ||
            tactical.CurrentGoal != Line2MobileNPCTactics.Goal.Retreat ||
            tactical.HasTacticalDestination ||
            !tactical.ReportValidatedRetreatPoint(
                new UnityEngine.Vector3(12f, 0f, 0f), 1.2f) ||
            !tactical.HasTacticalDestination)
        {
            Console.Error.WriteLine("Imported Line 1 tactical retreat policy FAILED.");
            return 4;
        }
        tactical.ReportGoalReached();
        if (tactical.CurrentGoal != Line2MobileNPCTactics.Goal.None ||
            tactical.HasTacticalDestination)
        {
            Console.Error.WriteLine("Committed tactical goal cleanup FAILED.");
            return 5;
        }
        tactical.ReportHealth(100f, 100f);
        tactical.ReportIncomingFire(6f);
        bool requestedCover = tactical.TryChooseDefensiveGoal(
            new UnityEngine.Vector3(0f, 0f, 0f),
            new UnityEngine.Vector3(2f, 0f, 5f),
            new UnityEngine.Vector3(-5f, 0f, 0f), 6.1f, false);
        if (!requestedCover ||
            tactical.CurrentGoal != Line2MobileNPCTactics.Goal.SeekCover ||
            !tactical.HasTacticalDestination)
        {
            Console.Error.WriteLine("Imported Line 1 tactical cover policy FAILED.");
            return 6;
        }
        Console.WriteLine("NPC_TACTICS_SELFTEST_OK: real wound withdrawal and verified-cover commitment.");
        Console.WriteLine("BrainTests PASSED: source C# state machine and perception safety.");
        Console.WriteLine("Not a Steam game compilation or NPC combat test.");
        return 0;
    }
}
