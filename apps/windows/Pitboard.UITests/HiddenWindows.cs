using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pitboard.UITests;

// The window each Pitboard makes as it starts, before it can show one, and which its icon's clicks reach.
internal static partial class HiddenWindows
{
    private const uint Close = 0x0010;

    // NotifyIcon's callback message, WM_APP + 1, its icon's id and WM_CONTEXTMENU.
    private const uint IconCallback = 0x8001;
    private const nint IconId = 1;
    private const nint ContextMenu = 0x007B;

    public static IEnumerable<nint> Of(Process process)
    {
        for (var window = FindWindowEx(0, 0, "Pitboard.HiddenWindow", null); window != 0;
             window = FindWindowEx(0, window, "Pitboard.HiddenWindow", null))
        {
            if (GetWindowThreadProcessId(window, out var owner) != 0 && owner == (uint)process.Id)
            {
                yield return window;
            }
        }
    }

    // As taskkill without /f asks, so Pitboard takes its icon with it.
    public static void AskToEnd(Process process)
    {
        foreach (var window in Of(process))
        {
            PostMessage(window, Close, 0, 0);
        }
    }

    // As Explorer tells Pitboard of a right click on its icon at a point, under NOTIFYICON_VERSION_4. The job user's
    // Explorer takes no icon of the test user's (CF3), so no click on it reaches Pitboard.
    public static void OpenMenu(Process process, int x, int y)
    {
        var at = ((nuint)(ushort)y << 16) | (ushort)x;
        foreach (var window in Of(process))
        {
            PostMessage(window, IconCallback, at, ContextMenu | (IconId << 16));
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
