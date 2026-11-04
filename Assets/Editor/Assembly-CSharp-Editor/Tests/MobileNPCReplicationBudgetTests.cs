using NUnit.Framework;
using SDG.Unturned;
using UnityEngine;

internal sealed class MobileNPCReplicationBudgetTests
{
    private static bool Send(Vector3 position, byte yaw, ushort gun,
        byte motion, byte pitch, float elapsed)
    {
        return MobileNPCNetworkReplica.ShouldTransmitSnapshot(true,
            position, yaw, gun, motion, pitch,
            Vector3.zero, 0, 0, 0, 90, elapsed);
    }

    [Test]
    public void FirstSnapshotAlwaysTransmits()
    {
        Assert.IsTrue(MobileNPCNetworkReplica.ShouldTransmitSnapshot(false,
            Vector3.zero, 0, 0, 0, 90,
            Vector3.zero, 0, 0, 0, 90, 0f));
    }

    [Test]
    public void IdleDuplicateIsSuppressedBeforeHeartbeat()
    {
        Assert.IsFalse(Send(Vector3.zero, 0, 0, 0, 90, 0.35f));
        Assert.IsFalse(Send(new Vector3(0.1f, 0f, 0f), 0, 0, 0, 90, 0.35f));
        Assert.IsTrue(Send(Vector3.zero, 0, 0, 0, 90, 1.4f));
    }

    [Test]
    public void MotionAndCombatPresentationChangesTransmitImmediately()
    {
        Assert.IsTrue(Send(Vector3.zero, 0, 0, 1, 90, 0.35f));
        Assert.IsTrue(Send(Vector3.zero, 0, 0, 4, 90, 0.35f));
        Assert.IsTrue(Send(Vector3.zero, 0, 42, 0, 90, 0.35f));
        Assert.IsTrue(Send(Vector3.zero, 0, 0, 0, 91, 0.35f));
        Assert.IsTrue(Send(Vector3.zero, 3, 0, 0, 90, 0.35f));
        Assert.IsTrue(Send(new Vector3(0.3f, 0f, 0f), 0, 0, 0, 90, 0.35f));
    }

    [Test]
    public void YawWraparoundDoesNotCauseFalseLargeRotation()
    {
        Assert.IsFalse(MobileNPCNetworkReplica.ShouldTransmitSnapshot(true,
            Vector3.zero, 0, 0, 0, 90,
            Vector3.zero, 255, 0, 0, 90, 0.35f));
    }
}
