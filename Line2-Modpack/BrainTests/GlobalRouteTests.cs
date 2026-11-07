using System;
using System.Collections.Generic;
using Unturned2.Line2;
using UnityEngine;

internal static class GlobalRouteTests
{
    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // A wall occupies the x=6 meter strip. The gap at positive z allows
    // the original Line1 incremental A* to prove a real detour.
    private static bool Edge(Vector3 a, Vector3 b)
    {
        float midX = (a.x + b.x) / 2f;
        float midZ = (a.z + b.z) / 2f;
        return !(midX > 4.0f && midX < 8.0f && Math.Abs(midZ) < 8.0f);
    }

    private static bool Floor(Vector3 estimate, out Vector3 actual)
    {
        actual = new Vector3(estimate.x, 0f, estimate.z);
        return Math.Abs(estimate.x) <= 30.0f && Math.Abs(estimate.z) <= 30.0f;
    }

    internal static bool Run()
    {
        Vector3 start = new Vector3(0f, 0f, 0f);
        Vector3 goal = new Vector3(24f, 0f, 0f);
        var planner = new Line2MobileNPCGlobalRoutePlanner();
        var route = new List<Vector3>();
        bool success = false;
        for (int frame = 0; frame < 5500; ++frame)
        {
            if (!planner.Step(start, goal, frame * 0.016f, frame, 0,
                Floor, Edge, route)) continue;
            success = true;
            break;
        }
        if (!success || route.Count < 4 ||
            FlatDistance(start, route[route.Count - 1]) < 6.0f)
            return false;
        Vector3 previous = start;
        bool detoured = false;
        foreach (Vector3 node in route)
        {
            if (FlatDistance(previous, node) > 3.1f || !Edge(previous, node))
                return false;
            // Either side of the barrier is a valid detour.
            if (Math.Abs(node.z) >= 8.5f) detoured = true;
            previous = node;
        }
        if (!detoured) return false;

        // A disconnected region cannot synthesize a path or teleport.
        var blocked = new Line2MobileNPCGlobalRoutePlanner();
        var impossible = new List<Vector3>();
        bool fabricated = false;
        for (int frame = 0; frame < 1100; ++frame)
        {
            if (blocked.Step(start, goal, frame * 0.016f, frame, 0,
                Floor, (a, b) => false, impossible))
            {
                fabricated = true;
                break;
            }
        }
        if (fabricated || impossible.Count != 0) return false;
        Console.WriteLine("NPC_COARSE_ASTAR_OK: original Line1 detour, physical edge safety and disconnected region fail-closed.");
        return true;
    }
}
