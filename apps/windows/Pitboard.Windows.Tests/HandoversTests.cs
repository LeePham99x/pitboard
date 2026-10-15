namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class HandoversTests
{
    [TestMethod]
    public void ALaunchHandedOverWhilePitboardStartsOpensTheWindowOnceItCan()
    {
        var handovers = new Handovers();
        var opened = 0;

        handovers.Arrived();
        handovers.Arrived();
        handovers.Ready(() => opened++);

        Assert.AreEqual(1, opened);
    }

    [TestMethod]
    public void EachLaunchHandedOverLaterOpensTheWindow()
    {
        var handovers = new Handovers();
        var opened = 0;
        handovers.Ready(() => opened++);

        handovers.Arrived();
        handovers.Arrived();

        Assert.AreEqual(2, opened);
    }

    [TestMethod]
    public void NoLaunchHandedOverOpensNothing()
    {
        var handovers = new Handovers();
        var opened = 0;

        handovers.Ready(() => opened++);

        Assert.AreEqual(0, opened);
    }

    [TestMethod]
    public void ALaunchHandedOverOnAnotherThreadIsNeverLost()
    {
        for (var round = 0; round < 200; round++)
        {
            var handovers = new Handovers();
            var opened = 0;
            using var go = new Barrier(2);
            var arriving = Task.Run(() =>
            {
                go.SignalAndWait();
                handovers.Arrived();
            });

            go.SignalAndWait();
            handovers.Ready(() => Interlocked.Increment(ref opened));
            arriving.Wait();

            Assert.AreEqual(1, opened, $"round {round}");
        }
    }
}
