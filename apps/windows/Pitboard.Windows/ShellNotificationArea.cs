using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pitboard.Windows;

public sealed class ShellNotificationArea : INotificationArea
{
    public bool Send(IconMessage message, IconData icon)
    {
        var data = Native(icon);
        if (message is IconMessage.SetVersion)
        {
            data.uVersion = PInvoke.NOTIFYICON_VERSION_4;
        }

        return PInvoke.Shell_NotifyIcon(
            message switch
            {
                IconMessage.Add => NOTIFY_ICON_MESSAGE.NIM_ADD,
                IconMessage.Modify => NOTIFY_ICON_MESSAGE.NIM_MODIFY,
                IconMessage.Delete => NOTIFY_ICON_MESSAGE.NIM_DELETE,
                IconMessage.SetFocus => NOTIFY_ICON_MESSAGE.NIM_SETFOCUS,
                IconMessage.SetVersion => NOTIFY_ICON_MESSAGE.NIM_SETVERSION,
                _ => throw new ArgumentOutOfRangeException(nameof(message)),
            },
            data);
    }

    // The standard tooltip, which NOTIFYICON_VERSION_4 leaves out unless asked for.
    internal static unsafe NOTIFYICONDATAW Native(IconData icon) =>
        new()
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = (HWND)icon.Window,
            uID = icon.Id,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP
                | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP | (icon.IconGuid is null ? 0 : NOTIFY_ICON_DATA_FLAGS.NIF_GUID),
            uCallbackMessage = icon.CallbackMessage,
            hIcon = (HICON)icon.Image,
            szTip = icon.Tip,
            guidItem = icon.IconGuid ?? Guid.Empty,
        };
}
