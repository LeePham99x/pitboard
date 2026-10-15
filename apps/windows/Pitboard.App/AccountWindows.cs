using Pitboard.Core;
using Pitboard.Views;
using Pitboard.Windows;

namespace Pitboard.App;

// WebView2's side of the account windows. What they show, and which stores go, is the model's: this tells it what the
// windows and their downloads did, and does what it says.
internal sealed class AccountWindows
{
    private readonly AppModel _model;
    private readonly WebEnvironment _web;
    private readonly Downloads _downloads;
    private readonly StoreJanitor _janitor;
    private readonly Dictionary<string, AccountWindow> _open = new(StringComparer.OrdinalIgnoreCase);

    // The janitor's pages are made in the hidden window, which lives as long as Pitboard.
    public AccountWindows(AppModel model, WebEnvironment web, nint hidden)
    {
        _model = model;
        _web = web;
        _downloads = new Downloads(web.Downloads, model.Send);
        _janitor = new StoreJanitor(
            store => Directory.Exists(web.ProfileFolder(store)), store => web.MarkDeleted(store, hidden));
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
            shown.Deleting,
            (store, deleted) => _model.Send(deleted ? new Intent.StoreDeleted(store) : new Intent.StoreHeld(store)));
    }
}
