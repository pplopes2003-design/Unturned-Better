using System;
using Unturned2.Line2;

internal static class FiringPositionTests
{
    internal static bool Run()
    {
        // Native range decides whether an armed NPC holds a real firing lane.
        // Any loss of vision, ammo, active retreat or actual damage prevents
        // a static posture and keeps the original locomotion plan in charge.
        if (!Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    true, true, false, false, 14f, 40f) ||
            !Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    true, true, false, false, 7f, 22f) ||
            Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    false, true, false, false, 14f, 40f) ||
            Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    true, false, false, false, 14f, 40f) ||
            Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    true, true, true, false, 14f, 40f) ||
            Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    true, true, false, true, 14f, 40f) ||
            Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    true, true, false, false, 2f, 40f) ||
            Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    true, true, false, false, 38f, 40f) ||
            Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    true, true, false, false, float.NaN, 40f) ||
            Line2NpcFiringPositionPolicy.ShouldHoldPosition(
                    true, true, false, false, 10f,
                    float.PositiveInfinity))
        {
            Console.Error.WriteLine("NPC_FIRING_POSITION_FAILED: unsafe hold distance.");
            return false;
        }
        if (!Line2NpcFiringPositionPolicy.CanSeekReloadCover(
                    true, true, false, 19f) ||
            Line2NpcFiringPositionPolicy.CanSeekReloadCover(
                    false, true, false, 19f) ||
            Line2NpcFiringPositionPolicy.CanSeekReloadCover(
                    true, false, false, 19f) ||
            Line2NpcFiringPositionPolicy.CanSeekReloadCover(
                    true, true, true, 19f) ||
            Line2NpcFiringPositionPolicy.CanSeekReloadCover(
                    true, true, false, float.NaN))
        {
            Console.Error.WriteLine("NPC_RELOAD_COVER_FAILED: stale or invalid target.");
            return false;
        }
        Console.WriteLine("NPC_FIRING_POSITION_OK: range, recent sight, movement owner and reload.");
        return true;
    }
}
