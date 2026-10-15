using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Pitboard.Windows.Tests;

// As Microsoft's unpackaged WinUI sample for MSTest hosts its tests.
public partial class UnitTestApp : Application
{
    private Window? _window;

    public UnitTestApp() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new UnitTestAppWindow();
        _window.Activate();
        UITestMethodAttribute.DispatcherQueue = _window.DispatcherQueue;
        try
        {
            Environment.ExitCode = await MicrosoftTestingPlatformApplication.RunAsync(
                Environment.GetCommandLineArgs()[1..]);
        }
        finally
        {
            _window.Close();
            Exit();
        }
    }
}
