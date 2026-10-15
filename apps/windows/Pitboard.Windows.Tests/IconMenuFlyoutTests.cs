using Microsoft.UI.Text;
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

    private static AppModel TwoTools(bool reading = false) => new(Snapshots.Read(1) with
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
    });

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
            + "| Add Account… | Refresh | - | Open Pitboard | Settings | - | About Pitboard | Pitboard Help | Exit",
            Said(menu));
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

    [UITestMethod]
    public void TheMenuSaysTheWindowsShortcuts()
    {
        var menu = Menu(TwoTools());

        Assert.AreEqual("Ctrl+N", Item(menu, "Add Account…").KeyboardAcceleratorTextOverride);
        Assert.AreEqual("Ctrl+R", Item(menu, "Refresh").KeyboardAcceleratorTextOverride);
        Assert.AreEqual("Ctrl+,", Item(menu, "Settings").KeyboardAcceleratorTextOverride);
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

        Assert.AreEqual("", mark?.Glyph);
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

        new MenuFlyoutItemAutomationPeer(Item(menu, "Add Account…")).Invoke();
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
