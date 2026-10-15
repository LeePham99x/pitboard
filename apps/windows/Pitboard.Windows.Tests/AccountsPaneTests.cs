using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class AccountsPaneTests
{
    private static readonly Choice Add = new("Add Account…", new Intent.PresentSheet(new Sheet.Add(Provider: null)), Enabled: true);

    private static (AccountsPane Pane, StandInModel Model, AppModel App) Over(Snapshot snapshot)
    {
        var model = new StandInModel(snapshot);
        var app = new AppModel(model);
        return (new AccountsPane(app, new DialogHost(() => UnitTestApp.Window?.Content?.XamlRoot)), model, app);
    }

    [UITestMethod]
    public async Task AnEmptyMachineOffersAnAccount()
    {
        var (pane, model, _) = Over(Snapshots.Read(1) with
        {
            AccountsShown = new AccountsShown.NoAccounts("No Accounts", "Sign in to Claude Code or Codex to add one.", Add),
        });
        await Views.Shown(pane);

        Assert.Contains("No Accounts", Views.Texts(pane));
        Views.Press(Views.Button(pane, "Add Account…"));

        Assert.Contains(Add.Intent, model.Sent);
    }

    [UITestMethod]
    public async Task AFailedReadSaysWhyAndHoldsItsRetryBack()
    {
        var retry = new Choice("Try Again", new Intent.Refresh(Asked: true), Enabled: false);
        var (pane, _, _) = Over(Snapshots.Read(1) with
        {
            AccountsShown = new AccountsShown.ReadFailed("Couldn’t Read Accounts", "The keychain is locked.", retry),
        });
        await Views.Shown(pane);

        Assert.Contains("The keychain is locked.", Views.Texts(pane));
        Assert.IsFalse(Views.Button(pane, "Try Again").IsEnabled);
    }

    [UITestMethod]
    public async Task BeforeTheFirstReadThePaneSaysItIsReading()
    {
        var (pane, _, _) = Over(Snapshots.Read(1) with { AccountsShown = new AccountsShown.Reading("Reading accounts…") });
        await Views.Shown(pane);

        Assert.Contains("Reading accounts…", Views.Texts(pane));
        Assert.IsTrue(Views.Visible<ProgressRing>(pane).Single().IsActive);
        Assert.IsFalse(Views.Visible<Button>(pane).Any(button => button.Content is string), "something to press while reading");
    }

    [UITestMethod]
    public async Task ShowingThePaneReadsAccountsWhoseNumbersAreOld()
    {
        var (pane, model, _) = Over(Snapshots.Read(1));
        await Views.Shown(pane);

        Assert.Contains(new Intent.PaneShown(Pane.Accounts), model.Sent);
    }

    [UITestMethod]
    public async Task EachToolsAccountsAreListedUnderItsName()
    {
        var (pane, _, _) = Over(Snapshots.Read(1) with
        {
            ShowsTools = true,
            Sections =
            [
                Items.Section("claude", "Claude Code", Items.Account("claude", "work", inUse: true)),
                Items.Section("codex", "Codex", Items.Account("codex", "spare")),
            ],
        });
        await Views.Shown(pane);

        var texts = Views.Texts(pane);
        Assert.Contains("Claude Code", texts);
        Assert.Contains("Codex", texts);
        Views.Element<ListViewItem>(pane, "account.claude/work");
        Views.Element<ListViewItem>(pane, "account.codex/spare");
    }

    // The model makes every list anew for each snapshot; the row the person selected stays selected.
    [UITestMethod]
    public async Task ASelectedRowStaysSelectedAcrossSnapshots()
    {
        var first = Snapshots.Read(1) with
        {
            Sections = [Items.Section("claude", null, Items.Account("claude", "work", inUse: true), Items.Account("claude", "spare"))],
        };
        var (pane, _, app) = Over(first);
        await Views.Shown(pane);
        var list = Views.Element<ListView>(pane, "accounts");
        list.SelectedIndex = 1;
        var selected = (ShownAccount)list.SelectedItem;

        app.Apply(first with
        {
            Revision = 2,
            Sections = [Items.Section("claude", null, Items.Account("claude", "work", inUse: true), Items.Account("claude", "spare") with { Title = "spare 12%" })],
        });

        Assert.AreSame(selected, list.SelectedItem);
        Assert.AreEqual("spare 12%", selected.Item.Title);
    }

    [UITestMethod]
    public async Task ANoticeAndTheSetupStepComeBeforeTheAccounts()
    {
        var notNow = new Choice("Not Now", new Intent.DeclineSecondAccount("claude"), Enabled: true);
        var (pane, model, _) = Over(Snapshots.Read(1) with
        {
            Notices = [Items.Notice("switch/codex", Items.Dismiss("codex"))],
            Setup = new SetupStep("Add a second account", "Pitboard switches once there are two.", [Add, notNow]),
            Sections = [Items.Section("claude", null, Items.Account("claude", "work", inUse: true))],
        });
        await Views.Shown(pane);

        Views.Element<InfoBar>(pane, "notice.switch/codex");
        Assert.Contains("Add a second account", Views.Texts(pane));
        Views.Press(Views.Button(pane, "Not Now"));

        Assert.Contains(notNow.Intent, model.Sent);
    }

    [UITestMethod]
    public async Task RefreshIsHeldBackWhileAReadIsUnderWay()
    {
        var (pane, _, _) = Over(Snapshots.Read(1) with { Reading = true });
        await Views.Shown(pane);

        Assert.IsFalse(Views.Element<AppBarButton>(pane, "accounts.refresh").IsEnabled);
    }
}
