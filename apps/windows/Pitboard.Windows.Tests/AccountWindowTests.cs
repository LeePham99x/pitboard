using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Microsoft.Web.WebView2.Core;
using Pitboard.Core;
using Pitboard.Views;
using WinRT.Interop;

namespace Pitboard.Windows.Tests;

// An account's window over what the model says of the windows, with no model: it sends what a person did.
[TestClass]
public sealed partial class AccountWindowTests
{
    private const uint Owner = 4;

    private static readonly WindowAccount Main = Items.Window("codex", "main", inUse: true);

    private static AccountWindowsShown Shown(WaitingShown waiting, params OpenWindow[] open) =>
        Snapshots.Read(1).AccountWindows with { Accounts = [Main], Open = open, Waiting = new WindowWaiting("Account", waiting) };

    private static readonly string Home = $"{WebEnvironment.FixtureScheme}://chatgpt.com/";

    private static readonly string Chat = $"{WebEnvironment.FixtureScheme}://chatgpt.com/chat/fixture";

    // The window's page, in the web view it shows.
    private static CoreWebView2? Page(AccountWindow window) =>
        Views.Descendants(window.Content).OfType<WebView2>().SingleOrDefault()?.CoreWebView2;

    // A button of the dialog shown over the window.
    private static async Task<Button> DialogButton(AccountWindow window, string content)
    {
        Button? found = null;
        await Views.Until(
            () => (found = VisualTreeHelper.GetOpenPopupsForXamlRoot(window.Content.XamlRoot)
                .Select(popup => popup.Child).OfType<UIElement>()
                .SelectMany(Views.Descendants).OfType<Button>()
                .FirstOrDefault(button => button.Content as string == content)) is not null,
            $"no dialog offered {content}");
        return found!;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint relation);

    // From the command bar's More, as a person chooses it.
    private static void RemoveWebsiteData(AccountWindow window)
    {
        var bar = Views.Descendants(window.Content).OfType<CommandBar>().Single();
        bar.IsOpen = true;
        Views.Press(bar.SecondaryCommands.OfType<AppBarButton>().Single(command => command.Label == "Remove website data…"));
    }

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

