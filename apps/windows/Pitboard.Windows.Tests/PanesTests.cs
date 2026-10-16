using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class PanesTests
{
    private static Snapshot With(Snapshot snapshot, ActivityShown activity) =>
        snapshot with { Machine = snapshot.Machine with { Activity = activity } };

    private static Snapshot With(Snapshot snapshot, ChecksShown checks) =>
        snapshot with { Machine = snapshot.Machine with { Checks = checks } };

    [UITestMethod]
    public async Task NoActivitySaysWhatWillBeThereAndShowingItReadsTheLog()
    {
        var model = new StandInModel(Snapshots.Read(1));
        var pane = await Views.Navigated<ActivityPane>(Views.Context(new AppModel(model)));

        Assert.Contains("No activity", Views.Texts(pane));
        Assert.Contains(new Intent.PaneShown(Pane.Activity), model.Sent);
    }

    [UITestMethod]
    public async Task EachChangeIsARowNewestFirst()
    {
        var activity = new ActivityShown(
            Lines:
            [
                new ActivityLine(0, "15/01/2027 09:00", "Switch", "claude/work", "done", Done: true, AskedBy: "pitboard"),
                new ActivityLine(1, "14/01/2027 18:00", "Enrol", "claude/spare", "refused", Done: false, AskedBy: "Pitboard app"),
            ],
            Empty: null);
        var pane = await Views.Navigated<ActivityPane>(Views.Context(new AppModel(new StandInModel(With(Snapshots.Read(1), activity)))));

        var rows = Views.Visible<ListViewItem>(pane).Select(row => AutomationProperties.GetName(row)).ToArray();

        Assert.AreEqual(
            "15/01/2027 09:00, Switch, claude/work, done, pitboard | 14/01/2027 18:00, Enrol, claude/spare, refused, Pitboard app",
            string.Join(" | ", rows));
        Assert.DoesNotContain("No activity", Views.Texts(pane));
    }

    [UITestMethod]
    public async Task ThisPCSaysEachCheckUnderDoctorsLastLine()
    {
        var checks = new ChecksShown(
            Lines:
            [
                new CheckLine(0, "claude", "Claude Code", Level.Ok, "Passed", "2.1.299", Advice: null),
                new CheckLine(1, "parked_login", "account personal", Level.Warn, "Warning", "lapses in 2 days", "Use it to renew it."),
            ],
            Summary: "One thing is worth looking at.", Checking: false, Checked: "Checked at 09:00", Waiting: null);
        var model = new StandInModel(With(Snapshots.Read(1), checks));
        var pane = await Views.Navigated<MachinePane>(Views.Context(new AppModel(model)));

        var texts = Views.Texts(pane);
        foreach (var said in new[] { "One thing is worth looking at.", "Checked at 09:00", "account personal", "Use it to renew it.", "lapses in 2 days" })
        {
            Assert.Contains(said, texts);
        }

        Assert.IsTrue(Views.Element<AppBarButton>(pane, "machine.check").IsEnabled);
        Assert.Contains(new Intent.PaneShown(Pane.Machine), model.Sent);
    }

    [UITestMethod]
    public async Task CheckAgainIsHeldBackWhileTheChecksRun()
    {
        var checks = new ChecksShown(Lines: [], Summary: "Everything looks right.", Checking: true, Checked: null, Waiting: null);
        var pane = await Views.Navigated<MachinePane>(Views.Context(new AppModel(new StandInModel(With(Snapshots.Read(1), checks)))));

        Assert.IsFalse(Views.Element<AppBarButton>(pane, "machine.check").IsEnabled);
        Assert.IsTrue(Views.Visible<ProgressRing>(pane).Single().IsActive);
    }

    [UITestMethod]
    public async Task BeforeTheFirstCheckThePaneSaysItIsChecking()
    {
        var pane = await Views.Navigated<MachinePane>(Views.Context(new AppModel(new StandInModel(Snapshots.Read(1)))));

        Assert.Contains("Checking this PC…", Views.Texts(pane));
    }
}
