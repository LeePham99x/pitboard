using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Pitboard.Views;
using Pitboard.Windows;
using Windows.Graphics;
using WinRT.Interop;

namespace Pitboard.App;

// The foreground window under the icon's menu, as TrackPopupMenu asks: WinUI closes a flyout as its window deactivates.
internal sealed partial class MenuHost : Window
{
    private readonly Grid _root = new();
    private readonly nint _handle;
    private MenuFlyout? _open;

    public MenuHost()
    {
        Title = "Pitboard menu";
        Content = _root;
        var presenter = OverlappedPresenter.CreateForContextMenu();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        _handle = WindowNative.GetWindowHandle(this);
    }

    // A click on the menu was this process's last input, so a window a choice opens may still come forward.
    public void Show(IReadOnlyList<IconMenuEntry> entries, PointInt32 at, Action opened, Action<IconCommand> chosen, Action cancelled)
    {
        _open?.Hide();
        IconCommand? choice = null;
        var menu = IconMenuFlyout.For(entries, command => choice = command);
        menu.Opened += (_, _) => opened();
        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_open, menu))
            {
                return;
            }

            _open = null;
            // Esc leaves this window in front; a click elsewhere has taken the foreground already.
            var escaped = choice is null && Shell.InFront(_handle);
            AppWindow.Hide();
            if (choice is not null)
            {
                chosen(choice);
            }
            else if (escaped)
            {
                cancelled();
            }
        };
        _open = menu;
        AppWindow.MoveAndResize(new RectInt32(at.X, at.Y, 1, 1));
        Activate();
        Shell.BringForward(_handle);
        if (_root.IsLoaded)
        {
            Open(menu);
        }
        else
        {
            _root.Loaded += OnLoaded;
        }

        void OnLoaded(object sender, RoutedEventArgs args)
        {
            _root.Loaded -= OnLoaded;
            if (ReferenceEquals(_open, menu))
            {
                Open(menu);
            }
        }
    }

    private void Open(MenuFlyout menu) =>
        menu.ShowAt(_root, new FlyoutShowOptions { Position = new(0, 0), ShowMode = FlyoutShowMode.Standard });
}
