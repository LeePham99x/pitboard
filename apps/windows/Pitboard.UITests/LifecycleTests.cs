using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;

namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed partial class LifecycleTests
{
    public TestContext TestContext { get; set; } = null!;

    // CF1 and CF5, as the fresh standard user: the window comes to the front and takes a click.
    [TestMethod]
    public void TheFirstLaunchsWindowComesToTheFrontAndTakesAClick()
    {
        var clock = Stopwatch.StartNew();
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");

        var window = launched.Window("Pitboard");
        TestContext.WriteLine($"CF5: the window was up {clock.ElapsedMilliseconds} ms after the start");
        var activity = window.FindFirstDescendant(found => found.ByAutomationId("pane.activity"))!;
        Assert.AreEqual(
            window.Properties.NativeWindowHandle.Value, GetForegroundWindow(), "CF1: the window is not in front");
        Mouse.Click(activity.GetClickablePoint());

        var selected = Retry.WhileFalse(
            () => activity.Patterns.SelectionItem.Pattern.IsSelected.Value, TimeSpan.FromSeconds(5)).Result;
        Assert.IsTrue(selected, "CF1: the click did not reach the window");
    }

    // CF2: UI Automation finds a WinUI element by its AutomationId and invokes it.
    [TestMethod]
    public void AWinUIElementIsFoundAndInvokedByItsAutomationId()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");

        var machine = launched.Window("Pitboard").FindFirstDescendant(found => found.ByAutomationId("pane.machine"));

        Assert.IsNotNull(machine, "CF2: no element pane.machine");
        machine.Patterns.SelectionItem.Pattern.Select();
        Assert.IsTrue(machine.Patterns.SelectionItem.Pattern.IsSelected.Value, "CF2: pane.machine was not selected");
    }

    [TestMethod]
    public void ClosingTheWindowLeavesPitboardRunning()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");
        var window = launched.Window("Pitboard");

        window.Close();

        Assert.IsTrue(Retry.WhileTrue(() => window.IsAvailable, TimeSpan.FromSeconds(10)).Result, "the window stayed open");
        Assert.IsFalse(launched.Process.WaitForExit(TimeSpan.FromSeconds(3)), "Pitboard quit with its window");
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
