using System;
using Unturned2.Line2;

internal static class SquadSupplyPolicyTests
{
    private static bool May(bool authorized = true, bool group = true,
        bool alive = true, bool sight = true, bool danger = false,
        bool need = true, bool donor = true, bool space = true,
        float meters = 1.7f)
    {
        return Line2NpcSquadSupplyPolicy.MayShare(authorized, group,
            alive, sight, danger, need, donor, space, meters);
    }

    internal static bool Run()
    {
        if (!May() || May(authorized:false) || May(group:false) ||
            May(alive:false) || May(sight:false) || May(danger:true) ||
            May(need:false) || May(donor:false) || May(space:false) ||
            May(meters:0.1f) || May(meters:2.76f) ||
            May(meters:float.NaN) ||
            May(meters:float.PositiveInfinity))
            return false;
        Console.WriteLine("NPC_SQUAD_SUPPLY_POLICY_OK: finite close-range native inventory sharing, group and LOS limits, no combat and no item fabrication.");
        return true;
    }
}
