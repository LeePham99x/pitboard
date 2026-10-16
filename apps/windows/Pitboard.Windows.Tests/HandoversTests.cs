namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class HandoversTests
{
    [TestMethod]
    public void ALaunchHandedOverWhilePitboardStartsOpensTheWindowOnceItCan()
    {
        var handovers = new Handovers();
        var taken = new List<string?>();

        handovers.Arrived(null);
        handovers.Arrived(null);
        handovers.Ready(taken.Add);

        CollectionAssert.AreEqual(new string?[] { null }, taken);
    }

    [TestMethod]
    public void EachLaunchHandedOverLaterOpensTheWindow()
    {
        var handovers = new Handovers();
        var taken = new List<string?>();
        handovers.Ready(taken.Add);

        handovers.Arrived(null);
        handovers.Arrived(null);

        CollectionAssert.AreEqual(new string?[] { null, null }, taken);
    }

    [TestMethod]
    public void NoLaunchHandedOverOpensNothing()
    {
        var handovers = new Handovers();
        var taken = new List<string?>();

        handovers.Ready(taken.Add);

        Assert.AreEqual(0, taken.Count);
    }

    // A copy started a moment after the first hands its launch over between the first's key and its model.
    [TestMethod]
    public void ALaunchHandedOverBeforeTheAppCanTakeItWaitsForIt()
    {
        var handovers = new Handovers();
        var taken = new List<string?>();

        handovers.Arrived(null);
        handovers.Arrived("pitboard://open?url=a");
        handovers.Ready(taken.Add);
        handovers.Arrived("pitboard://open?url=b");

        CollectionAssert.AreEqual(new[] { null, "pitboard://open?url=a", "pitboard://open?url=b" }, taken);
    }

    [TestMethod]
    public void EachLinkHandedOverWhilePitboardStartsIsKept()
    {
        var handovers = new Handovers();
        var taken = new List<string?>();
        var link = "pitboard://open?url=a";

        handovers.Arrived(link);
        handovers.Arrived(link);
        handovers.Ready(taken.Add);

        CollectionAssert.AreEqual(new[] { link, link }, taken);
    }

    [TestMethod]
    public void ALaunchHandedOverOnAnotherThreadIsNeverLost()
    {
        for (var round = 0; round < 200; round++)
        {
            var handovers = new Handovers();
            var taken = 0;
            using var go = new Barrier(2);
            var arriving = Task.Run(() =>
            {
                go.SignalAndWait();
                handovers.Arrived(null);
            });

            go.SignalAndWait();
            handovers.Ready(_ => Interlocked.Increment(ref taken));
            arriving.Wait();

            Assert.AreEqual(1, taken, $"round {round}");
        }
    }
}
