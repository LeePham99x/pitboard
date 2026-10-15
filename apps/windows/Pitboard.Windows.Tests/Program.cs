using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Pitboard.Windows.Tests;

// As Pitboard.App's Program starts WinUI, with the apartment read before and after.
public static class Program
{
    [STAThread]
    private static void Main()
    {
        Apartments.Read("at Main");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(initializing =>
        {
            Apartments.Read("as WinUI started");
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new UnitTestApp();
        });
    }
}
