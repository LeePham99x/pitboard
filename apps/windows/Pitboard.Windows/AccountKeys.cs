using Pitboard.Core;
using Windows.System;

namespace Pitboard.Windows;

public static class AccountKeys
{
    public static (Question? Confirm, Intent Intent)? Pressed(VirtualKey key, AccountItem item) => key switch
    {
        VirtualKey.Enter when item.Action is { } action => (null, action.Intent),
        VirtualKey.Delete when item.Forget is { Enabled: true } forget => (forget.Confirm, forget.Intent),
        VirtualKey.F2 when item.Offers.FirstOrDefault(Renames) is { } rename => (rename.Confirm, rename.Intent),
        _ => null,
    };

    private static bool Renames(ItemOffer offer) =>
        offer is { Enabled: true, Intent: Intent.PresentSheet { Sheet: Sheet.Rename } };
}
