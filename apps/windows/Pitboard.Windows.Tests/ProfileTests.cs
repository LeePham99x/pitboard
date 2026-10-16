using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Microsoft.Web.WebView2.Core;

namespace Pitboard.Windows.Tests;

// What the account windows' stores rest on in WebView2 (CF7), against a stand-in site on the loopback, with no model.
[TestClass]
public sealed class ProfileTests
{
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
        await using var webs = await Webs.Make();
        var one = await webs.Page("one");
        var other = await webs.Page("other");

        Assert.IsTrue(await Webs.Loads(one, site.Address("/set")), "the stand-in did not load");
        Assert.IsTrue(await Webs.Loads(other, site.Address("/")), "the stand-in did not load in the other profile");

        Assert.AreEqual("who", (await one.CookieManager.GetCookiesAsync(site.Address("/"))).Single().Name);
        Assert.IsEmpty(await other.CookieManager.GetCookiesAsync(site.Address("/")));
    }

    // Microsoft's contract for CoreWebView2Profile.Delete: the name is refused at once, and the folder goes as the browser
    // process exits or, where something held its files, as a later one starts. A browser process started since refused
    // the name at first even where the folder had gone at that exit (run 38852781290), and took it again later.
    [UITestMethod]
    public async Task ADeletedProfilesNameIsRefusedUntilALaterBrowserProcessLetsItGo()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make();
        var name = webs.Name("gone");
        var page = await webs.Page("gone");
        Assert.IsTrue(await Webs.Loads(page, site.Address("/set")), "the stand-in did not load");
        var folder = webs.Web.ProfileFolder(name);
        Assert.AreEqual(folder, page.Profile.ProfilePath, ignoreCase: true, "where WebView2 keeps a profile");

        await webs.Web.MarkDeleted(name, Webs.Window).WaitAsync(Webs.Patience);

        var refused = await Assert.ThrowsAsync<Exception>(() => webs.Page("gone"));
        Assert.AreEqual(WebEnvironment.DeletePending, refused.HResult, $"0x{refused.HResult:X8}: {refused.Message}");
        Assert.IsTrue(Directory.Exists(folder), "the folder went before the browser process exited");
        Assert.IsTrue(await Task.WhenAny(webs.Exited, Task.Delay(Webs.Patience)) == webs.Exited, "the browser process went on running");
        Console.WriteLine($"CF7: after its browser process exited, a deleted profile's folder {Said(await Goes(folder, TimeSpan.FromSeconds(5)), folder, 5)}");

        _ = await webs.Page("other");
        var started = Stopwatch.StartNew();
        TimeSpan? went = null;
        CoreWebView2? again = null;
        var refusals = 0;
        while (again is null && started.Elapsed < Webs.Patience)
        {
            went ??= Directory.Exists(folder) ? null : started.Elapsed;
            try
            {
                again = await webs.Page("gone");
            }
            catch (Exception pending) when (pending.HResult == WebEnvironment.DeletePending)
            {
                refusals++;
                await Task.Delay(250);
            }
        }

        var took = started.Elapsed;
        Console.WriteLine(
            $"CF7: a page of another profile started a browser process; the folder {(went is { } after ? $"had gone after {after.TotalMilliseconds:0} ms" : "was there at the last look")}, "
            + $"and the name was refused {refusals} times, {(again is null ? "never taken" : $"taken after {took.TotalMilliseconds:0} ms")}");
        Assert.IsNotNull(again, $"the name was refused for {Webs.Patience.TotalSeconds} s, the folder holding {Entries(folder)}");
        Assert.IsTrue(await Webs.Loads(again, site.Address("/")), "the name's new profile loaded nothing");
        Assert.IsEmpty(await again.CookieManager.GetCookiesAsync(site.Address("/")), "the name's new profile kept the old one's cookies");
    }

    // The janitor holds a store while its folder is there, and sweeps it with a browser process of its own once the one
    // that marked it has exited. Once it says the store is deleted, the store's window opens on an empty profile.
    [UITestMethod]
    public async Task TheJanitorHoldsAStoreUntilItsFolderGoes()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make();
        var page = await webs.Page("unused");
        Assert.IsTrue(await Webs.Loads(page, site.Address("/set")), "the stand-in did not load");
        var told = new List<(string Store, bool Deleted)>();
        var janitor = new StoreJanitor(new ProfileFolders(webs.Web, Webs.Window));
        var unused = webs.Name("unused");

        janitor.Delete([new Core.StoreDeletion(unused, 1)], [], (store, deleted) => told.Add((store, deleted)));
        await Views.Until(() => told.Count == 1, "the janitor did not answer", Webs.Patience);
        Assert.IsTrue(await Task.WhenAny(webs.Exited, Task.Delay(Webs.Patience)) == webs.Exited, "the browser process went on running");
        janitor.Delete([new Core.StoreDeletion(unused, 2)], [unused], (store, deleted) => told.Add((store, deleted)));
        await Views.Until(() => told.Count == 2, "the janitor did not answer again", Webs.Patience);

        CollectionAssert.AreEqual(new[] { (unused, false), (unused, true) }, told, Entries(webs.Web.ProfileFolder(unused)));
        var again = await webs.Web.Reusing(Webs.Window, () => webs.Page("unused")).WaitAsync(Webs.Patience);
        Assert.IsTrue(await Webs.Loads(again, site.Address("/")), "the store's new profile loaded nothing");
        Assert.IsEmpty(await again.CookieManager.GetCookiesAsync(site.Address("/")), "the store's new profile kept the old one's cookies");
    }

    // How long a folder took to go, or null where it stayed that long.
    private static async Task<TimeSpan?> Goes(string folder, TimeSpan within)
    {
        var waited = Stopwatch.StartNew();
        while (Directory.Exists(folder))
        {
            if (waited.Elapsed > within)
            {
                return null;
            }

            await Task.Delay(50);
        }

        return waited.Elapsed;
    }

    private static string Said(TimeSpan? gone, string folder, int seconds) =>
        gone is { } after ? $"went after {after.TotalMilliseconds:0} ms" : $"stayed {seconds} s, holding {Entries(folder)}";

    // What is left in a folder, by path within it, the first forty.
    private static string Entries(string folder)
    {
        try
        {
            var entries = Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
                .Select(entry => Path.GetRelativePath(folder, entry)).ToArray();
            return $"{entries.Length} entries: {string.Join(", ", entries.Take(40))}";
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return $"what could not be listed: {unread.Message}";
        }
    }
}
