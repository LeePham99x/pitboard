using Windows.Win32;
using Windows.Win32.Foundation;
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

    public static void Refuse(string message) =>
        PInvoke.MessageBox(HWND.Null, message, "Pitboard", MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR);
}
