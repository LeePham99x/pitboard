using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.System.Power;
using Pitboard.Core;
using Pitboard.Windows;
using Windows.Graphics;
using Launcher = Windows.System.Launcher;

namespace Pitboard.App;

public partial class App : Application
{
    private readonly AppStart _start;
    private readonly WindowRequests _requests = new();
    private readonly NoUpdates _updates = new();
    private readonly string _version = SettingsPage.Version(typeof(App).Assembly);
    private AppModel? _model;
    private StartAtSignIn? _startAtSignIn;
    private HiddenWindow? _hidden;
    private NotifyIcon? _icon;
    private MenuHost? _menu;
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
        _startAtSignIn = StartAtSignIn.For(_start, Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Pitboard.exe"));
        model.PropertyChanged += OnModelChanged;
        _hidden = new HiddenWindow(sessionEnding: EndSession, closing: Quit, icon: OnIcon, taskbarCreated: () => _icon?.Readd());
        _icon = new NotifyIcon(new ShellNotificationArea(), _hidden.Handle, Shell.AppIcon(), _start.Identity.IconGuid);
        _icon.Show(model.MenuBar.Spoken);
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
        if (_model is null)
        {
            return;
        }

        switch (args.PropertyName)
        {
            case nameof(AppModel.WindowRequest):
                Follow(_model.WindowRequest);
                break;
            case nameof(AppModel.MenuBar):
                _icon?.Show(_model.MenuBar.Spoken);
                break;
        }
    }

    private void OnIcon(IconEvent happened)
    {
        switch (happened)
        {
            case IconEvent.Activated:
                _model?.Send(new Intent.ShowWindow(Pane: null));
                break;
            case IconEvent.Menu at when _model is { } model:
                if (_menu is null)
                {
                    _menu = new MenuHost();
                    _menu.Closed += (_, _) => _menu = null;
                }

                _menu.Show(
                    IconMenu.Entries(model, _updates),
                    new PointInt32(at.X, at.Y),
                    opened: () => model.Send(new Intent.Glanced()),
                    chosen: Perform,
                    cancelled: () => _icon?.ReturnFocus());
                break;
        }
    }

    private void Perform(IconCommand command)
    {
        switch (command)
        {
            case IconCommand.Send send:
                _model?.Send(send.Intent);
                break;
            case IconCommand.OpenLink link:
                _ = Launcher.LaunchUriAsync(new Uri(link.Url));
                break;
            case IconCommand.ShowSettings:
                ShowWindow();
                _window?.Select(null);
                break;
            // About is a section of the settings page, as Microsoft's settings guidance places it.
            case IconCommand.ShowAbout:
                ShowWindow();
                _window?.ShowAbout();
                break;
            case IconCommand.CheckForUpdates:
                _updates.Check();
                break;
            case IconCommand.Quit:
                Quit();
                break;
        }
    }

    private void EndSession()
    {
        _icon?.Remove();
        _model?.Shutdown();
    }

    private void Quit()
    {
        EndSession();
        Exit();
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
        if (_model is null || _startAtSignIn is null)
        {
            return;
        }

        if (_window is null)
        {
            var window = new MainWindow(_model, _startAtSignIn, _updates, _version);
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