            var retry = new Choice("Try again", new Intent.Refresh(Asked: true), Enabled: true);
            window.Follow(Shown(new WaitingShown.ReadFailed("Couldn’t read accounts", "The index is damaged.", retry)));
            Views.Press(Views.Button(window.Content, "Try again"));

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
        }
        finally
        {
            window.Close();
        }
    }

    // Narrator hears an InfoBar as it opens, and the macOS window announces each note, so a note in place of another opens
    // the bar again.
    [UITestMethod]
    public async Task ANoteInPlaceOfAnotherOpensItsBarAgain()
    {
        await using var webs = await Webs.Make();
        var sent = new List<Intent>();
        var store = webs.Name(Main.Store);
        var window = await Views.Activated(new AccountWindow(store, webs.Web, webs.Downloads, sent.Add));
        try
        {
            window.Follow(Shown(
                new WaitingShown.Reading("Reading accounts…"),
                webs.Opened(Main, Home) with { Note = new WindowNoteKind.SignIn() }));
            await Views.Until(() => sent.Contains(new Intent.PageShown(store, Home)), "the page did not load", Webs.Patience);
            var note = Views.Element<InfoBar>(window.Content, "window.note");
            Assert.IsTrue(note.IsOpen);
            var opened = new List<bool>();
            note.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (_, _) => opened.Add(note.IsOpen));

            await Page(window)!.Script("location.href = 'https://accounts.google.com/o/oauth2/v2/auth'; 0");

            await Views.Until(() => note.Message.StartsWith("Google does not allow", StringComparison.Ordinal), "Google's refusal was not said", Webs.Patience);
            Assert.AreEqual("closed, opened", string.Join(", ", opened.Select(open => open ? "opened" : "closed")));
        }
        finally
        {
            window.Close();
        }
    }

    // Microsoft's title bar text guidance names the document before the program, where macOS's subtitle names the page.
    [UITestMethod]
    public async Task ItsTitleIsItsPagesAndThenItsAccounts()
    {
        await using var webs = await Webs.Make();
        var sent = new List<Intent>();
        var store = webs.Name(Main.Store);
        var window = await Views.Activated(new AccountWindow(store, webs.Web, webs.Downloads, sent.Add));
        try
        {
            window.Follow(Shown(new WaitingShown.Reading("Reading accounts…"), webs.Opened(Main, Home)));
            await Views.Until(() => window.Title == "chatgpt.com stand-in - main", () => $"the window is titled {window.Title}", Webs.Patience);

            await Page(window)!.Script("document.title = 'A chat'; 0");

            await Views.Until(() => window.Title == "A chat - main", () => $"the window is titled {window.Title}", Webs.Patience);
        }
        finally
        {
            window.Close();
        }
    }

    // Microsoft's keyboard guidance gives Ctrl+W to Close. Back, Forward and Reload take the keys WebView2 gives them in
    // the page, and the main window gives Refresh.
    [UITestMethod]
    public async Task TheWindowTakesTheKeysOfItsCommands()
    {
        var sent = new List<Intent>();
        var web = WebEnvironment.Fixture(Path.GetTempPath());
        var window = await Views.Activated(new AccountWindow(Main.Store, web, new Downloads(web, sent.Add), sent.Add));
        try
        {
            var root = (UIElement)window.Content;

            Assert.AreEqual(
                "Control+W, Menu+Left, Menu+Right, None+F5, Control+R",
                string.Join(", ", root.KeyboardAccelerators.Select(key => $"{key.Modifiers}+{key.Key}")));
        }
        finally
        {
            window.Close();
        }
    }

    // A window whose web view cannot be made says why, rather than staying blank: WebView2 takes no profile name that
    // ends with a period. A user data folder it cannot write would leave its browser process up behind an error dialog.
    [UITestMethod]
    public async Task AWindowWhoseWebViewCannotBeMadeSaysWhy()
    {
        await using var webs = await Webs.Make();
        var sent = new List<Intent>();
        var refused = Main with { Store = "no profile name." };
        var window = await Views.Activated(new AccountWindow(refused.Store, webs.Web, webs.Downloads, sent.Add));
        try
        {
            window.Follow(Shown(
                new WaitingShown.Reading("Reading accounts…"),
                new OpenWindow(
                    Store: refused.Store, Account: refused, Load: new PageLoad(1, Home), Note: null,
                    ClearDownloads: new Choice("Clear", new Intent.ClearDownloads(refused.Store), Enabled: false))));

            await Views.Until(
                () => Views.Texts(window.Content).Contains("Couldn’t open this window"), "the window did not say it could not open",
                Webs.Patience);
            Assert.DoesNotContain("Object reference not set to an instance of an object.", Views.Texts(window.Content));
        }
        finally
        {
            window.Close();
        }
    }

    // Right after its store's profile was deleted, WebView2 refuses the name for the first seconds of the next browser
    // process; the window waits for it rather than saying it could not open, and opens with none of the old cookies.
    [UITestMethod]
    public async Task AWindowWhoseProfileIsStillBeingDeletedOpensOnceWebView2TakesItsName()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make();
        var store = webs.Name(Main.Store);
        Assert.IsTrue(await Webs.Loads(await webs.Page(Main.Store), site.Address("/set")), "the stand-in did not load");
        await webs.Web.MarkDeleted(store, Webs.Window).WaitAsync(Webs.Patience);
        Assert.IsTrue(await Task.WhenAny(webs.Exited, Task.Delay(Webs.Patience)) == webs.Exited, "the browser process went on running");
        var sent = new List<Intent>();
        var window = await Views.Activated(new AccountWindow(store, webs.Web, webs.Downloads, sent.Add));
        try
        {
            window.Follow(Shown(new WaitingShown.Reading("Reading accounts…"), webs.Opened(Main, Home)));

            await Views.Until(
                () => sent.Contains(new Intent.PageShown(store, Home)) || Views.Texts(window.Content).Contains("Couldn’t open this window"),
                "the window neither opened nor said why", Webs.Patience);
            Assert.Contains(new Intent.PageShown(store, Home), sent, string.Join(" ", Views.Texts(window.Content)));
            Assert.IsEmpty(await Page(window)!.CookieManager.GetCookiesAsync(site.Address("/")));
        }
        finally
        {
            window.Close();
        }
    }

    // A page's window.open opens the sign-in window, a WinUI web view set as WebView2's new window, which keeps
    // window.opener and closes once its page is done.
    [UITestMethod]
    public async Task ASignInWindowKeepsItsOpenerAndClosesOnDone()
    {
        await using var webs = await Webs.Make();
        var sent = new List<Intent>();
        var store = webs.Name(Main.Store);
        var window = await Views.Activated(new AccountWindow(store, webs.Web, webs.Downloads, sent.Add));
        try
        {
            window.Follow(Shown(new WaitingShown.Reading("Reading accounts…"), webs.Opened(Main, Home)));
            await Views.Until(() => sent.Contains(new Intent.PageShown(store, Home)), "the page did not load", Webs.Patience);
            var host = Main.Site.SignInHosts[0];

            await Page(window)!.Script(
                $"window.open('{WebEnvironment.FixtureScheme}://{host}/sign-in', 'sign-in', 'width=480,height=600'); 0");

            await Views.Until(
                () => window.SignIn?.WebPage?.DocumentTitle == $"{host} sign-in stand-in", "no sign-in window loaded its page",
                Webs.Patience);
            var signIn = window.SignIn!;
            Assert.AreEqual("true", await signIn.WebPage!.Script("window.opener !== null"));
            var closed = false;
            signIn.Closed += (_, _) => closed = true;
            await signIn.WebPage!.Script("document.getElementById('done').click()");
            await Views.Until(() => closed, "the sign-in page's Done did not close its window", Webs.Patience);
            Assert.IsNull(window.SignIn);
        }
        finally
        {
            window.Close();
        }
    }

    // Microsoft's window guidance: a window that belongs to another is owned by it, so it stays above it and goes with it.
    [UITestMethod]
    public async Task ASignInWindowIsOwnedByItsAccountWindow()
    {
        await using var webs = await Webs.Make();
        var sent = new List<Intent>();
        var store = webs.Name(Main.Store);
        var window = await Views.Activated(new AccountWindow(store, webs.Web, webs.Downloads, sent.Add));
        try
        {
            window.Follow(Shown(new WaitingShown.Reading("Reading accounts…"), webs.Opened(Main, Home)));
            await Views.Until(() => sent.Contains(new Intent.PageShown(store, Home)), "the page did not load", Webs.Patience);

            await Page(window)!.Script(
                $"window.open('{WebEnvironment.FixtureScheme}://{Main.Site.SignInHosts[0]}/sign-in', 'sign-in', 'width=480,height=600'); 0");

            await Views.Until(() => window.SignIn is not null, "no sign-in window opened", Webs.Patience);
            Assert.AreEqual(WindowNative.GetWindowHandle(window), GetWindow(WindowNative.GetWindowHandle(window.SignIn!), Owner));
        }
        finally
        {
            window.Close();
        }
    }

    // Remove website data asks first, empties the window's profile, and starts again at the site's home in a web view
    // of its own, so Back cannot bring the signed-in page back.
    [UITestMethod]
    public async Task RemovingWebsiteDataAsksAndStartsAgainAtHome()
    {
        await using var webs = await Webs.Make();
        var sent = new List<Intent>();
        var store = webs.Name(Main.Store);
        var window = await Views.Activated(new AccountWindow(store, webs.Web, webs.Downloads, sent.Add));
        try
        {
            window.Follow(Shown(new WaitingShown.Reading("Reading accounts…"), webs.Opened(Main, Chat)));
            await Views.Until(() => sent.Contains(new Intent.PageShown(store, Chat)), "the chat did not load", Webs.Patience);
            var first = Page(window)!;
            first.CookieManager.AddOrUpdateCookie(first.CookieManager.CreateCookie("who", "here", "chatgpt.com", "/"));
            Assert.HasCount(1, await first.CookieManager.GetCookiesAsync("https://chatgpt.com/"));

            RemoveWebsiteData(window);
            Views.Press(await DialogButton(window, "Remove"));

            await Views.Until(() => sent.Contains(new Intent.WebsiteDataRemoved(store)), "the model was not told", Webs.Patience);
            await Views.Until(
                () => Page(window) is { } page && page != first && page.Source == Home, "the window did not start again at home",
                Webs.Patience);
            Assert.IsEmpty(await Page(window)!.CookieManager.GetCookiesAsync("https://chatgpt.com/"));
            Assert.IsFalse(Page(window)!.CanGoBack, "Back would bring the signed-in page back");
        }
        finally
        {
            window.Close();
        }
    }

    // Closed while its page leaves for a blank one, the window leaves the data and tells the model nothing.
    [UITestMethod]
    public async Task AWindowClosedWhileItsDataGoesKeepsItAndSaysNothing()
    {
        await using var webs = await Webs.Make();
        var sent = new List<Intent>();
        var store = webs.Name(Main.Store);
        var window = await Views.Activated(new AccountWindow(store, webs.Web, webs.Downloads, sent.Add));
        window.Follow(Shown(new WaitingShown.Reading("Reading accounts…"), webs.Opened(Main, Home)));
        await Views.Until(() => sent.Contains(new Intent.PageShown(store, Home)), "the page did not load", Webs.Patience);

        RemoveWebsiteData(window);
        Views.Press(await DialogButton(window, "Remove"));
        window.Close();
        await window.Removing!.WaitAsync(Webs.Patience);

        Assert.IsFalse(sent.Contains(new Intent.WebsiteDataRemoved(store)));
    }
}
