using Pitboard.Core;
using Pitboard.Views;
using Pitboard.Windows;

namespace Pitboard.App;

// WebView2's side of the account windows, and the Open Link window. What they show, and which stores go, is the model's:
// this tells it what the windows and their downloads did, and does what it says.
internal sealed class AccountWindows
{
    private readonly AppModel _model;
    private readonly WebEnvironment _web;
    private readonly Downloads _downloads;
    private readonly StoreJanitor _janitor;
    private readonly Dictionary<string, AccountWindow> _open = new(StringComparer.OrdinalIgnoreCase);
    private OpenLinkWindow? _picker;

    // The janitor's pages are made in the hidden window, which lives as long as Pitboard.
    public AccountWindows(AppModel model, WebEnvironment web, nint hidden)
    {
        _model = model;
        _web = web;
        _downloads = new Downloads(web, model.Send);
        _janitor = new StoreJanitor(new ProfileFolders(web, hidden));
    }

    // Opening one again brings the open one forward. A held store opens none: its menu item says why.
    public void Open(WindowAccount account)
    {
        if (_open.TryGetValue(account.Store, out var shown))
        {
            shown.Bring();
            return;
        }

        if (HeldStores.Note(account, _model.AccountWindows.Held) is not null)
        {
            return;
        }

        var window = new AccountWindow(account.Store, _web, _downloads, _model.Send);
        window.Closed += (_, _) =>
        {
            _open.Remove(window.Store);
            _model.Send(new Intent.WindowClosed(window.Store));
        };
        _open[account.Store] = window;
        _model.Send(new Intent.WindowOpened(account.Store));
        window.Follow(_model.AccountWindows);
        window.Activate();
    }

    // Asked over the window of an account whose download is under way, or another account window. With none open,
    // nothing is: a download goes with the browser process, which exits once the last page closes.
    public async Task<bool> MayQuit()
    {
        if (_downloads.QuitQuestion is not { } question)
        {
            return true;
        }

        var window = _downloads.Stores.Select(store => _open.GetValueOrDefault(store))
            .FirstOrDefault(open => open is not null) ?? _open.Values.FirstOrDefault();
        if (window is null)
        {
            return true;
        }

        window.Bring();
        return await window.Confirm(question);
    }

    public void StopDownloads() => _downloads.StopAll();

    public void Follow(AccountWindowsShown shown)
    {
        foreach (var store in shown.Closing)
        {
            if (_open.TryGetValue(store, out var closing))
            {
                closing.Close();
            }
        }

        foreach (var window in _open.Values.ToArray())
        {
            window.Follow(shown);
        }

        _janitor.Delete(
            shown.Deleting, shown.Held,
            (store, deleted) => _model.Send(deleted ? new Intent.StoreDeleted(store) : new Intent.StoreHeld(store)));
        FollowLink(shown.Picker);
    }

    // Each link that arrives brings the window forward, since the app that handed it over is in front; it goes once
    // no link waits.
    private void FollowLink(LinkPicker? picker)
    {
        if (picker is null)
        {
            _picker?.Withdraw();
            return;
        }

        var arrived = _picker?.Arrival != picker.Arrival;
        if (_picker is null)
        {
            // A fixture's Open in Browser opens nothing, as its pages send nothing elsewhere.
            var window = new OpenLinkWindow(_model.Send, Open, _web.OpenElsewhere);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_picker, window))
                {
                    _picker = null;
                }
            };
            _picker = window;
        }

        _picker.Follow(picker);
        if (arrived)
        {
            _picker.Bring();
        }
    }
}
