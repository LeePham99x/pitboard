using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

// An account's window over what the model says of the windows, with no model: it sends what a person did.
[TestClass]
public sealed class AccountWindowTests
{
    private static readonly WindowAccount Main = Items.Window("codex", "main", inUse: true);

    private static AccountWindowsShown Shown(WaitingShown waiting, params OpenWindow[] open) =>
        Snapshots.Read(1).AccountWindows with { Accounts = [Main], Open = open, Waiting = new WindowWaiting("Account", waiting) };

    private static async Task<AccountWindow> Up(AccountWindow window)
    {
        var root = (FrameworkElement)window.Content;
        var loaded = new TaskCompletionSource();
        root.Loaded += (_, _) => loaded.TrySetResult();
        window.Activate();
        await loaded.Task.WaitAsync(Webs.Patience);
        return window;
    }

    [UITestMethod]
    public async Task UntilItsPageCanShowItSaysWhatItWaitsFor()
    {
        var sent = new List<Intent>();
        var window = await Up(new AccountWindow(Main.Store, WebEnvironment.Fixture(Path.GetTempPath()), sent.Add));
        try
        {
            window.Follow(Shown(new WaitingShown.Reading("Reading accounts…")));
            await Views.Until(() => Views.Texts(window.Content).Contains("Reading accounts…"), "it did not say it reads the accounts");
            Assert.AreEqual("Account", window.Title);

            var retry = new Choice("Try Again", new Intent.Refresh(Asked: true), Enabled: true);
            window.Follow(Shown(new WaitingShown.ReadFailed("Couldn’t Read Accounts", "The index is damaged.", retry)));
            Views.Press(Views.Button(window.Content, "Try Again"));

            Assert.Contains("The index is damaged.", Views.Texts(window.Content));
            Assert.AreEqual(retry.Intent, sent.Single());
        }
        finally
        {
            window.Close();
        }
    }

    // The page loads in WebView2, over the fixture's stand-ins, and the model hears where it is.
    [UITestMethod]
    public async Task AnOpenWindowShowsItsPageAndHowToSignIn()
    {
        await using var webs = await Webs.Make(WebEnvironment.FixtureScheme);
        var sent = new List<Intent>();
        var window = await Up(new AccountWindow(Main.Store, webs.Web, sent.Add));
        try
        {
            var home = $"{WebEnvironment.FixtureScheme}://chatgpt.com/";
            window.Follow(Shown(
                new WaitingShown.Reading("Reading accounts…"),
                Webs.Opened(Main, home) with { Note = new WindowNoteKind.SignIn() }));

            await Webs.Until(() => sent.Contains(new Intent.PageShown(Main.Store, home)), "the model was not told the page");
            var note = Views.Element<InfoBar>(window.Content, "window.note");
            Assert.IsTrue(note.IsOpen);
            StringAssert.StartsWith(note.Message, "Sign in to chatgpt.com as main@example.com.");
            Assert.AreEqual("main", window.Title);
        }
        finally
        {
            window.Close();
        }
    }
}
