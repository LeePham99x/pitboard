using Microsoft.Windows.AppLifecycle;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;

namespace Pitboard.Windows;

public static class Redirection
{
    // Off the STA thread, which waits without blocking COM, as Microsoft's instancing guide redirects.
    public static void To(AppInstance running)
    {
        var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
        using var redirected = new ManualResetEvent(false);
        _ = Task.Run(() =>
        {
            try
            {
                running.RedirectActivationToAsync(activated).AsTask().Wait();
            }
            finally
            {
                redirected.Set();
            }
        });
        HANDLE[] handles = [(HANDLE)redirected.SafeWaitHandle.DangerousGetHandle()];
        PInvoke.CoWaitForMultipleObjects((uint)CWMO_FLAGS.CWMO_DEFAULT, PInvoke.INFINITE, handles, out _).ThrowOnFailure();
    }
}
