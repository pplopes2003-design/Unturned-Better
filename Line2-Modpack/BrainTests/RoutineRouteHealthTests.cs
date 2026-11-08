using System;
using Unturned2.Line2;
using UnityEngine;

internal static class RoutineRouteHealthTests
{
    internal static bool Run()
    {
        var watch = new Line2NpcRoutineRouteHealth();
        Vector3 goal = new Vector3(25f, 0f, 0f);
        if (watch.Observe(goal, new Vector3(0f, 0f, 0f), 0f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            watch.Observe(goal, new Vector3(1f, 0f, 0f), 3f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            watch.Observe(goal, new Vector3(1f, 0f, 0f), 25f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            watch.Observe(goal, new Vector3(1f, 0f, 0f), 28f,
                false, false) != Line2NpcRoutineRouteHealth.State.Blocked)
            return false;

        // Native A* is allowed a bounded long detour with little immediate
        // forward progress (e.g. route around a large building).
        var pending = new Line2NpcRoutineRouteHealth();
        if (pending.Observe(goal, new Vector3(0f, 0f, 0f), 0f,
                false, true) != Line2NpcRoutineRouteHealth.State.Moving ||
            pending.Observe(goal, new Vector3(-4f, 0f, 0f), 30f,
                false, true) != Line2NpcRoutineRouteHealth.State.Moving ||
            pending.Observe(goal, new Vector3(-4f, 0f, 0f), 34.2f,
                false, true) != Line2NpcRoutineRouteHealth.State.Moving ||
            pending.Observe(goal, new Vector3(-4f, 0f, 0f), 64.2f,
                false, true) != Line2NpcRoutineRouteHealth.State.Blocked)
            return false;

        // Once the original physical planner reports no connected route,
        // no blind forward steering can rescue that destination.
        var blocked = new Line2NpcRoutineRouteHealth();
        if (blocked.Observe(goal, new Vector3(0f, 0f, 0f), 0f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            blocked.Observe(goal, new Vector3(0f, 0f, 0f), 2f,
                true, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            blocked.Observe(goal, new Vector3(0f, 0f, 0f), 6.1f,
                true, false) != Line2NpcRoutineRouteHealth.State.Blocked)
            return false;

        // Small movement on the wrong floor is never successful arrival.
        if (blocked.Observe(new Vector3(10f, 7f, 0f),
                new Vector3(10f, 0f, 0f), 7f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            blocked.Observe(new Vector3(10f, 7f, 0f),
                new Vector3(10f, 0f, 0f), 33f,
                false, false) != Line2NpcRoutineRouteHealth.State.Blocked)
            return false;

        // A new legitimate patrol goal starts a new physical journey.
        if (blocked.Observe(new Vector3(15f, 0f, 0f),
                new Vector3(0f, 0f, 0f), 34f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            blocked.Observe(new Vector3(15f, 0f, 0f),
                new Vector3(14.5f, 0f, 0f), 35f,
                false, false) != Line2NpcRoutineRouteHealth.State.Reached ||
            blocked.Observe(new Vector3(float.NaN, 0f, 0f),
                new Vector3(0f, 0f, 0f), 36f,
                false, false) != Line2NpcRoutineRouteHealth.State.Blocked)
            return false;

        // A real A* corridor can move AWAY from the goal while routing
        // around a house. Valid measured steps should not be misdiagnosed
        // as a stalled NPC merely because straight-line distance grows.
        var detour = new Line2NpcRoutineRouteHealth();
        if (detour.Observe(goal, new Vector3(0f, 0f, 0f), 0f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            detour.Observe(goal, new Vector3(-1.5f, 0f, 0f), 12f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            detour.Observe(goal, new Vector3(-3f, 0f, 0f), 25f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            detour.Observe(goal, new Vector3(-4.5f, 0f, 0f), 39f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving ||
            detour.Observe(goal, new Vector3(-4.5f, 0f, 0f), 64f,
                false, false) != Line2NpcRoutineRouteHealth.State.Blocked)
        {
            Console.Error.WriteLine("NPC_ROUTINE_DETOUR_FAILED: legitimate physical travel or actual stall.");
            return false;
        }

        // Circling forever is not a valid journey. The absolute time bound
        // still applies even if the NPC is continuously moving.
        var endlessDetour = new Line2NpcRoutineRouteHealth();
        if (endlessDetour.Observe(goal, new Vector3(0f, 0f, 0f), 0f,
                false, false) != Line2NpcRoutineRouteHealth.State.Moving)
            return false;
        for (int tick = 1; tick < 14; ++tick)
        {
            if (endlessDetour.Observe(goal,
                    new Vector3(-2f * tick, 0f, 0f), 10f * tick,
                    false, false) != Line2NpcRoutineRouteHealth.State.Moving)
            {
                Console.Error.WriteLine("NPC_ROUTINE_DETOUR_FAILED: early timeout during valid travel.");
                return false;
            }
        }
        if (endlessDetour.Observe(goal, new Vector3(-28f, 0f, 0f),
                140f, false, false) !=
                    Line2NpcRoutineRouteHealth.State.Blocked)
        {
            Console.Error.WriteLine("NPC_ROUTINE_DETOUR_FAILED: absolute 140s bound.");
            return false;
        }

        Console.WriteLine("NPC_ROUTINE_ROUTE_OK: native A* grace, disconnected wall, long stagnation, floor mismatch, new destination and invalid coordinates.");
        return true;
    }
}
