using System.Text.Json;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pitboard.Windows;

// A window's restored bounds, in the workspace coordinates GetWindowPlacement gives, and whether it was maximized.
public sealed record Placement(int Left, int Top, int Right, int Bottom, bool Maximized);

// Where the main window was closed, in a file of the app's data folder, so it opens there again.
public sealed class WindowPlacement(string file)
{
    public Placement? Saved()
    {
        try
        {
            return JsonSerializer.Deserialize<Placement>(File.ReadAllText(file)) is { } saved
                && saved.Right > saved.Left && saved.Bottom > saved.Top
                ? saved
                : null;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // An unwritten placement leaves the window where it opened before.
    public void Save(Placement placement)
    {
        var written = file + ".new";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(written, JsonSerializer.Serialize(placement));
            File.Move(written, file, overwrite: true);
        }
        catch (Exception unwritten) when (unwritten is IOException or UnauthorizedAccessException)
        {
        }
    }

    // A minimized window is kept by the state it comes back to.
    public static unsafe Placement? Of(nint window)
    {
        var placement = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (!PInvoke.GetWindowPlacement((HWND)window, ref placement))
        {
            return null;
        }

        var normal = placement.rcNormalPosition;
        var maximized = placement.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED
            || (placement.showCmd == SHOW_WINDOW_CMD.SW_SHOWMINIMIZED
                && placement.flags.HasFlag(WINDOWPLACEMENT_FLAGS.WPF_RESTORETOMAXIMIZED));
        return new Placement(normal.left, normal.top, normal.right, normal.bottom, maximized);
    }

    // Before the window first shows: a restored one stays hidden until WinUI shows it, and a maximized one shows
    // maximized, which WinUI's first activation keeps. Windows moves bounds no screen holds now onto one that does.
    public static unsafe void Restore(nint window, Placement placement)
    {
        var restored = new WINDOWPLACEMENT
        {
            length = (uint)sizeof(WINDOWPLACEMENT),
            showCmd = placement.Maximized ? SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED : SHOW_WINDOW_CMD.SW_HIDE,
            rcNormalPosition = new RECT(placement.Left, placement.Top, placement.Right, placement.Bottom),
        };
        PInvoke.SetWindowPlacement((HWND)window, in restored);
    }
}
