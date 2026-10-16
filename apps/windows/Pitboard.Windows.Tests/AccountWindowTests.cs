using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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

    private static DownloadShown Download(string store) =>
        new(
            Id: "1", Store: store, Name: "notes.txt", State: new DownloadState.Finished(@"C:\Downloads\notes.txt"),
            Said: "Downloaded", Running: false);

    [UITestMethod]
    public async Task UntilItsPageCanShowItSaysWhatItWaitsFor()
    {
        var sent = new List<Intent>();
        var web = WebEnvironment.Fixture(Path.GetTempPath());
        var window = await Views.Activated(new AccountWindow(Main.Store, web, new Downloads(web, sent.Add), sent.Add));
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

    // As the macOS toolbar offers its list, the command bar offers WebView2's own once the window has a download.
    [UITestMethod]
    public async Task ItsDownloadsAreOfferedOnceItHasOne()
    {
        var sent = new List<Intent>();
        var web = WebEnvironment.Fixture(Path.GetTempPath());
        var window = await Views.Activated(new AccountWindow(Main.Store, web, new Downloads(web, sent.Add), sent.Add));
        try
        {
            var reading = Shown(new WaitingShown.Reading("Reading accounts…"));
            bool Offered() => Views.Visible<AppBarButton>(window.Content).Any(button => button.Label == "Downloads");
            window.Follow(reading);
            Assert.IsFalse(Offered());

            window.Follow(reading with { Downloads = [Download(Items.Window("codex", "spare").Store)] });
            Assert.IsFalse(Offered(), "another window's download");

            window.Follow(reading with { Downloads = [Download(Main.Store.ToUpperInvariant())] });
            await Views.Until(Offered, "its download was not offered");
        }
        finally
        {
            window.Close();
        }
    }

    // A window closed while it asks about a download answers no, whether the question showed or waited its turn.
    [UITestMethod]
    public async Task ClosingTheWindowAnswersItsDownloadQuestionNo()
    {
        var sent = new List<Intent>();
        var web = WebEnvironment.Fixture(Path.GetTempPath());
        var window = await Views.Activated(new AccountWindow(Main.Store, web, new Downloads(web, sent.Add), sent.Add));
        var root = window.Content.XamlRoot;
        var question = PitboardFfiMethods.DownloadQuestion("notes.txt", "chatgpt.com");
        var shown = window.AllowDownload(question);
        var waiting = window.AllowDownload(question);
        await Views.Until(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0, "the question did not show");

        window.Close();

        Assert.IsFalse(await shown.WaitAsync(Webs.Patience));
        Assert.IsFalse(await waiting.WaitAsync(Webs.Patience));
    }

    // The page loads in WebView2, over the fixture's stand-ins, and the model hears where it is.
    [UITestMethod]
    public async Task AnOpenWindowShowsItsPageAndHowToSignIn()
    {
        await using var webs = await Webs.Make();
        var sent = new List<Intent>();
        var store = webs.Name(Main.Store);
        var window = await Views.Activated(new AccountWindow(store, webs.Web, webs.Downloads, sent.Add));
        try
        {
            var home = $"{WebEnvironment.FixtureScheme}://chatgpt.com/";
            window.Follow(Shown(
                new WaitingShown.Reading("Reading accounts…"),
                webs.Opened(Main, home) with { Note = new WindowNoteKind.SignIn() }));

            await Views.Until(() => sent.Contains(new Intent.PageShown(store, home)), "the model was not told the page", Webs.Patience);
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
