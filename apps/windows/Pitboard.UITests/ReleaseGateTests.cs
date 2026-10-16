namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class ReleaseGateTests
{
    [TestMethod]
    public void TheReleaseBuildRefusesUntilPitboardForWindowsIsReleased()
    {
        if (Builds.ProductMajor() > 0)
        {
            Assert.Inconclusive(
                "the opened Release build registers with Windows as it starts; Pitboard.Core.Tests' " +
                "AWindowsModelReadsNothingUntilPitboardForWindowsIsReleased holds the opened gate");
        }

        using var launched = Launched.Start(Builds.Release);

        var window = launched.Window("Pitboard");
        StringAssert.Contains(Launched.Said(window), "Pitboard for Windows is not released yet.");
        Launched.Dismiss(window);
        Assert.IsTrue(launched.Process.WaitForExit(TimeSpan.FromSeconds(10)), "Pitboard went on running");
        Assert.AreEqual(1, launched.Process.ExitCode);
        Assert.AreEqual("", string.Join(", ", launched.Written()));
    }
}
