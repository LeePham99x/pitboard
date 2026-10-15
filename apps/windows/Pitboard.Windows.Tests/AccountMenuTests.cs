using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class AccountMenuTests
{
    private static readonly ItemOffer Use = new("Use spare", new Intent.SwitchTo("claude/spare"), true, null, null);
    private static readonly ItemOffer SignInAgain = new("Sign In Again…", new Intent.PresentSheet(new Sheet.SignInAgain("claude", "spare")), false, null, null);
    private static readonly ItemOffer Rename = new("Rename…", new Intent.PresentSheet(new Sheet.Rename("claude", "spare")), true, null, null);

    private static string[] Said(MenuFlyout menu) =>
        [.. menu.Items.Select(entry => entry is MenuFlyoutItem item ? item.Text : "-")];

    [UITestMethod]
    public void TheMenuOffersWhatTheModelOffersThenCopyingThenForgetting()
    {
        var item = Items.Account("claude", "spare") with
        {
            Offers = [Use, SignInAgain, Rename],
            Forget = Items.Forget("claude/spare"),
        };

        var menu = AccountMenu.For(item, (_, _) => { });

        Assert.AreEqual("Use spare, Sign In Again…, Rename…, -, Copy Email Address, -, Forget…", string.Join(", ", Said(menu)));
        Assert.IsFalse(((MenuFlyoutItem)menu.Items[1]).IsEnabled, "a sign-in held back");
    }

    [UITestMethod]
    public void AnOfferThatIsOnShowsACheckMark()
    {
        var warm = new ItemOffer("Warm Up", new Intent.SwitchTo("claude/spare"), true, null, Checked: true);

        var menu = AccountMenu.For(Items.Account("claude", "spare") with { Offers = [warm] }, (_, _) => { });

        Assert.IsTrue(((ToggleMenuFlyoutItem)menu.Items[0]).IsChecked);
    }

    [UITestMethod]
    public void TheAccountInUseOffersNoForget()
    {
        var item = Items.Account("claude", "work", inUse: true) with { Offers = [Rename], Forget = null };

        Assert.DoesNotContain("Forget…", Said(AccountMenu.For(item, (_, _) => { })));
    }

    [UITestMethod]
    public void AnAccountWithoutAnAddressOffersNoCopy()
    {
        var item = Items.Account("claude", "work") with { Email = "", Offers = [Rename] };

        Assert.AreEqual("Rename…", string.Join(", ", Said(AccountMenu.For(item, (_, _) => { }))));
    }

    [UITestMethod]
    public void ForgettingCarriesItsQuestion()
    {
        var forget = Items.Forget("claude/spare");
        var asked = new List<(Question?, Intent)>();
        var menu = AccountMenu.For(Items.Account("claude", "spare") with { Forget = forget }, (question, intent) => asked.Add((question, intent)));

        new MenuFlyoutItemAutomationPeer((MenuFlyoutItem)menu.Items[^1]).Invoke();

        Assert.AreEqual((forget.Confirm, forget.Intent), asked.Single());
    }
}
