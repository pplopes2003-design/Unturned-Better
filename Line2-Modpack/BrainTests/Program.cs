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

        if (!VisualRecognitionTests.Run())
            return 91;
        if (!SniperTests.Run())
            return 92;
        if (!ShotCorrectionMemoryTests.Run())
            return 93;
        if (!VehicleReservationTests.Run())
            return 94;
        if (!VehicleSteeringTests.Run())
            return 95;
        if (!VehicleMissionTests.Run())
            return 96;
        if (!VehicleObservationTests.Run())
            return 98;
        if (!SniperExperienceTests.Run())
            return 97;
        if (!NativeArrivalTests.Run())
            return 63;
        if (!PhysicalProgressTests.Run())
            return 64;
        if (!SquadRoleTests.Run())
            return 60;
        if (!FireCadenceTests.Run())
            return 61;
        if (!FiringPositionTests.Run())
            return 62;
        if (!CombatDynamicsTests.Run())
            return 66;
        if (!CombatTotalTests.Run())
            return 68;
        if (!CombatTotalSquadTests.Run())
            return 69;

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
        // An AGENT collider can be discovered by a zombie/animal overlap
        // even when the native game forbids player damage (PvE). Never
        // convert a forbidden damage channel into a real NPC injury.
        if (NpcExplosionMath.SelectEligibleDamage(false, false,
                75f, 60f) != 0f ||
            NpcExplosionMath.SelectEligibleDamage(false, true,
                75f, 60f) != 60f ||
            NpcExplosionMath.SelectEligibleDamage(true, true,
                75f, 60f) != 75f ||
            NpcExplosionMath.SelectEligibleDamage(true, false,
                75f, 60f) != 75f ||
            NpcExplosionMath.SelectEligibleDamage(false, false,
                0f, 75f) != 0f ||
            NpcExplosionMath.SelectEligibleDamage(true, false,
                float.NaN, 75f) != 0f ||
            NpcExplosionMath.SelectEligibleDamage(false, true,
                75f, float.PositiveInfinity) != 0f ||
            NpcExplosionMath.SelectEligibleDamage(true, true,
                float.NaN, 60f) != 60f)
        {
            Console.Error.WriteLine("NPC_EXPLOSION_PERMISSION_FAILED: unapproved native damage channel.");
            return 65;
        }
        Console.WriteLine("NPC_EXPLOSION_PERMISSION_OK: player/zombie flags and finite damage.");


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
        // Regression: a wounded Combat Total gunner with no visible cover
        // may still retreat ONLY if an independent physical ground candidate
        // was validated. This prevents endless firing in an exposed spot.
        var exposed = new Line2MobileNPCTactics { CombatOnlyMode = true };
        exposed.ReportDamage(82f, 100f, 20f);
        if (exposed.TryChooseDefensiveGoal(
                new UnityEngine.Vector3(0f, 0f, 0f), null,
                new UnityEngine.Vector3(4f, 0f, 0f), 20.2f,
                true, false) ||
            exposed.CurrentGoal == Line2MobileNPCTactics.Goal.Retreat)
        {
            Console.Error.WriteLine("NPC_TAC_EVASION_FAILED: unvalidated retreat.");
            return 84;
        }
        if (!exposed.TryChooseDefensiveGoal(
                new UnityEngine.Vector3(0f, 0f, 0f), null,
                new UnityEngine.Vector3(4f, 0f, 0f), 20.3f,
                true, true) ||
            exposed.CurrentGoal != Line2MobileNPCTactics.Goal.Retreat ||
            exposed.HasTacticalDestination ||
            !exposed.ReportValidatedRetreatPoint(
                new UnityEngine.Vector3(-6f, 0f, 0f), 20.4f))
        {
            Console.Error.WriteLine("NPC_TAC_EVASION_FAILED: wounded verified retreat.");
            return 85;
        }
        var exposedUnarmed = new Line2MobileNPCTactics { CombatOnlyMode = true };
        exposedUnarmed.ReportIncomingFire(31f);
        if (exposedUnarmed.TryChooseDefensiveGoal(
                new UnityEngine.Vector3(0f, 0f, 0f), null,
                new UnityEngine.Vector3(5f, 0f, 0f), 31.2f,
                false, false) ||
            exposedUnarmed.CurrentGoal != Line2MobileNPCTactics.Goal.None)
        {
            Console.Error.WriteLine("NPC_TAC_EVASION_FAILED: endless unreachable pending retreat.");
            return 86;
        }
        Console.WriteLine("NPC_TAC_EVASION_OK: real wound escapes with validated retreat; no invented route.");
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

        // Out-of-order localhost, radio and visual observations must never
        // rewind sensory memory or expose a not-yet-observed clue.
        memory.ObserveSound(new UnityEngine.Vector3(4f, 0f, 4f), 0.9f, 20f);
        memory.ObserveSight(new UnityEngine.Vector3(99f, 0f, 99f), 19f);
        memory.ObserveReport(new UnityEngine.Vector3(90f, 0f, 90f), 1f, 18f);
        if (!memory.TryGetEvidence(20.1f, out observed, out confidence,
                                    out uncertainty, out sense) ||
            sense != Line2MobileNPCPlayerKnowledge.Sense.Sound ||
            Math.Abs(observed.x - 4f) > 0.01f ||
            memory.TryGetEvidence(19f, out observed, out confidence,
                                  out uncertainty, out sense) ||
            memory.TrySuggestSearchPoint(19f, out observed))
        {
            Console.Error.WriteLine("NPC_EVIDENCE_ORDER_FAILED: delayed observations or future clues.");
            return 53;
        }
        memory.ObserveSight(new UnityEngine.Vector3(7f, 0f, 3f), 21f);
        memory.ObserveSound(new UnityEngine.Vector3(200f, 0f, 200f), 1f, 20.5f);
        if (!memory.TryGetEvidence(21.1f, out observed, out confidence,
                                    out uncertainty, out sense) ||
            sense != Line2MobileNPCPlayerKnowledge.Sense.Sight ||
            Math.Abs(observed.x - 7f) > 0.01f)
        {
            Console.Error.WriteLine("NPC_EVIDENCE_ORDER_FAILED: old sound replaced sight.");
            return 54;
        }
        memory.Clear();
        if (memory.HasEvidence ||
            memory.TryGetEvidence(22f, out observed, out confidence,
                                  out uncertainty, out sense))
        {
            Console.Error.WriteLine("NPC_EVIDENCE_ORDER_FAILED: stale clue after reset.");
            return 55;
        }
        Console.WriteLine("NPC_EVIDENCE_ORDER_OK: monotonic sensing, future-clue rejection and reset.");

        // The runtime must never restart a search when the sensory memory
        // rejects the new sound/radio evidence.
        var evidenceGate = new Line2MobileNPCPlayerKnowledge();
        var firstClue = new UnityEngine.Vector3(12f, 0f, 5f);
        if (!evidenceGate.TryObserveSound(firstClue, 0.64f, 30f) ||
            evidenceGate.TryObserveSound(firstClue, 0.64f, 31f) ||
            evidenceGate.TryObserveReport(firstClue, 0.58f, 32f) ||
            !evidenceGate.TryObserveSound(
                new UnityEngine.Vector3(32f, 0f, 5f), 0.8f, 34f))
        {
            Console.Error.WriteLine("NPC_CLUE_GATE_FAILED: duplicate sound/report reset investigation.");
            return 56;
        }
        evidenceGate.ObserveSight(new UnityEngine.Vector3(40f, 0f, 5f), 35f);
        if (evidenceGate.TryObserveSound(firstClue, 0.9f, 36f) ||
            evidenceGate.TryObserveReport(firstClue, 0.9f, 36f))
        {
            Console.Error.WriteLine("NPC_CLUE_GATE_FAILED: hearsay overrode recent eyesight.");
            return 57;
        }
        Console.WriteLine("NPC_CLUE_GATE_OK: duplicate acoustic and radio clues do not reset search.");

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
        // Investigation should cover distance promptly, then slow down
        // near the last observed clue. Commitment prevents gait flicker.
        var searchGait = new Line2MobileNPCLocomotionPolicy();
        var searchApproach = new Line2MobileNPCLocomotionPolicy.Situation
        {
            HasDestination = true, Investigating = true,
            TargetVisible = false, DestinationDistance = 24f
        };
        if (searchGait.Decide(searchApproach, 1f) !=
                Line2MobileNPCLocomotionPolicy.Gait.Sprint)
        {
            Console.Error.WriteLine("NPC_SEARCH_GAIT_FAILED: far clue approach.");
            return 87;
        }
        searchApproach.DestinationDistance = 12f;
        if (searchGait.Decide(searchApproach, 2.2f) !=
                Line2MobileNPCLocomotionPolicy.Gait.Walk)
        {
            Console.Error.WriteLine("NPC_SEARCH_GAIT_FAILED: middle-distance approach.");
            return 88;
        }
        searchApproach.DestinationDistance = 4f;
        if (searchGait.Decide(searchApproach, 3.4f) !=
                Line2MobileNPCLocomotionPolicy.Gait.Sneak)
        {
            Console.Error.WriteLine("NPC_SEARCH_GAIT_FAILED: final careful sweep.");
            return 89;
        }
        searchApproach.Injured = true;
        if (searchGait.Decide(searchApproach, 6f) !=
                Line2MobileNPCLocomotionPolicy.Gait.Walk)
        {
            Console.Error.WriteLine("NPC_SEARCH_GAIT_FAILED: injury must slow search.");
            return 90;
        }
        Console.WriteLine("NPC_SEARCH_GAIT_OK: sprint, walk, near-clue sneak and injury.");
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

        // Player visibility must respect body orientation as well as local
        // physics LOS: a transparent ray behind the NPC is not eyesight.
        if (!Line2NPCCombatRules.WithinVisualArc(1f) ||
            !Line2NPCCombatRules.WithinVisualArc(0.5f) ||
            Line2NPCCombatRules.WithinVisualArc(0.49f) ||
            Line2NPCCombatRules.WithinVisualArc(-1f) ||
            Line2NPCCombatRules.WithinVisualArc(float.NaN) ||
            Line2NPCCombatRules.WithinVisualArc(float.PositiveInfinity))
        {
            Console.Error.WriteLine("NPC_VISUAL_ARC_FAILED: rear target or invalid facing.");
            return 58;
        }
        // Native ItemMeleeAsset integration: test deterministic quality,
        // physical reach and rate safety independently of the Steam DLL.
        if (Math.Abs(Line2NPCCombatRules.MeleeDamage(20f, 0) - 10f) > 0.001f ||
            Math.Abs(Line2NPCCombatRules.MeleeDamage(20f, 25) - 15f) > 0.001f ||
            Math.Abs(Line2NPCCombatRules.MeleeDamage(20f, 100) - 20f) > 0.001f ||
            Line2NPCCombatRules.MeleeDamage(float.NaN, 100) != 0f ||
            Line2NPCCombatRules.MeleeDamage(50001f, 100) != 0f ||
            Line2NPCCombatRules.MeleeDamage(-1f, 100) != 0f ||
            Line2NPCCombatRules.MeleeReach(float.NaN) != 0f ||
            Line2NPCCombatRules.MeleeReach(0.2f) != 0f ||
            Math.Abs(Line2NPCCombatRules.MeleeReach(12f) - 3.2f) > 0.001f ||
            Math.Abs(Line2NPCCombatRules.MeleeCadence(0.5f) - 0.7f) > 0.001f ||
            Math.Abs(Line2NPCCombatRules.MeleeCadence(float.NaN) - 1.4f) > 0.001f)
        {
            Console.Error.WriteLine("NPC_MELEE_WEAPON_POLICY_FAILED: real item damage/reach/cadence.");
            return 59;
        }
        Console.WriteLine("NPC_MELEE_WEAPON_POLICY_OK: native item quality, reach and finite cadence.");

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
        UnityEngine.Vector3 squadOrigin = new UnityEngine.Vector3(0f, 0f, 0f);
        UnityEngine.Vector3 observedLeader = new UnityEngine.Vector3(25f, 0f, 0f);
        if (!Line2NpcSquadRegroupPolicy.MayRegroup(
                true, true, true, false, false, false, true,
                squadOrigin, observedLeader) ||
            Line2NpcSquadRegroupPolicy.MayRegroup(
                true, true, false, false, false, false, true,
                squadOrigin, observedLeader) ||
            Line2NpcSquadRegroupPolicy.MayRegroup(
                true, true, true, true, false, false, true,
                squadOrigin, observedLeader) ||
            Line2NpcSquadRegroupPolicy.MayRegroup(
                true, true, true, false, false, true, true,
                squadOrigin, observedLeader) ||
            Line2NpcSquadRegroupPolicy.MayRegroup(
                true, true, true, false, false, false, true,
                squadOrigin, new UnityEngine.Vector3(70f, 0f, 0f)) ||
            Line2NpcSquadRegroupPolicy.MayRegroup(
                true, true, true, false, false, false, true,
                squadOrigin, new UnityEngine.Vector3(8f, 0f, 0f)) ||
            Line2NpcSquadRegroupPolicy.MayRegroup(
                true, true, true, false, false, false, true,
                squadOrigin, new UnityEngine.Vector3(25f, 5f, 0f)) ||
            Line2NpcSquadRegroupPolicy.MayRegroup(
                true, true, true, false, false, false, true,
                squadOrigin, new UnityEngine.Vector3(float.NaN, 0f, 0f)))
        {
            Console.Error.WriteLine("NPC_SQUAD_REGROUP_FAILED: eyesight, combat, formation, bounds or floor safety.");
            return 28;
        }
        Console.WriteLine("NPC_SQUAD_REGROUP_OK: real local LOS, no combat preemption, 12-42m bounds and same-floor leader.");
        if (!InvestigationSafetyTests.Run())
        {
            Console.Error.WriteLine("NPC_SEARCH_AMBUSH_POLICY_FAILED: unsafe remembered location or blind attack.");
            return 29;
        }
        if (!RadioReinforcementTests.Run())
        {
            Console.Error.WriteLine("NPC_RADIO_REINFORCEMENT_FAILED: unsafe radio movement target.");
            return 30;
        }
        if (!EncounterPacingTests.Run())
        {
            Console.Error.WriteLine("NPC_ENCOUNTER_PACING_FAILED: speculative search capacity or map expiry.");
            return 31;
        }
        if (!SquadMoraleTests.Run())
        {
            Console.Error.WriteLine("NPC_SQUAD_MORALE_FAILED: group death permitted blind retreat.");
            return 32;
        }
        if (!LootClaimTests.Run())
        {
            Console.Error.WriteLine("NPC_LOOT_CLAIM_FAILED: exclusive item target lease / expiry.");
            return 33;
        }
        if (!RoutineRouteHealthTests.Run())
        {
            Console.Error.WriteLine("NPC_ROUTINE_ROUTE_FAILED: invalid physical A-Life progress monitoring.");
            return 34;
        }
        if (!SquadSupplyPolicyTests.Run())
        {
            Console.Error.WriteLine("NPC_SQUAD_SUPPLY_POLICY_FAILED: illegal resource sharing conditions.");
            return 35;
        }
        if (!CampSupplyPolicyTests.Run())
        {
            Console.Error.WriteLine("NPC_CAMP_STOCK_POLICY_FAILED: unauthorized native stockpile delivery.");
            return 36;
        }
        if (!CampWithdrawalTests.Run())
        {
            Console.Error.WriteLine("NPC_CAMP_WITHDRAW_FAILED: invalid native item custody or cross-wall loot.");
            return 37;
        }
        if (!ZombieSoundPolicyTests.Run())
        {
            Console.Error.WriteLine("ZOMBIE_REAL_SOUND_SEARCH_POLICY_FAILED: invented threat or invalid investigation path.");
            return 37;
        }
        if (!RaidGroupOrderTests.Run())
        {
            Console.Error.WriteLine("NPC_RAID_GROUP_ORDER_FAILED: illegal spoken raid formation or invisible-target authorization.");
            return 38;
        }
        Console.WriteLine("BrainTests PASSED: source C# state machine and perception safety.");
        Console.WriteLine("Not a Steam game compilation or NPC combat test.");
        return 0;
    }
}
