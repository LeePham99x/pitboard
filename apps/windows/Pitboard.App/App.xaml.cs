using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.System.Power;
using Pitboard.Core;
using Pitboard.Windows;

namespace Pitboard.App;

public partial class App : Application
{
    private readonly AppStart _start;
    private readonly WindowRequests _requests = new();
    private AppModel? _model;
    private HiddenWindow? _hidden;
    private MainWindow? _window;

    public App(AppStart start)
    {
        _start = start;
        InitializeComponent();
        // Closing the window leaves Pitboard running, as the macOS app does.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var model = Model(DispatcherQueue.GetForCurrentThread());
        if (model is null)
        {
            Exit();
            return;
        }

        _model = model;
        model.PropertyChanged += OnModelChanged;
        _hidden = new HiddenWindow(sessionEnding: model.Shutdown);
        PowerManager.SystemSuspendStatusChanged += OnSuspendStatusChanged;
        Follow(model.WindowRequest);
        model.Send(new Intent.Start());
    }

    // A second launch, which brings the window forward.
    public void Reopened() => ShowWindow();

    private AppModel? Model(DispatcherQueue queue)
    {
#if DEBUG
        if (_start.Fixture is { } fixture)
        {
            try
            {
                return AppModel.Listening(
                    queue, listener => PitboardModel.Fixture(fixture, listener, new WindowsLocalTime()));
            }
            catch (FixtureException refused)
            {
                Shell.Refuse(refused switch
                {
                    FixtureException.Unavailable unavailable => unavailable.reason,
                    FixtureException.Unknown unknown => unknown.reason,
                    FixtureException.Failed failed => failed.reason,
                    _ => refused.Message,
                });
                return null;
            }
        }
#endif
        var directory = PitboardFfiMethods.PitboardDirectory(_start.Environment);
        var windows = new WindowsLaunch(
            _start.DataFolder, PitboardFfiMethods.WindowsRecordsKey(directory), _start.Identity.LinkScheme);
        var launch = new AppLaunch(_start.Environment, AppLocation: null, Windows: windows);
        return AppModel.Listening(
            queue,
            listener => new PitboardModel(
                launch, listener, new NoOtherApps(), new NoNotifications(), new WindowsLocalTime()));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AppModel.WindowRequest) && _model is not null)
        {
            Follow(_model.WindowRequest);
        }
    }

    private void Follow(WindowRequest request)
    {
        if (_requests.Opens(request))
        {
            ShowWindow();
        }

        _window?.Select(_requests.PaneFor(request, _window.Selected));
    }

    private void ShowWindow()
    {
        if (_model is null)
        {
            return;
        }

        if (_window is null)
        {
            var window = new MainWindow(_model);
            window.Closed += (_, _) => _window = null;
            _window = window;
        }

        _window.Show();
    }

    // On the system's thread; the model takes a message from any.
    private void OnSuspendStatusChanged(object? sender, object args)
    {
        if (PowerManager.SystemSuspendStatus == SystemSuspendStatus.AutoResume)
        {
            _model?.Send(new Intent.Woke());
        }
    }
}
