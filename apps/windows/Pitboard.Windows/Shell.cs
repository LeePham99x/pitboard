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
    public static string LocalAppData() => KnownFolder(PInvoke.FOLDERID_LocalAppData);

    public static unsafe string KnownFolder(Guid id)
    {
        PInvoke.SHGetKnownFolderPath(id, KNOWN_FOLDER_FLAG.KF_FLAG_DONT_VERIFY, null, out var path).ThrowOnFailure();
        try
        {
            return path.ToString();
        }
        finally
        {
            PInvoke.CoTaskMemFree(path);
        }
    }

    // The .NET SDK numbers an ApplicationIcon 32512, IDI_APPLICATION's id.
    public static unsafe nint AppIcon()
    {
        using var module = PInvoke.GetModuleHandle((string?)null);
        HICON icon;
        PInvoke.LoadIconMetric((HINSTANCE)module.DangerousGetHandle(), (char*)32512, _LI_METRIC.LIM_SMALL, &icon).ThrowOnFailure();
        return (nint)icon.Value;
    }

    // Allowed while the icon's click has handed this process the foreground.
    public static void BringForward(nint window) => PInvoke.SetForegroundWindow((HWND)window);

    // A window's effective pixels to its physical ones, at its monitor's scale.
    public static double Scale(nint window) => PInvoke.GetDpiForWindow((HWND)window) / 96.0;

    public static bool InFront(nint window) => PInvoke.GetForegroundWindow() == (HWND)window;

    public static void Refuse(string message) =>
        PInvoke.MessageBox(HWND.Null, message, "Pitboard", MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
}
