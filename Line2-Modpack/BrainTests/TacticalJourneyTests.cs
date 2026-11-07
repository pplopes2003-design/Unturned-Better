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
                Line2NpcTacticalJourneyWatchdog.Outcome.Reached)
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
        Console.WriteLine("NPC_TACTICAL_JOURNEY_OK: verified movement, pending path timeout, no progress, floor mismatch, invalid coordinates and goal changes.");
        return true;
    }
}
