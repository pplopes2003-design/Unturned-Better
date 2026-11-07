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
        // Exact original Line 1 pursuit/search code, not a reimplementation:
        // vision loss must NOT track an invisible live Transform.
        var search = new Line2MobileNPCBrain();
        search.SetSpawnPosition(new UnityEngine.Vector3(0f, 0f, 0f));
        search.Reset();
        var target = new UnityEngine.Transform
        {
            position = new UnityEngine.Vector3(12f, 0f, 3f)
        };
        search.ReportVisibleTarget(target, 1f);
        if (search.CurrentState != Line2MobileNPCBrain.State.Pursuing ||
            !search.HasDestination)
        {
            Console.Error.WriteLine("Legacy brain did not pursue observed target.");
            return 7;
        }
        target.position = new UnityEngine.Vector3(90f, 0f, 90f);
        search.Tick(2f, false);
        if (Math.Abs(search.Destination.x - 12f) > 0.01f ||
            Math.Abs(search.Destination.z - 3f) > 0.01f)
        {
            Console.Error.WriteLine("Legacy brain tracked concealed live player.");
            return 8;
        }
        search.Tick(10f, false);
        if (search.CurrentState != Line2MobileNPCBrain.State.Investigating)
        {
            Console.Error.WriteLine("Legacy brain failed transition to physical search.");
            return 9;
        }
        for (int i = 0; i < 3; ++i)
        {
            search.ReportDestinationReached();
            if (search.CurrentState != Line2MobileNPCBrain.State.Investigating ||
                !search.HasDestination)
            {
                Console.Error.WriteLine("Legacy brain did not visit search waypoint " + i);
                return 10;
            }
        }
        search.ReportDestinationReached();
        if (search.CurrentState != Line2MobileNPCBrain.State.Returning)
        {
            Console.Error.WriteLine("Legacy brain failed return after three search points.");
            return 11;
        }
        search.ReportDestinationReached();
        if (search.CurrentState != Line2MobileNPCBrain.State.Idle || search.HasDestination)
        {
            Console.Error.WriteLine("Legacy brain failed physical return completion.");
            return 12;
        }

        var memory = new Line2MobileNPCPlayerKnowledge();
        memory.ObserveSight(new UnityEngine.Vector3(6f, 0f, 8f), 1f);
        UnityEngine.Vector3 observed;
        float confidence, uncertainty;
        Line2MobileNPCPlayerKnowledge.Sense sense;
        if (!memory.TryGetEvidence(2f, out observed, out confidence,
                                   out uncertainty, out sense) ||
            sense != Line2MobileNPCPlayerKnowledge.Sense.Sight ||
            confidence <= 0f)
        {
            Console.Error.WriteLine("Legacy sensory memory rejected verified sight.");
            return 13;
        }
        if (memory.TryGetEvidence(90f, out observed, out confidence,
                                  out uncertainty, out sense))
        {
            Console.Error.WriteLine("Legacy sensory memory never expired.");
            return 14;
        }
        memory.Clear();
        if (memory.HasEvidence)
        {
            Console.Error.WriteLine("Legacy sensory memory failed reset.");
            return 15;
        }

        var gait = new Line2MobileNPCLocomotionPolicy();
        var running = new Line2MobileNPCLocomotionPolicy.Situation
        {
            HasDestination = true, Pursuing = true, NearTarget = false,
            DestinationDistance = 12f
        };
        if (gait.Decide(running, 2f) != Line2MobileNPCLocomotionPolicy.Gait.Sprint)
        {
            Console.Error.WriteLine("Legacy locomotion did not sprint on pursuit.");
            return 16;
        }
        running.HasDestination = false;
        if (gait.Decide(running, 2.2f) != Line2MobileNPCLocomotionPolicy.Gait.Hold)
        {
            Console.Error.WriteLine("Legacy locomotion did not stop without destination.");
            return 17;
        }
        Console.WriteLine("LINE1_PORT_REGRESSION_OK: lost-LOS memory, physical search spiral,");
        Console.WriteLine("return to spawn, evidence expiry, pursuit sprint and no-destination hold.");

        // Line 1 combat prioritizes a verified player inside 20 metres;
        // a radio hint or player position alone must not authorize attack.
        if (Line2NPCCombatRules.CanEngagePlayer("hostile", "normal", false, 4f) ||
            Line2NPCCombatRules.CanEngagePlayer("friendly", "total", true, 4f) ||
            Line2NPCCombatRules.CanEngagePlayer("hostile", "passive", true, 4f) ||
            Line2NPCCombatRules.CanEngagePlayer("hostile", "normal", true, 401f) ||
            !Line2NPCCombatRules.CanEngagePlayer("hostile", "normal", true, 400f))
        {
            Console.Error.WriteLine("COMBAT_POLICY_FAILED: player vision / 20m / passive.");
            return 18;
        }

        if (!Line2NPCCombatRules.AreOpposed("hostile", "friendly", "normal") ||
            !Line2NPCCombatRules.AreOpposed("friendly", "hostile", "total") ||
            Line2NPCCombatRules.AreOpposed("neutral", "hostile", "normal") ||
            Line2NPCCombatRules.AreOpposed("hostile", "hostile", "normal") ||
            Line2NPCCombatRules.AreOpposed("hostile", "friendly", "passive") ||
            !Line2NPCCombatRules.CanEngageZombie("neutral", "normal") ||
            Line2NPCCombatRules.CanEngageZombie("hostile", "passive"))
        {
            Console.Error.WriteLine("COMBAT_POLICY_FAILED: faction safety / zombies.");
            return 19;
        }

        if (Line2NPCCombatRules.MayCommitHit("hostile", "normal", false, true, true) ||
            Line2NPCCombatRules.MayCommitHit("hostile", "normal", true, false, true) ||
            Line2NPCCombatRules.MayCommitHit("hostile", "normal", true, true, false) ||
            Line2NPCCombatRules.MayCommitHit("hostile", "passive", true, true, true) ||
            !Line2NPCCombatRules.MayCommitHit("hostile", "total", true, true, true))
        {
            Console.Error.WriteLine("COMBAT_POLICY_FAILED: server/health/LOS attack gate.");
            return 20;
        }

        Console.WriteLine("NPC_COMBAT_POLICY_OK: player priority, confirmed sight, " +
            "neutral immunity, faction hostility and passive-mode safety.");

        if (!Line2NPCAmmoRules.CanFire(24, 1, 24) ||
            Line2NPCAmmoRules.CanFire(25, 1, 24) ||
            Line2NPCAmmoRules.CanFire(0, 1, 24) ||
            Line2NPCAmmoRules.CanFire(24, 0, 24) ||
            Line2NPCAmmoRules.CanFire(1, 2, 24) ||
            Line2NPCAmmoRules.RemainingAfterShot(24, 2, 24) != 22 ||
            Line2NPCAmmoRules.RemainingAfterShot(1, 2, 24) != -1 ||
            !Line2NPCAmmoRules.CanLoadReplacement(12, 12) ||
            Line2NPCAmmoRules.CanLoadReplacement(0, 12) ||
            Line2NPCAmmoRules.CanLoadReplacement(14, 12))
        {
            Console.Error.WriteLine("NPC_FINITE_AMMO_FAILED: invalid mag/shot/reload.");
            return 21;
        }
        Console.WriteLine("NPC_FINITE_AMMO_OK: 24,12 rounds, ammo per shot, " +
            "no infinite cartridges, reload capacity and invalid state.");

        // Original Line 1 MobileNPCFaction relationship table:
        // friendlies and hostiles have explicit allies/foes, while neutral
        // scavengers must never become an arbitrary hostile target.
        var faction = Line2NPCFactionPolicy.Id.Raiders;
        if (Line2NPCFactionPolicy.DefaultForRole("hostile") != faction ||
            Line2NPCFactionPolicy.DefaultForRole("friendly") !=
                Line2NPCFactionPolicy.Id.Traders ||
            Line2NPCFactionPolicy.GetRelationship(
                faction, Line2NPCFactionPolicy.Id.Traders) !=
                Line2NPCFactionPolicy.Relationship.Hostile ||
            Line2NPCFactionPolicy.GetRelationship(
                faction, Line2NPCFactionPolicy.Id.Military) !=
                Line2NPCFactionPolicy.Relationship.Hostile ||
            Line2NPCFactionPolicy.GetRelationship(
                Line2NPCFactionPolicy.Id.Survivors,
                Line2NPCFactionPolicy.Id.Military) !=
                Line2NPCFactionPolicy.Relationship.Neutral ||
            Line2NPCFactionPolicy.GetRelationship(
                Line2NPCFactionPolicy.Id.Scavengers, faction) !=
                Line2NPCFactionPolicy.Relationship.Neutral ||
            Line2NPCFactionPolicy.GetRelationship(
                faction, faction) != Line2NPCFactionPolicy.Relationship.Allied)
        {
            Console.Error.WriteLine("NPC_FACTION_POLICY_FAILED: Line 1 relationship matrix.");
            return 22;
        }
        Console.WriteLine("NPC_FACTION_POLICY_OK: raiders, traders, military, " +
            "survivors, neutral scavengers and allied groups.");

        if (!Line2NPCRadioRules.MayRelay(2, 2,
                Line2NPCFactionPolicy.Id.Raiders,
                Line2NPCFactionPolicy.Id.Raiders,
                true, true, true, true, 90f) ||
            Line2NPCRadioRules.MayRelay(2, 3,
                Line2NPCFactionPolicy.Id.Raiders,
                Line2NPCFactionPolicy.Id.Raiders,
                true, true, true, true, 5f) ||
            Line2NPCRadioRules.MayRelay(2, 2,
                Line2NPCFactionPolicy.Id.Raiders,
                Line2NPCFactionPolicy.Id.Military,
                true, true, true, true, 5f) ||
            Line2NPCRadioRules.MayRelay(2, 2,
                Line2NPCFactionPolicy.Id.Raiders,
                Line2NPCFactionPolicy.Id.Raiders,
                true, true, true, false, 5f) ||
            Line2NPCRadioRules.MayRelay(2, 2,
                Line2NPCFactionPolicy.Id.Raiders,
                Line2NPCFactionPolicy.Id.Raiders,
                true, true, true, true, 91f) ||
            Line2NPCRadioRules.MayRelay(2, 2,
                Line2NPCFactionPolicy.Id.Raiders,
                Line2NPCFactionPolicy.Id.Raiders,
                true, false, true, true, 5f) ||
            Line2NPCRadioRules.QuantizeSector(12f) != 10f ||
            Line2NPCRadioRules.QuantizeSector(-12f) != -10f ||
            !float.IsNaN(Line2NPCRadioRules.QuantizeSector(float.NaN)))
        {
            Console.Error.WriteLine("NPC_RADIO_POLICY_FAILED: groups, radio items, " +
                "factions, alive, range or approximate sector.");
            return 23;
        }
        Console.WriteLine("NPC_RADIO_POLICY_OK: authentic equipment, same group/faction, " +
            "radio range, valid actors, coarse sector.");

        if (!PersistenceTests.Run())
        {
            Console.Error.WriteLine("NPC_PERSISTENCE_POLICY_FAILED: invalid checkpoint handling.");
            return 24;
        }
        if (!GlobalRouteTests.Run())
        {
            Console.Error.WriteLine("NPC_COARSE_ASTAR_FAILED: Line 1 graph detour/edge gate.");
            return 25;
        }
        if (!SettlementLedgerTests.Run())
        {
            Console.Error.WriteLine("NPC_SETTLEMENT_LEDGER_FAILED: invalid native base accounting.");
            return 26;
        }
        if (!TacticalJourneyTests.Run())
        {
            Console.Error.WriteLine("NPC_TACTICAL_JOURNEY_FAILED: path progress / stuck-route safety.");
            return 27;
        }
        Console.WriteLine("BrainTests PASSED: source C# state machine and perception safety.");
        Console.WriteLine("Not a Steam game compilation or NPC combat test.");
        return 0;
    }
}
