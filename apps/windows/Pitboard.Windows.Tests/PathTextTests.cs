namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class PathTextTests
{
    private const string Detail =
        @"Claude Code has not run on this machine yet (C:\Users\pbadmin\AppData\Local\Temp\pitboard-fixture\home\.claude.json does not exist). Run claude once, sign in, then try again.";

    // This PC broke the config file's path before "\home", with "pitboard-fixture" free to break at its hyphen.
    [TestMethod]
    public void APathBreaksOnlyAfterABackslash()
    {
        Assert.AreEqual(
            "(C:\u2060\\\u200BUsers\u2060\\\u200Bpbadmin\u2060\\\u200Bpitboard-\u2060fixture\u2060\\\u200B.claude.json",
            PathText.Shown(@"(C:\Users\pbadmin\pitboard-fixture\.claude.json"));
        Assert.AreEqual(@"\\server\share", PathText.Shown(@"\\server\share").Replace("\u2060", "").Replace("\u200B", ""));
        Assert.AreEqual("\\\\\u200Bserver\u2060\\\u200Bshare", PathText.Shown(@"\\server\share"));
    }

    [TestMethod]
    public void WhatIsNoPathIsShownAsItIs()
    {
        foreach (var text in new[] { "Credential Manager  ·  Claude Code-credentials/claude-code-user", "Warm-ups are not available", "", "  " })
        {
            Assert.AreEqual(text, PathText.Shown(text));
        }

        Assert.AreEqual("", PathText.Shown(null));
    }

    [TestMethod]
    public void CopyingGivesBackWhatWasShown()
    {
        Assert.AreEqual(Detail, PathText.Copied(PathText.Shown(Detail)));
        Assert.AreEqual(@"C:\Program Files\ClaudeCode\claude.exe", PathText.Copied(PathText.Shown(@"C:\Program Files\ClaudeCode\claude.exe")));
    }
}
