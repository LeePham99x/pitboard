using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class SettingsPaneTests
{
    private const string Watching = "Pitboard watches work: 42% used at 09:00.";

    private static readonly WarmShown TwoWarmed = new(
        From: 360, To: 24 * 60, RangeLabel: "06:00 to 24:00",
        Accounts:
        [
            new WarmAccountShown("claude/personal", "personal", "Max", On: true, Standing: "Warms up again at 22:50"),
            new WarmAccountShown("claude/work", "work", Plan: null, On: false, Standing: null),
        ],
        Standing: "Next warm-up: personal at 22:50.", Note: "Pitboard warms up Claude Code accounts only.", Enabled: true);

    private static Snapshot With(ulong revision, Func<MachineShown, MachineShown> change)
    {
        var read = Snapshots.Read(revision);
        return read with { Machine = change(read.Machine) };
    }

    private static Task<SettingsPane> Shown(
        AppModel model, StartAtSignIn? start = null, IUpdates? updates = null, string version = "0.0.0", Action<Uri>? open = null) =>
        Views.Shown(new SettingsPane(
            model, start ?? new StartAtSignIn(new FixtureStartupItem()), updates ?? new NoUpdates(), version, open ?? (_ => { })));

    private static bool Shows(SettingsPane pane, string automationId) =>
        Views.Visible<TextBlock>(pane).Any(text => AutomationProperties.GetAutomationId(text) == automationId);

    private static async Task Expand(SettingsPane pane, string automationId, string inside)
    {
        Views.Element<Expander>(pane, automationId).IsExpanded = true;
        await Views.Until(() => Views.Texts(pane).Contains(inside), $"{automationId} did not open on '{inside}'");
    }

    [UITestMethod]
    public async Task ShowingThePageReadsTheScheduleAndLooksForTheCommandLine()
    {
        var model = new StandInModel(Snapshots.Read(1));

        await Shown(new AppModel(model));

        Assert.Contains(new Intent.ReadSchedule(), model.Sent);
        Assert.Contains(new Intent.LookForCommandLine(), model.Sent);
    }

    [UITestMethod]
    public async Task StartAtSignInTurnsOnAndOffAndSaysWhereElseItIsTurnedOff()
    {
        var start = new StartAtSignIn(new FixtureStartupItem());
        var pane = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), start);
        var toggle = Views.Element<ToggleSwitch>(pane, "settings.startAtSignIn");

        Assert.IsFalse(toggle.IsOn);
        Assert.Contains(SettingsPage.StartupNote, Views.Texts(pane));

        toggle.IsOn = true;
        Assert.IsTrue(start.On);

        toggle.IsOn = false;
        Assert.IsFalse(start.On);
    }

    [UITestMethod]
    public async Task ADebugBuildHoldsTheSwitchBackAndSaysWhy()
    {
        var start = StartAtSignIn.For(
            new AppStart(new Identity(Build.Debug), new(), null, @"C:\Local\Pitboard App Debug"), @"C:\Build\Pitboard.exe");
        var pane = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), start);

        Assert.IsFalse(Views.Element<ToggleSwitch>(pane, "settings.startAtSignIn").IsEnabled);
        Assert.Contains("A Debug build never starts when you sign in to Windows.", Views.Texts(pane));
        Assert.DoesNotContain(SettingsPage.StartupNote, Views.Texts(pane));
    }

    [UITestMethod]
    public async Task AChangeWindowsRefusedIsSaidAndTheSwitchShowsWhatWindowsHas()
    {
        var pane = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), new StartAtSignIn(new Refusing()));
        var toggle = Views.Element<ToggleSwitch>(pane, "settings.startAtSignIn");

        toggle.IsOn = true;

        Assert.IsFalse(toggle.IsOn);
        Assert.Contains("Access is denied.", Views.Texts(pane));
    }

    [UITestMethod]
    public async Task SwitchingAutomaticallySendsTheSwitchAndTheShare()
    {
        var model = new StandInModel(Snapshots.Read(1));
        var app = new AppModel(model);
        var pane = await Shown(app);
        var toggle = Views.Element<ToggleSwitch>(pane, "settings.autoSwitch");
        var share = Views.Element<NumberBox>(pane, "settings.autoSwitchAt");

        Assert.IsTrue(toggle.IsEnabled);
        Assert.IsFalse(toggle.IsOn);
        Assert.IsFalse(share.IsEnabled, "no share to change while it is off");
        Assert.IsFalse(Shows(pane, "settings.autoSwitchStanding"), "nothing said while it is off");
        Assert.AreEqual("Switch when a limit reaches 95%", AutomationProperties.GetName(share));

        toggle.IsOn = true;
        Assert.Contains(new Intent.SetAutoSwitch(true, 95), model.Sent);

        app.Apply(With(2, machine => machine with { AutoSwitch = machine.AutoSwitch with { On = true, Standing = Watching } }));
        Assert.IsTrue(share.IsEnabled);
        Assert.IsTrue(Shows(pane, "settings.autoSwitchStanding"));
        Assert.Contains(Watching, Views.Texts(pane));

        share.Value = 96;
        Assert.Contains(new Intent.SetAutoSwitch(true, 96), model.Sent);
    }

    [UITestMethod]
    public async Task TheModelHeldBackSwitchesUntilItHasReadThePreferences()
    {
        var unread = With(1, machine => machine with { AutoSwitch = machine.AutoSwitch with { Enabled = false } });
        var pane = await Shown(new AppModel(new StandInModel(unread)));

        Assert.IsFalse(Views.Element<ToggleSwitch>(pane, "settings.autoSwitch").IsEnabled);
        Assert.IsFalse(Views.Element<NumberBox>(pane, "settings.autoSwitchAt").IsEnabled);
    }

    // Each control raises its change event when the page sets it to what the model says.
    [UITestMethod]
    public async Task ShowingWhatTheModelSaysSendsNothing()
    {
        var model = new StandInModel(Snapshots.Read(1));
        var app = new AppModel(model);
        var pane = await Shown(app);
        await Expand(pane, "settings.warm", "No Claude Code account is enrolled yet.");
        var sent = model.Sent.Count;

        app.Apply(With(2, machine => machine with
        {
            AutoSwitch = machine.AutoSwitch with { On = true, At = 90, AtLabel = "Switch when a limit reaches 90%" },
            Warm = TwoWarmed with { From = 420, To = 1260 },
            Schedule = machine.Schedule with { On = true, Runs = "Every day" },
        }));

        Assert.AreEqual(90.0, Views.Element<NumberBox>(pane, "settings.autoSwitchAt").Value);
        Assert.IsTrue(Views.Element<ToggleSwitch>(pane, "settings.warm.claude/personal").IsOn);
        Assert.AreEqual(new TimeSpan(7, 0, 0), Views.Element<TimePicker>(pane, "settings.warmFrom").SelectedTime);
        Assert.AreEqual(sent, model.Sent.Count, string.Join(", ", model.Sent.Skip(sent)));
    }

    [UITestMethod]
    public async Task WarmUpListsEachAccountUnderItsExpander()
    {
        var model = new StandInModel(With(1, machine => machine with { Warm = TwoWarmed }));
        var pane = await Shown(new AppModel(model));

        Assert.IsTrue(Shows(pane, "settings.warmStanding"), "what comes next shows while it is closed");
        await Expand(pane, "settings.warm", "personal · Max");

        var texts = Views.Texts(pane);
        Assert.Contains("Warms up again at 22:50", texts);
        Assert.Contains("Next warm-up: personal at 22:50.", texts);
        Assert.DoesNotContain("No Claude Code account is enrolled yet.", texts);
        Assert.IsTrue(Views.Element<ToggleSwitch>(pane, "settings.warm.claude/personal").IsOn);

        Views.Element<ToggleSwitch>(pane, "settings.warm.claude/work").IsOn = true;
        Assert.Contains(new Intent.SetWarm("claude/work", true), model.Sent);
    }

    [UITestMethod]
    public async Task WarmUpWithNoAccountSaysSoAndHoldsItsHoursUntilTheCoreHasSpoken()
    {
        var pane = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))));

        await Expand(pane, "settings.warm", "No Claude Code account is enrolled yet.");

        Assert.IsFalse(Views.Element<TimePicker>(pane, "settings.warmFrom").IsEnabled);
        Assert.IsFalse(Views.Element<TimePicker>(pane, "settings.warmTo").IsEnabled);
    }

    // A time picker holds no 24:00, so each end sends the model's own other end.
    [UITestMethod]
    public async Task EachEndOfTheHoursSendsTheModelsOtherEnd()
    {
        var model = new StandInModel(With(1, machine => machine with { Warm = TwoWarmed }));
        var pane = await Shown(new AppModel(model));
        await Expand(pane, "settings.warm", "personal · Max");
        var from = Views.Element<TimePicker>(pane, "settings.warmFrom");
        var to = Views.Element<TimePicker>(pane, "settings.warmTo");

        Assert.AreEqual(TimeSpan.Zero, to.SelectedTime);

        from.SelectedTime = new TimeSpan(7, 0, 0);
        Assert.Contains(new Intent.SetWarmRange(420, 1440), model.Sent);

        to.SelectedTime = new TimeSpan(21, 0, 0);
        Assert.Contains(new Intent.SetWarmRange(360, 1260), model.Sent);
    }

    [UITestMethod]
    public async Task RenewalSaysHowOftenAndWhereAndRenewsNow()
    {
        const string task = @"\Pitboard\Renew S-1-5-21-1004";
        var model = new StandInModel(With(1, machine => machine with
        {
            Schedule = new ScheduleShown(
                Schedule: new Schedule.Installed(task, 86_400), On: true, Changing: false, Enabled: true, Runs: "Every day",
                ScheduledIn: task, Note: null, Failed: null),
        }));
        var pane = await Shown(new AppModel(model));
        var texts = Views.Texts(pane);

        Assert.Contains("Every day", texts);
        Assert.Contains(task, texts);
        Assert.IsTrue(Views.Element<ToggleSwitch>(pane, "settings.renewDaily").IsOn);

        Views.Press(Views.Button(pane, "Renew Now"));
        Assert.Contains(new Intent.RenewNow(), model.Sent);

        Views.Element<ToggleSwitch>(pane, "settings.renewDaily").IsOn = false;
        Assert.Contains(new Intent.SetSchedule(false), model.Sent);
    }

    [UITestMethod]
    public async Task RenewalWithNoSchedulerSaysWhyAndHoldsRenewNowWhileItRuns()
    {
        const string none = "This PC has no scheduler Pitboard knows how to write to.";
        var pane = await Shown(new AppModel(new StandInModel(With(1, machine => machine with
        {
            Schedule = machine.Schedule with { Schedule = new Schedule.Unsupported(), Enabled = false, Note = none },
            Renewal = machine.Renewal with { Renewing = true },
        }))));
        var texts = Views.Texts(pane);

        Assert.Contains(none, texts);
        Assert.DoesNotContain("Runs", texts);
        Assert.DoesNotContain("Scheduled in", texts);
        Assert.IsFalse(Views.Element<ToggleSwitch>(pane, "settings.renewDaily").IsEnabled);
        Assert.IsFalse(Views.Button(pane, "Renew Now").IsEnabled);
    }

    // The installer owns the PATH entry, so the section offers no link even where the model would.
    [UITestMethod]
    public async Task TheCommandLineSaysWherePitboardIsAndOffersNoButton()
    {
        const string bundled = @"C:\Users\dana\AppData\Local\Programs\Pitboard\bin\pitboard.exe";
        var pane = await Shown(new AppModel(new StandInModel(With(1, machine => machine with
        {
            CommandLine = new CommandLineShown(
                new FoundCommandLine.Bundled(bundled), bundled, "The one inside this app, so it updates with the app.", OffersLink: true,
                CannotLink: null),
        }))));
        var texts = Views.Texts(pane);

        Assert.Contains(bundled, texts);
        Assert.Contains("The one inside this app, so it updates with the app.", texts);
        Assert.DoesNotContain(SettingsPage.NewTerminals, texts);
        Assert.IsFalse(Views.Visible<Button>(pane).Any(button => button.Content is string said && said.StartsWith("Install", StringComparison.Ordinal)));
    }

    [UITestMethod]
    public async Task ACommandLineFoundNowhereSaysWhoPutsItOnThePath()
    {
        var pane = await Shown(new AppModel(new StandInModel(With(1, machine => machine with
        {
            CommandLine = new CommandLineShown(new FoundCommandLine.Nowhere(), "Not installed", null, OffersLink: true, CannotLink: null),
        }))));
        var texts = Views.Texts(pane);

        Assert.Contains("Not installed", texts);
        Assert.Contains(SettingsPage.NewTerminals, texts);
    }

    [UITestMethod]
    public async Task UntilItHasLookedTheCommandLineSectionWaits()
    {
        var pane = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))));

        Assert.IsTrue(Views.Visible<ProgressRing>(pane).Single(ring => AutomationProperties.GetName(ring) == "Looking for pitboard").IsActive);
    }

    [UITestMethod]
    public async Task UpdatesSayWhenTheBuildCannotUpdate()
    {
        var pane = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))));
        var texts = Views.Texts(pane);

        Assert.IsTrue(texts.Any(text => text.StartsWith("This copy of Pitboard can", StringComparison.Ordinal)));
        Assert.Contains("Pitboard 0.0.0", texts);
        Assert.DoesNotContain(SettingsPage.BetaNote, texts);
        Assert.IsFalse(Views.Element<Button>(pane, "settings.checkNow").IsEnabled);
    }

    [UITestMethod]
    public async Task ABetaSaysHowItGetsTheNextBeta()
    {
        var pane = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), version: "1.0.0-beta.1");

        Assert.Contains(SettingsPage.BetaNote, Views.Texts(pane));
    }

    [UITestMethod]
    public async Task AboutOpensOnItsVersionAndItsLinks()
    {
        var opened = new List<Uri>();
        var pane = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), version: "1.2.3", open: opened.Add);

        pane.ShowAbout();
        await Views.Until(() => Views.Visible<HyperlinkButton>(pane).Any(), "About did not open");

        Assert.Contains("1.2.3", Views.Texts(pane));
        Assert.Contains(SettingsPage.Copyright, Views.Texts(pane));
        var report = Views.Visible<HyperlinkButton>(pane).Single(link => link.Content as string == "Report a problem");
        new HyperlinkButtonAutomationPeer(report).Invoke();
        Assert.AreEqual(new Uri("https://github.com/datlechin/pitboard/issues"), opened.Single());
    }

    private sealed class Refusing : IStartupItem
    {
        public string? Refusal => null;

        public bool Read() => false;

        public void Write(bool starts) => throw new UnauthorizedAccessException("Access is denied.");
    }
}
