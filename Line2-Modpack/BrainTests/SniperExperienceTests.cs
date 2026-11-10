using System;
using Unturned2.Line2;

internal static class SniperExperienceTests
{
    internal static bool Run()
    {
        var novice = new Line2NpcSniperExperience();
        if (novice.SpreadMultiplier(297, 85f) != 1f)
            return Fail("novice got free precision");
        for (int i = 0; i < 100; ++i)
            novice.Observe(297, 85f, true, i % 5 == 0);
        float experienced = novice.SpreadMultiplier(297, 85f);
        if (experienced >= 0.96f || experienced < 0.85f)
            return Fail("lifetime experience too strong or absent");
        if (novice.SpreadMultiplier(129, 85f) != 1f ||
            novice.SpreadMultiplier(297, 25f) != 1f)
            return Fail("practice leaked across weapon or distance");
        float side, elevation;
        if (novice.TryGetBallisticBias(297, 85f, out side, out elevation))
        {
            // Newly learned shots without physically verified impact
            // cannot yet create a non-zero drift.
            if (side != 0f || elevation != 0f)
                return Fail("invented ballistic experience");
        }
        novice.ObserveImpact(297, 85f, true, true, 1.0f, -0.5f, 1.2f);
        if (!novice.TryGetBallisticBias(297, 85f, out side,
                out elevation) || side >= 0f || elevation <= 0f)
            return Fail("physical impact did not calibrate lifetime bias");
        var save = novice.Export();
        var restored = new Line2NpcSniperExperience();
        if (!restored.Restore(save) ||
            Math.Abs(restored.SpreadMultiplier(297, 85f) -
                experienced) > 0.0001f ||
            !restored.TryGetBallisticBias(297, 85f, out side,
                out elevation) || side >= 0f || elevation <= 0f)
            return Fail("individual experience not restorable");
        if (restored.Restore(new [] {
                new Line2NpcSniperExperience.Record {
                    weaponId = 297, distanceBand = 2, shots = 2, strikes = 3
                } }))
            return Fail("invalid memory accepted");
        var replacement = new Line2NpcSniperExperience();
        if (replacement.SpreadMultiplier(297, 85f) != 1f)
            return Fail("experience carried over to a new NPC");
        Console.WriteLine("NPC_SNIPER_LIFETIME_OK: practice retained, bounded, per identity");
        return true;
    }

    private static bool Fail(string why)
    {
        Console.Error.WriteLine("NPC_SNIPER_LIFETIME_FAILED: " + why);
        return false;
    }
}
