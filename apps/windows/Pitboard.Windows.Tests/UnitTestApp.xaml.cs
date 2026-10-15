using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Pitboard.Windows.Tests;

// As Microsoft's unpackaged WinUI sample for MSTest hosts its tests.
public partial class UnitTestApp : Application
{
    public UnitTestApp() => InitializeComponent();

    // Where a view test puts its view, to be laid out and loaded as in Pitboard's window.
    public static Window? Window { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new UnitTestAppWindow();
        Window = window;
        window.Activate();
        UITestMethodAttribute.DispatcherQueue = window.DispatcherQueue;
        try
        {
            Environment.ExitCode = await MicrosoftTestingPlatformApplication.RunAsync(
                Environment.GetCommandLineArgs()[1..]);
        }
        finally
        {
            window.Close();
            Exit();
        }
    }
}
