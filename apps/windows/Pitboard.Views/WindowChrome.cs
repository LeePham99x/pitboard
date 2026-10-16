using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Pitboard.Windows;

namespace Pitboard.Views;

// What each of Pitboard's windows carries: the app's icon, which WinUI's window class has none of, and a title bar in
// the person's app mode, where it is otherwise light whatever the mode.
internal static class WindowChrome
{
    public static void Apply(Window window)
    {
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, WindowsNotifications.IconFile));
        window.AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
    }
}
