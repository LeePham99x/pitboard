namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class PanesTests
{
    // Newest first: the fixture's two switches are newer than both its enrolments.
    [TestMethod]
    public void ActivityListsChanges()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        launched.OpenWindow();
        launched.Select("pane.activity");

        var switched = launched.Named("Switch");
        var enrolled = launched.Named("Enrol");

        Assert.IsLessThan(enrolled.BoundingRectangle.Top, switched.BoundingRectangle.Top, "newest first");
    }

    // Worth looking at in this fixture: personal's parked login, which lapses soon, and the status
    // line, since nothing on the fixture's PATH is found.
    [TestMethod]
    public void ThisPCShowsTheChecks()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        launched.OpenWindow();
        launched.Select("pane.machine");

        launched.Named("account personal");
        launched.Named("status line");
        launched.Named("2 things are worth looking at.");
    }
}
