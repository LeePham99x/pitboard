using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;

namespace Pitboard.UITests;

// Pictures for the docs, as the macOS ScreenshotTests takes them. CI keeps windows-2025's as windows-app-pictures.
[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed partial class ScreenshotTests
{
    private const uint ExtendedFrameBounds = 9;

    // A window and a menu animate as they open, and UI Automation gives the bounds they end at.
    private static readonly TimeSpan Settling = TimeSpan.FromSeconds(1);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void TheMenuAndTheWindowForTheDocs()
    {
        var pictures = Directory.CreateTempSubdirectory("pitboard-pictures-");
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");
        var window = launched.OpenWindow();
        // A picture is of the screen, so it shows whatever is in front.
        if (!Foreground.Takes(window))
        {
            Assert.Inconclusive($"CF1: {Foreground.Holder()} keeps the foreground, so a picture would show it");
        }

        window.Move(0, 0);
        launched.Row("codex/spare");
        Launched.Shows(window, "5-hour limit, 42", exact: false);
        Thread.Sleep(Settling);
        Keep(Capture.Rectangle(Frame(window)), pictures, "window");

        // The menu opens over the window, as the macOS picture shows it.
        launched.Select("pane.machine");
        var frame = Frame(window);
        HiddenWindows.OpenMenu(launched.Process, frame.Left + 40, frame.Top + 60);
        var main = launched.Find(found => found.ByName("main · Pro").And(found.ByControlType(ControlType.MenuItem)), "the menu's main");
        Thread.Sleep(Settling);
        Keep(Capture.Element(Around(main, ControlType.Menu)), pictures, "menu");
    }

    private void Keep(CaptureImage picture, DirectoryInfo pictures, string name)
    {
        using (picture)
        {
            var file = Path.Combine(pictures.FullName, $"{name}.png");
            picture.ToFile(file);
            TestContext.AddResultFile(file);
        }
    }

    private static AutomationElement Around(AutomationElement element, ControlType kind)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Properties.ControlType.ValueOrDefault == kind)
            {
                return parent;
            }
        }

        throw new AssertFailedException($"no {kind} around '{element.Name}'");
    }

    // GetWindowRect takes in the window's drop shadow; the frame DWM draws leaves it out.
    private static Rectangle Frame(Window window)
    {
        var handle = window.Properties.NativeWindowHandle.Value;
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(handle, ExtendedFrameBounds, out var frame, (uint)Marshal.SizeOf<Bounds>()));
        return Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, out Bounds value, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Bounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
