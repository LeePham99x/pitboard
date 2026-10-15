using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;

namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed partial class LifecycleTests
{
    private const uint GetIcon = 0x007F;
    private const nuint BigIcon = 1;

    public TestContext TestContext { get; set; } = null!;

    // CF1 and CF5. Windows may keep the foreground for a window that held it before the launch (SetForegroundWindow).
    [TestMethod]
    public void TheFirstLaunchWindowComesToTheFrontAndTakesAClick()
    {
        var before = GetForegroundWindow();
        var clock = Stopwatch.StartNew();
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");

        var window = launched.Window("Pitboard");
        TestContext.WriteLine($"CF5: the window was up {clock.ElapsedMilliseconds} ms after the start");
        var activity = window.FindFirstDescendant(found => found.ByAutomationId("pane.activity"))!;
        var handle = window.Properties.NativeWindowHandle.Value;
        var inFront = Retry.WhileFalse(() => GetForegroundWindow() == handle, TimeSpan.FromSeconds(5)).Result;
        if (!inFront && before != 0 && GetForegroundWindow() == before)
        {
            Assert.Inconclusive($"CF1: {InFront()} kept the foreground it held before the launch, over {window.BoundingRectangle}");
        }

        Assert.IsTrue(inFront, $"CF1: the window is not in front, {InFront()} is");
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

    // Its title bar, Alt+Tab and a Debug build's taskbar button show it; WinUI's window class has none.
    [TestMethod]
    public void TheWindowCarriesPitboardsIcon()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");

        var handle = launched.Window("Pitboard").Properties.NativeWindowHandle.Value;

        Assert.AreNotEqual(0, SendMessage(handle, GetIcon, BigIcon, 0), "the window has no icon of its own");
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

    [TestMethod]
    public void ASecondLaunchOpensTheWindowAgain()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");
        var window = launched.Window("Pitboard");
        window.Close();
        Assert.IsTrue(Retry.WhileTrue(() => window.IsAvailable, TimeSpan.FromSeconds(10)).Result, "the window stayed open");

        launched.OpenWindow();
    }

    // As a double click on Pitboard's shortcut can.
    [TestMethod]
    public void TwoLaunchesAtOnceOpenOneWindow()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "noClaudeCode");
        using var again = launched.Again();

        var either = Retry.WhileFalse(
            () => launched.Process.HasExited || again.Process.HasExited, TimeSpan.FromSeconds(60)).Result;
        Assert.IsTrue(either, "neither copy handed its launch to the other");
        var (gone, stays) = launched.Process.HasExited ? (launched, again) : (again, launched);
        Assert.IsTrue(gone.HandedOver(out var why), why);
        stays.Window("Pitboard");
        Assert.HasCount(1, stays.Windows("Pitboard"));
    }

    // noClaudeCode is not a first launch, and Windows puts a new icon in the overflow.
    [TestMethod]
    public void ALaunchAsFromStartOpensTheWindow()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "noClaudeCode");

        launched.Window("Pitboard");
    }

    [TestMethod]
    public void AStartAtSignInOpensNoWindow()
    {
        using var launched = Launched.Start(Builds.Debug, "noClaudeCode", "--sign-in");

        launched.Running();
        Assert.IsFalse(
            Retry.WhileFalse(() => launched.Windows("Pitboard").Length > 0, TimeSpan.FromSeconds(10)).Result,
            $"a start at sign-in opened the window: {launched.State()}");
    }

    private static unsafe string InFront()
    {
        var window = GetForegroundWindow();
        if (window == 0)
        {
            return "no window";
        }

        var name = stackalloc char[256];
        var length = GetClassName(window, name, 256);
        var title = stackalloc char[256];
        var titled = GetWindowText(window, title, 256);
        _ = GetWindowRect(window, out var place);
        _ = GetWindowThreadProcessId(window, out var owner);
        string process;
        try
        {
            process = Process.GetProcessById((int)owner).ProcessName;
        }
        catch (ArgumentException)
        {
            process = $"process {owner}";
        }

        return $"{new string(name, 0, length)} '{new string(title, 0, titled)}' of {process} at {place.Left},{place.Top} to {place.Right},{place.Bottom}";
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(nint window, char* name, int capacity);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowText(nint window, char* text, int capacity);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect place);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
