using System;
using Unturned2.Line2;

internal static class CombatDynamicsTests
{
    internal static bool Run()
    {
        // Damage is applied once by the appropriate authoritative health
        // owner; corrupted Workshop values cannot pass any player, zombie
        // or NPC damage channel.
        if (!Line2NpcGunDamagePolicy.IsValid(1f) ||
            !Line2NpcGunDamagePolicy.IsValid(5000f) ||
            Line2NpcGunDamagePolicy.IsValid(0f) ||
            Line2NpcGunDamagePolicy.IsValid(-1f) ||
            Line2NpcGunDamagePolicy.IsValid(5001f) ||
            Line2NpcGunDamagePolicy.IsValid(float.NaN) ||
            Line2NpcGunDamagePolicy.IsValid(float.PositiveInfinity) ||
            !Line2NpcGunDamagePolicy.IsValidFalloff(1f) ||
            Line2NpcGunDamagePolicy.IsValidFalloff(float.NegativeInfinity) ||
            Line2NpcGunDamagePolicy.IsValidFalloff(9f))
        {
            Console.Error.WriteLine("NPC_GUN_DAMAGE_FAILED: invalid pellet or Workshop falloff.");
            return false;
        }

        // Legitimate missed shot at a currently seen rival creates ONLY
        // defensive pressure; hits, non-hostiles, magic remote targets,
        // unstamped fake shots and rapid duplicate events do not.
        if (!Line2NpcSuppressionPolicy.CanReport(
                true, true, true, false, 17f, 10f, -100f) ||
            Line2NpcSuppressionPolicy.CanReport(
                true, true, true, true, 17f, 10f, -100f) ||
            Line2NpcSuppressionPolicy.CanReport(
                false, true, true, false, 17f, 10f, -100f) ||
            Line2NpcSuppressionPolicy.CanReport(
                true, false, true, false, 17f, 10f, -100f) ||
            Line2NpcSuppressionPolicy.CanReport(
                true, true, false, false, 17f, 10f, -100f) ||
            Line2NpcSuppressionPolicy.CanReport(
                true, true, true, false, 36f, 10f, -100f) ||
            Line2NpcSuppressionPolicy.CanReport(
                true, true, true, false, 12f, 10.5f, 10f) ||
            !Line2NpcSuppressionPolicy.CanReport(
                true, true, true, false, 12f, 11.1f, 10f) ||
            !Line2NpcSuppressionPolicy.IsCurrent(12f, 10f) ||
            Line2NpcSuppressionPolicy.IsCurrent(12.6f, 10f) ||
            Line2NpcSuppressionPolicy.IsCurrent(9f, 10f) ||
            Line2NpcSuppressionPolicy.IsCurrent(
                float.NaN, 10f))
        {
            Console.Error.WriteLine("NPC_SUPPRESSION_FAILED: fake threat, future event or duplicate burst.");
            return false;
        }

        float steady = Line2NpcAimSpreadPolicy.ComputeDegrees(
            20f, false, 5, false, false, 1f);
        float moving = Line2NpcAimSpreadPolicy.ComputeDegrees(
            20f, true, 5, false, false, 1f);
        float bursting = Line2NpcAimSpreadPolicy.ComputeDegrees(
            20f, false, 1, false, false, 1f);
        float wounded = Line2NpcAimSpreadPolicy.ComputeDegrees(
            20f, false, 5, true, false, 0.45f);
        float suppressed = Line2NpcAimSpreadPolicy.ComputeDegrees(
            20f, false, 5, false, true, 1f);
        if (Math.Abs(steady - 3.4f) > 0.001f ||
            moving <= steady || bursting <= steady ||
            wounded <= steady || suppressed <= steady ||
            Line2NpcAimSpreadPolicy.ComputeDegrees(
                float.NaN, false, 5, false, false, 1f) != 12f ||
            Line2NpcAimSpreadPolicy.ComputeDegrees(
                10000f, true, 1, true, true, 0f) != 12f)
        {
            Console.Error.WriteLine("NPC_AIM_SPREAD_FAILED: moving, recoil, injury, suppression or invalid range.");
            return false;
        }

        // The real selected native fire mode determines the pattern.
        // A semi-only firearm gets one round per trigger; a three-round
        // burst never silently becomes five-round full auto. Safety
        // and unsupported gun states never authorize ammunition debit.
        if (Line2NpcFireModePolicy.ShotsInCycle(
                true, false, false, 3, 5) != 1 ||
            Line2NpcFireModePolicy.ShotsInCycle(
                false, true, false, 3, 5) != 3 ||
            Line2NpcFireModePolicy.ShotsInCycle(
                false, false, true, 3, 5) != 5 ||
            Line2NpcFireModePolicy.ShotsInCycle(
                false, false, true, 3, 100) != 6 ||
            Line2NpcFireModePolicy.ShotsInCycle(
                false, true, false, 0, 5) != 1 ||
            Line2NpcFireModePolicy.ShotsInCycle(
                false, false, false, 3, 5) != 0)
        {
            Console.Error.WriteLine("NPC_FIREMODE_FAILED: semi, burst, automatic, safety or unsupported gun.");
            return false;
        }

        Console.WriteLine("NPC_COMBAT_DYNAMICS_OK: finite gun damage, real missed-shot pressure, humanized recoil and injury.");
        return true;
    }
}
