using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace Pitboard.UITests;

// Explorer's notification area as UI Automation shows it: the taskbar's own icons, then on Windows 11 the
// overflow its chevron opens, where Windows puts a new icon.
internal static partial class NotificationArea
{
    private static readonly string[] Overflows = ["TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow"];

    private static readonly TimeSpan Shortly = TimeSpan.FromSeconds(10);

    public static AutomationElement? Taskbar(UIA3Automation automation) =>
        automation.GetDesktop().FindFirstChild(found => found.ByClassName("Shell_TrayWnd"));

    // Pitboard's tooltip, and so its name, starts with "Pitboard".
    public static AutomationElement? Icon(UIA3Automation automation) =>
        Retry.WhileNull(() => Look(automation), Shortly, TimeSpan.FromMilliseconds(500), ignoreException: true).Result;

    // Where the icon was found, for the run's log.
    public static string Where(AutomationElement icon) =>
        icon.Parent?.ClassName is { Length: > 0 } parent ? parent : icon.ClassName;

    private static AutomationElement? Look(UIA3Automation automation)
    {
        if (Taskbar(automation) is not { } taskbar)
        {
            return null;
        }

        if (Pitboards(taskbar) is { } shown)
        {
            return shown;
        }

        var overflow = Opened(automation);
        if (overflow is null)
        {
            var chevron = taskbar.FindAllDescendants(found => found.ByControlType(ControlType.Button))
                .FirstOrDefault(button => button.Name.Contains("hidden icons", StringComparison.OrdinalIgnoreCase));
            chevron?.Click();
            overflow = Retry.WhileNull(() => Opened(automation), TimeSpan.FromSeconds(2)).Result;
        }

        return overflow is null ? null : Pitboards(overflow);
    }

    private static AutomationElement? Opened(UIA3Automation automation) =>
        automation.GetDesktop().FindAllChildren().FirstOrDefault(window => Overflows.Contains(window.ClassName));

    private static AutomationElement? Pitboards(AutomationElement scope) =>
        scope.FindAllDescendants(found => found.ByControlType(ControlType.Button))
            .FirstOrDefault(button => button.Name.StartsWith("Pitboard", StringComparison.Ordinal));

    // Asks Pitboard to end as taskkill without /f does, through its hidden window, so it takes its icon with it.
    public static void AskToEnd(Process process)
    {
        for (var window = FindWindowEx(0, 0, "Pitboard.HiddenWindow", null); window != 0;
             window = FindWindowEx(0, window, "Pitboard.HiddenWindow", null))
        {
            if (GetWindowThreadProcessId(window, out var owner) != 0 && owner == (uint)process.Id)
            {
                PostMessage(window, 0x0010, 0, 0);
            }
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint parent, nint after, string className, string? windowName);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
