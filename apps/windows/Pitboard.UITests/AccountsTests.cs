namespace Pitboard.UITests;

[TestClass]
[Elevated(ConditionMode.Exclude)]
public sealed class AccountsTests
{
    [TestMethod]
    [Ignore("W23a: Claude Code's Credential Manager store, which a machine in memory plays")]
    public void TheWindowListsEachAccount()
    {
        using var launched = Launched.Start(Builds.Debug, fixture: "twoTools");

        var window = launched.Window("Pitboard");

        Assert.IsNotNull(window.FindFirstDescendant(found => found.ByAutomationId("account.claude/work")));
        Assert.IsNotNull(window.FindFirstDescendant(found => found.ByAutomationId("account.codex/work")));
    }
}
