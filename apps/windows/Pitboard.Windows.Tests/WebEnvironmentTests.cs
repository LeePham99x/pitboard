namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class WebEnvironmentTests
{
    [TestMethod]
    public void ARuntimeAsNewAsTheSdksOwnOrNewerTakesTheWindows()
    {
        Assert.IsNull(WebEnvironment.Refusal(WebEnvironment.MinimumRuntime));
        Assert.IsNull(WebEnvironment.Refusal("150.0.4258.31"));
        Assert.IsNull(WebEnvironment.Refusal("150.0.4258.31 dev"), "a channel after the version");
    }

    [TestMethod]
    public void AnOlderRuntimeOrNoneSaysWhatTheWindowsNeed()
    {
        Assert.AreEqual(
            "This window needs the Microsoft Edge WebView2 Runtime 144.0.3719.77 or later, and this PC has version 143.0.3650.58. "
            + "Windows Update keeps it up to date.",
            WebEnvironment.Refusal("143.0.3650.58"));
        StringAssert.Contains(WebEnvironment.Refusal(null), "and this PC has none.");
    }

    // The user data folder is the app's own data folder's WebView2, and WebView2 names a profile's folder after it. The
    // windows save in the person's Downloads.
    [TestMethod]
    public void EachLaunchKeepsItsProfilesInItsDataFolder()
    {
        var live = WebEnvironment.Live(Path.Combine("data", "Pitboard App"), devTools: false);
        var fixture = WebEnvironment.Fixture(Path.Combine("temp", "pitboard-fixture"));

        Assert.AreEqual("https", live.Scheme);
        Assert.AreEqual(Path.Combine("data", "Pitboard App", "WebView2", "EBWebView", "a"), live.ProfileFolder("a"));
        Assert.AreEqual(WebEnvironment.FixtureScheme, fixture.Scheme);
        Assert.AreEqual(Path.Combine("temp", "pitboard-fixture", "WebView2"), fixture.UserDataFolder);
        Assert.AreEqual(Path.Combine("temp", "pitboard-fixture", "Downloads"), fixture.Downloads, "a fixture saves in its own folder");
        Assert.AreEqual(Shell.Downloads(), live.Downloads);
    }
}
