using System.Diagnostics;
using Windows.Win32.Foundation;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class WindowsAppsTests
{
    private const string Family = "Vendor.App_0000000000000";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    // A Store app's own process starts helpers that may have windows, and console children that have none.
    [TestMethod]
    public void RunningGivesTheAumidOfThePackagesWindowedProcessWhichReopenTakes()
    {
        var did = new List<string>();
        AppProcess[] listed =
        [
            new(Id: 10, Parent: 4, Family: null, Aumid: null, OwnsAWindow: true),
            new(Id: 21, Parent: 20, Family: Family, Aumid: null, OwnsAWindow: false),
            new(Id: 22, Parent: 20, Family: Family.ToUpperInvariant(), Aumid: $"{Family}!Helper", OwnsAWindow: true),
            new(Id: 20, Parent: 4, Family: Family.ToUpperInvariant(), Aumid: $"{Family}!Main", OwnsAWindow: true),
        ];
        var apps = new WindowsApps(() => listed, aumid => did.Add($"activate {aumid}"), () => did.Add("Pitboard in front"));

        var location = apps.Running($"{Family}!App");
        apps.Reopen(location!, front: true);
        apps.Reopen(location!, front: false);

        Assert.AreEqual($"{Family}!Main", location);
        CollectionAssert.AreEqual(
            new[] { $"activate {Family}!Main", $"activate {Family}!Main", "Pitboard in front" }, did);
        Assert.IsNull(apps.Running("Other.App_1111111111111!App"));
    }

    // Its codex can outlive it, and the model waits for that through the process list.
    [TestMethod]
    public void AnAppWithOnlyWindowlessProcessesLeftIsNotRunning()
    {
        AppProcess[] listed = [new(Id: 21, Parent: 20, Family: Family, Aumid: null, OwnsAWindow: false)];

        Assert.IsNull(new WindowsApps(() => listed, _ => { }, () => { }).Running($"{Family}!App"));
    }

    [TestMethod]
    public void AnAppWhoseOwnIdCannotBeReadRunsAsTheCoreNamesIt()
    {
        AppProcess[] listed = [new(Id: 20, Parent: 4, Family: Family, Aumid: null, OwnsAWindow: true)];

        Assert.AreEqual($"{Family}!App", new WindowsApps(() => listed, _ => { }, () => { }).Running($"{Family}!App"));
    }

    // CF9, as the fresh standard user. Never forced: an app that agrees ends, and its console child, never registered, runs on.
    [TestMethod]
    public async Task RestartManagerEndsAnAppThatHonoursItAndLeavesItsConsoleChild()
    {
        using var standIn = StandInApp.Start(honours: true);
        var apps = standIn.Apps();
        Assert.AreEqual(StandInApp.Id, apps.Running(StandInApp.Id));

        var timer = Stopwatch.StartNew();
        var shut = await apps.Quit(StandInApp.Id).WaitAsync(Patience);
        var took = timer.ElapsedMilliseconds;
        var ended = standIn.App.WaitForExit(Patience);
        Say(
            $"CF9: RmShutdown gave {shut} after {took} ms; the stand-in that honours ENDSESSION_CLOSEAPP "
            + $"{(ended ? $"exited {standIn.App.ExitCode}" : "runs on")}, its console child {(standIn.Child.HasExited ? "ended" : "runs on")}");

        Assert.IsTrue(ended, $"the stand-in runs on after RmShutdown gave {shut}");
        Assert.AreEqual(WIN32_ERROR.ERROR_SUCCESS, shut);
        Assert.IsNull(apps.Running(StandInApp.Id));
        Assert.IsFalse(standIn.Child.HasExited, "its console child was ended");
    }

    [TestMethod]
    public async Task RestartManagerLeavesAnAppThatRefusesRunning()
    {
        using var standIn = StandInApp.Start(honours: false);
        var apps = standIn.Apps();

        var timer = Stopwatch.StartNew();
        var shut = await apps.Quit(StandInApp.Id).WaitAsync(Patience);
        Say(
            $"CF9: RmShutdown gave {shut} after {timer.ElapsedMilliseconds} ms; the stand-in that refuses "
            + $"{(standIn.App.HasExited ? "ended" : "runs on")}, its console child {(standIn.Child.HasExited ? "ended" : "runs on")}");

        Assert.IsFalse(standIn.App.HasExited, $"the stand-in ended, and RmShutdown gave {shut}");
        Assert.AreEqual(WIN32_ERROR.ERROR_FAIL_SHUTDOWN, shut);
        Assert.AreEqual(StandInApp.Id, apps.Running(StandInApp.Id));
        Assert.IsFalse(standIn.Child.HasExited, "its console child was ended");
    }

    [TestMethod]
    public void TheProcessListSaysThisUnpackagedProcessOwnsAWindow()
    {
        var own = PackagedApps.Listed().Single(listed => listed.Id == (uint)Environment.ProcessId);

        Assert.IsNull(own.Family);
        Assert.IsNull(own.Aumid);
        Assert.IsTrue(own.OwnsAWindow);
    }

    // Past MSTest, which keeps a test method's Console output in its TRX alone, so the job's log has it.
    private static void Say(string fact)
    {
        using var output = new StreamWriter(Console.OpenStandardOutput());
        output.WriteLine(fact);
    }
}
