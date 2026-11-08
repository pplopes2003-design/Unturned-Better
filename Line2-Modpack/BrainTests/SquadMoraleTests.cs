using System;
using Unturned2.Line2;
using UnityEngine;

internal static class SquadMoraleTests
{
    internal static bool Run()
    {
        var own = new Vector3(0f, 0f, 0f);
        var casualty = new Vector3(8f, 0f, 8f);
        if (!Line2NpcSquadMoralePolicy.MayWithdraw(
                true, true, true, true, false, own, casualty) ||
            Line2NpcSquadMoralePolicy.MayWithdraw(
                false, true, true, true, false, own, casualty) ||
            Line2NpcSquadMoralePolicy.MayWithdraw(
                true, false, true, true, false, own, casualty) ||
            Line2NpcSquadMoralePolicy.MayWithdraw(
                true, true, false, true, false, own, casualty) ||
            Line2NpcSquadMoralePolicy.MayWithdraw(
                true, true, true, false, false, own, casualty) ||
            Line2NpcSquadMoralePolicy.MayWithdraw(
                true, true, true, true, true, own, casualty) ||
            Line2NpcSquadMoralePolicy.MayWithdraw(
                true, true, true, true, false,
                own, new Vector3(29f, 0f, 0f)) ||
            Line2NpcSquadMoralePolicy.MayWithdraw(
                true, true, true, true, false,
                own, new Vector3(8f, 4f, 8f)) ||
            Line2NpcSquadMoralePolicy.MayWithdraw(
                true, true, true, true, false,
                own, new Vector3(float.NaN, 0f, 8f)))
            return false;
        Console.WriteLine("NPC_SQUAD_MORALE_OK: same group, firsthand casualty, real violence and same-floor withdrawal bounds.");
        return true;
    }
}
