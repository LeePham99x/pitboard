using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Pitboard.UITests;

// The macOS AccountWindowTests. In a fixture an account's window loads the fixture's stand-in pages, and saves in the
// fixture's folder, so nothing reaches either site or the Downloads of whoever runs them.
[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class AccountWindowTests
{
    [TestMethod]
    [Ignore(Pending.W23a)]
    [ExplorerTakesIcons]
    public void OpeningAClaudeAccountsWindowFromTheMenu()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "onlyOne");

        launched.OpenMenu();
        launched.Choose("Open claude.ai as work");

        Shows(launched.Window("work"), "claude.ai stand-in");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    [ExplorerTakesIcons]
    public void EachSiteOffersItsOwnToolsAccounts()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");

        launched.OpenMenu();
        launched.Named("Open claude.ai").Patterns.ExpandCollapse.Pattern.Expand();
        foreach (var label in new[] { "work", "personal", "old" })
        {
            launched.Find(found => found.ByAutomationId($"window.claude.ai/{label}"), label);
        }

        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("window.claude.ai/main")), "main is offered on claude.ai");
        launched.Named("Open chatgpt.com").Patterns.ExpandCollapse.Pattern.Expand();
        foreach (var label in new[] { "main", "spare" })
        {
            launched.Find(found => found.ByAutomationId($"window.chatgpt.com/{label}"), label);
        }

        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("window.chatgpt.com/work")), "work is offered on chatgpt.com");
    }

    [TestMethod]
    [ExplorerTakesIcons]
    public void NoAccountOffersNoWindow()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "empty");

        launched.OpenMenu();

        Assert.IsTrue(launched.Gone(found => found.ByName("Open claude.ai")), "claude.ai is offered");
        Assert.IsTrue(launched.Gone(found => found.ByName("Open chatgpt.com")), "chatgpt.com is offered");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void OpeningAWindowAgainBringsTheSameOneForward()
    {
        using var launched = OpenChatGPT();

        OpenFromItsRow(launched, "codex/main");

        Assert.IsFalse(
            Retry.WhileFalse(() => Windows(launched, "main") > 1, TimeSpan.FromSeconds(3)).Result, "a second window of main opened");
        Assert.AreEqual(1, Windows(launched, "main"));
    }

    // The link asks for a new window, which WebView2 asks the app for, and the page set there loads it.
    [TestMethod]
    [Ignore(Pending.W23a)]
    public void ASignInLinkOpensASignInWindowThatClosesItself()
    {
        using var launched = OpenChatGPT();
        var window = launched.Window("main");

        Invoke(window, "Continue with appleid.apple.com");
        var signIn = launched.Find(found => found.ByAutomationId("sign-in"), "sign-in window");
        Invoke(signIn, "Done");

        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("sign-in")), "the sign-in window stayed");
        Shows(window, "chatgpt.com stand-in");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void ASignInScriptOpensASignInWindow()
    {
        using var launched = OpenChatGPT();

        Invoke(launched.Window("main"), "Sign in in a window");
        var signIn = launched.Find(found => found.ByAutomationId("sign-in"), "sign-in window");
        Invoke(signIn, "Done");

        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("sign-in")), "the sign-in window stayed");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void ClosingTheAccountsWindowClosesItsSignInWindow()
    {
        using var launched = OpenChatGPT();
        var window = launched.Window("main");
        Invoke(window, "Continue with appleid.apple.com");
        launched.Find(found => found.ByAutomationId("sign-in"), "sign-in window");

        window.Close();

        Assert.IsFalse(Retry.WhileTrue(() => Windows(launched, "main") > 0, TimeSpan.FromSeconds(10)).Result, "the account's window stayed");
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("sign-in")), "the sign-in window stayed");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void GoogleSignInIsStoppedInTheWindow()
    {
        using var launched = OpenChatGPT();
        var window = launched.Window("main");

        Invoke(window, "Continue with Google");

        Assert.IsTrue(Says(window, "window.note", "Pitboard stopped it"), "the window did not say Google was stopped");
        Shows(window, "chatgpt.com stand-in");
    }

    // WebView2 names no frame that started a download, so the site's own asks first; WebView2's own list then names it.
    [TestMethod]
    [Ignore(Pending.W23a)]
    public void ASiteDownloadIsListedInTheWindow()
    {
        using var launched = OpenChatGPT();
        var window = launched.Window("main");

        Invoke(window, "Download notes");
        launched.Invoke("Download");
        Invoke(window, "Downloads");

        var listed = Retry.WhileFalse(
            () => Launched.Lines(window).Any(line => line.Contains("notes.txt", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250), ignoreException: true).Result;
        Assert.IsTrue(listed, "WebView2's download list does not name notes.txt");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void FindShowsTheFindBar()
    {
        using var launched = OpenChatGPT();
        var window = launched.Window("main");
        Launched.Within(window, found => found.ByName("chatgpt.com stand-in").And(found.ByControlType(ControlType.Text)), "the page")
            .Click();

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);

        var bar = Retry.WhileNull(
            () => window.FindFirstDescendant(found => found.ByControlType(ControlType.Edit)),
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(250), ignoreException: true).Result;
        Assert.IsNotNull(bar, "WebView2's find bar did not show");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void RemovingWebsiteDataAsksFirstAndStartsOver()
    {
        using var launched = OpenChatGPT();
        var window = launched.Window("main");
        // The first opening says how to sign in; put it away, and leave the home page.
        var note = Launched.Within(window, found => found.ByAutomationId("window.note"), "note");
        Launched.Within(note, found => found.ByAutomationId("CloseButton"), "the note's close button").Patterns.Invoke.Pattern.Invoke();
        Invoke(window, "A chat");
        Shows(window, "A Pitboard fixture page at /chat/fixture", exact: false);

        Launched.Within(window, found => found.ByAutomationId("MoreButton"), "the command bar's More").Patterns.Invoke.Pattern.Invoke();
        launched.Invoke("Remove Website Data…");
        launched.Invoke("Remove");

        Assert.IsTrue(Says(window, "window.note", "Sign in to chatgpt.com"), "the window did not say how to sign in");
        Shows(window, "A Pitboard fixture page at /.", exact: false);
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void AnAccountsShortcutMenuOpensItsWindow()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");
        launched.OpenWindow();

        OpenFromItsRow(launched, "codex/spare");

        Shows(launched.Window("spare"), "chatgpt.com stand-in");
    }

    // From its row's shortcut menu, which needs no notification-area icon.
    private static Launched OpenChatGPT()
    {
        var launched = Launched.Start(Builds.Debug, fixture: "twoTools");
        launched.OpenWindow();
        OpenFromItsRow(launched, "codex/main");
        Shows(launched.Window("main"), "chatgpt.com stand-in");
        return launched;
    }

    private static void OpenFromItsRow(Launched launched, string qualified)
    {
        launched.Row(qualified).RightClick();
        var label = qualified[(qualified.IndexOf('/', StringComparison.Ordinal) + 1)..];
        launched.Find(found => found.ByAutomationId($"window.chatgpt.com/{label}"), "the row's window item").Click();
    }

    private static int Windows(Launched launched, string title) =>
        FlaUI.Core.Application.Attach(launched.Process).GetAllTopLevelWindows(launched.Automation)
            .Count(window => window.Title == title);

    // A page's link or button, as a person would press it.
    private static void Invoke(AutomationElement scope, string name)
    {
        var found = Retry.WhileNull(
            () => scope.FindFirstDescendant(condition => condition.ByName(name)
                .And(condition.ByControlType(ControlType.Hyperlink).Or(condition.ByControlType(ControlType.Button)))),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250), ignoreException: true).Result;
        Assert.IsNotNull(found, $"no '{name}' in {scope.Name}");
        found.Patterns.Invoke.Pattern.Invoke();
    }

    private static void Shows(AutomationElement window, string text, bool exact = true)
    {
        var shown = Retry.WhileFalse(
            () => Launched.Lines(window).Any(line => exact ? line == text : line.StartsWith(text, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250), ignoreException: true).Result;
        Assert.IsTrue(shown, $"{window.Name} does not show '{text}'");
    }

    private static bool Says(AutomationElement window, string automationId, string text) =>
        Retry.WhileFalse(
            () => window.FindFirstDescendant(found => found.ByAutomationId(automationId)) is { } element
                && Launched.Lines(element).Any(line => line.Contains(text, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250), ignoreException: true).Result;
}
