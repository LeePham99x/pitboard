namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class PanesTests
{
    // Newest first: the fixture's two switches are newer than both its enrolments.
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void ActivityListsChanges()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        launched.OpenWindow();
        launched.Select("pane.activity");

        var switched = launched.Named("Switch");
        var enrolled = launched.Named("Enrol");

        Assert.IsLessThan(enrolled.BoundingRectangle.Top, switched.BoundingRectangle.Top, "newest first");
    }

    // The one thing worth looking at in this fixture is personal's parked login, which lapses soon.
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void ThisPCShowsTheChecks()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        launched.OpenWindow();
        launched.Select("pane.machine");

        launched.Named("account personal");
        launched.Named("One thing is worth looking at.");
    }
}
