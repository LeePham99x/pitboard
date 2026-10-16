using Pitboard.Core;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class LaunchingTests
{
    private const string Fixture = @"C:\Temp\pitboard-fixture";
    private const string Shared = "pitboard-debug://open?url=https%3A%2F%2Fclaude.ai%2Fnew";

    private static readonly ReadFailure Elevated = new("elevated", "Pitboard runs elevated.");
    private static readonly Identity Debug = new(Build.Debug);
    private static readonly Identity Release = new(Build.Release);

    private static Func<T> Unasked<T>(string what) => () => throw new AssertFailedException($"{what} was asked");

    [TestMethod]
    public void UnregisteringAsksNeitherTheCoreNorARunningCopy()
    {
        var start = Launching.Decide(["--unregister"], Release, null, Unasked<ReadFailure?>("the core"), Unasked<bool>("the key"));

        Assert.AreEqual(Start.Unregister, start);
    }

    [TestMethod]
    public void ARefusedLaunchTakesNoKey()
    {
        var start = Launching.Decide([], Release, null, () => Elevated, Unasked<bool>("the key"));

        Assert.AreEqual(Start.Refused, start);
    }

    [TestMethod]
    public void TheFirstCopyRunsAndASecondHandsItsLaunchOver()
    {
        Assert.AreEqual(Start.Run, Launching.Decide([], Release, null, () => null, () => true));
        Assert.AreEqual(Start.Redirect, Launching.Decide([], Release, null, () => null, () => false));
    }

    [TestMethod]
    public void TheLaunchAfterAnUpdateIsDroppedByARunningCopy()
    {
        Assert.AreEqual(Start.Dropped, Launching.Decide(["--updated"], Release, null, () => null, () => false));
        Assert.AreEqual(Start.Run, Launching.Decide(["--updated"], Release, null, () => null, () => true));
    }

    // Windows starts Pitboard at sign-in through the Run value, before the person asked for anything.
    [TestMethod]
    public void AStartAtSignInIsDroppedByARunningCopy()
    {
        Assert.AreEqual(Start.Dropped, Launching.Decide(["--sign-in"], Release, null, () => null, () => false));
        Assert.AreEqual(Start.Run, Launching.Decide(["--sign-in"], Release, null, () => null, () => true));
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

        Assert.AreEqual(Start.Dropped, Launching.Decide(clicked, Release, null, () => null, () => false));
        Assert.AreEqual(Start.Run, Launching.Decide(clicked, Release, null, () => null, () => true));
    }

    [TestMethod]
    public void ALaunchCarriesTheLinkOnItsBuildsScheme()
    {
        Assert.AreEqual("pitboard://open?url=x", Launching.Link(["pitboard://open?url=x"], "pitboard"));
        Assert.IsNull(Launching.Link(["--updated", "pitboard://open?url=x"], "pitboard"), "a link after another argument");
        Assert.AreEqual("PITBOARD://open?url=x", Launching.Link(["PITBOARD://open?url=x"], "pitboard"), "a scheme in another case");
        Assert.IsNull(Launching.Link([Shared], "pitboard"), "the Debug build's");
        Assert.IsNull(Launching.Link(["https://claude.ai/new"], "pitboard"), "a site's own");
        Assert.IsNull(Launching.Link([], "pitboard"));
    }

    [TestMethod]
    public void TheAppStartsWithTheLinkOnItsBuildsScheme()
    {
        Assert.AreEqual(Shared, new AppStart(Debug, new(), "oneTool", Fixture) { Arguments = [Shared] }.Link);
        Assert.IsNull(new AppStart(Release, new(), null, @"C:\Local\Pitboard App") { Arguments = [Shared] }.Link);
        Assert.IsNull(new AppStart(Release, new(), null, @"C:\Local\Pitboard App").Link, "a launch with no arguments");
    }

    // Nothing registers pitboard-debug, so only a person or a test hands a Debug build one, which outside a fixture
    // would open on whatever homes that launch has.
    [TestMethod]
    public void ADebugBuildOutsideAFixtureRefusesALinkAndTakesNoKey()
    {
        Assert.AreEqual(Start.LinkRefused, Launching.Decide([Shared], Debug, null, () => null, Unasked<bool>("the key")));
        Assert.AreEqual(Start.Run, Launching.Decide([Shared], Debug, Fixture, () => null, () => true));
        Assert.AreEqual(Start.Run, Launching.Decide([], Debug, null, () => null, () => true), "a launch with no link");
        Assert.AreEqual(
            Start.Run, Launching.Decide(["pitboard://open?url=x"], Release, null, () => null, () => true), "a Release build's");
    }

    [TestMethod]
    public void ASecondCopyHandsItsLinkOver()
    {
        Assert.AreEqual(Start.Redirect, Launching.Decide([Shared], Debug, Fixture, () => null, () => false));
        Assert.AreEqual(Start.Redirect, Launching.Decide(["pitboard://open?url=x"], Release, null, () => null, () => false));
    }

    // Microsoft's guide to URI schemes: a link with unencoded quotes or spaces reaches Pitboard split into more
    // arguments, so nothing after a link is read as a flag.
    [TestMethod]
    public void ALinkLaunchReadsNoFlagAfterItsLink()
    {
        string[] split = ["pitboard://open?x=", "--unregister", ""];

        Assert.AreEqual(Start.Run, Launching.Decide(split, Release, null, () => null, () => true));
        Assert.AreEqual(Start.Redirect, Launching.Decide(split, Release, null, () => null, () => false));
        Assert.AreEqual(Start.Redirect, Launching.Decide(["pitboard://open?x=", "--updated"], Release, null, () => null, () => false));
        Assert.AreEqual(
            "pitboard://open?x=", Launching.Handed(@"""C:\Pitboard.exe"" ""pitboard://open?x="" --unregister """"", Release, null));
        Assert.IsNull(Launching.Handed(@"""C:\Pitboard.exe"" --updated pitboard://open?url=x", Release, null), "a link after a flag");
    }

    // Windows App SDK hands the running copy an unpackaged launch's whole command line, its program first, as
    // GetCommandLine has it.
    [TestMethod]
    public void TheRunningCopyReadsTheLinkFromTheCommandLineHandedOver()
    {
        Assert.AreEqual(Shared, Launching.Handed($@"""C:\Program Files\Pitboard\Pitboard.exe"" ""{Shared}""", Debug, Fixture));
        Assert.AreEqual(Shared, Launching.Handed($@"C:\pb\Pitboard.exe {Shared}", Debug, Fixture));
        Assert.AreEqual(
            "pitboard://open?url=x", Launching.Handed(@"""C:\Pitboard.exe"" ""pitboard://open?url=x""", Release, null));
        Assert.IsNull(Launching.Handed(@"""C:\Program Files\Pitboard\Pitboard.exe""", Release, null), "a launch with no link");
        Assert.IsNull(Launching.Handed($@"""C:\pb\Pitboard.exe"" {Shared}", Debug, null), "outside a fixture");
    }
}
