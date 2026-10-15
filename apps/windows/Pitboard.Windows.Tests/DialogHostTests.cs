using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class DialogHostTests
{
    // Microsoft's contract for ContentDialog, which is why the window's dialogs wait their turn.
    [UITestMethod]
    public async Task OnlyOneContentDialogShowsAtATime()
    {
        var content = await Views.Shown(new Grid());
        var first = new ContentDialog { XamlRoot = content.XamlRoot, Title = "first", CloseButtonText = "OK" };
        var second = new ContentDialog { XamlRoot = content.XamlRoot, Title = "second", CloseButtonText = "OK" };
        var shown = first.ShowAsync();
        try
        {
            var refused = await Assert.ThrowsAsync<Exception>(
                () => second.ShowAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsNotInstanceOfType<TimeoutException>(refused, "the second dialog waited instead of being refused");
        }
        finally
        {
            second.Hide();
            first.Hide();
            await shown;
        }
    }

    [UITestMethod]
    public async Task TheWindowsDialogsWaitTheirTurn()
    {
        var content = await Views.Shown(new Grid());
        var dialogs = new DialogHost(() => content.XamlRoot);
        var first = new ContentDialog { Title = "first", CloseButtonText = "OK" };
        var second = new ContentDialog { Title = "second", CloseButtonText = "OK" };

        var firstShown = dialogs.Show(first);
        var secondShown = dialogs.Show(second);
        await Views.Until(() => dialogs.Open == first, "the first dialog did not show");
        first.Hide();
        await Views.Until(() => dialogs.Open == second, "the second dialog did not follow the first");
        second.Hide();

        Assert.AreEqual(ContentDialogResult.None, await firstShown);
        Assert.AreEqual(ContentDialogResult.None, await secondShown);
    }

    // As an account window closes: its question goes, and one waiting its turn never shows.
    [UITestMethod]
    public async Task AWithdrawnHostsDialogGoesAndNoneWaitingShows()
    {
        var content = await Views.Shown(new Grid());
        var dialogs = new DialogHost(() => content.XamlRoot);
        var first = new ContentDialog { Title = "first", CloseButtonText = "OK" };
        var second = new ContentDialog { Title = "second", CloseButtonText = "OK" };
        var firstShown = dialogs.Show(first);
        var secondShown = dialogs.Show(second);
        await Views.Until(() => dialogs.Open == first, "the first dialog did not show");

        dialogs.Withdraw();

        Assert.AreEqual(ContentDialogResult.None, await firstShown);
        Assert.IsNull(await secondShown);
    }

    // An account window's question waits for a sheet over the main window, and the other way round.
    [UITestMethod]
    public async Task EachWindowsDialogsWaitForTheOthers()
    {
        var content = await Views.Shown(new Grid());
        var main = new DialogHost(() => content.XamlRoot);
        var account = new DialogHost(() => content.XamlRoot);
        var first = new ContentDialog { Title = "first", CloseButtonText = "OK" };
        var second = new ContentDialog { Title = "second", CloseButtonText = "OK" };

        var firstShown = main.Show(first);
        var secondShown = account.Show(second);
        await Views.Until(() => main.Open == first, "the first dialog did not show");
        Assert.IsNull(account.Open, "the second dialog showed over the first");
        first.Hide();
        await Views.Until(() => account.Open == second, "the second dialog did not follow the first");
        second.Hide();

        Assert.AreEqual(ContentDialogResult.None, await firstShown);
        Assert.AreEqual(ContentDialogResult.None, await secondShown);
    }
}
