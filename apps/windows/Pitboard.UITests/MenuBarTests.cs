using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace Pitboard.UITests;

// The macOS menu bar item's tests, through the notification-area icon's menu.
[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class MenuBarTests
{
    [TestMethod]
    [Ignore(Pending.W23a)]
    public void TheMenuListsEveryAccountUnderItsTool()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");

        launched.OpenMenu();

        foreach (var title in new[] { "Claude Code", "Codex", "work", "personal", "old", "main · Pro", "spare · Pro" })
        {
            launched.Named(title);
        }

        foreach (var command in new[] { "Add Account…", "Refresh", "Open Pitboard", "Settings", "Exit" })
        {
            launched.Named(command);
        }
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void ChoosingAnotherAccountSwitchesToIt()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");

        launched.OpenMenu();
        launched.Choose("personal");
        launched.OpenMenu();
        launched.Choose("Open Pitboard");

        Launched.Within(launched.Row("claude/personal"), found => found.ByName("In Use"), "In Use");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void AnExpiredAccountOpensSigningInAgain()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");

        launched.OpenMenu();
        launched.Choose("old");

        launched.Named("Sign In to old Again");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void AddAccountOpensTheSheet()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");

        launched.OpenMenu();
        launched.Choose("Add Account…");

        launched.Find(found => found.ByName("Add Account").And(found.ByControlType(ControlType.Text)), "the sheet's title");
        launched.Field("sheet.name");
    }

    // NavigationView names its settings item SettingsItem.
    [TestMethod]
    [Ignore(Pending.W23a)]
    public void SettingsOpens()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");

        launched.OpenMenu();
        launched.Choose("Settings");

        Assert.IsTrue(launched.Selected("SettingsItem"), "the window opened on another pane");
    }

    [TestMethod]
    [Ignore(Pending.W16)]
    public void AMachineWithoutClaudeCodeSaysSo()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "noClaudeCode");

        launched.OpenMenu();

        launched.Named("Claude Code isn’t installed");
    }

    // The fixture's account list cannot be read, so there is no account to list.
    [TestMethod]
    [Ignore(Pending.W23a)]
    public void AFailedReadIsSaidInTheMenuAndInTheWindow()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "readFailure");

        launched.OpenMenu();
        launched.Choose("Couldn’t read usage");

        launched.Named("Couldn’t Read Accounts");
        var window = launched.Window("Pitboard");
        Assert.IsTrue(
            Retry.WhileFalse(
                () => Launched.Lines(window).Any(line => line.StartsWith("could not read Pitboard's account list at ", StringComparison.Ordinal)),
                TimeSpan.FromSeconds(10)).Result,
            "the window does not say why");
        launched.Named("Try Again");
        Assert.IsTrue(launched.Gone(found => found.ByAutomationId("account.claude/work")), "work is listed");
    }

    // The window is left on another pane first: the notice's item opens it on the accounts whichever pane it was on.
    [TestMethod]
    [Ignore(Pending.W23a)]
    public void ShowingANoticeOpensTheWindowOnTheAccounts()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "stuck");
        launched.OpenWindow();
        launched.Select("pane.machine");
        Assert.IsTrue(launched.Selected("pane.machine"), "This PC did not open");

        launched.OpenMenu();
        launched.Choose("An interrupted switch is waiting");

        launched.Named("Give Up…");
        Assert.IsTrue(launched.Selected("pane.accounts"), "the window stayed on This PC");
    }

    [TestMethod]
    [Ignore(Pending.W23a)]
    public void ExitEndsPitboardAndTakesItsIcon()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "oneTool");

        launched.OpenMenu();
        launched.Choose("Exit");

        Assert.IsTrue(launched.Process.WaitForExit(TimeSpan.FromSeconds(10)), "Pitboard went on running");
        Assert.IsNull(NotificationArea.Icon(launched.Automation), "its icon stayed in the notification area");
    }
}
