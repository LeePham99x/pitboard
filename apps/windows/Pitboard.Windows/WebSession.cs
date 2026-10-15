using Microsoft.Web.WebView2.Core;
using Pitboard.Core;

namespace Pitboard.Windows;

// What the window around a session does for it.
public interface ISessionWindow
{
    // A sign-in window at the size given, whose page is made from the same environment and profile and has loaded
    // nothing yet; null where none could be made.
    Task<CoreWebView2?> OpenSignIn(SignInWindowSize size);

    void CloseSignIn();

    // Whether the person lets a download the policy asks about save, asked over the account's window.
    Task<bool> AllowDownload(AlertText question);

    // The window's page is now at this address.
    void Shown(string address);

    // Its note or its failure changed.
    void Changed();
}

// One account's window as WebView2 runs it: its own page, and the sign-in window one of its pages opened, each under
// the policy of the account's site in its role.
public sealed class WebSession
{
    private readonly WebEnvironment _web;
    private readonly Downloads _downloads;
    private readonly ISessionWindow _window;
    private readonly TimeProvider _time;
    private readonly Dictionary<PageRole, DateTimeOffset> _lastEnded = [];
    private CoreWebView2? _page;
    private ulong _loaded;
    private string _first;

    public WebSession(OpenWindow open, WebEnvironment web, Downloads downloads, ISessionWindow window, TimeProvider? time = null)
    {
        Account = open.Account;
        Policy = web.Policy(open.Account.Site);
        Note = open.Note;
        _web = web;
        _downloads = downloads;
        _window = window;
        _time = time ?? TimeProvider.System;
        _loaded = open.Load.Serial;
        _first = open.Load.Url;
    }

    public WindowAccount Account { get; private set; }

    public NavigationPolicy Policy { get; }

    public WindowNoteKind? Note { get; private set; }

    // Why the page is not shown, after its content stopped twice within a minute.
    public string? Failure { get; private set; }

    // The window's page, which loads the page the session starts at.
    public void Attach(CoreWebView2 page)
    {
        _page = page;
        Prepare(page, PageRole.Window);
        _web.Serve(page);
        page.SourceChanged += (sender, _) => _window.Shown(sender.Source);
        page.Navigate(_first);
    }

    // A rename retitles the window and keeps everything else.
    public void Update(WindowAccount account) => Account = account;

    // A link chosen for this window while it is open; Back returns to where the window was.
    public void Load(PageLoad load)
    {
        if (load.Serial == _loaded)
        {
            return;
        }

        _loaded = load.Serial;
        if (_page is null)
        {
            _first = load.Url;
            return;
        }

        if (Failure is not null)
        {
            Failure = null;
            _window.Changed();
        }

        _page.Navigate(load.Url);
    }

    public void Reload()
    {
        Failure = null;
        _window.Changed();
        _page?.Reload();
    }

    public void Dismiss()
    {
        Note = null;
        _window.Changed();
    }

    // The signed-in page leaves first, so nothing it still does lands in the profile after it is cleared.
    public async Task RemoveWebsiteData()
    {
        _window.CloseSignIn();
        if (_page is not { } page)
        {
            return;
        }

        await Leave(page);
        await page.Profile.ClearBrowsingDataAsync();
    }

    // A page of its own after the data went, so Back cannot bring the signed-in one back.
    public void StartOver(CoreWebView2 page)
    {
        Note = PitboardFfiMethods.OpeningNote(storeMadeBefore: false);
        Failure = null;
        _first = PitboardFfiMethods.WindowHome(Policy);
        _window.Changed();
        Attach(page);
    }

    private void Prepare(CoreWebView2 page, PageRole role)
    {
        _web.Prepare(page);
        page.NavigationStarting += (sender, args) => Navigating(role, sender, args, frame: false);
        page.FrameNavigationStarting += (sender, args) => Navigating(role, sender, args, frame: true);
        page.NewWindowRequested += (sender, args) => NewWindow(role, sender, args);
        // A sign-in window's page closes it once it is done; an account window's page cannot close it.
        page.WindowCloseRequested += (_, _) =>
        {
            if (PitboardFfiMethods.PageMayClose(role))
            {
                _window.CloseSignIn();
            }
        };
        page.PermissionRequested += (_, args) => Permission(args);
        page.ProcessFailed += (sender, args) => ProcessFailed(role, sender, args);
        // After the policy's own handlers, so a navigation it stopped is not under way.
        var navigations = new PageNavigations();
        page.NavigationStarting += (_, args) => navigations.Starting(args.NavigationId, args.Uri, frame: false, args.Cancel);
        page.FrameNavigationStarting += (_, args) => navigations.Starting(args.NavigationId, args.Uri, frame: true, args.Cancel);
        page.NavigationCompleted += (_, args) => navigations.Completed(args.NavigationId, frame: false);
        page.FrameNavigationCompleted += (_, args) => navigations.Completed(args.NavigationId, frame: true);
        page.DownloadStarting += (_, args) => Download(role, navigations, args);
        // What a frame loads is the page's to decide, and an address it loads that WebView2 hands to another app never
        // leaves through Pitboard.
        page.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
    }

