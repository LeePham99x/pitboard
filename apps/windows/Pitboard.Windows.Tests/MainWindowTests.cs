using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;
using Windows.Foundation;
using Windows.Graphics;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class MainWindowTests
{
    private static readonly Pane?[] Panes = [Pane.Accounts, Pane.Activity, Pane.Machine, null];

    // A window over its own placement file, which goes with it.
    internal static MainWindow Made(Snapshot snapshot)
    {
        var placement = Path.Combine(Path.GetTempPath(), $"pitboard-placement-{Guid.NewGuid()}.json");
        var window = new MainWindow(
            new AppModel(new StandInModel(snapshot)), new StartAtSignIn(new FixtureStartupItem()), new NoUpdates(), "0.0.0",
            _ => { }, new WindowPlacement(placement));
        window.Closed += (_, _) => File.Delete(placement);
        return window;
    }

    // Something on every page: accounts, a change and a check.
    private static Snapshot Filled()
    {
        var read = Snapshots.Read(1);
        return read with
        {
            Sections = [Items.Section("claude", null, Items.Account("claude", "work", inUse: true), Items.Account("claude", "spare"))],
            Machine = read.Machine with
            {
                Activity = new ActivityShown(
                    Lines: [new ActivityLine(0, "15/01/2027 09:00", "Switch", "claude/work", "done", Done: true, AskedBy: "pitboard")],
                    Empty: null),
                Checks = new ChecksShown(
                    Lines: [new CheckLine(0, "claude", "Claude Code", Level.Ok, "Passed", "2.1.299", Advice: null)],
                    Summary: "Everything looks right.", Checking: false, Checked: "Checked at 09:00", Waiting: null),
            },
        };
    }

    private static Frame Pages(MainWindow window) => Views.Descendants(window.Content).OfType<Frame>().Single();

    private static NavigationView Navigation(MainWindow window) => Views.Element<NavigationView>(window.Content, "navigation");

    private static async Task<Page> Showing(MainWindow window, Pane? pane)
    {
        window.Select(pane);
        var frame = Pages(window);
        Page? page = null;
        await Views.Until(
            () => (page = frame.Content as Page) is { IsLoaded: true } && window.Selected == pane, $"the {pane?.ToString() ?? "settings"} page did not show");
        page!.UpdateLayout();
        return page;
    }

    private static TextBlock Title(Page page) =>
        Views.Visible<TextBlock>(page).Single(text => AutomationProperties.GetHeadingLevel(text) == AutomationHeadingLevel.Level1);

    // A list's first row, or what the page's scroll viewer holds.
    private static FrameworkElement Content(Page page) =>
        Views.Visible<ListViewItem>(page).FirstOrDefault() as FrameworkElement
        ?? (FrameworkElement)Views.Descendants(page).OfType<ScrollViewer>().First().Content;

    private static FrameworkElement ScrollHost(Page page) =>
        Views.Visible<ListView>(page).FirstOrDefault() as FrameworkElement ?? Views.Descendants(page).OfType<ScrollViewer>().First();

    private static Rect Bounds(FrameworkElement element, UIElement within) =>
        element.TransformToVisual(within).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    // As the WinUI Gallery's main window keeps.
    [UITestMethod]
    public async Task TheWindowIsNeverSmallerThan640By500()
    {
        var window = await Views.Activated(Made(Snapshots.Read(1)));
        try
        {
            var scale = window.Content.XamlRoot.RasterizationScale;
            var presenter = (OverlappedPresenter)window.AppWindow.Presenter;

            Assert.AreEqual((int)Math.Ceiling(640 * scale), presenter.PreferredMinimumWidth);
            Assert.AreEqual((int)Math.Ceiling(500 * scale), presenter.PreferredMinimumHeight);
        }
        finally
        {
            window.Close();
        }
    }

    // NavigationView's header sat 40 epx right of its pane's content (shots 01, 06 and 34). Its guidance gives content
    // 24 epx sides, 12 in Minimal.
    [UITestMethod]
    public async Task EachPagesTitleLinesUpWithItsContent()
    {
        var window = await Views.Activated(Made(Filled()));
        try
        {
            // Narrower than the settings' 1064 epx column in every mode, so nothing is centred.
            var scale = window.Content.XamlRoot.RasterizationScale;
            window.AppWindow.Resize(new SizeInt32((int)(900 * scale), (int)(700 * scale)));
            var navigation = Navigation(window);
            foreach (var (mode, shown, side) in new[]
            {
                (NavigationViewPaneDisplayMode.Left, NavigationViewDisplayMode.Expanded, 24.0),
                (NavigationViewPaneDisplayMode.LeftCompact, NavigationViewDisplayMode.Compact, 24.0),
                (NavigationViewPaneDisplayMode.LeftMinimal, NavigationViewDisplayMode.Minimal, 12.0),
            })
            {
                navigation.PaneDisplayMode = mode;
                await Views.Until(() => navigation.DisplayMode == shown, $"the pane did not go {shown}");
                foreach (var pane in Panes)
                {
                    var page = await Showing(window, pane);
                    var frame = Pages(window);
                    double Left(FrameworkElement element) => Bounds(element, frame).Left;
                    var title = Title(page);
                    var content = Content(page);

                    await Views.Until(
                        () => Math.Abs(Left(title) - side) < 0.5 && Math.Abs(Left(content) - side) < 0.5,
                        () => $"{page.GetType().Name} in {mode}: the title at {Left(title)} and its content at {Left(content)}, not {side}");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task EachPagesTitleIsALevel1Heading()
    {
        var window = await Views.Activated(Made(Filled()));
        try
        {
            var titles = new List<string>();
            foreach (var pane in Panes)
            {
                titles.Add(Title(await Showing(window, pane)).Text);
            }

            Assert.AreEqual("Accounts, Activity, This PC, Settings", string.Join(", ", titles));
        }
        finally
        {
            window.Close();
        }
    }

    // Scrolled content went under a title that stayed put, cut off with no room between (shots 05, 08 and 11).
    [UITestMethod]
    public async Task ThePageScrollsBelowItsTitle()
    {
        var window = await Views.Activated(Made(Filled()));
        try
        {
            foreach (var pane in Panes)
            {
                var page = await Showing(window, pane);
                var title = Bounds(Title(page), page);
                var host = Bounds(ScrollHost(page), page);

                Assert.IsGreaterThanOrEqualTo(title.Bottom + 24, host.Top, $"{page.GetType().Name}: the title ends at {title.Bottom}, its scrolling at {host.Top}");
            }
        }
        finally
        {
            window.Close();
        }
    }

    // As NavigationView's guidance navigates: only to a page not shown already, with no back stack, and each page kept.
    [UITestMethod]
    public async Task ChangingPagesNavigatesTheFrameOnce()
    {
        var window = await Views.Activated(Made(Filled()));
        try
        {
            var frame = Pages(window);
            var accounts = frame.Content;
            var navigated = new List<string>();
            void Navigated(object sender, NavigationEventArgs args) => navigated.Add(args.SourcePageType.Name);
            frame.Navigated += Navigated;

            await Showing(window, Pane.Activity);
            await Showing(window, Pane.Activity);
            await Showing(window, null);
            await Showing(window, Pane.Accounts);
            frame.Navigated -= Navigated;

            Assert.AreEqual("ActivityPage, SettingsPane, AccountsPage", string.Join(", ", navigated));
            Assert.AreSame(accounts, frame.Content, "the accounts page was made again");
            Assert.IsEmpty(frame.BackStack);
        }
        finally
        {
            window.Close();
        }
    }
}
