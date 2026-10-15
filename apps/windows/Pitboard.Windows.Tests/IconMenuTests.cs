using Pitboard.Core;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class IconMenuTests
{
    private static readonly MenuEntry Install = new(
        "Claude Code isn’t installed", "Install it, then come back", null, null, null, "https://example.com/install", true);

    private static readonly MenuEntry Advice = new(
        "Switch Claude Code to personal", "work is at 91%", null, Severity.Warning,
        new Intent.SwitchTo("claude/personal"), null, true);

    private static readonly MenuEntry Stuck = new(
        "An interrupted switch is waiting", null, "Pitboard can finish it or give it up.", null,
        new Intent.ShowWindow(Pane.Accounts), null, true);

    private static AppModel Model(Func<Snapshot, Snapshot> change) => new(change(Snapshots.Read(1)));

    private static AppModel TwoTools(bool reading = false) => Model(snapshot => snapshot with
    {
        Reading = reading,
        Sections =
        [
            Items.Section("claude", "Claude Code", Items.Account("claude", "work", inUse: true), Items.Account("claude", "personal")),
            Items.Section("codex", "Codex", Items.Account("codex", "main", inUse: true)),
        ],
    });

    // One line per entry: a separator as "-", a heading as "# ", a note as "> ", an account as "( )" or "(x)".
    private static string Said(IEnumerable<IconMenuEntry> entries) => string.Join(" | ", entries.Select(entry => entry switch
    {
        IconMenuEntry.Separator => "-",
        IconMenuEntry.Label { Heading: true } label => "# " + label.Text,
        IconMenuEntry.Label label => "> " + label.Text,
        IconMenuEntry.Account account => (account.InUse ? "(x) " : "( ) ") + account.Text,
        IconMenuEntry.Command command => command.Text,
        _ => throw new AssertFailedException($"an entry of no kind: {entry}"),
    }));

    private static IconMenuEntry.Command Command(IEnumerable<IconMenuEntry> entries, string text) =>
        entries.OfType<IconMenuEntry.Command>().FirstOrDefault(command => command.Text == text)
        ?? throw new AssertFailedException($"no '{text}' in: {Said(entries)}");

    [TestMethod]
    public void TheMenuGoesInTheMacOSOrder()
    {
        var model = Model(snapshot => snapshot with
        {
            MenuNotices = new MenuNotices(Install, [Advice], Stuck),
            Sections =
            [
                Items.Section("claude", "Claude Code", Items.Account("claude", "work", inUse: true), Items.Account("claude", "personal")),
                Items.Section("codex", "Codex", Items.Account("codex", "main", inUse: true)),
            ],
        });

        var entries = IconMenu.Entries(model, new NoUpdates());

        Assert.AreEqual(
            "Claude Code isn’t installed | Switch Claude Code to personal | An interrupted switch is waiting | - "
            + "| # Claude Code | (x) work | ( ) personal | - | # Codex | (x) main | - "
            + "| Add Account… | Refresh | - | Open Pitboard | Settings | - | Pitboard Help | Exit",
            Said(entries));
    }

    [TestMethod]
    public void EachToolsAccountsAreARadioGroupOfTheirOwn()
    {
        var accounts = IconMenu.Entries(TwoTools(), new NoUpdates()).OfType<IconMenuEntry.Account>().ToArray();

        Assert.AreEqual("claude, claude, codex", string.Join(", ", accounts.Select(account => account.Group)));
    }

    // A machine with one tool shows its accounts unheaded, as the macOS menu does.
    [TestMethod]
    public void OneToolsAccountsHaveNoHeading()
    {
        var model = Model(snapshot => snapshot with
        {
            Sections = [Items.Section("claude", null, Items.Account("claude", "work", inUse: true))],
        });

        Assert.AreEqual(
            "(x) work | - | Add Account… | Refresh | - | Open Pitboard | Settings | - | Pitboard Help | Exit",
            Said(IconMenu.Entries(model, new NoUpdates())));
    }

    [TestMethod]
    public void TheNoteStandsInForTheAccounts()
    {
        var model = Model(snapshot => snapshot with
        {
            MenuAccountsNote = "Couldn’t read usage",
            Sections = [Items.Section("claude", null, Items.Account("claude", "work", inUse: true))],
        });

        var entries = IconMenu.Entries(model, new NoUpdates());

        Assert.StartsWith("> Couldn’t read usage | - | Add Account…", Said(entries));
        Assert.IsEmpty(entries.OfType<IconMenuEntry.Account>());
    }

    // What macOS shows as a second line: an account's summary, a notice's subtitle, when the menu last read.
    [TestMethod]
    public void EachSecondLineIsTheEntrysDetail()
    {
        var model = Model(snapshot => snapshot with
        {
            MenuNotices = new MenuNotices(null, [Advice], Stuck),
            Sections =
            [
                Items.Section(
                    "claude", null,
                    Items.Account("claude", "work", inUse: true) with { Summary = "5h 42% · resets in 2h", Help = "In use by Claude Code" }),
            ],
        });

        var entries = IconMenu.Entries(model, new NoUpdates());

        Assert.AreEqual("5h 42% · resets in 2h\nIn use by Claude Code", entries.OfType<IconMenuEntry.Account>().Single().Detail);
        Assert.AreEqual("work is at 91%", Command(entries, Advice.Title).Detail);
        Assert.AreEqual("Pitboard can finish it or give it up.", Command(entries, Stuck.Title).Detail);
        Assert.AreEqual("Updated 08:00", Command(entries, "Refresh").Detail);
        Assert.IsNull(Command(entries, "Open Pitboard").Detail);
    }

    [TestMethod]
    public void ChoosingAnAccountDoesWhatItsButtonDoes()
    {
        var expired = Items.Account("claude", "old") with
        {
            Action = new ItemAction("Sign In", "Sign in to old again", new Intent.PresentSheet(new Sheet.SignInAgain("claude", "old"))),
        };
        var unusable = Items.Account("claude", "broken") with { Action = null };
        var model = Model(snapshot => snapshot with
        {
            Sections = [Items.Section("claude", null, Items.Account("claude", "work", inUse: true), Items.Account("claude", "personal"), expired, unusable)],
        });

        var accounts = IconMenu.Entries(model, new NoUpdates()).OfType<IconMenuEntry.Account>().ToDictionary(account => account.Text);

        Assert.AreEqual(new IconCommand.Send(new Intent.SwitchTo("claude/personal")), accounts["personal"].Does);
        Assert.AreEqual(new IconCommand.Send(new Intent.PresentSheet(new Sheet.SignInAgain("claude", "old"))), accounts["old"].Does);
        Assert.IsNull(accounts["work"].Does);
        Assert.IsTrue(accounts["work"].Enabled, "the account in use");
        Assert.IsFalse(accounts["broken"].Enabled, "an account with nothing to do");
    }

    [TestMethod]
    public void ANoticeSendsItsIntentOrOpensItsLink()
    {
        var held = Advice with { Enabled = false };
        var entries = IconMenu.Entries(Model(snapshot => snapshot with { MenuNotices = new MenuNotices(Install, [held], Stuck) }), new NoUpdates());

        Assert.AreEqual(new IconCommand.OpenLink("https://example.com/install"), Command(entries, Install.Title).Does);
        Assert.AreEqual(IconMark.Help, Command(entries, Install.Title).Mark);
        Assert.AreEqual(new IconCommand.Send(Advice.Intent!), Command(entries, Advice.Title).Does);
        Assert.AreEqual(IconMark.Warning, Command(entries, Advice.Title).Mark);
        Assert.IsFalse(Command(entries, Advice.Title).Enabled);
        Assert.AreEqual(new IconCommand.Send(new Intent.ShowWindow(Pane.Accounts)), Command(entries, Stuck.Title).Does);
        Assert.AreEqual(IconMark.Warning, Command(entries, Stuck.Title).Mark);
    }

    [TestMethod]
    public void AnAdviceWithoutASeverityShowsTheSwitchMark()
    {
        var entries = IconMenu.Entries(Model(snapshot => snapshot with { MenuNotices = new MenuNotices(null, [Advice with { Severity = null }], null) }), new NoUpdates());

        Assert.AreEqual(IconMark.Switch, Command(entries, Advice.Title).Mark);
    }

    [TestMethod]
    public void RefreshWaitsForTheReadUnderWay()
    {
        Assert.IsTrue(Command(IconMenu.Entries(TwoTools(), new NoUpdates()), "Refresh").Enabled);
        Assert.IsFalse(Command(IconMenu.Entries(TwoTools(reading: true), new NoUpdates()), "Refresh").Enabled);
    }

    [TestMethod]
    public void TheCommandsSendWhatTheMacOSMenuSends()
    {
        var entries = IconMenu.Entries(TwoTools(), new NoUpdates());

        Assert.AreEqual(new IconCommand.Send(new Intent.PresentSheet(new Sheet.Add(Provider: null))), Command(entries, "Add Account…").Does);
        Assert.AreEqual(new IconCommand.Send(new Intent.Refresh(Asked: true)), Command(entries, "Refresh").Does);
        Assert.AreEqual(new IconCommand.Send(new Intent.ShowWindow(Pane: null)), Command(entries, "Open Pitboard").Does);
        Assert.AreEqual(new IconCommand.ShowSettings(), Command(entries, "Settings").Does);
        Assert.AreEqual(new IconCommand.OpenLink("https://docs.usepitboard.com"), Command(entries, "Pitboard Help").Does);
        Assert.AreEqual(new IconCommand.Quit(), Command(entries, "Exit").Does);
    }

    [TestMethod]
    public void OpenPitboardIsWhatAClickOnTheIconDoes()
    {
        var defaults = IconMenu.Entries(TwoTools(), new NoUpdates()).OfType<IconMenuEntry.Command>().Where(command => command.Default);

        Assert.AreEqual("Open Pitboard", defaults.Single().Text);
    }

    [TestMethod]
    public void CheckForUpdatesShowsOnlyWhileTheUpdaterIsOn()
    {
        Assert.DoesNotContain("Check for Updates", Said(IconMenu.Entries(TwoTools(), new NoUpdates())));

        var entries = IconMenu.Entries(TwoTools(), new StandInUpdates(waiting: null));

        Assert.AreEqual(new IconCommand.CheckForUpdates(), Command(entries, "Check for Updates").Does);
        Assert.Contains("Open Pitboard | Settings | Check for Updates | -", Said(entries));
    }

    [TestMethod]
    public void AnUpdateWaitingIsOfferedFirst()
    {
        var entries = IconMenu.Entries(TwoTools(), new StandInUpdates(waiting: "1.2.0"));

        Assert.StartsWith("Install Pitboard 1.2.0… | - | # Claude Code", Said(entries));
        Assert.AreEqual(IconMark.Update, Command(entries, "Install Pitboard 1.2.0…").Mark);
    }

    private sealed class StandInUpdates(string? waiting) : IUpdates
    {
        public bool Available => true;

        public string? Waiting => waiting;

        public void Check()
        {
        }
    }
}
