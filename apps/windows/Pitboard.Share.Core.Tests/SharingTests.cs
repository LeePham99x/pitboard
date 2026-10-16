namespace Pitboard.Share.Core.Tests;

[TestClass]
public sealed class SharingTests
{
    private const string SignIn =
        "Pitboard doesn’t open claude.ai sign-in links from outside: one would sign the window in as "
        + "whoever the link belongs to. Sign in inside the account’s claude.ai window.";

    private const string NoLink = "There is no claude.ai or chatgpt.com link in what was shared.";

    [TestMethod]
    public async Task ASitesPageLaunchesItsPitboardLinkAndCompletes()
    {
        var share = new RecordedShare(webLink: "https://chat.openai.com/c/x?y=1#z");

        await Sharing.HandOverAsync(share, "pitboard");

        string[] said = ["started", "launched pitboard://open?url=https%3A%2F%2Fchatgpt.com%2Fc%2Fx%3Fy%3D1%23z", "completed"];
        CollectionAssert.AreEqual(said, share.Said);
    }

    [TestMethod]
    [DataRow("https://claude.ai/magic-link#a:b", SignIn, DisplayName = "a sign-in page")]
    [DataRow("https://example.com/", "Pitboard opens claude.ai and chatgpt.com links only. This link is on example.com.", DisplayName = "another site")]
    [DataRow(null, NoLink, DisplayName = "no web link")]
    public async Task APageItRefusesReportsTheRefusalAndLaunchesNothing(string? webLink, string sentence)
    {
        var share = new RecordedShare(webLink);

        await Sharing.HandOverAsync(share, "pitboard");

        string[] said = ["started", "error: " + sentence];
        CollectionAssert.AreEqual(said, share.Said);
    }

    [TestMethod]
    public async Task ALinkNoAppTakesIsReportedAsAnError()
    {
        var share = new RecordedShare(webLink: "https://chatgpt.com/", launches: false);

        await Sharing.HandOverAsync(share, "pitboard");

        string[] said = ["started", "launched pitboard://open?url=https%3A%2F%2Fchatgpt.com%2F", "error: " + Sharing.NotLaunched];
        CollectionAssert.AreEqual(said, share.Said);
    }

    // The source app gone, or its delayed rendering failed.
    [TestMethod]
    public async Task AShareWhoseLinkCannotBeReadIsReportedAsAnError()
    {
        var share = new RecordedShare(unreadable: true);

        await Sharing.HandOverAsync(share, "pitboard");

        string[] said = ["started", "error: " + Sharing.NotRead];
        CollectionAssert.AreEqual(said, share.Said);
    }

    [TestMethod]
    public async Task ALaunchThatThrowsIsReportedAsAnError()
    {
        var share = new RecordedShare(webLink: "https://chatgpt.com/", launchThrows: true);

        await Sharing.HandOverAsync(share, "pitboard");

        string[] said = ["started", "launched pitboard://open?url=https%3A%2F%2Fchatgpt.com%2F", "error: " + Sharing.NotLaunched];
        CollectionAssert.AreEqual(said, share.Said);
    }

    private sealed class RecordedShare(string? webLink = null, bool launches = true, bool unreadable = false, bool launchThrows = false)
        : IShareOperation
    {
        public List<string> Said { get; } = [];

        public void ReportStarted() => Said.Add("started");

        public Task<string?> WebLinkAsync() =>
            unreadable ? Task.FromException<string?>(new InvalidOperationException("The data is no longer there.")) : Task.FromResult(webLink);

        public Task<bool> LaunchAsync(string link)
        {
            Said.Add("launched " + link);
            return launchThrows ? Task.FromException<bool>(new InvalidOperationException("No app takes the link.")) : Task.FromResult(launches);
        }

        public void ReportCompleted() => Said.Add("completed");

        public void ReportError(string sentence) => Said.Add("error: " + sentence);
    }
}
