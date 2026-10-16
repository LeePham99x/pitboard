using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Microsoft.Web.WebView2.Core;
using Pitboard.Core;

namespace Pitboard.Windows.Tests;

// An account's window as WebView2 runs it, over the fixture's stand-in pages and a stand-in site on the loopback, with
// no model.
[TestClass]
public sealed class WebSessionTests
{
    private static readonly WindowAccount Main = Items.Window("codex", "main", inUse: true);

    private static readonly string Home = $"{WebEnvironment.FixtureScheme}://chatgpt.com/";

    [UITestMethod]
    public async Task APageGoesWhereThePolicySays()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make();
        var window = new StandInWindow(webs, Main);
        var session = new WebSession(webs.Opened(Main, Home), webs.Web, webs.Downloads, window);
        var page = await webs.Page(Main.Store);

        Assert.IsTrue(await Webs.Attached(session, page), "the fixture's page did not load");
        Assert.AreEqual("chatgpt.com stand-in", page.DocumentTitle);

        // WebView2 counts a navigation a host's script starts as the person's, so the page's own script leaves.
        var leave = $"{WebEnvironment.FixtureScheme}://chatgpt.com/leave";
        var away = site.Address("/away");
        page.WebResourceRequested += (_, args) =>
        {
            if (args.Request.Uri == leave)
            {
                var body = $"<!doctype html><title>leaving</title><script>location.href = '{away}'</script>";
                args.Response = page.Environment.CreateWebResourceResponse(
                    new MemoryStream(Encoding.UTF8.GetBytes(body)).AsRandomAccessStream(), 200, "OK",
                    "Content-Type: text/html; charset=utf-8");
            }
        };
        bool? awayClicked = null;
        page.NavigationStarting += (_, args) =>
        {
            if (args.Uri == away)
            {
                awayClicked = args.IsUserInitiated;
            }
        };
        await page.ExecuteScriptAsync($"location.href = '{leave}'");
        await Views.Until(() => webs.Elsewhere.Count == 1, "a page outside the site was not handed to the browser", Webs.Patience);
        Assert.AreEqual(away, webs.Elsewhere[0]);
        Console.WriteLine($"CF7: a page's own script leaving is user-initiated: {awayClicked}");
        Assert.IsInstanceOfType<WindowNoteKind.OpenedInBrowser>(session.Note, $"user-initiated: {awayClicked}");

        await page.ExecuteScriptAsync("location.href = 'https://accounts.google.com/o/oauth2/v2/auth'");
        await Views.Until(() => session.Note is WindowNoteKind.GoogleRefused, "Google's sign-in was not refused", Webs.Patience);

