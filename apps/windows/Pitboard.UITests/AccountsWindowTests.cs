using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class AccountsWindowTests
{
    [TestMethod]
    [Ignore(Pending.W23a)]
    public void TheWindowListsEachAccount()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");
        launched.OpenWindow();

        launched.Row("claude/work");
        launched.Row("codex/main");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
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
    [Ignore(Pending.W23a)]
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
    [Ignore(Pending.W23a)]
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

    // The sheet it opens is tested with the sheets.
    [TestMethod]
    [Ignore(Pending.W16)]
    public void AnEmptyMachineOffersAnAccount()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "empty");
        launched.OpenWindow();

        launched.Named("No Accounts");
        Assert.IsTrue(launched.Named("Add Account…").IsEnabled);
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void GivingUpOnAnInterruptedSwitch()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "stuck");
        launched.OpenWindow();

        launched.Invoke("Give Up…");
        launched.Invoke("Give Up");
        var notice = launched.Find(found => found.ByAutomationId("notice.abandoned"), "notice abandoned");

        Assert.Contains("Gave up on the interrupted switch", Launched.Lines(notice));
        Assert.Contains("2 logins kept", Launched.Said(notice));
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void OneAccountIsOfferedASecondUntilNotNow()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "onlyOne");
        launched.OpenWindow();
        launched.Named("Add a second account");

        launched.Invoke("Not Now");

        Assert.IsTrue(launched.Gone(found => found.ByName("Add a second account")), "the tip stayed");
        launched.Row("claude/work");
    }

    // The window goes to the accounts, where the sheet comes up.
    [TestMethod]
    [Ignore(Pending.W23a)]
    public void CtrlNAddsAnAccountFromAnyPane()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");
        var window = launched.OpenWindow();
        launched.Select("pane.activity");
        launched.Named("Switch");

        window.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);

        Assert.IsTrue(launched.Selected("pane.accounts"), "Ctrl+N left the window on Activity");
        launched.Row("claude/work");
    }

    [TestMethod]
    [Ignore(Pending.W16)]
    public void TheFirstLaunchOpensTheWindow()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "firstLaunch");

        launched.Window("Pitboard");

        launched.Named("No Accounts");
    }
}
