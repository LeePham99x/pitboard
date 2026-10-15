using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

// The Open Link window over what the model says of the link waiting, with no model: it sends what a person chose, and
// opens no browser.
[TestClass]
public sealed class OpenLinkWindowTests
{
    private static readonly WindowAccount Main = Items.Window("codex", "main", inUse: true);
    private static readonly WindowAccount Spare = Items.Window("codex", "spare");
    private static readonly SiteLink Shared = new(Main.Site, "https://chatgpt.com/c/shared");

    private static LinkPicker Choose(ulong arrival, bool armed) =>
        new(arrival, armed, new PickerShown.Choose(
            Title: "Open this chatgpt.com link as:", Link: Shared, LinkText: "chatgpt.com/c/shared",
            Accounts:
            [
                new PickerAccount(Main, "main@example.com", Open: false),
                new PickerAccount(Spare, "spare@example.com, window open", Open: true),
            ],
            Chosen: Main.Store, Open: "Open"));

    private static async Task<OpenLinkWindow> Up(OpenLinkWindow window)
    {
        var root = (FrameworkElement)window.Content;
        var loaded = new TaskCompletionSource();
        root.Loaded += (_, _) => loaded.TrySetResult();
        window.Activate();
        await loaded.Task.WaitAsync(Webs.Patience);
        return window;
    }

    private static string? Selected(OpenLinkWindow window) =>
        (Views.Element<ListView>(window.Content, "picker.accounts").SelectedItem as ListViewItem)?.Tag is WindowAccount account
            ? account.Label
            : null;

    private static void Select(OpenLinkWindow window, string label)
    {
        var list = Views.Element<ListView>(window.Content, "picker.accounts");
        list.SelectedItem = list.Items.OfType<ListViewItem>().Single(item => item.Tag is WindowAccount { } account && account.Label == label);
    }

