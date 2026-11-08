using System;
using Unturned2.Line2;
using UnityEngine;

internal static class TacticalJourneyTests
{
    private static Line2NpcTacticalJourneyWatchdog.Outcome Observe(
        Line2NpcTacticalJourneyWatchdog watch,
        Line2MobileNPCTactics.Goal goal, float x, float now,
        bool pending = false, float goalX = 12f)
    {
        return watch.Observe(goal, new Vector3(goalX, 0f, 0f),
            new Vector3(x, 0f, 0f), now, pending);
    }

    internal static bool Run()
    {
        var watch = new Line2NpcTacticalJourneyWatchdog();
        var flank = Line2MobileNPCTactics.Goal.Flank;
        if (Observe(watch, flank, 0f, 0f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(watch, flank, 0.2f, 1f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(watch, flank, 1f, 2f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(watch, flank, 1f, 6f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(watch, flank, 1f, 7.2f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Blocked)
            return false;

        // A pending Steam Seeker gets extra time, but never forever.
        var pending = new Line2NpcTacticalJourneyWatchdog();
        if (Observe(pending, Line2MobileNPCTactics.Goal.Retreat, 0f, 0f, true) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(pending, Line2MobileNPCTactics.Goal.Retreat, 0f, 6f, true) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(pending, Line2MobileNPCTactics.Goal.Retreat, 0f, 8.2f, true) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Blocked)
            return false;

        // A newly verified DIFFERENT destination is a new route; the same
        // failed destination would be rejected by the runtime 16s blacklist.
        if (Observe(pending, flank, 0f, 9f, false, 22f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(pending, flank, 0f, 9.1f, false, 25f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(pending, flank, 24.8f, 10f, false, 25f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Holding ||
            Observe(pending, flank, 24.8f, 10.8f, false, 25f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Holding)
            return false;

        // A successfully reached defensive refuge must stay committed until
        // the real tactics deadline expires; don't immediately charge back.
        var retreat = new Line2NpcTacticalJourneyWatchdog();
        if (Observe(retreat, Line2MobileNPCTactics.Goal.Retreat,
                10.9f, 1f) != Line2NpcTacticalJourneyWatchdog.Outcome.Holding ||
            Observe(retreat, Line2MobileNPCTactics.Goal.Retreat,
                10.9f, 2f) != Line2NpcTacticalJourneyWatchdog.Outcome.Holding ||
            Observe(retreat, Line2MobileNPCTactics.Goal.SeekCover,
                10.9f, 3f) != Line2NpcTacticalJourneyWatchdog.Outcome.Reached)
            return false;

        // Never "reach" a destination on another floor just because x/z
        // overlap with the planned destination.
        var vertical = new Line2NpcTacticalJourneyWatchdog();
        if (vertical.Observe(flank, new Vector3(0f, 7f, 0f),
                new Vector3(0f, 0f, 0f), 0f, false) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            vertical.Observe(flank, new Vector3(0f, 7f, 0f),
                new Vector3(0f, 0f, 0f), 5.2f, false) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Blocked)
            return false;

        // Never allow NaN to become a legitimate movement goal.
        if (watch.Observe(flank, new Vector3(float.NaN, 0f, 0f),
                new Vector3(0f, 0f, 0f), 9f, false) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Blocked ||
            watch.Observe(Line2MobileNPCTactics.Goal.None,
                new Vector3(), new Vector3(), 10f, false) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.None)
            return false;
        // A genuine lateral route to flank or retreat can increase direct
        // distance for several seconds. Measured displacement, not just
        // progress toward the geometric goal, preserves the maneuver.
        var detour = new Line2NpcTacticalJourneyWatchdog();
        if (Observe(detour, flank, 0f, 0f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(detour, flank, -1.2f, 4f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(detour, flank, -2.4f, 8f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(detour, flank, -3.6f, 12f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving ||
            Observe(detour, flank, -3.6f, 17.2f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Blocked)
        {
            Console.Error.WriteLine("NPC_TACTICAL_DETOUR_FAILED: actual progress or stalled flank.");
            return false;
        }

        // A moving actor must not renew the absolute 45-second deadline.
        var finiteDetour = new Line2NpcTacticalJourneyWatchdog();
        if (Observe(finiteDetour, flank, 0f, 0f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Moving)
            return false;
        for (int i = 1; i <= 11; ++i)
        {
            if (Observe(finiteDetour, flank, -1.1f * i,
                    4f * i) != Line2NpcTacticalJourneyWatchdog.Outcome.Moving)
            {
                Console.Error.WriteLine("NPC_TACTICAL_DETOUR_FAILED: moving A* route aborted.");
                return false;
            }
        }
        if (Observe(finiteDetour, flank, -13f, 45.1f) !=
                Line2NpcTacticalJourneyWatchdog.Outcome.Blocked)
        {
            Console.Error.WriteLine("NPC_TACTICAL_DETOUR_FAILED: maximum journey limit.");
            return false;
        }

        Console.WriteLine("NPC_TACTICAL_JOURNEY_OK: verified movement, pending path timeout, no progress, floor mismatch, invalid coordinates and goal changes.");
        return true;
    }
}
