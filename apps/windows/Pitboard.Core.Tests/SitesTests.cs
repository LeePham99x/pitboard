namespace Pitboard.Core.Tests;

// The core tests what its rules say; these prove the records and refusals cross.
[TestClass]
public sealed class SitesTests
{
    private static readonly string[] Hosts = ["claude.ai", "chatgpt.com"];

    [TestMethod]
    public void TheSitesAreTheCoresInTheirOrder()
    {
        var sites = PitboardFfiMethods.Sites();

        CollectionAssert.AreEqual(Hosts, sites.Select(site => site.Host).ToArray());
        Assert.AreEqual("codex", sites[1].Provider);
        Assert.AreEqual("codex", sites[1].StoreName);
        Assert.AreEqual("chatgpt.com", PitboardFfiMethods.SitesFor("codex").Single().Name);
        Assert.IsEmpty(PitboardFfiMethods.SitesFor("gemini"));
        Assert.AreEqual("claude.ai or chatgpt.com", PitboardFfiMethods.SiteNames(Conjunction.Or));
    }

    [TestMethod]
    public void APitboardLinkReadsAsTheLinkItCarries()
    {
        var link = PitboardFfiMethods.ReadPitboardLink(
            "pitboard://open?url=https%3A%2F%2Fchat.openai.com%2Fc%2Fx%3Fy%3D1%23z", "pitboard");

        Assert.AreEqual("https://chatgpt.com/c/x?y=1#z", link.Url);
        Assert.AreEqual("chatgpt.com", link.Site.Host);
    }

    [TestMethod]
    public void ARefusalCrossesAsItsKind()
    {
        var signIn = Assert.ThrowsExactly<LinkRefusal.SignInLink>(
            () => PitboardFfiMethods.SiteLink("https://claude.ai/magic-link#a"));

        Assert.AreEqual("claude.ai", signIn.host);
        Assert.ThrowsExactly<LinkRefusal.Unreadable>(
            () => PitboardFfiMethods.ReadPitboardLink("pitboard://open?url=a&url=b", "pitboard"));
    }
}