        var chat = $"{WebEnvironment.FixtureScheme}://chatgpt.com/chat/fixture";
        Assert.IsTrue(await Webs.Loads(page, async () => await page.ExecuteScriptAsync($"location.href = '{chat}'")));
        Assert.AreEqual(chat, page.Source);
        CollectionAssert.Contains(window.Pages, chat);
        Assert.HasCount(1, webs.Elsewhere);
        Assert.IsEmpty(site.Asked, $"the stand-in was asked for {string.Join(", ", site.Asked)}");
    }

    // The sign-in window's page is set as the new window before it loads, which is what keeps window.opener.
    [UITestMethod]
    public async Task ASignInWindowKeepsItsOpenerAndClosesWhenItsPageIsDone()
    {
        await using var webs = await Webs.Make();
        var window = new StandInWindow(webs, Main);
        var session = new WebSession(webs.Opened(Main, Home), webs.Web, webs.Downloads, window);
        var page = await webs.Page(Main.Store);
        Assert.IsTrue(await Webs.Attached(session, page), "the fixture's page did not load");
        var host = Main.Site.SignInHosts[0];

        await page.ExecuteScriptAsync(
            $"window.open('{WebEnvironment.FixtureScheme}://{host}/sign-in', 'sign-in', 'width=480,height=600'); 0");

        await Views.Until(() => window.SignIn?.DocumentTitle == $"{host} sign-in stand-in", "no sign-in window loaded its page", Webs.Patience);
        Assert.AreEqual(new SignInWindowSize(480, 600), window.Sizes.Single());
        Assert.AreEqual("true", await window.SignIn!.ExecuteScriptAsync("window.opener !== null"));
        await window.SignIn.ExecuteScriptAsync("document.getElementById('done').click()");
        await Views.Until(() => window.Closed == 1, "the sign-in page's Done did not close its window", Webs.Patience);
        Assert.AreEqual(1, window.Closed);
    }

    // A render process that ends reloads its page, unless it ended within the minute before on the session's clock.
    [UITestMethod]
    public async Task APageWhoseRenderProcessEndsReloadsUnlessItEndedAMinuteBefore()
    {
        await using var webs = await Webs.Make();
        var clock = new Clock(DateTimeOffset.UnixEpoch);
        var session = new WebSession(webs.Opened(Main, Home), webs.Web, webs.Downloads, new StandInWindow(webs, Main), clock);
        var page = await webs.Page(Main.Store);
        Assert.IsTrue(await Webs.Attached(session, page), "the fixture's page did not load");

        Assert.IsTrue(await Webs.Loads(page, () => EndRenderers(page)), "the page did not reload after its render process ended");
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.IsTrue(await Webs.Loads(page, () => EndRenderers(page)), "the page did not reload two minutes later");
        clock.Now += TimeSpan.FromSeconds(30);
        await EndRenderers(page);

        await Views.Until(() => session.Failure is not null, "the window did not stop after two ends within a minute", Webs.Patience);
        Assert.AreEqual("The page stopped working.", session.Failure);
        Assert.IsFalse(session.PageClosed);
    }

    // WebView2 closes every page with its browser process, and a page of its own brings the window back where it was.
    [UITestMethod]
    public async Task APageWhoseBrowserProcessEndsComesBackInANewOne()
    {
        await using var webs = await Webs.Make();
        var session = new WebSession(webs.Opened(Main, Home), webs.Web, webs.Downloads, new StandInWindow(webs, Main));
        var page = await webs.Page(Main.Store);
        Assert.IsTrue(await Webs.Attached(session, page), "the fixture's page did not load");
        var chat = $"{WebEnvironment.FixtureScheme}://chatgpt.com/chat/fixture";
        Assert.IsTrue(await Webs.Loads(page, chat), "the chat did not load");

        using (var browser = Process.GetProcessById((int)page.BrowserProcessId))
        {
            browser.Kill();
        }

        await Views.Until(() => session.PageClosed, "the session did not see its page closed", Webs.Patience);
        Assert.AreEqual("The page stopped working.", session.Failure);
        Assert.IsTrue(await Task.WhenAny(webs.Exited, Task.Delay(Webs.Patience)) == webs.Exited, "the browser process did not exit");
        var again = await webs.Page(Main.Store);
        Assert.IsTrue(
            await Webs.Loads(again, () =>
            {
                session.Recover(again);
                return Task.CompletedTask;
            }),
            "the new page did not load");
        Assert.AreEqual(chat, again.Source);
        Assert.IsNull(session.Failure);
        Assert.IsFalse(session.PageClosed);
    }

    // As a crash ends them: every render process of the test's browser process, this page's among them.
    private static Task EndRenderers(CoreWebView2 page)
    {
        foreach (var renderer in page.Environment.GetProcessInfos().Where(info => info.Kind == CoreWebView2ProcessKind.Renderer))
        {
            using var process = Process.GetProcessById(renderer.ProcessId);
            process.Kill();
        }

        return Task.CompletedTask;
    }

    // Each profile's data comes from the stand-in, which only a page outside any session can load.
    [UITestMethod]
    public async Task RemovingWebsiteDataEmptiesTheWindowsProfileAlone()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make();
        var spare = Items.Window("codex", "spare");
        var page = await webs.Page(Main.Store);
        var other = await webs.Page(spare.Store);
        Assert.IsTrue(await Webs.Loads(page, site.Address("/set")), "the stand-in did not load");
        Assert.IsTrue(await Webs.Loads(other, site.Address("/set")), "the stand-in did not load in the other profile");
        var session = new WebSession(webs.Opened(Main, Home), webs.Web, webs.Downloads, new StandInWindow(webs, Main));
        Assert.IsTrue(await Webs.Attached(session, page), "the fixture's page did not load");

        await session.RemoveWebsiteData();

        Assert.IsEmpty(await page.CookieManager.GetCookiesAsync(site.Address("/")));
        Assert.AreEqual("who", (await other.CookieManager.GetCookiesAsync(site.Address("/"))).Single().Name);
    }
}
