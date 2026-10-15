using Microsoft.Win32;

namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Include)]
public sealed class ElevatedTests
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static readonly string[] Keys =
        [@"Software\Classes\pitboard-debug", @"Software\Classes\AppUserModelId\Pitboard.Pitboard.Debug"];

    // Before the fixture too, whose folder it would empty and make again.
    [TestMethod]
    public void AnElevatedLaunchSaysWhyAndWritesNothing()
    {
        var fixture = Path.Combine(Path.GetTempPath(), "pitboard-fixture");
        var fixtureWasThere = Directory.Exists(fixture);
        // The leak check read the job user's HKCU before this step.
        var registeredBefore = Registered().ToArray();
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");

        var box = launched.Window("Pitboard");

        StringAssert.Contains(Launched.Said(box), "runs elevated");
        Launched.Dismiss(box);
        Assert.IsTrue(launched.Process.WaitForExit(TimeSpan.FromSeconds(10)), "Pitboard went on running");
        Assert.AreEqual(1, launched.Process.ExitCode);
        Assert.AreEqual("", string.Join(", ", launched.Written()));
        Assert.AreEqual(fixtureWasThere, Directory.Exists(fixture));
        Assert.AreEqual("", string.Join(", ", Registered().Except(registeredBefore)));
    }

    private static IEnumerable<string> Registered()
    {
        foreach (var name in Keys)
        {
            using var key = Registry.CurrentUser.OpenSubKey(name);
            if (key is not null)
            {
                yield return name;
            }
        }

        using var run = Registry.CurrentUser.OpenSubKey(Run);
        if (run?.GetValue("Pitboard") is not null)
        {
            yield return $@"{Run}\Pitboard";
        }
    }
}
