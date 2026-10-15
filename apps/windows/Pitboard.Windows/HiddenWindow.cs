using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pitboard.Windows;

// Never shown, and not message-only: a message-only window gets no broadcast, such as WM_ENDSESSION.
public sealed unsafe class HiddenWindow
{
    private const string ClassName = "Pitboard.HiddenWindow";

    private readonly Action _sessionEnding;
    private readonly WNDPROC _procedure;
    private readonly HWND _window;

    public HiddenWindow(Action sessionEnding)
    {
        _sessionEnding = sessionEnding;
        _procedure = Procedure;
        var instance = PInvoke.GetModuleHandle((string?)null);
        fixed (char* name = ClassName)
        {
            var registered = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _procedure,
                hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                lpszClassName = name,
            };
            if (PInvoke.RegisterClassEx(registered) == 0)
            {
                throw new InvalidOperationException(
                    $"the hidden window's class was refused: {Marshal.GetLastPInvokeErrorMessage()}");
            }
        }

        _window = PInvoke.CreateWindowEx(
            0, ClassName, ClassName, WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 0, 0, HWND.Null, null, instance, null);
        if (_window.IsNull)
        {
            throw new InvalidOperationException(
                $"the hidden window was refused: {Marshal.GetLastPInvokeErrorMessage()}");
        }
    }

    private LRESULT Procedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            // The process may end as soon as this returns.
            case PInvoke.WM_ENDSESSION when wParam.Value != 0:
                _sessionEnding();
                return (LRESULT)0;
            case PInvoke.WM_SETTINGCHANGE:
                CultureInfo.CurrentCulture.ClearCachedData();
                TimeZoneInfo.ClearCachedData();
                break;
            case PInvoke.WM_TIMECHANGE:
                TimeZoneInfo.ClearCachedData();
                break;
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }
}
