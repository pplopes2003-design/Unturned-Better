using NUnit.Framework;
using SDG.Unturned;

public class MobileNPCFriendsHostWorkshopTests
{
    [Test]
    public void WorkshopIdsAreDeduplicated()
    {
        var config = new MobileNPCIntegratedHostConfig();
        config.WorkshopFileIds = "123,456;123 789";
        Assert.IsTrue(config.TryParseWorkshopIds(out var ids, out var error), error);
        CollectionAssert.AreEqual(new ulong[] { 123, 456, 789 }, ids);
    }

    [Test]
    public void WorkshopIdsRejectInvalidValues()
    {
        var config = new MobileNPCIntegratedHostConfig();
        config.WorkshopFileIds = "123,abc";
        Assert.IsFalse(config.TryParseWorkshopIds(out _, out _));
        config.WorkshopFileIds = "0";
        Assert.IsFalse(config.TryParseWorkshopIds(out _, out _));
        config.WorkshopFileIds = "";
        Assert.IsTrue(config.TryParseWorkshopIds(out var ids, out _));
        Assert.AreEqual(0, ids.Count);
    }
}
