using System;
using UnityEngine;
using Unturned2.Line2;

internal static class NativeArrivalTests
{
    internal static bool Run()
    {
        Vector3 actual = new Vector3(10f, 1f, 5f);
        if (!Line2NpcNativeArrivalPolicy.HasPhysicallyArrived(
                actual, new Vector3(10.7f, 1.1f, 5.3f)) ||
            !Line2NpcNativeArrivalPolicy.HasPhysicallyArrived(
                actual, new Vector3(11.2f, 1f, 5f)) ||
            Line2NpcNativeArrivalPolicy.HasPhysicallyArrived(
                actual, new Vector3(11.5f, 1f, 5f)) ||
            Line2NpcNativeArrivalPolicy.HasPhysicallyArrived(
                actual, new Vector3(10f, 3.6f, 5f)) ||
            Line2NpcNativeArrivalPolicy.HasPhysicallyArrived(
                actual, new Vector3(float.NaN, 1f, 5f)) ||
            Line2NpcNativeArrivalPolicy.HasPhysicallyArrived(
                new Vector3(float.PositiveInfinity, 1f, 5f),
                new Vector3(10f, 1f, 5f)))
        {
            Console.Error.WriteLine("NPC_NATIVE_ARRIVAL_FAILED: graph endpoint mistaken for physical goal.");
            return false;
        }
        Console.WriteLine("NPC_NATIVE_ARRIVAL_OK: correct floor, goal proximity and invalid endpoints.");
        return true;
    }
}
