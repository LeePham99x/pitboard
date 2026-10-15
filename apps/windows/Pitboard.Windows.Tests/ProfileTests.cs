using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Microsoft.Web.WebView2.Core;

namespace Pitboard.Windows.Tests;

// What the account windows' stores rest on in WebView2 (CF7), against a stand-in site on the loopback, with no model.
[TestClass]
public sealed class ProfileTests
{
    [GlobalTestInitialize]
    public static void ReadTheWindowsApartment(TestContext context) =>
        Apartments.Before($"{context.FullyQualifiedTestClassName}.{context.TestName}");

    // CF4: the Runtime of the PC the tests run on is one the windows take.
    [TestMethod]
    public void ThisPCsRuntimeTakesTheWindows()
    {
        Assert.IsNull(WebEnvironment.Refusal(), WebEnvironment.Refusal());
    }

    [UITestMethod]
    public async Task TwoProfilesKeepTheirCookiesApart()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make("https");
        var one = await webs.Page("one");
        var other = await webs.Page("other");

        Assert.IsTrue(await Webs.Loads(one, site.Address("/set")), "the stand-in did not load");
        Assert.IsTrue(await Webs.Loads(other, site.Address("/")), "the stand-in did not load in the other profile");

        Assert.AreEqual("who", (await one.CookieManager.GetCookiesAsync(site.Address("/"))).Single().Name);
        Assert.IsEmpty(await other.CookieManager.GetCookiesAsync(site.Address("/")));
    }

    // Microsoft's contract for CoreWebView2Profile.Delete, which StoreJanitor and the model's hold stand on.
    [UITestMethod]
    public async Task ADeletedProfilesNameIsRefusedUntilItsFolderGoes()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make("https");
        var page = await webs.Page("gone");
        Assert.IsTrue(await Webs.Loads(page, site.Address("/set")), "the stand-in did not load");
        var folder = webs.Web.ProfileFolder("gone");
        Assert.AreEqual(folder, page.Profile.ProfilePath, ignoreCase: true, "where WebView2 keeps a profile");

        await webs.Web.MarkDeleted("gone", Webs.Window);

        var refused = await Assert.ThrowsAsync<Exception>(() => webs.Page("gone"));
        Assert.AreEqual(WebEnvironment.DeletePending, refused.HResult, $"0x{refused.HResult:X8}: {refused.Message}");
        Assert.IsTrue(Directory.Exists(folder), "the folder went before the browser process exited");
        Assert.IsTrue(await Task.WhenAny(webs.Exited, Task.Delay(Webs.Patience)) == webs.Exited, "the browser process went on running");
        var exited = Stopwatch.StartNew();
        await Webs.Until(() => !Directory.Exists(folder), "the folder stayed after the browser process exited");
        Console.WriteLine($"CF7: a deleted profile's folder went {exited.ElapsedMilliseconds} ms after its browser process exited");
        Assert.IsTrue(await Webs.Loads(await webs.Page("gone"), site.Address("/")), "the name made no profile once its folder had gone");
        Assert.IsTrue(Directory.Exists(folder));
    }

    // The janitor holds a store until WebView2 has deleted its folder, which the next ask then finds gone.
    [UITestMethod]
    public async Task TheJanitorHoldsAStoreUntilItsFolderGoes()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make("https");
        var page = await webs.Page("unused");
        Assert.IsTrue(await Webs.Loads(page, site.Address("/set")), "the stand-in did not load");
        var told = new List<(string Store, bool Deleted)>();
        var janitor = new StoreJanitor(
            store => Directory.Exists(webs.Web.ProfileFolder(store)), store => webs.Web.MarkDeleted(store, Webs.Window));

        janitor.Delete([new Core.StoreDeletion("unused", 1)], (store, deleted) => told.Add((store, deleted)));
        await Webs.Until(() => told.Count == 1, "the janitor did not answer");
        Assert.IsTrue(await Task.WhenAny(webs.Exited, Task.Delay(Webs.Patience)) == webs.Exited, "the browser process went on running");
        await Webs.Until(() => !Directory.Exists(webs.Web.ProfileFolder("unused")), "the folder stayed");
        janitor.Delete([new Core.StoreDeletion("unused", 2)], (store, deleted) => told.Add((store, deleted)));

        CollectionAssert.AreEqual(new[] { ("unused", false), ("unused", true) }, told);
    }
}
