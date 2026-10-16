using Pitboard.Core;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class ActivityViewModelTests
{
    private static Snapshot With(ulong revision, ActivityShown activity)
    {
        var read = Snapshots.Read(revision);
        return read with { Machine = read.Machine with { Activity = activity } };
    }

    private static readonly ActivityShown OneChange = new(
        Lines: [new ActivityLine(0, "15/01/2027 09:00", "Switch", "claude/work", "Done", Done: true, AskedBy: "Command line")],
        Empty: null);

    [TestMethod]
    public void NoActivitySaysWhatWillBeThere()
    {
        var shown = new ActivityViewModel(new AppModel(new StandInModel(Snapshots.Read(1))));
        shown.Listen();

        Assert.IsFalse(shown.Listed);
        Assert.IsTrue(shown.ShowsEmpty);
        Assert.AreEqual("No activity", shown.EmptyTitle);
        Assert.AreEqual("Pitboard lists every change it makes here.", shown.EmptyDetail);
    }

    [TestMethod]
    public void ReadingTheLogIsAskedOfTheModel()
    {
        var model = new StandInModel(Snapshots.Read(1));
        var shown = new ActivityViewModel(new AppModel(model));

        shown.Read();

        Assert.AreEqual(new Intent.PaneShown(Pane.Activity), model.Sent.Single());
    }

    // The machine's other parts change as often; the list is told only when the log changed.
    [TestMethod]
    public void ThePageIsToldOnlyWhenTheLogChanged()
    {
        var app = new AppModel(new StandInModel(With(1, OneChange)));
        var shown = new ActivityViewModel(app);
        shown.Listen();
        var told = 0;
        shown.PropertyChanged += (_, _) => told++;

        var checking = With(2, OneChange);
        app.Apply(checking with { Machine = checking.Machine with { Checks = checking.Machine.Checks with { Checking = true } } });
        Assert.AreEqual(0, told);

        app.Apply(With(3, OneChange with { Lines = [.. OneChange.Lines, OneChange.Lines[0] with { Id = 1 }] }));
        Assert.AreEqual(1, told);
        Assert.HasCount(2, shown.Lines);
        Assert.IsTrue(shown.Listed);
    }
}
