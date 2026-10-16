using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class IconMenuFlyoutTests
{
    private static readonly MenuEntry Stuck = new(
        "An interrupted switch is waiting", null, "Pitboard can finish it or give it up.", null,
        new Intent.ShowWindow(Pane.Accounts), null, true);

    private static AppModel TwoTools(bool reading = false) => new(TwoToolsRead(reading));

    private static Snapshot TwoToolsRead(bool reading = false) => Snapshots.Read(1) with
    {
        Reading = reading,
        MenuNotices = new MenuNotices(null, [], Stuck),
        Sections =
        [
            Items.Section(
                "claude", "Claude Code",
                Items.Account("claude", "work", inUse: true) with { Summary = "5h 42% · resets in 2h" },
                Items.Account("claude", "personal")),
            Items.Section("codex", "Codex", Items.Account("codex", "main", inUse: true)),
        ],
    };

    private static MenuFlyout Menu(AppModel model, List<IconCommand>? chosen = null) =>
        IconMenuFlyout.For(IconMenu.Entries(model, new NoUpdates()), command => chosen?.Add(command));

    private static string Said(MenuFlyout menu) => string.Join(" | ", menu.Items.Select(item => item switch
    {
        MenuFlyoutSeparator => "-",
        RadioMenuFlyoutItem radio => (radio.IsChecked ? "(x) " : "( ) ") + radio.Text,
        MenuFlyoutItem entry => entry.Text,
        _ => item.GetType().Name,
    }));

    // A RadioMenuFlyoutItem is a ToggleMenuFlyoutItem inside, and toggles as UI Automation sees it.
    private static void Toggle(MenuFlyoutItem radio) =>
        ((IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(radio).GetPattern(PatternInterface.Toggle)).Toggle();

    private static MenuFlyoutItem Item(MenuFlyout menu, string text) =>
        menu.Items.OfType<MenuFlyoutItem>().FirstOrDefault(item => item.Text == text)
        ?? throw new AssertFailedException($"no '{text}' in: {Said(menu)}");

    [UITestMethod]
    public void TheMenuShowsTheEntriesInOrder()
    {
        var menu = Menu(TwoTools());

        Assert.AreEqual(
            "An interrupted switch is waiting | - | Claude Code | (x) work | ( ) personal | - | Codex | (x) main | - "
            + "| Add account… | Refresh | - | Open Pitboard | Settings | - | Pitboard help | Exit",
            Said(menu));
    }

    // Its windows' items name the account and its site for UI Automation, since an account's own item has its label too.
    [UITestMethod]
    public void ASiteWithSeveralWindowsIsASubmenu()
    {
        var main = Items.Window("codex", "main");
        var spare = Items.Window("codex", "spare");
        var snapshot = Snapshots.Read(1);
        var model = new AppModel(snapshot with
        {
            AccountWindows = snapshot.AccountWindows with { Menus = [new SiteMenu.Several("Open chatgpt.com", main.Site, [main, spare])] },
        });
        var chosen = new List<IconCommand>();

        var site = Menu(model, chosen).Items.OfType<MenuFlyoutSubItem>().Single();
        var window = (MenuFlyoutItem)site.Items[1];
        new MenuFlyoutItemAutomationPeer(window).Invoke();

        Assert.AreEqual("Open chatgpt.com", site.Text);
        Assert.AreEqual("main, spare", string.Join(", ", site.Items.OfType<MenuFlyoutItem>().Select(item => item.Text)));
        Assert.AreEqual("window.chatgpt.com/spare", AutomationProperties.GetAutomationId(window));
        Assert.AreEqual(new IconCommand.OpenWindow(spare), chosen.Single());
    }

    // The window it opens from is a pixel wide.
    [UITestMethod]
    public void TheMenuDrawsOutsideTheWindowItOpensFrom()
    {
        Assert.IsFalse(Menu(TwoTools()).ShouldConstrainToRootBounds);
    }

    [UITestMethod]
    public void EachToolsAccountsAreOneRadioGroup()
    {
        var radios = Menu(TwoTools()).Items.OfType<RadioMenuFlyoutItem>().ToArray();

        Assert.AreEqual("claude, claude, codex", string.Join(", ", radios.Select(radio => radio.GroupName)));
    }

    [UITestMethod]
    public void AToolsNameIsAHeadingNobodyChooses()
    {
        var heading = Item(Menu(TwoTools()), "Claude Code");

        Assert.IsFalse(heading.IsEnabled);
        Assert.AreEqual(AutomationHeadingLevel.Level1, AutomationProperties.GetHeadingLevel(heading));
    }

    [UITestMethod]
    public void EachSecondLineIsTheItemsTooltipAndHelpText()
    {
        var menu = Menu(TwoTools());

        foreach (var (text, detail) in new[]
        {
            ("work", "5h 42% · resets in 2h"),
            ("Refresh", "Updated 08:00"),
            ("An interrupted switch is waiting", "Pitboard can finish it or give it up."),
        })
        {
            var item = Item(menu, text);
            Assert.AreEqual(detail, ToolTipService.GetToolTip(item), text);
            Assert.AreEqual(detail, AutomationProperties.GetHelpText(item), text);
        }

        Assert.IsNull(ToolTipService.GetToolTip(Item(menu, "Exit")));
    }

    // The window's keys do nothing in this menu, and the menu guidance shows a context menu no shortcuts.
    [UITestMethod]
    public void TheMenuShowsNoShortcuts()
    {
        var shown = Menu(TwoTools()).Items.OfType<MenuFlyoutItem>().Where(item => !string.IsNullOrEmpty(item.KeyboardAcceleratorTextOverride));

        Assert.AreEqual("", string.Join(", ", shown.Select(item => item.Text)));
    }

    [UITestMethod]
    public void OpenPitboardIsInBold()
    {
        var menu = Menu(TwoTools());

        Assert.AreEqual(FontWeights.SemiBold.Weight, Item(menu, "Open Pitboard").FontWeight.Weight);
        Assert.AreEqual(FontWeights.Normal.Weight, Item(menu, "Settings").FontWeight.Weight);
    }

    [UITestMethod]
    public void ANoticeShowsItsMark()
    {
        var mark = Item(Menu(TwoTools()), "An interrupted switch is waiting").Icon as FontIcon;

        Assert.AreEqual("\uE7BA", mark?.Glyph);
    }

    [UITestMethod]
    public void RefreshWaitsForTheReadUnderWay()
    {
        Assert.IsFalse(Item(Menu(TwoTools(reading: true)), "Refresh").IsEnabled);
    }

    [UITestMethod]
    public void ChoosingAnItemReportsItsCommand()
    {
        var chosen = new List<IconCommand>();
        var menu = Menu(TwoTools(), chosen);

        new MenuFlyoutItemAutomationPeer(Item(menu, "Add account…")).Invoke();
        Toggle(Item(menu, "personal"));
        new MenuFlyoutItemAutomationPeer(Item(menu, "Exit")).Invoke();

        CollectionAssert.AreEqual(
            new IconCommand[]
            {
                new IconCommand.Send(new Intent.PresentSheet(new Sheet.Add(Provider: null))),
                new IconCommand.Send(new Intent.SwitchTo("claude/personal")),
                new IconCommand.Quit(),
            },
            chosen);
    }

    // Shots 20 and 26 of 16 October 2026: rows were taller in one opening of the menu than in another.
    [UITestMethod]
    public void EachRowIsPaddedAsAMouseOpenedMenu()
    {
        var narrow = (Thickness)Application.Current.Resources["MenuFlyoutItemThemePaddingNarrow"];
        var main = Items.Window("codex", "main");
        var spare = Items.Window("codex", "spare");
        var read = TwoToolsRead();
        var model = new AppModel(read with
        {
            AccountWindows = read.AccountWindows with { Menus = [new SiteMenu.Several("Open chatgpt.com", main.Site, [main, spare])] },
        });

        var rows = Rows(Menu(model).Items).ToArray();

        Assert.IsTrue(rows.OfType<MenuFlyoutSubItem>().Any(), "no submenu to look in");
        foreach (var row in rows)
        {
            Assert.AreEqual(narrow, row.Padding, row is MenuFlyoutItem item ? item.Text : ((MenuFlyoutSubItem)row).Text);
        }

        static IEnumerable<MenuFlyoutItemBase> Rows(IEnumerable<MenuFlyoutItemBase> items) =>
            items.Where(item => item is not MenuFlyoutSeparator)
                .SelectMany(item => item is MenuFlyoutSubItem sub ? Rows(sub.Items).Prepend(sub) : [item]);
    }

    // The account in use has nothing to do, and choosing it again does nothing.
    [UITestMethod]
    public void TheAccountInUseReportsNothing()
    {
        var chosen = new List<IconCommand>();
        var menu = Menu(TwoTools(), chosen);

        Toggle(Item(menu, "work"));

        Assert.IsEmpty(chosen);
        Assert.IsTrue(((RadioMenuFlyoutItem)Item(menu, "work")).IsChecked);
    }
}
