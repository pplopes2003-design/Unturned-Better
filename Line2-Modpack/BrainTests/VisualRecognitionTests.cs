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
        for (int i = 0; i < 20; i++)
            if (sight.Sample(i * 0.15f, true, 0.45f, 5f, 4f, 1f) !=
                    Line2NpcVisualRecognitionPolicy.Recognition.None)
                return Fail("identified outside visual arc");
        sight.Reset();
        for (int i = 0; i < 20; i++)
            if (sight.Sample(i * 0.15f, true, 0.99f, 24f, 4f, 1f) !=
                    Line2NpcVisualRecognitionPolicy.Recognition.None)
                return Fail("identified outside visual range");
        sight.Reset();
        if (sight.Sample(0f, true, float.NaN, 1f, 2f, 1f) !=
            Line2NpcVisualRecognitionPolicy.Recognition.None)
            return Fail("accepted invalid sample");
        Console.WriteLine("NPC_VISUAL_RECOGNITION_OK: gradual detection, LOS, arc, range, invalid sample");
        return true;
    }

    private static bool Fail(string reason)
    {
        Console.Error.WriteLine("NPC_VISUAL_RECOGNITION_FAILED: " + reason);
        return false;
    }
}
