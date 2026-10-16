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

    // Microsoft's contract for CoreWebView2Profile.Delete, which StoreJanitor and the model's hold stand on. CF7: what
    // removes the folder, which outlived its browser process on both runners in run 38825074643.
    [UITestMethod]
    public async Task ADeletedProfilesNameIsRefusedUntilItsFolderGoes()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make();
        var name = webs.Name("gone");
        var page = await webs.Page("gone");
        Assert.IsTrue(await Webs.Loads(page, site.Address("/set")), "the stand-in did not load");
        var folder = webs.Web.ProfileFolder(name);
        Assert.AreEqual(folder, page.Profile.ProfilePath, ignoreCase: true, "where WebView2 keeps a profile");

        await webs.Web.MarkDeleted(name, Webs.Window);

        var refused = await Assert.ThrowsAsync<Exception>(() => webs.Page("gone"));
        Assert.AreEqual(WebEnvironment.DeletePending, refused.HResult, $"0x{refused.HResult:X8}: {refused.Message}");
        Assert.IsTrue(Directory.Exists(folder), "the folder went before the browser process exited");
        Assert.IsTrue(await Task.WhenAny(webs.Exited, Task.Delay(Webs.Patience)) == webs.Exited, "the browser process went on running");
        var gone = await Goes(folder, TimeSpan.FromSeconds(10));
        Console.WriteLine($"CF7: after its browser process exited, a deleted profile's folder {Said(gone, folder, 10)}");
        if (gone is null)
        {
            var exited = false;
            webs.Web.BrowserExited += () => exited = true;
            var started = Stopwatch.StartNew();
            await webs.Web.Made();
            Console.WriteLine(
                $"CF7: a new environment over the user data folder took {started.ElapsedMilliseconds} ms, and the folder was "
                + $"{(Directory.Exists(folder) ? "there" : "gone")} as it was made");
            gone = await Goes(folder, TimeSpan.FromSeconds(20));
            Console.WriteLine(
                $"CF7: with that environment and no page, the folder {Said(gone, folder, 20)}; its browser process exited: {exited}");
        }

        if (gone is null)
        {
            var made = Stopwatch.StartNew();
            _ = await webs.Page("other");
            Console.WriteLine(
                $"CF7: a page of another profile took {made.ElapsedMilliseconds} ms, and the folder was "
                + $"{(Directory.Exists(folder) ? "there" : "gone")} as it was made");
            gone = await Goes(folder, TimeSpan.FromSeconds(20));
            Console.WriteLine($"CF7: with that page, the folder {Said(gone, folder, 20)}");
        }

        Assert.IsNotNull(gone, $"the folder stayed, holding {Entries(folder)}");
        Assert.IsTrue(await Webs.Loads(await webs.Page("gone"), site.Address("/")), "the name made no profile once its folder had gone");
        Assert.IsTrue(Directory.Exists(folder));
    }

    // The janitor holds a store until WebView2 has deleted its folder, which the next ask then finds gone.
    [UITestMethod]
    public async Task TheJanitorHoldsAStoreUntilItsFolderGoes()
    {
        using var site = new StandInSite();
        await using var webs = await Webs.Make();
        var page = await webs.Page("unused");
        Assert.IsTrue(await Webs.Loads(page, site.Address("/set")), "the stand-in did not load");
        var told = new List<(string Store, bool Deleted)>();
        var janitor = new StoreJanitor(
            store => Directory.Exists(webs.Web.ProfileFolder(store)), store => webs.Web.MarkDeleted(store, Webs.Window),
            () => webs.Web.LayoutKnown);
        var unused = webs.Name("unused");
        var folder = webs.Web.ProfileFolder(unused);

        janitor.Delete([new Core.StoreDeletion(unused, 1)], (store, deleted) => told.Add((store, deleted)));
        await Views.Until(() => told.Count == 1, "the janitor did not answer", Webs.Patience);
        Assert.IsTrue(await Task.WhenAny(webs.Exited, Task.Delay(Webs.Patience)) == webs.Exited, "the browser process went on running");
        Assert.IsNotNull(await Goes(folder, Webs.Patience), $"the folder stayed, holding {Entries(folder)}");
        janitor.Delete([new Core.StoreDeletion(unused, 2)], (store, deleted) => told.Add((store, deleted)));

        CollectionAssert.AreEqual(new[] { (unused, false), (unused, true) }, told);
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
