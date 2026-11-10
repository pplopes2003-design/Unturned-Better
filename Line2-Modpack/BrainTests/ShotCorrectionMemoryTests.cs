using System;
using Unturned2.Line2;

internal static class ShotCorrectionMemoryTests
{
    internal static bool Run()
    {
        var memory = new Line2NpcShotCorrectionMemory();
        if (memory.Observe(1f, 10, 20, false, true, true, false,
                1f, -1f, 2f) || memory.CanApply(1f, 10, 20))
            return Fail("unseen impact taught target position");
        if (memory.Observe(1f, 10, 20, true, true, false, false,
                1f, -1f, 2f))
            return Fail("unverified collision taught");
        if (!memory.Observe(2f, 10, 20, true, true, true, false,
                2f, -1f, 2.5f) ||
            !memory.CanApply(3f, 10, 20) ||
            !(memory.Lateral < 0f && memory.Vertical > 0f))
            return Fail("miss correction sign and retention");
        if (memory.CanApply(3f, 11, 20) ||
            memory.CanApply(3f, 10, 21) ||
            memory.CanApply(38f, 10, 20))
            return Fail("memory leaked across gun, target or time");
        float old = memory.Lateral;
        if (memory.Observe(4f, 10, 20, true, true, true, false,
                20f, 10f, 21f) || memory.Lateral != old)
            return Fail("remote wall treated as observed miss");
        if (!memory.Observe(5f, 10, 20, true, true, true, true,
                0f, 0f, 0f) || Math.Abs(memory.Lateral) >= Math.Abs(old))
            return Fail("real hit did not damp overcorrection");
        memory.Reset();
        if (memory.CanApply(5f, 10, 20))
            return Fail("reset retained cross-engagement learning");
        Console.WriteLine("NPC_SHOT_MEMORY_OK: physical impact only, bounded correction, decay");
        return true;
    }

    private static bool Fail(string why)
    {
        Console.Error.WriteLine("NPC_SHOT_MEMORY_FAILED: " + why);
        return false;
    }
}
