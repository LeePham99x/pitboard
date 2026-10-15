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

    // Windows starts Pitboard at sign-in through the Run value, before the person asked for anything.
    [TestMethod]
    public void AStartAtSignInIsDroppedByARunningCopy()
    {
        Assert.AreEqual(Start.Dropped, Launching.Decide(["--sign-in"], () => null, () => false));
        Assert.AreEqual(Start.Run, Launching.Decide(["--sign-in"], () => null, () => true));
    }

    // A new icon goes to the overflow, so a launch from Start or a shortcut that opened no window would show nothing.
    [TestMethod]
    public void OnlyALaunchThePersonAskedForOpensTheWindow()
    {
        Assert.IsTrue(Launching.OpensWindow([]));
        Assert.IsFalse(Launching.OpensWindow(["--sign-in"]));
        Assert.IsFalse(Launching.OpensWindow(["--updated"]));
        Assert.IsFalse(Launching.OpensWindow(["----AppNotificationActivated:", "-Embedding"]));
    }

    // COM gives a click's arguments only to the copy that registered for clicks.
    [TestMethod]
    public void AClickOnANotificationIsDroppedByASecondCopy()
    {
        string[] clicked = ["----AppNotificationActivated:", "-Embedding"];

        Assert.AreEqual(Start.Dropped, Launching.Decide(clicked, () => null, () => false));
        Assert.AreEqual(Start.Run, Launching.Decide(clicked, () => null, () => true));
    }
}
