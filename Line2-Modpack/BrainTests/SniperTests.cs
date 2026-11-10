using System;
using Unturned2.Line2;

internal static class SniperTests
{
    internal static bool Run()
    {
        if (Line2NpcSniperPolicy.IsPrecisionWeapon(true, false, 65f) ||
            Line2NpcSniperPolicy.IsPrecisionWeapon(true, true, 140f) ||
            !Line2NpcSniperPolicy.IsPrecisionWeapon(true, false, 120f))
            return Fail("weapon qualification");
        if (Math.Abs(Line2NpcSniperPolicy.SightRange(true, 200f) - 120f) >
                0.001f ||
            Math.Abs(Line2NpcSniperPolicy.SightRange(false, 200f) - 28f) >
                0.001f)
            return Fail("sight distance cap");
        if (Line2NpcSniperPolicy.MayTakePrecisionShot(
                true, true, true, false, false, 3f, 60f, 100f) ||
            Line2NpcSniperPolicy.MayTakePrecisionShot(
                true, false, false, false, false, 3f, 60f, 100f) ||
            Line2NpcSniperPolicy.MayTakePrecisionShot(
                true, true, false, false, true, 3f, 60f, 100f) ||
            Line2NpcSniperPolicy.MayTakePrecisionShot(
                true, true, false, false, false, 0.5f, 60f, 100f) ||
            Line2NpcSniperPolicy.MayTakePrecisionShot(
                true, true, false, false, false, 3f, 125f, 140f) ||
            !Line2NpcSniperPolicy.MayTakePrecisionShot(
                true, true, false, false, false, 2f, 60f, 100f))
            return Fail("real LOS, settle, motion, suppression or max range");
        float stable = Line2NpcSniperPolicy.PrecisionSpread(80f, false, 6f);
        if (stable <= 0f || stable >= 3f ||
            Line2NpcSniperPolicy.PrecisionSpread(80f, true, 6f) <= stable)
            return Fail("inaccurate or perfectly accurate sniper");
        if (Line2NPCCombatRules.CanEngagePlayer(
                "hostile", "total", false, 6400f, 120f) ||
            !Line2NPCCombatRules.CanEngagePlayer(
                "hostile", "total", true, 6400f, 120f) ||
            Line2NPCCombatRules.CanEngagePlayer(
                "hostile", "total", true, 6400f))
            return Fail("sniper player engagement must require acquired LOS");
        Console.WriteLine("NPC_SNIPER_POLICY_OK: native rifle, LOS, settling, ammo range, realistic spread");
        return true;
    }

    private static bool Fail(string reason)
    {
        Console.Error.WriteLine("NPC_SNIPER_POLICY_FAILED: " + reason);
        return false;
    }
}
