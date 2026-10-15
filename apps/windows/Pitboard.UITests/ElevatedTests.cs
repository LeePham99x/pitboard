namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Include)]
public sealed class ElevatedTests
{
    // Before the fixture too, whose folder it would empty and make again.
    [TestMethod]
    public void AnElevatedLaunchSaysWhyAndWritesNothing()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "pitboard-fixture");
        var fixtureWasThere = Directory.Exists(fixture);
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");

        var box = launched.Window("Pitboard");

        StringAssert.Contains(Launched.Said(box), "runs elevated");
        Launched.Dismiss(box);
        Assert.IsTrue(launched.Process.WaitForExit(TimeSpan.FromSeconds(10)), "Pitboard went on running");
        Assert.AreEqual(1, launched.Process.ExitCode);
        Assert.AreEqual("", string.Join(", ", launched.Written()));
        Assert.AreEqual(fixtureWasThere, Directory.Exists(fixture));
    }
}
