using Pitboard.Core;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class LaunchingTests
{
    private static readonly ReadFailure Elevated = new("elevated", "Pitboard runs elevated.");

    private static Func<T> Unasked<T>(string what) => () => throw new AssertFailedException($"{what} was asked");

    [TestMethod]
    public void UnregisteringAsksNeitherTheCoreNorARunningCopy()
    {
        var start = Launching.Decide(["--unregister"], Unasked<ReadFailure?>("the core"), Unasked<bool>("the key"));

        Assert.AreEqual(Start.Unregister, start);
    }

    [TestMethod]
    public void ARefusedLaunchTakesNoKey()
    {
        var start = Launching.Decide([], () => Elevated, Unasked<bool>("the key"));

        Assert.AreEqual(Start.Refused, start);
    }

    [TestMethod]
    public void TheFirstCopyRunsAndASecondHandsItsLaunchOver()
    {
        Assert.AreEqual(Start.Run, Launching.Decide([], () => null, () => true));
        Assert.AreEqual(Start.Redirect, Launching.Decide([], () => null, () => false));
    }

    [TestMethod]
    public void TheLaunchAfterAnUpdateIsDroppedByARunningCopy()
    {
        Assert.AreEqual(Start.Dropped, Launching.Decide(["--updated"], () => null, () => false));
        Assert.AreEqual(Start.Run, Launching.Decide(["--updated"], () => null, () => true));
    }

    // COM starts a copy for a click only when no running copy registered for clicks, and gives its arguments
    // to the copy that registers: a second copy has nothing to hand over.
    [TestMethod]
    public void AClickOnANotificationIsDroppedByASecondCopy()
    {
        string[] clicked = ["----AppNotificationActivated:", "-Embedding"];

        Assert.AreEqual(Start.Dropped, Launching.Decide(clicked, () => null, () => false));
        Assert.AreEqual(Start.Run, Launching.Decide(clicked, () => null, () => true));
    }
}
