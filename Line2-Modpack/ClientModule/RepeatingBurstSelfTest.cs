using System;

namespace Unturned2.Line2
{
    /// <summary>
    /// Safe deterministic checks for the *scheduler*, not Unturned gun firing.
    /// Runs once on module initialization and reports to probe.log.
    /// </summary>
    internal static class RepeatingBurstSelfTest
    {
        internal static void Run()
        {
            try
            {
                var scheduler = new RepeatingBurstScheduler(0.1f, 0.25f);
                Assert(scheduler.TrySchedule(0.00f, true, true), "shot-1");
                Assert(!scheduler.TrySchedule(0.05f, true, true), "no-early-shot");
                Assert(scheduler.TrySchedule(0.10f, true, true), "shot-2");
                Assert(scheduler.TrySchedule(0.20f, true, true), "shot-3");
                Assert(!scheduler.TrySchedule(0.30f, true, true), "burst-pause");
                Assert(scheduler.TrySchedule(0.45f, true, true), "next-burst-shot-1");
                Assert(!scheduler.TrySchedule(0.46f, false, true), "released");
                Assert(scheduler.TrySchedule(1.00f, true, true), "fresh-press");
                Assert(!scheduler.TrySchedule(1.01f, true, false), "no-ammo-reset");
                Assert(scheduler.TrySchedule(1.50f, true, true), "restart-after-ammo");
                ProbeLogger.Log("BURST_SCHEDULER_SELFTEST_OK: held trigger repeats 3-shot groups; release and canFire block.");
            }
            catch (Exception ex)
            {
                ProbeLogger.Log("BURST_SCHEDULER_SELFTEST_FAILED: " + ex.Message);
            }
        }

        private static void Assert(bool actual, string label)
        {
            if (!actual)
                throw new InvalidOperationException("Burst scheduler case failed: " + label);
        }
    }
}
