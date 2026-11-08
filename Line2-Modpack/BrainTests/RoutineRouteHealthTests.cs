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

        Console.WriteLine("NPC_ROUTINE_ROUTE_OK: native A* grace, disconnected wall, long stagnation, floor mismatch, new destination and invalid coordinates.");
        return true;
    }
}
