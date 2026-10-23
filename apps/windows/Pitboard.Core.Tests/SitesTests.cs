namespace Pitboard.Core.Tests;

// The core tests what its rules say; this proves the records cross.
[TestClass]
public sealed class SitesTests
{
    private static readonly string[] Hosts = ["chatgpt.com", "chat.openai.com", "www.chatgpt.com", "chat.com"];

    [TestMethod]
    public void EachToolsSiteCrossesWithItsFields()
    {
        var chatGPT = PitboardFfiMethods.SitesFor("codex").Single();

        Assert.AreEqual("claude.ai", PitboardFfiMethods.SitesFor("claude").Single().Host);
        Assert.AreEqual("chatgpt.com", chatGPT.Name);
        Assert.AreEqual("codex", chatGPT.Provider);
        Assert.AreEqual("codex", chatGPT.StoreName);
        CollectionAssert.AreEqual(Hosts, chatGPT.Hosts.ToArray());
        Assert.IsEmpty(PitboardFfiMethods.SitesFor("gemini"));
        Assert.AreEqual("claude.ai or chatgpt.com", PitboardFfiMethods.SiteNames(Conjunction.Or));
    }
}
