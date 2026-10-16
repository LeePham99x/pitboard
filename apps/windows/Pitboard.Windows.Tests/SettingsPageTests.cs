using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;
using Windows.Foundation;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class SettingsPageTests
{
    private const string Watching = "Pitboard watches work: 42% used at 09:00.";

    private static readonly WarmShown TwoWarmed = new(
        From: 360, To: 24 * 60, RangeLabel: "6:00 AM to midnight",
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

    private static Task<SettingsPage> Shown(
        AppModel model, StartAtSignIn? start = null, IUpdates? updates = null, string version = "0.0.0", Action<Uri>? open = null,
        Action<string>? copy = null, string? notificationsOff = null) =>
        Views.Navigated<SettingsPage>(Views.Context(model, start, updates, version, open, copy: copy, notificationsOff: notificationsOff));

    private static bool Shows(SettingsPage page, string automationId) =>
        Views.Visible<TextBlock>(page).Any(text => AutomationProperties.GetAutomationId(text) == automationId);

    private static async Task Expand(SettingsPage page, string automationId, string inside)
    {
        Views.Element<SettingsExpander>(page, automationId).IsExpanded = true;
        await Views.Until(() => Views.Texts(page).Contains(inside), $"{automationId} did not open on '{inside}'");
    }

    private static Rect Bounds(FrameworkElement element, UIElement within) =>
        element.TransformToVisual(within).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static SettingsCard Card(DependencyObject inside) =>
        Look(inside) ?? throw new AssertFailedException("not in a SettingsCard");

    private static SettingsCard? Look(DependencyObject? at) =>
        at is null ? null : at as SettingsCard ?? Look(VisualTreeHelper.GetParent(at));

    private static TextBlock Said(SettingsPage page, string text) =>
        Views.Visible<TextBlock>(page).SingleOrDefault(shown => shown.Text == text)
        ?? throw new AssertFailedException($"'{text}' is not among: {string.Join(", ", Views.Texts(page))}");

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
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), start);
        var toggle = Views.Element<ToggleSwitch>(page, "settings.startAtSignIn");

        Assert.IsFalse(toggle.IsOn);
        Assert.Contains(SettingsViewModel.SignInNote, Views.Texts(page));

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
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), start);

        Assert.IsFalse(Views.Element<ToggleSwitch>(page, "settings.startAtSignIn").IsEnabled);
        Assert.Contains("A Debug build never starts when you sign in to Windows.", Views.Texts(page));
        Assert.DoesNotContain(SettingsViewModel.SignInNote, Views.Texts(page));
    }

    [UITestMethod]
    public async Task AChangeWindowsRefusedIsSaidAndTheSwitchShowsWhatWindowsHas()
    {
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), new StartAtSignIn(new Refusing()));
        var toggle = Views.Element<ToggleSwitch>(page, "settings.startAtSignIn");

        toggle.IsOn = true;

        Assert.IsFalse(toggle.IsOn);
        Assert.AreEqual(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(Said(page, "Access is denied.")));
    }

    // Narrator says only the switch's new state, so the reason is a live region.
    [UITestMethod]
    public async Task AScheduleWindowsRefusedIsSaidAloud()
    {
        var page = await Shown(new AppModel(new StandInModel(With(1, machine => machine with
        {
            Schedule = machine.Schedule with { Failed = "Task Scheduler refused the task." },
        }))));

        Assert.AreEqual(
            AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(Said(page, "Task Scheduler refused the task.")));
    }

    [UITestMethod]
    public async Task SwitchingAutomaticallySendsTheSwitchAndTheShare()
    {
        var model = new StandInModel(Snapshots.Read(1));
        var app = new AppModel(model);
        var page = await Shown(app);
        var toggle = Views.Element<ToggleSwitch>(page, "settings.autoSwitch");
        await Expand(page, "settings.autoSwitchGroup", "Switch when a limit reaches");
        var share = Views.Element<NumberBox>(page, "settings.autoSwitchAt");

        Assert.IsTrue(toggle.IsEnabled);
        Assert.IsFalse(toggle.IsOn);
        Assert.IsFalse(share.IsEnabled, "no share to change while it is off");
        Assert.IsFalse(Shows(page, "settings.autoSwitchStanding"), "nothing said while it is off");
        Assert.AreEqual("Switch when a limit reaches 95%", AutomationProperties.GetName(share));

        toggle.IsOn = true;
        Assert.Contains(new Intent.SetAutoSwitch(true, 95), model.Sent);

        app.Apply(With(2, machine => machine with { AutoSwitch = machine.AutoSwitch with { On = true, Standing = Watching } }));
        Assert.IsTrue(share.IsEnabled);
        Assert.IsTrue(Shows(page, "settings.autoSwitchStanding"));
        Assert.Contains(Watching, Views.Texts(page));

        share.Value = 96;
        Assert.Contains(new Intent.SetAutoSwitch(true, 96), model.Sent);
    }

    [UITestMethod]
    public async Task TheModelHeldBackSwitchesUntilItHasReadThePreferences()
    {
        var unread = With(1, machine => machine with { AutoSwitch = machine.AutoSwitch with { Enabled = false } });
        var page = await Shown(new AppModel(new StandInModel(unread)));
        await Expand(page, "settings.autoSwitchGroup", "Switch when a limit reaches");

        Assert.IsFalse(Views.Element<ToggleSwitch>(page, "settings.autoSwitch").IsEnabled);
        Assert.IsFalse(Views.Element<NumberBox>(page, "settings.autoSwitchAt").IsEnabled);
    }

    // Each control raises its change event when the page sets it to what the model says.
    [UITestMethod]
    public async Task ShowingWhatTheModelSaysSendsNothing()
    {
        var model = new StandInModel(Snapshots.Read(1));
        var app = new AppModel(model);
        var page = await Shown(app);
        await Expand(page, "settings.warm", "No Claude Code account is enrolled yet.");
        await Expand(page, "settings.autoSwitchGroup", "Switch when a limit reaches");
        var sent = model.Sent.Count;

        app.Apply(With(2, machine => machine with
        {
            AutoSwitch = machine.AutoSwitch with { On = true, At = 90, AtLabel = "Switch when a limit reaches 90%" },
            Warm = TwoWarmed with { From = 420, To = 1260 },
            Schedule = machine.Schedule with { On = true, Runs = "Every day" },
        }));
        // The expander's repeater makes a card for each account on its next layout.
        await Views.Until(
            () => Views.Descendants(page).OfType<ToggleSwitch>().Any(on => AutomationProperties.GetAutomationId(on) == "settings.warm.claude/personal"),
            "the warm-up's accounts did not show");

        Assert.AreEqual(90.0, Views.Element<NumberBox>(page, "settings.autoSwitchAt").Value);
        Assert.IsTrue(Views.Element<ToggleSwitch>(page, "settings.warm.claude/personal").IsOn);
        Assert.AreEqual(new TimeSpan(7, 0, 0), Views.Element<TimePicker>(page, "settings.warmFrom").SelectedTime);
        Assert.AreEqual(sent, model.Sent.Count, string.Join(", ", model.Sent.Skip(sent)));
    }

    [UITestMethod]
    public async Task WarmUpListsEachAccountUnderItsExpander()
    {
        var model = new StandInModel(With(1, machine => machine with { Warm = TwoWarmed }));
        var page = await Shown(new AppModel(model));

        Assert.IsTrue(Shows(page, "settings.warmStanding"), "what comes next shows while it is closed");
        await Expand(page, "settings.warm", "personal · Max");

        var texts = Views.Texts(page);
        Assert.Contains("Warms up again at 22:50", texts);
        Assert.Contains("Next warm-up: personal at 22:50.", texts);
        Assert.DoesNotContain("No Claude Code account is enrolled yet.", texts);
        Assert.IsTrue(Views.Element<ToggleSwitch>(page, "settings.warm.claude/personal").IsOn);

        Views.Element<ToggleSwitch>(page, "settings.warm.claude/work").IsOn = true;
        Assert.Contains(new Intent.SetWarm("claude/work", true), model.Sent);
    }

    [UITestMethod]
    public async Task WarmUpWithNoAccountSaysSoAndHoldsItsHoursUntilTheCoreHasSpoken()
    {
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))));

        await Expand(page, "settings.warm", "No Claude Code account is enrolled yet.");

        Assert.IsFalse(Views.Element<TimePicker>(page, "settings.warmFrom").IsEnabled);
        Assert.IsFalse(Views.Element<TimePicker>(page, "settings.warmTo").IsEnabled);
    }

    // A time picker holds no 24:00, so each end sends the model's own other end.
    [UITestMethod]
    public async Task EachEndOfTheHoursSendsTheModelsOtherEnd()
    {
        var model = new StandInModel(With(1, machine => machine with { Warm = TwoWarmed }));
        var page = await Shown(new AppModel(model));
        await Expand(page, "settings.warm", "personal · Max");
        var from = Views.Element<TimePicker>(page, "settings.warmFrom");
        var to = Views.Element<TimePicker>(page, "settings.warmTo");

        Assert.AreEqual(TimeSpan.Zero, to.SelectedTime);

        from.SelectedTime = new TimeSpan(7, 0, 0);
        Assert.Contains(new Intent.SetWarmRange(420, 1440), model.Sent);

        to.SelectedTime = new TimeSpan(21, 0, 0);
        Assert.Contains(new Intent.SetWarmRange(360, 1260), model.Sent);
    }

    // The toolkit's own sample gives a card in ItemsHeader or ItemsFooter the style of the expander's items.
    [UITestMethod]
    public async Task TheWarmUpsHoursAndNoteAreCardsAsItsAccountsAre()
    {
        var page = await Shown(new AppModel(new StandInModel(With(1, machine => machine with { Warm = TwoWarmed }))));
        await Expand(page, "settings.warm", "personal · Max");
        var account = Card(Views.Element<ToggleSwitch>(page, "settings.warm.claude/personal"));

        foreach (var (name, card) in new[]
        {
            ("From", Card(Views.Element<TimePicker>(page, "settings.warmFrom"))),
            ("To", Card(Views.Element<TimePicker>(page, "settings.warmTo"))),
            ("the note", Card(Said(page, "Pitboard warms up Claude Code accounts only."))),
        })
        {
            Assert.AreEqual(account.Padding, card.Padding, name);
            Assert.AreEqual(account.MinHeight, card.MinHeight, name);
            Assert.AreEqual(account.BorderThickness, card.BorderThickness, name);
            Assert.AreEqual(account.CornerRadius, card.CornerRadius, name);
        }
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
        var page = await Shown(new AppModel(model));
        var texts = Views.Texts(page);

        Assert.Contains("Every day", texts);
        Assert.Contains(task, texts);
        Assert.IsTrue(Views.Element<ToggleSwitch>(page, "settings.renewDaily").IsOn);

        Views.Press(Views.Button(page, "Renew now"));
        Assert.Contains(new Intent.RenewNow(), model.Sent);

        Views.Element<ToggleSwitch>(page, "settings.renewDaily").IsOn = false;
        Assert.Contains(new Intent.SetSchedule(false), model.Sent);
    }

    [UITestMethod]
    public async Task RenewalWithNoSchedulerSaysWhyAndHoldsRenewNowWhileItRuns()
    {
        const string none = "This PC has no scheduler Pitboard knows how to write to.";
        var page = await Shown(new AppModel(new StandInModel(With(1, machine => machine with
        {
            Schedule = machine.Schedule with { Schedule = new Schedule.Unsupported(), Enabled = false, Note = none },
            Renewal = machine.Renewal with { Renewing = true },
        }))));
        var texts = Views.Texts(page);

        Assert.Contains(none, texts);
        Assert.DoesNotContain("Runs", texts);
        Assert.DoesNotContain("Scheduled in", texts);
        Assert.IsFalse(Views.Element<ToggleSwitch>(page, "settings.renewDaily").IsEnabled);
        Assert.IsFalse(Views.Button(page, "Renew now").IsEnabled);
    }

    // The installer owns the PATH entry, so the section offers no link even where the model would.
    [UITestMethod]
    public async Task TheCommandLineSaysWherePitboardIsAndOffersNoButton()
    {
        const string bundled = @"C:\Users\dana\AppData\Local\Programs\Pitboard\bin\pitboard.exe";
        var page = await Shown(new AppModel(new StandInModel(With(1, machine => machine with
        {
            CommandLine = new CommandLineShown(
                new FoundCommandLine.Bundled(bundled), bundled, "The one inside this app, so it updates with the app.", OffersLink: true,
                CannotLink: null),
        }))));
        var texts = Views.Texts(page);

        Assert.Contains(bundled, texts);
        Assert.Contains("The one inside this app, so it updates with the app.", texts);
        Assert.DoesNotContain(SettingsViewModel.NewTerminals, texts);
        Assert.IsFalse(Views.Visible<Button>(page).Any(button => button.Content is string said && said.StartsWith("Install", StringComparison.Ordinal)));
    }

    [UITestMethod]
    public async Task ACommandLineFoundNowhereSaysWhoPutsItOnThePath()
    {
        var page = await Shown(new AppModel(new StandInModel(With(1, machine => machine with
        {
            CommandLine = new CommandLineShown(new FoundCommandLine.Nowhere(), "Not installed", null, OffersLink: true, CannotLink: null),
        }))));
        var texts = Views.Texts(page);

        Assert.Contains("Not installed", texts);
        Assert.Contains(SettingsViewModel.NewTerminals, texts);
    }

    [UITestMethod]
    public async Task UntilItHasLookedTheCommandLineSectionWaits()
    {
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))));

        Assert.IsTrue(Views.Visible<ProgressRing>(page).Single(ring => AutomationProperties.GetName(ring) == "Looking for pitboard").IsActive);
    }

    [UITestMethod]
    public async Task UpdatesSayWhenTheBuildCannotUpdate()
    {
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))));
        var texts = Views.Texts(page);

        Assert.IsTrue(texts.Any(text => text.StartsWith("This copy of Pitboard can", StringComparison.Ordinal)));
        Assert.Contains("Pitboard 0.0.0", texts);
        Assert.DoesNotContain(SettingsViewModel.BetaNote, texts);
        Assert.IsFalse(Views.Element<Button>(page, "settings.checkNow").IsEnabled);
    }

    // As the macOS settings: automatic checks only where the build can update, and Check now beside them.
    [UITestMethod]
    public async Task AutomaticChecksShowWhileTheUpdaterIsOnAndTurnOff()
    {
        var updates = new StandInUpdates();
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), updates: updates);
        var toggle = Views.Element<ToggleSwitch>(page, "settings.checkAutomatically");

        Assert.IsTrue(toggle.IsOn);
        toggle.IsOn = false;
        Assert.IsFalse(updates.ChecksAutomatically);

        Views.Press(Views.Element<Button>(page, "settings.checkNow"));
        Assert.AreEqual(1, updates.Checks);

        var off = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))));
        Assert.IsFalse(
            Views.Visible<ToggleSwitch>(off).Any(shown => AutomationProperties.GetAutomationId(shown) == "settings.checkAutomatically"),
            "a build that cannot update offers automatic checks");
    }

    [UITestMethod]
    public async Task AnUpdateFoundShowsAtOnce()
    {
        var updates = new StandInUpdates();
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), updates: updates);

        updates.Found("1.0.1");

        Assert.Contains("Pitboard 1.0.1 is ready to install.", Views.Texts(page));
    }

    [UITestMethod]
    public async Task ABetaSaysHowItGetsTheNextBeta()
    {
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), version: "1.0.0-beta.1");

        Assert.Contains(SettingsViewModel.BetaNote, Views.Texts(page));
    }

    [UITestMethod]
    public async Task AboutOpensOnItsVersionAndItsLinks()
    {
        var opened = new List<Uri>();
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), version: "1.2.3", open: opened.Add);

        // Scrolled to first, as a person does: the expander's ItemsRepeater makes no item far below the view, where About
        // is on CI's smaller screens.
        var about = Views.Element<SettingsExpander>(page, "settings.about");
        about.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        about.IsExpanded = true;
        await Views.Until(() => Views.Visible<HyperlinkButton>(page).Any(), "About did not open");

        Assert.Contains("1.2.3", Views.Texts(page));
        Assert.Contains(SettingsViewModel.Copyright, Views.Texts(page));
        var report = Views.Visible<HyperlinkButton>(page).Single(link => link.Content as string == "Report a problem");
        new HyperlinkButtonAutomationPeer(report).Invoke();
        Assert.AreEqual(new Uri("https://github.com/datlechin/pitboard/issues"), opened.Single());
    }

    // A switch sat about 100 epx in from its card's edge with "Off" after it (shots 06 and 33). Windows Settings puts it at
    // the edge with its state before it, as the SettingsCard's own toggle style does.
    [UITestMethod]
    public async Task EachSwitchIsAtItsCardsRightEdgeWithItsStateBeforeIt()
    {
        var page = await Shown(
            new AppModel(new StandInModel(With(1, machine => machine with { Warm = TwoWarmed }))), updates: new StandInUpdates());
        await Expand(page, "settings.warm", "personal · Max");

        var toggles = Views.Visible<ToggleSwitch>(page).ToArray();
        Assert.HasCount(6, toggles, "start at sign-in, the automatic switch, two warm-ups, daily renewal and automatic checks");
        foreach (var toggle in toggles)
        {
            var card = Card(toggle);
            var name = AutomationProperties.GetAutomationId(toggle);
            var track = Views.Descendants(toggle).OfType<Rectangle>().Single(shape => shape.Name == "OuterBorder");
            var state = Views.Descendants(toggle).OfType<ContentPresenter>()
                .Single(presenter => presenter.Name == (toggle.IsOn ? "OnContentPresenter" : "OffContentPresenter"));

            var edge = Bounds(card, page).Right - card.BorderThickness.Right - card.Padding.Right;
            Assert.AreEqual(edge, Bounds(track, page).Right, 0.5, $"{name} is not at its card's edge");
            Assert.IsLessThanOrEqualTo(Bounds(track, page).Left, Bounds(state, page).Right, $"{name} says its state after the switch");
        }
    }

    // A setting that does nothing while another is off is dimmed, header and all, as Windows Settings dims one.
    [UITestMethod]
    public async Task ATurnedOffSettingDimsItsHeader()
    {
        var model = new StandInModel(Snapshots.Read(1));
        var app = new AppModel(model);
        var page = await Shown(app);
        await Expand(page, "settings.autoSwitchGroup", "Switch when a limit reaches");
        var card = Card(Views.Element<NumberBox>(page, "settings.autoSwitchAt"));

        Assert.IsFalse(card.IsEnabled);

        app.Apply(With(2, machine => machine with { AutoSwitch = machine.AutoSwitch with { On = true } }));

        Assert.IsTrue(card.IsEnabled);
    }

    // The header repeated the share the box beside it held; the box's name says it for Narrator.
    [UITestMethod]
    public async Task TheShareCardKeepsItsHeaderAsTheShareChanges()
    {
        var model = new StandInModel(Snapshots.Read(1));
        var app = new AppModel(model);
        var page = await Shown(app);
        await Expand(page, "settings.autoSwitchGroup", "Switch when a limit reaches");
        var share = Views.Element<NumberBox>(page, "settings.autoSwitchAt");
        var card = Card(share);

        app.Apply(With(2, machine => machine with
        {
            AutoSwitch = machine.AutoSwitch with { On = true, At = 90, AtLabel = "Switch when a limit reaches 90%" },
        }));

        Assert.AreEqual("Switch when a limit reaches", card.Header);
        Assert.AreEqual("From 50% to 99%", card.Description);
        Assert.AreEqual("Switch when a limit reaches 90%", AutomationProperties.GetName(share));
        Assert.AreEqual(90.0, share.Value);
    }

    // A failed change was said in the colour of the notes around it.
    [UITestMethod]
    public async Task AFailedChangeIsSaidInItsCardInTheCriticalColour()
    {
        var page = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), new StartAtSignIn(new Refusing()));
        var toggle = Views.Element<ToggleSwitch>(page, "settings.startAtSignIn");

        toggle.IsOn = true;

        var failed = Said(page, "Access is denied.");
        var critical = ((SolidColorBrush)Application.Current.Resources["SystemFillColorCriticalBrush"]).Color;
        Assert.AreSame(Card(toggle), Card(failed));
        Assert.AreEqual(critical, ((SolidColorBrush)failed.Foreground).Color);
    }

    [UITestMethod]
    public async Task TheScheduledInPathIsCopied()
    {
        const string task = @"\Pitboard\Renew S-1-5-21-1004";
        var copied = new List<string>();
        var page = await Shown(
            new AppModel(new StandInModel(With(1, machine => machine with
            {
                Schedule = new ScheduleShown(
                    Schedule: new Schedule.Installed(task, 86_400), On: true, Changing: false, Enabled: true, Runs: "Every day",
                    ScheduledIn: task, Note: null, Failed: null),
            }))),
            copy: copied.Add);

        Views.Press(Views.Element<Button>(page, "settings.copyScheduledIn"));

        Assert.AreEqual(task, copied.Single());
        Assert.IsTrue(Said(page, task).IsTextSelectionEnabled);
    }

    // A Debug build's own off would be said in the docs' pictures, so only a Release build's refusal is said.
    [UITestMethod]
    public async Task AReleaseBuildWhoseNotificationsAreOffSaysWhy()
    {
        const string why = "Windows offers this process no app notifications.";
        var off = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))), notificationsOff: why);

        Assert.AreSame(Card(Said(off, why)), Views.Element<SettingsCard>(off, "settings.notifications"));

        var on = await Shown(new AppModel(new StandInModel(Snapshots.Read(1))));

        Assert.IsFalse(Views.Visible<SettingsCard>(on).Any(card => AutomationProperties.GetAutomationId(card) == "settings.notifications"));
    }

    private sealed class Refusing : IStartupItem
    {
        public string? Refusal => null;

        public bool Read() => false;

        public void Write(bool starts) => throw new UnauthorizedAccessException("Access is denied.");
    }
}
