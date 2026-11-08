using System;
using Unturned2.Line2;

internal static class CampSupplyPolicyTests
{
    private static bool May(bool authorized = true,
        bool group = true, bool storage = true,
        bool sight = true, bool combat = false,
        bool spare = true, bool needy = false, bool vendor = false,
        float meters = 2f)
    {
        return Line2NpcCampSupplyPolicy.MayDeliver(
            authorized, group, storage, sight, combat, spare,
            needy, vendor, meters);
    }

    internal static bool Run()
    {
        if (!May() ||
            May(authorized:false) ||
            May(group:false) ||
            May(storage:false) ||
            May(combat:true) ||
            May(spare:false) ||
            May(needy:true) ||
            May(vendor:true) ||
            May(meters:3.5f) ||
            May(meters:-0.1f) ||
            May(meters:float.NaN) ||
            May(meters:float.PositiveInfinity))
            return false;
        Console.WriteLine("NPC_CAMP_STOCK_POLICY_OK: real saved native storage, same living group, spare supplies, no combat, no vendor drain, physical ≤3.4m.");
        return true;
    }
}
