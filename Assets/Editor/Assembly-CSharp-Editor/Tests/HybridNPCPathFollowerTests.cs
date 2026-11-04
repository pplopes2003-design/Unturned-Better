using System.Collections.Generic;
using NUnit.Framework;
using SDG.Unturned;
using UnityEngine;

internal sealed class HybridNPCPathFollowerTests
{
    [Test]
    public void SidewaysOscillationDoesNotCountAsProgress()
    {
        var follower = new HybridNPCPathFollower();
        follower.SetPath(new List<Vector3> { new Vector3(5f, 0f, 0f) });
        follower.Tick(Vector3.zero, 0.1f);
        for (int i = 0; i < 7; ++i)
            follower.Tick(new Vector3(0f, 0f, i % 2 == 0 ? 1f : -1f), 0.5f);
        Assert.AreEqual(HybridNPCPathFollower.RouteState.Stalled, follower.State);
    }

    [Test]
    public void StandingUnderUpstairsWaypointIsNotArrival()
    {
        var follower = new HybridNPCPathFollower();
        follower.SetPath(new List<Vector3> { new Vector3(0f, 3f, 0f) });
        Assert.AreEqual(HybridNPCPathFollower.RouteState.Following,
            follower.Tick(Vector3.zero, 0.1f));
    }

    [Test]
    public void RealApproachResetsStallAccumulation()
    {
        var follower = new HybridNPCPathFollower();
        follower.SetPath(new List<Vector3> { new Vector3(5f, 0f, 0f) });
        follower.Tick(Vector3.zero, 0.1f);
        follower.Tick(Vector3.zero, 0.5f);
        follower.Tick(Vector3.zero, 0.5f);
        follower.Tick(new Vector3(1f, 0f, 0f), 0.5f);
        Assert.AreEqual(0f, follower.SecondsWithoutProgress, 0.001f);
        Assert.AreEqual(HybridNPCPathFollower.RouteState.Following, follower.State);
    }

    [Test]
    public void WaypointMustBeReachedOnCorrectLevel()
    {
        var follower = new HybridNPCPathFollower();
        follower.SetPath(new List<Vector3> {
            new Vector3(1f, 3f, 0f),
            new Vector3(3f, 3f, 0f)
        });
        follower.Tick(new Vector3(1f, 0f, 0f), 0.1f);
        Assert.AreEqual(0, follower.WaypointIndex);
        follower.Tick(new Vector3(1f, 3f, 0f), 0.1f);
        Assert.AreEqual(1, follower.WaypointIndex);
    }
}