    [UITestMethod]
    public async Task OpenAnswersOnceArmedWithTheAccountChosen()
    {
        var sent = new List<Intent>();
        var opened = new List<WindowAccount>();
        var window = await Up(new OpenLinkWindow(sent.Add, opened.Add, _ => Assert.Fail("a browser opened")));
        try
        {
            window.Follow(Choose(3, armed: false));
            // The list makes its second row on a later layout pass.
            await Views.Until(
                () => Views.Texts(window.Content).Contains("spare@example.com, window open"), "the second account was not listed");
            var texts = Views.Texts(window.Content);
            Assert.Contains("Open this chatgpt.com link as:", texts);
            Assert.Contains("chatgpt.com/c/shared", texts);
            Assert.AreEqual("main", Selected(window), "the account the model prefers");
            Assert.IsFalse(Views.Button(window.Content, "Open").IsEnabled, "Open answers before the link is armed");

            Select(window, "spare");
            window.Follow(Choose(3, armed: true));
            Assert.AreEqual("spare", Selected(window), "arming kept the choice");
            Views.Press(Views.Button(window.Content, "Open"));

            Assert.AreEqual(new Intent.OpenLink(3, Spare.Store), sent.Single());
            Assert.AreEqual(Spare, opened.Single());
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task ALinkThatArrivesAgainIsChosenAfresh()
    {
        var window = await Up(new OpenLinkWindow(_ => { }, _ => { }, _ => { }));
        try
        {
            window.Follow(Choose(3, armed: true));
            Select(window, "spare");

            window.Follow(Choose(4, armed: true));

            Assert.AreEqual("main", Selected(window));
        }
        finally
        {
            window.Close();
        }
    }

    [UITestMethod]
    public async Task CancellingOrClosingDismissesTheLink()
    {
        var sent = new List<Intent>();
        var window = await Up(new OpenLinkWindow(sent.Add, _ => Assert.Fail("a window opened"), _ => { }));
        window.Follow(Choose(5, armed: true));

        Views.Press(Views.Button(window.Content, "Cancel"));
        window.Close();

        CollectionAssert.AreEqual(new Intent[] { new Intent.DismissLink(5), new Intent.DismissLink(5) }, sent);
    }

    // The model has answered the link: closing then dismisses nothing.
    [UITestMethod]
    public async Task AWithdrawnWindowDismissesNothing()
    {
        var sent = new List<Intent>();
        var window = await Up(new OpenLinkWindow(sent.Add, _ => { }, _ => { }));
        window.Follow(Choose(6, armed: true));

        window.Withdraw();

        Assert.IsEmpty(sent);
    }

    [UITestMethod]
    public async Task ARefusedLinkSaysWhy()
    {
        var sent = new List<Intent>();
        var window = await Up(new OpenLinkWindow(sent.Add, _ => { }, _ => { }));
        try
        {
            window.Follow(new LinkPicker(7, false, new PickerShown.Refused("Can’t Open This Link", "This link is on example.com.")));

            var texts = Views.Texts(window.Content);
            Assert.Contains("Can’t Open This Link", texts);
            Assert.Contains("This link is on example.com.", texts);
            Views.Press(Views.Button(window.Content, "OK"));
            Assert.AreEqual(new Intent.DismissLink(7), sent.Single());
        }
        finally
        {
            window.Withdraw();
        }
    }

    [UITestMethod]
    public async Task ALinkForASiteWithNoAccountOffersToAddOneOrTheBrowser()
    {
        var sent = new List<Intent>();
        var browsed = new List<string>();
        var window = await Up(new OpenLinkWindow(sent.Add, _ => { }, browsed.Add));
        try
        {
            var add = new Choice("Add Account…", new Intent.PresentSheet(new Sheet.Add(Provider: "codex")), Enabled: true);
            window.Follow(new LinkPicker(8, false, new PickerShown.NoAccount(
                Title: "No chatgpt.com Account", Link: Shared, LinkText: "chatgpt.com/c/shared",
                Detail: "None of the accounts Pitboard has opens chatgpt.com.", InBrowser: "Open in Browser", Add: add)));

            Views.Press(Views.Button(window.Content, "Add Account…"));
            Views.Press(Views.Button(window.Content, "Open in Browser"));

            CollectionAssert.AreEqual(new[] { add.Intent, new Intent.DismissLink(8) }, sent);
            Assert.AreEqual(Shared.Url, browsed.Single());
        }
        finally
        {
            window.Withdraw();
        }
    }

    [UITestMethod]
    public async Task UntilTheAccountsAreReadItSaysSoAndOffersToTryAgain()
    {
        var sent = new List<Intent>();
        var window = await Up(new OpenLinkWindow(sent.Add, _ => { }, _ => { }));
        try
        {
            window.Follow(new LinkPicker(9, false, new PickerShown.Reading("Reading accounts…")));
            Assert.Contains("Reading accounts…", Views.Texts(window.Content));

            var retry = new Choice("Try Again", new Intent.Refresh(Asked: true), Enabled: true);
            window.Follow(new LinkPicker(9, false, new PickerShown.ReadFailed("Couldn’t Read Accounts", "The index is damaged.", retry)));
            Views.Press(Views.Button(window.Content, "Try Again"));

            Assert.Contains("The index is damaged.", Views.Texts(window.Content));
            Assert.AreEqual(retry.Intent, sent.Single());
        }
        finally
        {
            window.Withdraw();
        }
    }

    [UITestMethod]
    public async Task WithNoLinkItSaysHowToShareOne()
    {
        var sent = new List<Intent>();
        var window = await Up(new OpenLinkWindow(sent.Add, _ => { }, _ => { }));
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Follow(null);

        var texts = Views.Texts(window.Content);
        Assert.Contains("No Link to Open", texts);
        Assert.IsTrue(
            texts.Any(text => text.StartsWith($"Share a {PitboardFfiMethods.SiteNames(Conjunction.Or)} page", StringComparison.Ordinal)),
            string.Join(" | ", texts));
        Views.Press(Views.Button(window.Content, "Close"));
        Assert.IsTrue(closed);
        Assert.IsEmpty(sent);
    }
}
