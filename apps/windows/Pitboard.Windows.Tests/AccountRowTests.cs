using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class AccountRowTests
{
    [UITestMethod]
    public async Task AnAccountNotInUseSaysItsLimitsAndOffersItsAction()
    {
        var sent = new List<Intent>();
        var item = Items.Account("codex", "spare") with
        {
            Plan = "Plus",
            Limits = [Items.Limit(42, PaceStanding.Over)],
            ParkedNote = "Its parked login lasts 6 more days",
            Action = new ItemAction("Use", "Use spare (Codex)", new Intent.SwitchTo("codex/spare")),
        };
        var row = await Views.Shown(new AccountRow { Item = item, Perform = sent.Add });

        var texts = Views.Texts(row);
        foreach (var said in new[] { "spare", "Plus", "spare@example.com", "5h", "42%", "resets in 2h 10m", "over pace", "Its parked login lasts 6 more days" })
        {
            Assert.Contains(said, texts);
        }

        Assert.DoesNotContain("In use", texts);
        var use = Views.Button(row, "Use");
        Assert.AreEqual("Use spare (Codex)", AutomationProperties.GetName(use));
        Views.Press(use);
        Assert.AreEqual(new Intent.SwitchTo("codex/spare"), sent.Single());
    }

    [UITestMethod]
    public async Task TheAccountInUseSaysSoAndHasNothingToPress()
    {
        var row = await Views.Shown(new AccountRow { Item = Items.Account("claude", "work", inUse: true) });

        Assert.Contains("In use", Views.Texts(row));
        Assert.IsFalse(Views.Visible<Button>(row).Any(), "a button on the account in use");
    }

    // Narrator reads a limit once, as a sentence, and not the bar and each figure on their own.
    [UITestMethod]
    public async Task ALimitIsSpokenOnce()
    {
        var limit = Items.Limit(42);
        var row = await Views.Shown(new AccountRow { Item = Items.Account("claude", "work") with { Limits = [limit] } });

        var spoken = Views.Visible<TextBlock>(row).Single(text => text.Text == "5h");
        Assert.AreEqual(limit.Spoken, AutomationProperties.GetName(spoken));
        foreach (var unspoken in Views.Visible<TextBlock>(row).Where(text => text.Text is "42%" or "resets in 2h 10m"))
        {
            Assert.AreEqual(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(unspoken));
        }

        Assert.AreEqual(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(Views.Visible<ProgressBar>(row).Single()));
    }
}
