using System;
using Unturned2.Line2;

internal static class FireCadenceTests
{
    internal static bool Run()
    {
        // During first-shot reaction, and subsequently between each burst
        // round, the same visually confirmed target must remain acquired.
        object target = new object();
        object acquired = null;
        float acquiredAt = 0f;
        float readyAt = 0f;
        int committedShots = 0;
        for (int tick = 0; tick < 30; ++tick)
        {
            float now = tick * 0.1f;
            bool visible = target != null;
            if (!Line2NpcFireCadencePolicy.RetainTarget(
                    visible, retreating: false))
            {
                acquired = null;
                continue;
            }
            if (!Object.ReferenceEquals(target, acquired))
            {
                acquired = target;
                acquiredAt = now;
                readyAt = now + 0.5f;
                continue;
            }
            if (!Line2NpcFireCadencePolicy.MayFire(now,
                    acquiredAt, readyAt))
                continue;
            committedShots++;
            readyAt = now + 0.5f;
        }
        if (committedShots < 3 ||
            !Line2NpcFireCadencePolicy.RetainTarget(true, false) ||
            Line2NpcFireCadencePolicy.RetainTarget(false, false) ||
            Line2NpcFireCadencePolicy.RetainTarget(true, true) ||
            Line2NpcFireCadencePolicy.MayFire(0.1f, 0f, 0.5f) ||
            Line2NpcFireCadencePolicy.MayFire(float.NaN, 0f, 0f) ||
            Line2NpcFireCadencePolicy.MayFire(1f, float.PositiveInfinity, 0f))
        {
            Console.Error.WriteLine("NPC_FIRE_CADENCE_FAILED: reaction/cooldown resets target.");
            return false;
        }

        // The target must be forgotten after leaving direct perception
        // or while making a committed tactical withdrawal.
        if (!Line2NpcFireCadencePolicy.MayFire(2f, 1f, 1.5f))
        {
            Console.Error.WriteLine("NPC_FIRE_CADENCE_FAILED: ready shot rejected.");
            return false;
        }
        Console.WriteLine("NPC_FIRE_CADENCE_OK: retained target through successive shots.");
        return true;
    }
}
