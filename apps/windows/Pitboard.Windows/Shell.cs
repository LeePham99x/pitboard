using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pitboard.Windows;

public static class Shell
{
    // Before any window, so each one groups under it on the taskbar.
    public static void Name(Identity identity) =>
        PInvoke.SetCurrentProcessExplicitAppUserModelID(identity.Aumid).ThrowOnFailure();

    // Environment.GetFolderPath falls back to LOCALAPPDATA where this fails, and no variable is to move it.
    public static unsafe string LocalAppData()
    {
        PInvoke.SHGetKnownFolderPath(
            PInvoke.FOLDERID_LocalAppData, KNOWN_FOLDER_FLAG.KF_FLAG_DONT_VERIFY, null, out var path).ThrowOnFailure();
        try
        {
            return path.ToString();
        }
        finally
        {
            PInvoke.CoTaskMemFree(path);
        }
    }

    // The icon Pitboard.exe carries, at the size the notification area draws on this screen. The .NET SDK
    // numbers an ApplicationIcon 32512, as IDI_APPLICATION is.
    public static unsafe nint AppIcon()
    {
        using var module = PInvoke.GetModuleHandle((string?)null);
        HICON icon;
        PInvoke.LoadIconMetric((HINSTANCE)module.DangerousGetHandle(), (char*)32512, _LI_METRIC.LIM_SMALL, &icon).ThrowOnFailure();
        return (nint)icon.Value;
    }

    // Allowed while the icon's click has handed this process the foreground.
    public static void BringForward(nint window) => PInvoke.SetForegroundWindow((HWND)window);

    public static bool InFront(nint window) => PInvoke.GetForegroundWindow() == (HWND)window;

    public static void Refuse(string message) =>
        PInvoke.MessageBox(HWND.Null, message, "Pitboard", MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
}
