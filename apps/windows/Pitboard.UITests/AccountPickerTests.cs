using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;

namespace Pitboard.UITests;

// The macOS AccountPickerTests. Each link goes to a second Debug copy in the same fixture, which hands it to the one
// running.
[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class AccountPickerTests
{
    [TestMethod]
    public void ASharedLinkAsksWhichAccountOpensIt()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");

        var picker = launched.Share("https://chatgpt.com/c/shared");

        Launched.Shows(picker, "Open this chatgpt.com link as:");
        // Each row is one element for Narrator, its label and email read together.
        var spare = launched.Find(found => found.ByAutomationId("picker.account.spare"), "spare's row");
        launched.Find(found => found.ByAutomationId("picker.account.main"), "main's row");
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("picker.account.work")), "only the site's own accounts");
        Assert.AreEqual(0, AccountWindows(launched), "nothing opens before a choice");

        spare.Patterns.SelectionItem.Pattern.Select();
        // Open waits a moment after the accounts appear, so a stray Enter opens nothing.
        Assert.IsTrue(launched.Enables("Open"), "Open was held back");
        Launched.Press(picker, "Open");

        Launched.Loads(launched.AccountWindow("spare"), "A Pitboard fixture page at /c/shared", exact: false);
        Assert.IsTrue(Closed(picker), "the Open link window stayed");
    }

    [TestMethod]
    public void ALinkToAnotherSiteIsRefused()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "empty");

        var picker = launched.Share("https://example.com/page");

        Launched.Shows(picker, "Can’t open this link");
        Assert.IsTrue(
            Launched.Lines(picker).Any(line => line.Contains("This link is on example.com.", StringComparison.Ordinal)),
            string.Join(" | ", Launched.Lines(picker)));
        Launched.Press(picker, "OK");
        Assert.IsTrue(Closed(picker), "the Open link window stayed");
        Assert.AreEqual(0, AccountWindows(launched));
    }

    [TestMethod]
    public void CancellingOpensNothing()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "onlyOne");

        var picker = launched.Share("https://claude.ai/new");

        Launched.Shows(picker, "Open this claude.ai link as:");
        Launched.Press(picker, "Cancel");
        Assert.IsTrue(Closed(picker), "the Open link window stayed");
        Assert.AreEqual(0, AccountWindows(launched));
    }

    [TestMethod]
    public void ALinkForASiteWithNoAccountOffersToAddOne()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "empty");

        var picker = launched.Share("https://chatgpt.com/");

        Launched.Shows(picker, "No chatgpt.com account");
        Assert.IsNotNull(Launched.Pressable(picker, "Add account…"));
        Assert.IsNotNull(Launched.Pressable(picker, "Open in browser"));
        Launched.Press(picker, "Cancel");
        Assert.IsTrue(Closed(picker), "the Open link window stayed");
    }

    // Someone else's sign-in link is refused: it would sign the window in as them.
    [TestMethod]
    public void ASignInLinkIsRefused()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "empty");

        var picker = launched.Share("https://claude.ai/magic-link#someone");

        Launched.Shows(picker, "Can’t open this link");
        Launched.Shows(picker, "Pitboard doesn’t open claude.ai sign-in links", exact: false);
        Launched.Press(picker, "OK");
        Assert.IsTrue(Closed(picker), "the Open link window stayed");
    }

    // Outside a fixture a Debug build runs on whatever homes it was given, so it opens no link there.
    [TestMethod]
    public void ADebugBuildOutsideAFixtureRefusesALinkAndWritesNothing()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: null, Launched.Link("https://claude.ai/new"));

        var box = launched.Window("Pitboard");

        StringAssert.Contains(Launched.Said(box), "only in a fixture");
        Launched.Dismiss(box);
        Assert.IsTrue(launched.Process.WaitForExit(TimeSpan.FromSeconds(10)), "Pitboard went on running");
        Assert.AreEqual(1, launched.Process.ExitCode);
        Assert.AreEqual("", string.Join(", ", launched.Written()));
    }

    private static bool Closed(AutomationElement picker) =>
        Retry.WhileTrue(() => picker.IsAvailable, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250), ignoreException: true).Result;

    // Every window but the Open link window and Pitboard's own is an account's.
    private static int AccountWindows(Launched launched) => launched.Titles().Count(title => title is not ("Open link" or "Pitboard"));
}
