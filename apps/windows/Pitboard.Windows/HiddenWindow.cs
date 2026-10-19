using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pitboard.Windows;

// Not message-only: a message-only window gets no WM_ENDSESSION or TaskbarCreated.
public sealed unsafe class HiddenWindow
{
    private const string ClassName = "Pitboard.HiddenWindow";

    // One class serves every hidden window of the process, so its procedure finds each window's own.
    private static readonly WNDPROC Procedure = Dispatch;
    private static readonly Dictionary<nint, HiddenWindow> ByHandle = [];
    private static bool _registered;

    private readonly Action _sessionEnding;
    private readonly Action _closing;
    private readonly Action<IconEvent> _icon;
    private readonly Action _taskbarCreated;
    private readonly Action _displayChanged;
    private readonly uint _taskbarCreatedMessage;
    private readonly HWND _window;

    public HiddenWindow(
        Action sessionEnding, Action closing, Action<IconEvent> icon, Action taskbarCreated, Action displayChanged)
    {
        _sessionEnding = sessionEnding;
        _closing = closing;
        _icon = icon;
        _taskbarCreated = taskbarCreated;
        _displayChanged = displayChanged;
        var instance = PInvoke.GetModuleHandle((string?)null);
        Register(instance);
        _taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
        _window = PInvoke.CreateWindowEx(
            0, ClassName, ClassName, WINDOW_STYLE.WS_OVERLAPPED, 0, 0, 0, 0, HWND.Null, null, instance, null);
        if (_window.IsNull)
        {
            throw new InvalidOperationException(
                $"the hidden window was refused: {Marshal.GetLastPInvokeErrorMessage()}");
        }

        lock (ByHandle)
        {
            ByHandle[Handle] = this;
        }
    }

    public nint Handle => (nint)_window.Value;

    private static void Register(SafeHandle instance)
    {
        lock (ByHandle)
        {
            if (_registered)
            {
                return;
            }

            fixed (char* name = ClassName)
            {
                var registered = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = Procedure,
                    hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                    lpszClassName = name,
                };
                if (PInvoke.RegisterClassEx(registered) == 0)
                {
                    throw new InvalidOperationException(
                        $"the hidden window's class was refused: {Marshal.GetLastPInvokeErrorMessage()}");
                }
            }

            _registered = true;
        }
    }

    // Before CreateWindowEx returns, the window's first messages find no HiddenWindow yet.
    private static LRESULT Dispatch(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        HiddenWindow? hidden;
        lock (ByHandle)
        {
            ByHandle.TryGetValue((nint)window.Value, out hidden);
        }

        return hidden is not null && hidden.Receive(message, wParam, lParam)
            ? (LRESULT)0
            : PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private bool Receive(uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            // Restart Manager waits for the process to exit; at a sign-out Windows may end it as soon as this returns.
            case PInvoke.WM_ENDSESSION when wParam.Value != 0:
                if ((lParam.Value & PInvoke.ENDSESSION_CLOSEAPP) != 0)
                {
                    _closing();
                }
                else
                {
                    _sessionEnding();
                }

                return true;
            // As taskkill without /f asks a program to end.
            case PInvoke.WM_CLOSE:
                _closing();
                return true;
            case NotifyIcon.CallbackMessage:
                if (IconEvent.Read(wParam.Value, lParam.Value) is { } happened)
                {
                    _icon(happened);
                }

                return true;
            case PInvoke.WM_SETTINGCHANGE:
                CultureInfo.CurrentCulture.ClearCachedData();
                TimeZoneInfo.ClearCachedData();
                _displayChanged();
                return false;
            case PInvoke.WM_DISPLAYCHANGE:
                _displayChanged();
                return false;
            // A window of no size has nothing to resize to the rectangle suggested.
            case PInvoke.WM_DPICHANGED:
                _displayChanged();
                return true;
            case PInvoke.WM_TIMECHANGE:
                TimeZoneInfo.ClearCachedData();
                return false;
        }

        if (message == _taskbarCreatedMessage)
        {
            _taskbarCreated();
            return true;
        }

        return false;
    }
}
