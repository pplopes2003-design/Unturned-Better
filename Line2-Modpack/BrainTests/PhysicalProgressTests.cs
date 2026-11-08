using System;
using UnityEngine;
using Unturned2.Line2;

internal static class PhysicalProgressTests
{
    internal static bool Run()
    {
        var memory = new Line2NpcPhysicalProgressLedger();
        memory.Reset(new Vector3(0f, 0f, 0f));
        if (memory.TryObserveNewPosition(new Vector3(0.25f, 0f, 0f), 0.8f) ||
            !memory.TryObserveNewPosition(new Vector3(-1.2f, 0f, 0f), 0.8f) ||
            memory.TryObserveNewPosition(new Vector3(0f, 0f, 0f), 0.8f) ||
            memory.TryObserveNewPosition(new Vector3(-1.2f, 0f, 0f), 0.8f) ||
            !memory.TryObserveNewPosition(new Vector3(-2.3f, 0f, 0f), 0.8f) ||
            memory.TryObserveNewPosition(
                new Vector3(float.NaN, 0f, 0f), 0.8f) ||
            memory.TryObserveNewPosition(
                new Vector3(-3f, 0f, 0f), float.NaN))
        {
            Console.Error.WriteLine("NPC_PHYSICAL_PROGRESS_FAILED: fake oscillation or invalid position.");
            return false;
        }
        memory.Reset(new Vector3(15f, 0f, 0f));
        if (!memory.TryObserveNewPosition(
                new Vector3(0f, 0f, 0f), 0.8f))
        {
            Console.Error.WriteLine("NPC_PHYSICAL_PROGRESS_FAILED: new journey kept prior visits.");
            return false;
        }
        Console.WriteLine("NPC_PHYSICAL_PROGRESS_OK: genuinely new positions, backtrack rejection, fresh routes.");
        return true;
    }
}
