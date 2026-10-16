namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class AccountPickerTests
{
    // Outside a fixture a Debug build runs on whatever homes it was given, so it opens no link there.
    [TestMethod]
    public void ADebugBuildOutsideAFixtureRefusesALinkAndWritesNothing()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: null, Launched.Link("https://claude.ai/new"));

        var box = launched.Window("Pitboard");

        StringAssert.Contains(Launched.Said(box), "only in a fixture");
        Launched.Dismiss(box);
        Assert.IsTrue(launched.Process.WaitForExit(TimeSpan.FromSeconds(10)), "Pitboard went on running");
        Assert.AreEqual(1, launched.Process.ExitCode);
        Assert.AreEqual("", string.Join(", ", launched.Written()));
    }
}
