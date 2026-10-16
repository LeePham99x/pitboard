using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Pitboard.Windows.Tests;

// As Pitboard.App's Program starts WinUI. WebView2 is made only on a single-threaded apartment's thread, and the XAML
// compiler's Main left this one MTA (run 38815163618).
public static class Program
{
    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(initializing =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new UnitTestApp();
        });
    }
}
