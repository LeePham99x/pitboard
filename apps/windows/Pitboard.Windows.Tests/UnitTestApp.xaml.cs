using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;

namespace Pitboard.Windows.Tests;

// As Microsoft's unpackaged WinUI sample for MSTest hosts its tests.
public partial class UnitTestApp : Application
{
    public UnitTestApp()
    {
        // Unhandled, it fails fast with 0xC000027B and the run's log says nothing more.
        UnhandledException += (_, args) => Console.Error.WriteLine($"{args.Message}{Environment.NewLine}{args.Exception}");
        DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        DebugSettings.XamlResourceReferenceFailed += (_, args) => Console.Error.WriteLine(args.Message);
        InitializeComponent();
    }

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
