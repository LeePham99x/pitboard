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

// The window a page of an account's window opens for its site's sign-in, over that window and in its profile, so what
// it signs in is that account's. Its page closes it once the sign-in is done.
public sealed partial class SignInWindow : Window
{
    private readonly WebView2 _view = new();

    public SignInWindow(SignInWindowSize size, Window over, string title)
    {
        Title = title;
        // What UI Automation finds the window by, since its title is its page's.
        AutomationProperties.SetAutomationId(_view, "sign-in");
        Content = _view;
        WindowChrome.Apply(this);
        Shell.Own(WindowNative.GetWindowHandle(this), WindowNative.GetWindowHandle(over));
        var scale = Shell.Scale(WindowNative.GetWindowHandle(this));
        var physical = new SizeInt32((int)(size.Width * scale), (int)(size.Height * scale));
        var parent = over.AppWindow;
        AppWindow.MoveAndResize(new RectInt32(
            parent.Position.X + ((parent.Size.Width - physical.Width) / 2),
            parent.Position.Y + ((parent.Size.Height - physical.Height) / 2),
            physical.Width,
            physical.Height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        Closed += (_, _) => _view.Close();
    }

    internal CoreWebView2? WebPage => _view.CoreWebView2;

    // Before it has loaded anything, as WebView2 takes a new window's page.
    public async Task<CoreWebView2> Page(CoreWebView2Environment environment, CoreWebView2ControllerOptions options)
    {
        var page = await WebViews.Made(_view, environment, options);
        page.DocumentTitleChanged += (sender, _) =>
        {
            if (sender.DocumentTitle.Length > 0)
            {
                Title = sender.DocumentTitle;
            }
        };
        return page;
    }
}
