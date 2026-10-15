namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class ReleaseGateTests
{
    [TestMethod]
    public void TheReleaseBuildRunsOnlyOncePitboardForWindowsIsReleased()
    {
        using var launched = Launched.Start(Builds.Release);

        var window = launched.Window("Pitboard");
        if (Builds.ProductMajor() == 0)
        {
            StringAssert.Contains(Launched.Said(window), "Pitboard for Windows is not released yet.");
            Launched.Dismiss(window);
            Assert.IsTrue(launched.Process.WaitForExit(TimeSpan.FromSeconds(10)), "Pitboard went on running");
            Assert.AreEqual(1, launched.Process.ExitCode);
            Assert.AreEqual("", string.Join(", ", launched.Written()));
        }
        else
        {
            Assert.IsNotNull(window.FindFirstDescendant(found => found.ByAutomationId("navigation")));
        }
    }
}
