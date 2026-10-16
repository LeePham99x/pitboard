using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Pitboard.StandInApp;

// ChatGPT open, as Restart Manager sees it: a GUI process with a window, and a console child, as its codex. It ends
// where Restart Manager asks, or refuses to.
public static unsafe class Program
{
    private const string ClassName = "Pitboard.StandInApp";

    private static readonly WNDPROC Procedure = Receive;
    private static bool _honours;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args is not [("honours" or "refuses") and var how])
        {
            Console.Error.WriteLine("Usage: Pitboard.StandInApp honours | refuses");
            return 2;
        }

        _honours = how == "honours";
        var instance = PInvoke.GetModuleHandle((string?)null);
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
                return Refused("its window's class");
            }
        }

        var window = PInvoke.CreateWindowEx(
            0, ClassName, ClassName, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, 0, 0, 320, 200, HWND.Null, null, instance, null);
        if (window.IsNull)
        {
            return Refused("its window");
        }

        // Shown, as an open app's is, without taking the focus from the tests.
        PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);

        using var child = Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c ping -n 180 127.0.0.1 >nul")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        Console.WriteLine($"ready {child.Id}");
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0).Value > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }

        return 0;
    }

    private static LRESULT Receive(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case PInvoke.WM_QUERYENDSESSION:
                return (LRESULT)(_honours ? 1 : 0);
            case PInvoke.WM_ENDSESSION when wParam.Value != 0 && (lParam.Value & PInvoke.ENDSESSION_CLOSEAPP) != 0:
                PInvoke.PostQuitMessage(0);
                return (LRESULT)0;
            default:
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
    }

    private static int Refused(string what)
    {
        Console.Error.WriteLine($"Windows refused {what}: {Marshal.GetLastPInvokeErrorMessage()}");
        return 1;
    }
}
