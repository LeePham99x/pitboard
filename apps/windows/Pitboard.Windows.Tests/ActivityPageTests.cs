using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;
using Windows.Foundation;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class ActivityPageTests
{
    private static readonly ActivityShown TwoChanges = new(
        Lines:
        [
            new ActivityLine(0, "15/01/2027 09:00", "Switch", "claude/work", "done", Done: true, AskedBy: "pitboard"),
            new ActivityLine(1, "14/01/2027 18:00", "Enrol", "claude/spare", "refused", Done: false, AskedBy: "Pitboard app"),
        ],
        Empty: null);

    private static Snapshot With(ActivityShown activity)
    {
        var read = Snapshots.Read(1);
        return read with { Machine = read.Machine with { Activity = activity } };
    }

    // A page of a given width, as a window of that width shows it.
    private static async Task<ActivityPage> Shown(Snapshot snapshot, double width = double.NaN)
    {
        var frame = await Views.Shown(new Frame { IsNavigationStackEnabled = false, Width = width });
        return await Views.Navigate<ActivityPage>(frame, Views.Context(new AppModel(new StandInModel(snapshot))));
    }

    private static Rect Bounds(FrameworkElement element, UIElement within) =>
        element.TransformToVisual(within).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static TextBlock Cell(ListViewItem row, string text) =>
        Views.Visible<TextBlock>(row).Single(cell => cell.Text == text);

    [UITestMethod]
    public async Task NoActivitySaysWhatWillBeThereAndShowingItReadsTheLog()
    {
        var model = new StandInModel(Snapshots.Read(1));
        var page = await Views.Navigated<ActivityPage>(Views.Context(new AppModel(model)));

        Assert.Contains("No activity", Views.Texts(page));
        Assert.Contains(new Intent.PaneShown(Pane.Activity), model.Sent);
    }

    [UITestMethod]
    public async Task EachChangeIsARowNewestFirst()
    {
        var page = await Shown(With(TwoChanges));

        var rows = Views.Visible<ListViewItem>(page).Select(row => AutomationProperties.GetName(row)).ToArray();

        Assert.AreEqual(
            "15/01/2027 09:00, Switch, claude/work, done, pitboard | 14/01/2027 18:00, Enrol, claude/spare, refused, Pitboard app",
            string.Join(" | ", rows));
        Assert.DoesNotContain("No activity", Views.Texts(page));
    }

    // The headers sat in a Grid of their own over the list, outside what scrolls and what Narrator reads as the table.
    [UITestMethod]
    public async Task TheColumnHeadersAreTheListsHeader()
    {
        var page = await Shown(With(TwoChanges), width: 1000);
        var list = Views.Element<ListView>(page, "activity");
        var header = (FrameworkElement)list.Header;

        Assert.AreEqual(
            "Date, Change, Account, Result, Asked by",
            string.Join(", ", Views.Visible<TextBlock>(header).Select(text => text.Text)));
        var first = Views.Visible<ListViewItem>(page).First();
        foreach (var (title, cell) in new[] { ("Date", "15/01/2027 09:00"), ("Account", "claude/work"), ("Asked by", "pitboard") })
        {
            var column = Views.Visible<TextBlock>(header).Single(text => text.Text == title);
            Assert.AreEqual(Bounds(column, page).Left, Bounds(Cell(first, cell), page).Left, 0.5, $"{title} is not over its column");
        }
    }

    [UITestMethod]
    public async Task ANarrowPageStacksEachChangeUnderItsDate()
    {
        var page = await Shown(With(TwoChanges), width: 600);
        var list = Views.Element<ListView>(page, "activity");
        ListViewItem? first = null;

        await Views.Until(
            () => (first = Views.Visible<ListViewItem>(page).FirstOrDefault()) is not null
                && Bounds(Cell(first, "15/01/2027 09:00"), page).Bottom <= Bounds(Cell(first, "Switch"), page).Top,
            "the change is not under its date");
        Assert.IsLessThan(Bounds(Cell(first!, "claude/work"), page).Left, Bounds(Cell(first!, "Switch"), page).Right, "the account is not beside its change");
        Assert.IsLessThanOrEqualTo(Bounds(Cell(first!, "done"), page).Top, Bounds(Cell(first!, "Switch"), page).Bottom, "the result is not under the change");
        Assert.AreEqual(Visibility.Collapsed, ((FrameworkElement)list.Header).Visibility, "column headers over stacked lines");
        Assert.AreEqual(
            "15/01/2027 09:00, Switch, claude/work, done, pitboard",
            AutomationProperties.GetName(first!),
            "Narrator reads a stacked change as a row");
    }

    // A change that went as asked is said quietly, and one that did not stands out.
    [UITestMethod]
    public async Task AChangeThatDidNotGoAsAskedStandsOut()
    {
        var page = await Shown(With(TwoChanges), width: 1000);
        var rows = Views.Visible<ListViewItem>(page).ToArray();
        var secondary = (Style)Application.Current.Resources["SecondaryTextBlockStyle"];

        Assert.AreSame(secondary, Cell(rows[0], "done").Style);
        Assert.AreNotSame(secondary, Cell(rows[1], "refused").Style);
    }
}
