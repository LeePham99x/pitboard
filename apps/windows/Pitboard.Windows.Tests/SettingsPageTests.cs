using Pitboard.Core;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class SettingsPageTests
{
    private static readonly AutoSwitchShown Auto = new(
        On: true, At: 95, Lowest: 50, Highest: 99, Enabled: true, AtLabel: "Switch when a limit reaches 95%", Note: "",
        Standing: null);

    [TestMethod]
    public void TheVersionLeavesOutTheCommitTheSdkAdds()
    {
        Assert.AreEqual("0.0.0", SettingsPage.Version("0.0.0+3f2a9c1"));
        Assert.AreEqual("1.0.0-beta.1", SettingsPage.Version("1.0.0-beta.1"));
        Assert.AreEqual("", SettingsPage.Version((string?)null));
        Assert.AreEqual("Pitboard 1.0.0", SettingsPage.Named("1.0.0"));
        Assert.AreEqual("Pitboard", SettingsPage.Named(""));
    }

    [TestMethod]
    public void ABuildThatCannotUpdateSaysSoAndABetaSaysHowItGetsTheNext()
    {
        Assert.AreEqual(SettingsPage.NoUpdateKey, SettingsPage.UpdatesNotes(new NoUpdates(), "0.0.0").Single());
        CollectionAssert.AreEqual(
            new[] { SettingsPage.NoUpdateKey, SettingsPage.BetaNote },
            SettingsPage.UpdatesNotes(new NoUpdates(), "1.0.0-beta.1"));
        Assert.IsEmpty(SettingsPage.UpdatesNotes(new Updating(), "1.0.0"));
    }

    // The model's span runs to 24:00, which a time picker cannot hold.
    [TestMethod]
    public void TheEndOfADayShowsAsMidnight()
    {
        Assert.AreEqual(TimeSpan.Zero, SettingsPage.TimeOfDay(24 * 60));
        Assert.AreEqual(new TimeSpan(22, 30, 0), SettingsPage.TimeOfDay((22 * 60) + 30));
        Assert.AreEqual((ushort)360, SettingsPage.Minutes(new TimeSpan(6, 0, 0)));
        Assert.AreEqual((ushort)0, SettingsPage.Minutes(TimeSpan.Zero));
    }

    [TestMethod]
    public void TheShareIsAWholePercentageTheModelTakes()
    {
        Assert.AreEqual((byte)96, SettingsPage.Share(96.4, Auto));
        Assert.AreEqual((byte)99, SettingsPage.Share(120, Auto));
        Assert.AreEqual((byte)50, SettingsPage.Share(10, Auto));
        Assert.IsNull(SettingsPage.Share(double.NaN, Auto), "a cleared box sends nothing");
    }

    [TestMethod]
    public void OnlyACommandLineFoundNowhereSaysWhoPutsItOnThePath()
    {
        static CommandLineShown Found(FoundCommandLine found) =>
            new(Found: found, InTerminal: "", UpdateNote: null, OffersLink: false, CannotLink: null);

        Assert.AreEqual(SettingsPage.NewTerminals, SettingsPage.TerminalNote(Found(new FoundCommandLine.Nowhere())));
        Assert.IsNull(SettingsPage.TerminalNote(Found(new FoundCommandLine.Bundled(@"C:\Pitboard\bin\pitboard.exe"))));
        Assert.IsNull(SettingsPage.TerminalNote(new CommandLineShown(null, null, null, false, null)), "not looked for yet");
    }

    private sealed class Updating : IUpdates
    {
        public bool Available => true;

        public string? Waiting => null;

        public void Check()
        {
        }
    }
}
