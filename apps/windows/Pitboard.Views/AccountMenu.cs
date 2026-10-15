using Microsoft.UI.Xaml.Controls;
using Pitboard.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Pitboard.Views;

// An account's context menu, in the macOS order: its offers, copying its address, then forgetting it.
public static class AccountMenu
{
    public static MenuFlyout For(AccountItem item, Action<Question?, Intent> ask)
    {
        var menu = new MenuFlyout();
        foreach (var offer in item.Offers)
        {
            menu.Items.Add(Entry(offer, ask));
        }

        if (item.Email.Length > 0)
        {
            Separate(menu);
            var copy = new MenuFlyoutItem { Text = "Copy Email Address" };
            copy.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(item.Email);
                Clipboard.SetContent(package);
            };
            menu.Items.Add(copy);
        }

        if (item.Forget is { } forget)
        {
            Separate(menu);
            menu.Items.Add(Entry(forget, ask));
        }

        return menu;
    }

    // A check mark beside it, as a menu shows a setting that is on.
    private static MenuFlyoutItem Entry(ItemOffer offer, Action<Question?, Intent> ask)
    {
        MenuFlyoutItem entry = offer.Checked is { } on ? new ToggleMenuFlyoutItem { IsChecked = on } : new MenuFlyoutItem();
        entry.Text = offer.Title;
        entry.IsEnabled = offer.Enabled;
        entry.Click += (_, _) => ask(offer.Confirm, offer.Intent);
        return entry;
    }

    private static void Separate(MenuFlyout menu)
    {
        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
        }
    }
}
