using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class AccountsWindowTests
{
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void TheWindowListsEachAccount()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");
        launched.OpenWindow();

        launched.Row("claude/work");
        launched.Row("codex/main");
    }

    // Open is never pressed: it would open the fixture's address in a real browser.
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void AddingAClaudeCodeAccount()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        launched.OpenWindow();

        launched.Invoke("Add account");
        launched.Field("sheet.name").Enter("third");
        launched.Invoke("Sign in");
        var code = launched.Field("sheet.code");
        Assert.IsTrue(launched.Enables("Open"), "Open stayed dimmed");
        launched.Invoke("Copy link");
        Assert.AreEqual("https://claude.com/cai/oauth/authorize?code=true&state=pitboard-fixture", ClipboardText.Read());
        code.Enter("fixture-code#state");
        launched.Invoke("Submit code");

        launched.Row("claude/third");
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("sheet.code")), "the sheet stayed up");
    }

    // A Codex sign-in finishes by itself once the browser is done.
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void AddingACodexAccount()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");
        launched.OpenWindow();

        launched.Invoke("Add account");
        launched.Find(found => found.ByAutomationId("sheet.tool"), "sheet.tool").AsComboBox().Select("Codex");
        launched.Field("sheet.name").Enter("third");
        launched.Invoke("Sign in");

        launched.Row("codex/third");
    }

    // Sign In stays dimmed while a sign-in runs, so a second one finishing shows the first was stopped.
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void CancellingASignInAddsNothing()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        launched.OpenWindow();

        launched.Invoke("Add account");
        launched.Field("sheet.name").Enter("third");
        launched.Invoke("Sign in");
        launched.Field("sheet.code");
        launched.Invoke("Cancel");
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("sheet.code")), "the sheet stayed up");
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("account.claude/third")), "third was added");

        launched.Invoke("Add account");
        launched.Field("sheet.name").Enter("third");
        launched.Invoke("Sign in");
        launched.Field("sheet.code").Enter("fixture-code#state");
        launched.Invoke("Submit code");

        launched.Row("claude/third");
    }

    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void RenamingAnAccount()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        launched.OpenWindow();

        launched.Row("claude/personal").RightClick();
        launched.Named("Rename…").Click();
        var name = launched.Field("sheet.name");
        name.Enter("work");
        launched.Invoke("Rename");
        launched.Named("Couldn’t rename personal");
        name.Enter("home");
        launched.Invoke("Rename");

        launched.Row("claude/home");
    }

    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void ForgettingAsksFirst()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        launched.OpenWindow();
        var row = launched.Row("claude/personal");

        row.RightClick();
        launched.Named("Forget…").Click();
        launched.Named("Forget");
        launched.Invoke("Cancel");
        Assert.IsTrue(row.IsAvailable, "forgotten after Cancel");

        row.RightClick();
        launched.Named("Forget…").Click();
        launched.Invoke("Forget");
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("account.claude/personal")), "personal is still listed");
    }

    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void TheAccountInUseOffersNoForget()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        launched.OpenWindow();

        launched.Row("claude/work").RightClick();
        launched.Named("Rename…");

        Assert.IsTrue(launched.Gone(found => found.ByName("Forget…")), "the account in use offers Forget…");
        Keyboard.Type(VirtualKeyShort.ESCAPE);
    }

    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void UsingACodexAccountSaysSessionsKeepTheOldOne()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");
        launched.OpenWindow();

        // Named with its tool, since Claude Code could have a spare too.
        Launched.Within(launched.Row("codex/spare"), found => found.ByName("Use spare (Codex)"), "Use spare (Codex)").AsButton().Invoke();
        var notice = launched.Find(found => found.ByAutomationId("notice.switch/codex"), "notice switch/codex");

        Assert.IsTrue(
            Launched.Lines(notice).Any(line => line.StartsWith("Any codex session started before this switch keeps using main", StringComparison.Ordinal)),
            Launched.Said(notice));
        Launched.Within(notice, found => found.ByAutomationId("CloseButton").Or(found.ByName("Close")), "close button")
            .AsButton().Invoke();
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("notice.switch/codex")), "the notice stayed");
    }

    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void NamingTheAccountInUse()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "unnamed");
        launched.OpenWindow();

        launched.Named("Give this account a name");
        launched.Invoke("Name…");
        launched.Field("sheet.name").Enter("work");
        launched.Invoke("Save");

        launched.Row("claude/work");
    }

    // macOS has no UI test of this: no world of its fixtures left a login in a file until this one.
    [TestMethod]
    [Ignore("W23b: Claude Code's login left in a file behind its Credential Manager store")]
    public void PuttingAwayALoginLeftInAFile()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "leftInFile");
        launched.OpenWindow();

        launched.Invoke("Put away…");
        Assert.IsTrue(launched.Enables("Put away"), "Put away stayed dimmed after the look");
        launched.Invoke("Put away");

        launched.Find(found => found.ByAutomationId("notice.stowed"), "notice stowed");
        Assert.IsTrue(launched.Gone(found => found.ByName("Put away…")), "the file is still said to be there");
    }

    [TestMethod]
    public void AnEmptyMachineOffersAnAccount()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "empty");
        launched.OpenWindow();

        launched.Named("No accounts");
        launched.Invoke("Add account…");

        launched.Field("sheet.name");
    }

    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void GivingUpOnAnInterruptedSwitch()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "stuck");
        launched.OpenWindow();

        launched.Invoke("Give up…");
        launched.Invoke("Give up");
        var notice = launched.Find(found => found.ByAutomationId("notice.abandoned"), "notice abandoned");

        Assert.Contains("Gave up on the interrupted switch", Launched.Lines(notice));
        Assert.Contains("2 logins kept", Launched.Said(notice));
    }

    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void OneAccountIsOfferedASecondUntilNotNow()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "onlyOne");
        launched.OpenWindow();
        launched.Named("Add a second account");

        launched.Invoke("Not now");

        Assert.IsTrue(launched.Gone(found => found.ByName("Add a second account")), "the tip stayed");
        launched.Row("claude/work");
    }

    // The window goes to the accounts, where the sheet comes up.
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void CtrlNAddsAnAccountFromAnyPane()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        var window = launched.OpenWindow();
        launched.Select("pane.activity");
        launched.Named("Switch");

        window.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
        launched.Field("sheet.name");
        launched.Find(found => found.ByName("Add account").And(found.ByControlType(ControlType.Text)), "the sheet's title");
        launched.Invoke("Cancel");

        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("sheet.name")), "the sheet stayed up");
        Assert.IsTrue(launched.Selected("pane.accounts"), "Ctrl+N left the window on Activity");
        launched.Row("claude/work");
    }

    [TestMethod]
    public void TheFirstLaunchOpensTheWindow()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");

        launched.Window("Pitboard");

        launched.Named("No accounts");
    }
}
