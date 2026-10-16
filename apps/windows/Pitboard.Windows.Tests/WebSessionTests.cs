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
