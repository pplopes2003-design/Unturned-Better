using System;
using Unturned2.Line2;
using UnityEngine;

internal static class RadioReinforcementTests
{
    internal static bool Run()
    {
        var here = new Vector3(0f, 0f, 0f);
        var sector = new Vector3(80f, 0f, 0f);
        Vector3 proposed;
        if (!Line2NpcRadioReinforcementPolicy.TryPropose(
            true, true, false, false, false, false,
            here, sector, out proposed))
            return false;
        if (proposed.x < 11.9f || proposed.x > 12.1f ||
            proposed.y != 0f || proposed.z != 0f)
            return false; // Coarse report never becomes an exact victim waypoint.
        if (Line2NpcRadioReinforcementPolicy.TryPropose(
                false, true, false, false, false, false,
                here, sector, out proposed) ||
            Line2NpcRadioReinforcementPolicy.TryPropose(
                true, false, false, false, false, false,
                here, sector, out proposed) ||
            Line2NpcRadioReinforcementPolicy.TryPropose(
                true, true, true, false, false, false,
                here, sector, out proposed) ||
            Line2NpcRadioReinforcementPolicy.TryPropose(
                true, true, false, true, false, false,
                here, sector, out proposed) ||
            Line2NpcRadioReinforcementPolicy.TryPropose(
                true, true, false, false, true, false,
                here, sector, out proposed) ||
            Line2NpcRadioReinforcementPolicy.TryPropose(
                true, true, false, false, false, true,
                here, sector, out proposed) ||
            Line2NpcRadioReinforcementPolicy.TryPropose(
                true, true, false, false, false, false,
                here, new Vector3(95f, 0f, 0f), out proposed) ||
            Line2NpcRadioReinforcementPolicy.TryPropose(
                true, true, false, false, false, false,
                here, new Vector3(7f, 0f, 0f), out proposed) ||
            Line2NpcRadioReinforcementPolicy.TryPropose(
                true, true, false, false, false, false,
                here, new Vector3(25f, 5f, 0f), out proposed) ||
            Line2NpcRadioReinforcementPolicy.TryPropose(
                true, true, false, false, false, false,
                here, new Vector3(float.NaN, 0f, 0f), out proposed))
            return false;
        Console.WriteLine("NPC_RADIO_REINFORCEMENT_OK: bounded coarse waypoint, verified group, no combat override and same floor.");
        return true;
    }
}
