using System;
using Unturned2.Line2;

internal static class CampWithdrawalTests
{
    private static bool May(bool authorized = true, bool group = true,
        bool native = true, bool unowned = true, bool sight = true,
        bool needed = true, bool peace = true, bool space = true,
        bool jar = true, float distance = 1.4f)
    {
        return Line2NpcCampWithdrawalPolicy.MayWithdraw(
            authorized, group, native, unowned, sight,
            needed, peace, space, jar, distance);
    }

    internal static bool Run()
    {
        if (!May() ||
            May(authorized:false) ||
            May(group:false) ||
            May(native:false) ||
            May(unowned:false) ||
            May(sight:false) ||
            May(needed:false) ||
            May(peace:false) ||
            May(space:false) ||
            May(jar:false) ||
            May(distance:0.05f) ||
            May(distance:3.4f) ||
            May(distance:float.NaN) ||
            May(distance:float.PositiveInfinity))
            return false;
        Console.WriteLine("NPC_CAMP_WITHDRAW_OK: stored real ItemJar, original group native ID, no player property, LOS, need, capacity and safe physical range.");
        return true;
    }
}
