using NUnit.Framework;
using SDG.Unturned;

internal sealed class MobileNPCGroupRestoreKeyTests
{
    [Test]
    public void SameSavedGroupAndIdentityMapsToSameKey()
    {
        string first = MobileNPCGroupMember.BuildRestoreGroupKey(22,
            MobileNPCHumanoidDisposition.Role.Hostile, MobileNPCFaction.Id.Raiders);
        string second = MobileNPCGroupMember.BuildRestoreGroupKey(22,
            MobileNPCHumanoidDisposition.Role.Hostile, MobileNPCFaction.Id.Raiders);
        Assert.AreEqual(first, second);
    }

    [Test]
    public void IncompatibleFactionsNeverShareRestoreKey()
    {
        string raiders = MobileNPCGroupMember.BuildRestoreGroupKey(22,
            MobileNPCHumanoidDisposition.Role.Hostile, MobileNPCFaction.Id.Raiders);
        string survivors = MobileNPCGroupMember.BuildRestoreGroupKey(22,
            MobileNPCHumanoidDisposition.Role.Hostile, MobileNPCFaction.Id.Survivors);
        Assert.AreNotEqual(raiders, survivors);
    }

    [Test]
    public void IncompatibleRolesNeverShareRestoreKey()
    {
        string raiders = MobileNPCGroupMember.BuildRestoreGroupKey(22,
            MobileNPCHumanoidDisposition.Role.Hostile, MobileNPCFaction.Id.Raiders);
        string neutral = MobileNPCGroupMember.BuildRestoreGroupKey(22,
            MobileNPCHumanoidDisposition.Role.Neutral, MobileNPCFaction.Id.Raiders);
        Assert.AreNotEqual(raiders, neutral);
    }

    [Test]
    public void DifferentSavedGroupsNeverShareRestoreKey()
    {
        string first = MobileNPCGroupMember.BuildRestoreGroupKey(22,
            MobileNPCHumanoidDisposition.Role.Hostile, MobileNPCFaction.Id.Raiders);
        string second = MobileNPCGroupMember.BuildRestoreGroupKey(23,
            MobileNPCHumanoidDisposition.Role.Hostile, MobileNPCFaction.Id.Raiders);
        Assert.AreNotEqual(first, second);
    }
}
