using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;
using WinRT.Interop;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed partial class WindowChromeTests
{
    private const uint GetIcon = 0x007F;
    private const nuint BigIcon = 1;

    // Without both, a secondary window's title bar stayed light in dark mode, and only the main window had the icon.
    [UITestMethod]
    public void EachWindowFollowsTheAppModeAndCarriesTheIcon()
    {
        var web = WebEnvironment.Fixture(Path.GetTempPath());
        var account = new AccountWindow(Items.Window("codex", "main").Store, web, new Downloads(web, _ => { }), _ => { });
        Window[] windows =
        [
            MainWindowTests.Made(Snapshots.Read(1)),
            account,
            new SignInWindow(new SignInWindowSize(480, 640), account, "Sign in"),
            new OpenLinkWindow(_ => { }, _ => { }, _ => { }),
        ];
        try
        {
            foreach (var window in windows)
            {
                var name = window.GetType().Name;
                Assert.AreEqual(TitleBarTheme.UseDefaultAppMode, window.AppWindow.TitleBar.PreferredTheme, name);
                Assert.AreNotEqual(0, SendMessage(WindowNative.GetWindowHandle(window), GetIcon, BigIcon, 0), $"{name} has no icon");
            }
        }
        finally
        {
            // The sign-in window first: closing its account window destroys it, and WinUI's Close then fails fast.
            foreach (var window in Enumerable.Reverse(windows))
            {
                window.Close();
            }
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint window, uint message, nuint wParam, nint lParam);
}
