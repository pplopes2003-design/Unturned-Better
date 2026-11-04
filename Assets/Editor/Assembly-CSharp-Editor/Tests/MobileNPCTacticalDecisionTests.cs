using NUnit.Framework;
using SDG.Unturned;
using UnityEngine;

/// <summary>
/// Pure selector regression tests. These do not substitute for Washington
/// physics, live NPC perception, weapon damage or multiplayer integration.
/// </summary>
internal sealed class MobileNPCTacticalDecisionTests
{
    private static readonly Vector3 CoverA = new Vector3(3f, 0f, 2f);
    private static readonly Vector3 CoverB = new Vector3(-4f, 0f, 5f);
    private static readonly Vector3 Threat = new Vector3(18f, 0f, 0f);

    [Test]
    public void DamagedCriticalSurvivorRequestsRetreatBeforeCover()
    {
        var tactics = new MobileNPCTactics();
        tactics.ReportDamage(1f, 100f, 100f);
        Assert.IsTrue(tactics.TryChooseDefensiveGoal(Vector3.zero,
            CoverA, Threat, 100f, true));
        Assert.AreEqual(MobileNPCTactics.Goal.Retreat, tactics.CurrentGoal);
        Assert.IsFalse(tactics.HasTacticalDestination,
            "Unsafe cover must not be promoted to a verified retreat point.");
        Assert.IsTrue(tactics.ReportValidatedRetreatPoint(CoverB, 100f));
        Assert.AreEqual(CoverB, tactics.TacticalDestination);
    }

    [Test]
    public void RepeatedGunfireCannotReplaceCommittedCoverRoute()
    {
        var tactics = new MobileNPCTactics();
        tactics.ReportHealth(50f, 100f);
        tactics.ReportIncomingFire(100f);
        Assert.IsTrue(tactics.TryChooseDefensiveGoal(Vector3.zero,
            CoverA, Threat, 100f));
        Assert.AreEqual(MobileNPCTactics.Goal.SeekCover, tactics.CurrentGoal);
        tactics.ReportIncomingFire(101f);
        Assert.IsTrue(tactics.TryChooseDefensiveGoal(Vector3.zero,
            CoverB, Threat, 101f));
        Assert.AreEqual(CoverA, tactics.TacticalDestination,
            "Fresh sounds cannot replace a cover path still in progress.");
    }

    [Test]
    public void HealthEmergencyPreemptsAnOrdinaryCoverGoal()
    {
        var tactics = new MobileNPCTactics();
        tactics.ReportHealth(50f, 100f);
        tactics.ReportIncomingFire(100f);
        Assert.IsTrue(tactics.TryChooseDefensiveGoal(Vector3.zero,
            CoverA, Threat, 100f));
        tactics.ReportDamage(1f, 100f, 101f);
        Assert.IsTrue(tactics.TryChooseDefensiveGoal(Vector3.zero,
            CoverA, Threat, 101f));
        Assert.AreEqual(MobileNPCTactics.Goal.Retreat, tactics.CurrentGoal);
        Assert.IsFalse(tactics.HasTacticalDestination);
    }

    [Test]
    public void FlankRemainsCommittedAgainstOptionalCompetingPlans()
    {
        var tactics = new MobileNPCTactics();
        Assert.IsTrue(tactics.TryStartFlank(CoverA, 100f));
        Assert.IsFalse(tactics.TryStartFlank(CoverB, 100.2f));
        Assert.IsFalse(tactics.TryStartLoot(CoverB, 100.3f));
        Assert.IsFalse(tactics.TryStartReloadCover(CoverB, 100.4f));
        Assert.AreEqual(CoverA, tactics.TacticalDestination);
        Assert.AreEqual(MobileNPCTactics.Goal.Flank, tactics.CurrentGoal);
    }

    [Test]
    public void VerifiedCoverPreemptsOptionalFlankUnderFire()
    {
        var tactics = new MobileNPCTactics();
        Assert.IsTrue(tactics.TryStartFlank(CoverA, 100f));
        tactics.ReportHealth(45f, 100f);
        tactics.ReportIncomingFire(101f);
        Assert.IsTrue(tactics.TryChooseDefensiveGoal(Vector3.zero,
            CoverB, Threat, 101f));
        Assert.AreEqual(MobileNPCTactics.Goal.SeekCover, tactics.CurrentGoal);
        Assert.AreEqual(CoverB, tactics.TacticalDestination);
    }

    [Test]
    public void HealthyFighterCanReturnFireWhenCoverIsLessUseful()
    {
        var tactics = new MobileNPCTactics();
        tactics.ReportHealth(100f, 100f);
        tactics.ReportIncomingFire(100f);
        Assert.IsFalse(tactics.TryChooseDefensiveGoal(Vector3.zero,
            CoverA, Threat, 100f, true));
        Assert.AreEqual(MobileNPCTactics.Goal.None, tactics.CurrentGoal);
    }

    [Test]
    public void LowHealthWithoutEscapePointDoesNotCreateFakeRoute()
    {
        var tactics = new MobileNPCTactics();
        tactics.ReportDamage(5f, 100f, 100f);
        Assert.IsTrue(tactics.TryChooseDefensiveGoal(Vector3.zero,
            null, Threat, 100f));
        Assert.AreEqual(MobileNPCTactics.Goal.Retreat, tactics.CurrentGoal);
        Assert.IsFalse(tactics.HasTacticalDestination);
    }

    [Test]
    public void ReachedFlankMayExpireAndBeReleased()
    {
        var tactics = new MobileNPCTactics();
        Assert.IsTrue(tactics.TryStartFlank(CoverA, 100f));
        Assert.IsFalse(tactics.IsGoalExpired(101f));
        Assert.IsTrue(tactics.IsGoalExpired(119f));
        tactics.ReportGoalReached();
        Assert.AreEqual(MobileNPCTactics.Goal.None, tactics.CurrentGoal);
        Assert.IsFalse(tactics.HasTacticalDestination);
    }
}
