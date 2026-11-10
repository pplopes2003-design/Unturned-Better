using System;
using Unturned2.Line2;

internal static class VisualRecognitionTests
{
    internal static bool Run()
    {
        var sight = new Line2NpcVisualRecognitionPolicy();
        if (sight.Sample(0f, true, 0.95f, 8f, 0f, 1f) ==
            Line2NpcVisualRecognitionPolicy.Recognition.Confirmed)
            return Fail("instant identification");
        bool sawSuspicion = false;
        bool confirmed = false;
        for (int i = 1; i < 22; i++)
        {
            var state = sight.Sample(i * 0.15f, true, 0.95f, 8f, 1.5f, 1f);
            sawSuspicion |= state ==
                Line2NpcVisualRecognitionPolicy.Recognition.Suspicious;
            confirmed |= state ==
                Line2NpcVisualRecognitionPolicy.Recognition.Confirmed;
            if (confirmed) break;
        }
        if (!sawSuspicion || !confirmed) return Fail("no graded identification");
        if (sight.Sample(4f, false, 1f, 3f, 5f, 1f) !=
                Line2NpcVisualRecognitionPolicy.Recognition.None)
            return Fail("confirmed through obstacle");
        sight.Reset();
        bool peripheralSuspicion = false;
        for (int i = 0; i < 35; i++)
        {
            var state = sight.Sample(i * 0.15f, true, 0.30f, 5f, 4f, 1f);
            peripheralSuspicion |= state ==
                Line2NpcVisualRecognitionPolicy.Recognition.Suspicious;
            if (state == Line2NpcVisualRecognitionPolicy.Recognition.Confirmed)
                return Fail("peripheral movement confirmed combat target");
        }
        if (!peripheralSuspicion)
            return Fail("peripheral movement never raises suspicion");
        sight.Reset();
        for (int i = 0; i < 20; i++)
            if (sight.Sample(i * 0.15f, true, 0.30f, 5f, 0f, 1f) !=
                    Line2NpcVisualRecognitionPolicy.Recognition.None)
                return Fail("stationary peripheral target detected");
        sight.Reset();
        for (int i = 0; i < 20; i++)
            if (sight.Sample(i * 0.15f, true, -0.25f, 5f, 4f, 1f) !=
                    Line2NpcVisualRecognitionPolicy.Recognition.None)
                return Fail("target behind NPC detected");
        var daylight = new Line2NpcVisualRecognitionPolicy();
        var darkness = new Line2NpcVisualRecognitionPolicy();
        for (int i = 0; i < 7; i++)
        {
            daylight.Sample(i * 0.15f, true, 0.95f, 8f, 1.5f, 1f);
            darkness.Sample(i * 0.15f, true, 0.95f, 8f, 1.5f, 0.45f);
        }
        if (darkness.Evidence >= daylight.Evidence)
            return Fail("night does not delay recognition");
        var standing = new Line2NpcVisualRecognitionPolicy();
        var crouching = new Line2NpcVisualRecognitionPolicy();
        var prone = new Line2NpcVisualRecognitionPolicy();
        for (int i = 0; i < 7; ++i)
        {
            float now = i * 0.15f;
            standing.Sample(now, true, 0.9f, 14f, 0f, 1f, 1f);
            crouching.Sample(now, true, 0.9f, 14f, 0f, 1f, 0.70f);
            prone.Sample(now, true, 0.9f, 14f, 0f, 1f, 0.42f);
        }
        if (!(standing.Evidence > crouching.Evidence &&
              crouching.Evidence > prone.Evidence))
            return Fail("native player posture does not modify silhouette");
        var nearbyStand = new Line2NpcVisualRecognitionPolicy();
        var nearbyProne = new Line2NpcVisualRecognitionPolicy();
        for (int i = 0; i < 8; ++i)
        {
            float now = i * 0.15f;
            nearbyStand.Sample(now, true, 1f, 0f, 0f, 1f, 1f);
            nearbyProne.Sample(now, true, 1f, 0f, 0f, 1f, 0.42f);
        }
        if (Math.Abs(nearbyStand.Evidence - nearbyProne.Evidence) > 0.0001f)
            return Fail("prone posture falsely hides a point-blank target");
        sight.Reset();
        for (int i = 0; i < 20; i++)
            if (sight.Sample(i * 0.15f, true, 0.99f, 24f, 4f, 1f) !=
                    Line2NpcVisualRecognitionPolicy.Recognition.None)
                return Fail("identified outside visual range");
        sight.Reset();
        if (sight.Sample(0f, true, float.NaN, 1f, 2f, 1f) !=
            Line2NpcVisualRecognitionPolicy.Recognition.None)
            return Fail("accepted invalid sample");
        Console.WriteLine("NPC_VISUAL_RECOGNITION_OK: graded detection, peripheral suspicion, night, LOS, range");
        return true;
    }

    private static bool Fail(string reason)
    {
        Console.Error.WriteLine("NPC_VISUAL_RECOGNITION_FAILED: " + reason);
        return false;
    }
}