    private void Navigating(PageRole role, CoreWebView2 page, CoreWebView2NavigationStartingEventArgs args, bool frame)
    {
        var request = PageRequests.Navigation(Policy, role, args.Uri, frame, args.IsUserInitiated, frame ? null : page.Source);
        switch (PitboardFfiMethods.DecideNavigation(Policy, role, request))
        {
            case NavigationDecision.Load or NavigationDecision.LoadInPage or NavigationDecision.Popup:
                return;
            case NavigationDecision.OpenElsewhere away:
                args.Cancel = true;
                HandOver(away.Url, away.Note);
                return;
            case NavigationDecision.Refuse refused:
                args.Cancel = true;
                Say(refused.Note);
                return;
            default:
                args.Cancel = true;
                return;
        }
    }

    // Always handled, so WebView2 opens no window of its own. A sign-in window keeps window.opener: its page is set as
    // the new window before it loads.
    private async void NewWindow(PageRole role, CoreWebView2 page, CoreWebView2NewWindowRequestedEventArgs args)
    {
        var source = args.OriginalSourceFrameInfo;
        var request = PageRequests.NewWindow(
            Policy, role, args.Uri, args.IsUserInitiated, source?.Source,
            mainFrame: source?.FrameKind == CoreWebView2FrameKind.MainFrame);
        args.Handled = true;
        switch (PitboardFfiMethods.DecideNavigation(Policy, role, request))
        {
            case NavigationDecision.Popup:
                var deferral = args.GetDeferral();
                try
                {
                    var features = args.WindowFeatures;
                    var size = PitboardFfiMethods.SignInWindowSize(
                        features.HasSize ? features.Width : (double?)null, features.HasSize ? features.Height : (double?)null);
                    // WebView2 takes a new window's settings before it is set, and its resource requests after.
                    if (await _window.OpenSignIn(size) is { } signIn)
                    {
                        Prepare(signIn, PageRole.Popup);
                        args.NewWindow = signIn;
                        _web.Serve(signIn);
                    }
                }
                finally
                {
                    deferral.Complete();
                }

                break;
            case NavigationDecision.LoadInPage:
                page.Navigate(args.Uri);
                break;
            case NavigationDecision.OpenElsewhere away:
                HandOver(away.Url, away.Note);
                break;
            case NavigationDecision.Refuse refused:
                Say(refused.Note);
                break;
        }
    }

    // Left unhandled, so WebView2's own download dialog shows it, as Edge's does.
    private async void Download(PageRole role, PageNavigations navigations, CoreWebView2DownloadStartingEventArgs args)
    {
        var operation = args.DownloadOperation;
        var facts = PageRequests.Download(operation.Uri, operation.ContentDisposition, navigations.MainFrame(operation.Uri));
        if (PitboardFfiMethods.DecideResponse(Policy, role, facts) is ResponseDecision.Download saving)
        {
            await _downloads.Start(Account.Store, args, saving.Ask, _window.AllowDownload);
        }
        else
        {
            args.Cancel = true;
        }
    }

    // A camera or a microphone is never given; WebView2 asks the person about anything else.
    private static void Permission(CoreWebView2PermissionRequestedEventArgs args)
    {
        PagePermission? asked = args.PermissionKind switch
        {
            CoreWebView2PermissionKind.Camera => PagePermission.Camera,
            CoreWebView2PermissionKind.Microphone => PagePermission.Microphone,
            _ => null,
        };
        if (asked is { } permission && !PitboardFfiMethods.PageMayUse(permission))
        {
            args.State = CoreWebView2PermissionState.Deny;
            args.Handled = true;
        }
    }

    private void ProcessFailed(PageRole role, CoreWebView2 page, CoreWebView2ProcessFailedEventArgs args)
    {
        if (args.ProcessFailedKind is not (CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive))
        {
            return;
        }

        var now = _time.GetUtcNow();
        double? last = _lastEnded.TryGetValue(role, out var ended) ? Seconds(ended) : null;
        _lastEnded[role] = now;
        switch (PitboardFfiMethods.AfterContentProcessEnded(last, Seconds(now)))
        {
            case ProcessEnded.Reload:
                page.Reload();
                break;
            case ProcessEnded.Stopped stopped when role == PageRole.Window:
                Failure = stopped.Reason;
                _window.Changed();
                break;
        }
    }

    private void HandOver(string address, WindowNoteKind? note)
    {
        _web.OpenElsewhere(address);
        if (note is not null)
        {
            Say(note);
        }
    }

    private void Say(WindowNoteKind note)
    {
        Note = note;
        _window.Changed();
    }

    // Up to two seconds, until the page has left for a blank one and run its unload handlers.
    private static async Task Leave(CoreWebView2 page)
    {
        var left = new TaskCompletionSource();
        void Done(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) => left.TrySetResult();
        page.NavigationCompleted += Done;
        try
        {
            page.Stop();
            page.Navigate("about:blank");
            await Task.WhenAny(left.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            page.NavigationCompleted -= Done;
        }
    }

    private static double Seconds(DateTimeOffset at) => at.ToUnixTimeMilliseconds() / 1000.0;
}
