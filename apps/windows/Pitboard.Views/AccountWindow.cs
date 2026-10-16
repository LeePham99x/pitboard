using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Pitboard.Core;
using Pitboard.Windows;
using Windows.Graphics;
using WinRT.Interop;

namespace Pitboard.Views;

// A window for one site and one account, not a browser: no address bar and no tabs of its own. WebView2's find bar,
// zoom, dialogs, download list and keys serve its page, and a page that leaves the site goes to the browser.
public sealed partial class AccountWindow : Window, ISessionWindow
{
    private readonly WebEnvironment _web;
    private readonly Downloads _downloads;
    private readonly Action<Intent> _send;
    private readonly DialogHost _dialogs;
    private readonly AppBarButton _back = Command("Back", Symbol.Back, "Show the previous page");
    private readonly AppBarButton _forward = Command("Forward", Symbol.Forward, "Show the next page");
    private readonly AppBarButton _reload = Command("Reload", Symbol.Refresh, "Load this page again");
    private readonly AppBarButton _stop = Command("Stop", Symbol.Cancel, "Stop loading this page");
    private readonly AppBarButton _showDownloads = Command("Downloads", Symbol.Download, "Show this window’s downloads");
    private readonly AppBarButton _removeData = Command("Remove Website Data…", Symbol.Delete, null);
    private readonly InfoBar _note = new() { Severity = InfoBarSeverity.Informational, IsClosable = true };
    private readonly Grid _page = new();
    private readonly StackPanel _standIn = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 480,
        Spacing = 12,
    };

    private WebView2? _view;
    private WebSession? _session;
    private SignInWindow? _signIn;
    private bool _starting;
    private bool _closed;

    public AccountWindow(string store, WebEnvironment web, Downloads downloads, Action<Intent> send)
    {
        Store = store;
        _web = web;
        _downloads = downloads;
        _send = send;
        var bar = new CommandBar
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed,
            PrimaryCommands = { _back, _forward, _reload, _stop, _showDownloads },
            SecondaryCommands = { _removeData },
        };
        _stop.Visibility = Visibility.Collapsed;
        _showDownloads.Visibility = Visibility.Collapsed;
        _back.Click += (_, _) => Page?.GoBack();
        _forward.Click += (_, _) => Page?.GoForward();
        _reload.Click += (_, _) => TryAgain();
        _stop.Click += (_, _) => Page?.Stop();
        _showDownloads.Click += (_, _) => Page?.OpenDefaultDownloadDialog();
        _removeData.Click += (_, _) => RemoveWebsiteData();
        AutomationProperties.SetAutomationId(_note, "window.note");
        _note.CloseButtonClick += (_, _) => _session?.Dismiss();
        _page.Children.Add(_standIn);
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        Grid.SetRow(_note, 1);
        Grid.SetRow(_page, 2);
        root.Children.Add(bar);
        root.Children.Add(_note);
        root.Children.Add(_page);
        Content = root;
        _dialogs = new DialogHost(() => root.XamlRoot);
        var scale = Shell.Scale(WindowNative.GetWindowHandle(this));
        AppWindow.Resize(new SizeInt32((int)(1100 * scale), (int)(800 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(480 * scale);
            presenter.PreferredMinimumHeight = (int)(360 * scale);
        }

        Closed += (_, _) =>
        {
            _closed = true;
            _dialogs.Withdraw();
            _signIn?.Close();
            _view?.Close();
        };
        Show(null);
    }

    // Its account's store, which is also its identity: one window per account.
    public string Store { get; }

    internal SignInWindow? SignIn => _signIn;

    // The window's page, until WebView2 closes it with its browser process.
    private CoreWebView2? Page => _session is { PageClosed: true } ? null : _view?.CoreWebView2;

    // What the model says of the windows: this one's page once it can show it, what it shows until then, and whether it
    // has downloads to list.
    public void Follow(AccountWindowsShown shown)
    {
        var downloaded = shown.Downloads.Any(
            download => string.Equals(download.Store, Store, StringComparison.OrdinalIgnoreCase));
        _showDownloads.Visibility = downloaded ? Visibility.Visible : Visibility.Collapsed;
        var open = shown.Open.FirstOrDefault(window => string.Equals(window.Store, Store, StringComparison.OrdinalIgnoreCase));
        if (open is not null)
        {
            Show(open);
        }
        else if (_session is null && !_starting)
        {
            Wait(shown.Waiting);
        }
    }

    // A window closed meanwhile answers no.
    public Task<bool> Confirm(Question question) => _dialogs.Confirm(question);

    public void Bring()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    public async Task<CoreWebView2?> OpenSignIn(SignInWindowSize size)
    {
        CloseSignIn();
        if (_session is null)
        {
            return null;
        }

        var window = new SignInWindow(size, this, _session.Account.Title);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_signIn, window))
            {
                _signIn = null;
            }
        };
        _signIn = window;
        window.Activate();
        var environment = await _web.Made();
        return await window.Page(environment, WebEnvironment.Options(environment, Store));
    }

    public void CloseSignIn() => _signIn?.Close();

    // As the macOS app asks, over the window.
    public Task<bool> AllowDownload(AlertText question) =>
        Confirm(new Question(question.Title, question.Message, "Download"));

    public void Shown(string address) => _send(new Intent.PageShown(Store, address));

    public void Changed() => ShowSession();

    private static AppBarButton Command(string label, Symbol symbol, string? tip)
    {
        var button = new AppBarButton { Label = label, Icon = new SymbolIcon(symbol) };
        if (tip is not null)
        {
            ToolTipService.SetToolTip(button, tip);
        }

        return button;
    }

    private void Show(OpenWindow? open)
    {
        if (open is null)
        {
            Title = "Account";
            _removeData.IsEnabled = false;
            _back.IsEnabled = _forward.IsEnabled = _reload.IsEnabled = false;
            return;
        }

        Title = open.Account.Title;
        if (_session is { } session)
        {
            session.Update(open.Account);
            session.Load(open.Load);
            ShowSession();
            return;
        }

        if (!_starting)
        {
            _starting = true;
            _ = Start(open);
        }
    }

    private async Task Start(OpenWindow open)
    {
        if (WebEnvironment.Refusal() is { } refusal)
        {
            Unopened(refusal);
            return;
        }

        var session = new WebSession(open, _web, _downloads, this);
        _session = session;
        try
        {
            var page = await NewPage();
            session.Attach(page);
            Watch(page);
            ShowSession();
        }
        catch (Exception failed)
        {
            _session = null;
            if (!_closed)
            {
                Unopened(failed.Message);
            }
        }
    }

    // A page of its own each time, so after its data was removed Back cannot bring the signed-in page back.
    private async Task<CoreWebView2> NewPage()
    {
        _view?.Close();
        if (_view is not null)
        {
            _page.Children.Remove(_view);
        }

        var view = new WebView2();
        _view = view;
        _page.Children.Insert(0, view);
        var environment = await _web.Made();
        var page = await WebViews.Made(view, environment, WebEnvironment.Options(environment, Store));
        // Under Downloads, at the left of the command bar.
        page.DefaultDownloadDialogCornerAlignment = CoreWebView2DefaultDownloadDialogCornerAlignment.TopLeft;
        return page;
    }

    // After the session's own handlers, so a navigation it stopped shows no loading.
    private void Watch(CoreWebView2 page)
    {
        page.HistoryChanged += (_, _) => ShowHistory();
        page.NavigationStarting += (_, args) => ShowLoading(!args.Cancel);
        page.NavigationCompleted += (_, _) => ShowLoading(false);
    }

    private void Wait(WindowWaiting waiting)
    {
        Title = waiting.WindowTitle;
        switch (waiting.Shown)
        {
            case WaitingShown.Reading reading:
                _standIn.Children.Clear();
                _standIn.Children.Add(new ProgressRing { IsActive = true });
                _standIn.Children.Add(new TextBlock { Text = reading.Title, HorizontalAlignment = HorizontalAlignment.Center });
                _standIn.Visibility = Visibility.Visible;
                break;
            case WaitingShown.ReadFailed failed:
                StandIn(Glyphs.Warning, failed.Title, failed.Detail, failed.Retry.Title, failed.Retry.Enabled, () => _send(failed.Retry.Intent));
                break;
        }
    }

    private void StandIn(string mark, string title, string detail, string? choice, bool enabled, Action? chosen)
    {
        _standIn.Children.Clear();
        _standIn.Children.Add(new FontIcon { Glyph = mark, FontSize = 32 });
        _standIn.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        _standIn.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, IsTextSelectionEnabled = true });
        if (choice is not null && chosen is not null)
        {
            var button = new Button { Content = choice, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => chosen();
            _standIn.Children.Add(button);
        }

        _standIn.Visibility = Visibility.Visible;
        if (_view is not null)
        {
            _view.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowSession()
    {
        if (_session is not { } session)
        {
            return;
        }

        _note.Message = session.Note is { } note ? PitboardFfiMethods.WindowNote(note, session.Account) : "";
        _note.IsOpen = session.Note is not null;
        _removeData.IsEnabled = !session.PageClosed;
        _reload.IsEnabled = true;
        ShowHistory();
        if (session.Failure is { } failure)
        {
            StandIn(Glyphs.Warning, "Couldn’t Load the Page", failure, "Try Again", enabled: true, TryAgain);
            return;
        }

        _standIn.Visibility = Visibility.Collapsed;
        if (_view is not null)
        {
            _view.Visibility = Visibility.Visible;
        }
    }

    // A page WebView2 closed with its browser process comes back only in a web view of its own.
    private async void TryAgain()
    {
        if (_session is not { } session)
        {
            return;
        }

        if (!session.PageClosed)
        {
            session.Reload();
            return;
        }

        try
        {
            var page = await NewPage();
            session.Recover(page);
            Watch(page);
            ShowSession();
        }
        catch (Exception failed)
        {
            if (!_closed)
            {
                Unopened(failed.Message);
            }
        }
    }

    private void Unopened(string why) =>
        StandIn(Glyphs.Warning, "Couldn’t Open This Window", why, choice: null, enabled: false, chosen: null);

    private void ShowHistory()
    {
        _back.IsEnabled = Page?.CanGoBack == true;
        _forward.IsEnabled = Page?.CanGoForward == true;
    }

    private void ShowLoading(bool loading)
    {
        _reload.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        _stop.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
    }

    // The site is not told: the account stays signed in everywhere else.
    private async void RemoveWebsiteData()
    {
        if (_session is not { } session)
        {
            return;
        }

        var alert = PitboardFfiMethods.RemoveDataAlert(session.Account);
        if (!await _dialogs.Confirm(new Question(alert.Title, alert.Message, "Remove")))
        {
            return;
        }

        try
        {
            await session.RemoveWebsiteData();
        }
        // Its page closed meanwhile, with the window or its browser process, and its data stays.
        catch (Exception closed) when (closed is COMException or InvalidOperationException)
        {
            return;
        }

        _send(new Intent.WebsiteDataRemoved(Store));
        if (_closed)
        {
            return;
        }

        try
        {
            var page = await NewPage();
            session.StartOver(page);
            Watch(page);
            ShowSession();
        }
        catch (Exception failed)
        {
            if (!_closed)
            {
                Unopened(failed.Message);
            }
        }
    }
}
