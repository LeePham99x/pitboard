using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer;
using Pitboard.Core;
using Pitboard.Views;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class AccountSheetTests
{
    private const string Address = "https://claude.com/cai/oauth/authorize?code=true&state=pitboard-fixture";

    private static readonly Sheet Add = new Sheet.Add(Provider: null);

    [UITestMethod]
    public async Task TheSheetForANewAccountSignsInToThePickedToolAsTheNameTyped()
    {
        await using var up = await Up.Over(Snapshots.Read(1) with
        {
            Sheet = Add,
            SheetText = Items.AddAccount(Items.Tool("claude", "Claude Code"), Items.Tool("codex", "Codex")),
        });
        var picker = up.Element<ComboBox>("sheet.tool");
        Assert.AreEqual("Add account", up.Sheet.Title);
        Assert.AreEqual(0, picker.SelectedIndex);
        Assert.IsFalse(up.Sheet.IsPrimaryButtonEnabled, "Sign in with no name");

        picker.SelectedIndex = 1;
        await up.Type("sheet.name", "third");
        Views.Press(up.Part("PrimaryButton"));

        Assert.Contains("Pitboard opens Codex’s own sign-in in your browser.", Views.Texts(up.Sheet));
        await Views.Until(() => up.Model.Sent.Contains(new Intent.SignIn("codex", "third")), "no sign-in to Codex as third");
        Assert.AreSame(up.Sheet, up.Dialogs.Open, "the sheet went before the model closed it");
    }

    [UITestMethod]
    public async Task TheNameFieldOffersOnlyANameTheAccountDoesNotHave()
    {
        await using var up = await Up.Over(Snapshots.Read(1) with
        {
            Sheet = new Sheet.Rename("claude", "personal"),
            SheetText = Items.Rename("personal"),
        });
        Assert.AreEqual("personal", up.Element<TextBox>("sheet.name").Text);
        Assert.IsFalse(up.Sheet.IsPrimaryButtonEnabled, "the name it has");

        await up.Type("sheet.name", "  ");
        Assert.IsFalse(up.Sheet.IsPrimaryButtonEnabled, "white space");
        await up.Type("sheet.name", "home");
        Views.Press(up.Part("PrimaryButton"));

        await Views.Until(() => up.Model.Sent.Contains(new Intent.Rename("claude", "personal", "home")), "no rename to home");
    }

    [UITestMethod]
    public async Task SigningInAgainShowsTheAccountInPlaceOfAName()
    {
        await using var up = await Up.Over(Snapshots.Read(1) with
        {
            Sheet = new Sheet.SignInAgain("claude", "work"),
            SheetText = Items.AddAccount() with { Title = "Sign in to work again", Account = "work" },
        });
        Assert.IsFalse(up.Visible("sheet.name"), "a name field for an account that has one");
        Assert.IsTrue(Views.Visible<TextBox>(up.Sheet).Any(field => field.Text == "work"), "the account it signs in to");

        Views.Press(up.Part("PrimaryButton"));

        await Views.Until(() => up.Model.Sent.Contains(new Intent.SignIn("claude", "work")), "no sign-in as work");
    }

    // Saying what went wrong where the name typed is still there to correct.
    [UITestMethod]
    public async Task AFailureIsSaidInTheSheetWithTheNameStillInIt()
    {
        var renaming = Snapshots.Read(1) with { Sheet = new Sheet.Rename("claude", "personal"), SheetText = Items.Rename("personal") };
        await using var up = await Up.Over(renaming);
        await up.Type("sheet.name", "work");
        var failure = new Failure(7, "Couldn’t rename personal", "work is already the name of another account.", "name_taken", []);

        up.App.Apply(renaming with { Revision = 2, SheetFailure = failure });

        var bar = up.Element<InfoBar>("sheet.failure");
        await Views.Until(() => bar.IsOpen, "the failure was not said");
        Assert.AreEqual("Couldn’t rename personal", bar.Title);
        Assert.AreEqual("work", up.Element<TextBox>("sheet.name").Text);
    }

    // A tool Pitboard did not find says what it needs and links to how, and cannot be signed in to.
    [UITestMethod]
    public async Task AToolPitboardDidNotFindSaysWhatToInstall()
    {
        var missing = Items.CodexCliMissing();
        await using var up = await Up.Over(Snapshots.Read(1) with
        {
            Sheet = Add,
            SheetText = Items.AddAccount(Items.Tool("claude", "Claude Code"), Items.Tool("codex", "Codex", missing)),
        });
        var bar = up.Element<InfoBar>("sheet.missing");
        Assert.IsFalse(bar.IsOpen, "Claude Code is here");

        up.Element<ComboBox>("sheet.tool").SelectedIndex = 1;
        await up.Type("sheet.name", "third");

        await Views.Until(() => bar.IsOpen, "the missing CLI was not said");
        Assert.AreEqual(missing.Title, bar.Title);
        Assert.IsFalse(up.Sheet.IsPrimaryButtonEnabled, "Sign in to a tool not here");
        new Microsoft.UI.Xaml.Automation.Peers.HyperlinkButtonAutomationPeer(up.Element<HyperlinkButton>("sheet.missing.link")).Invoke();
        CollectionAssert.AreEqual(new[] { new Uri(missing.Link) }, up.Opened);
    }

    // The name field says under itself why what is typed cannot be a name, and a failure goes once it changes.
    [UITestMethod]
    public async Task ANameThatCannotBeOneSaysWhyAndAFailureGoesWithAnEdit()
    {
        var renaming = Snapshots.Read(1) with { Sheet = new Sheet.Rename("claude", "personal"), SheetText = Items.Rename("personal") };
        await using var up = await Up.Over(renaming);
        var field = up.Element<TextBox>("sheet.name");

        await up.Type("sheet.name", "a/b");
        Assert.AreEqual("A name can’t contain “/”.", field.Description);
        Assert.IsFalse(up.Sheet.IsPrimaryButtonEnabled);

        var failure = new Failure(7, "Couldn’t rename personal", "work is already the name of another account.", "name_taken", []);
        up.App.Apply(renaming with { Revision = 2, SheetFailure = failure });
        await Views.Until(() => up.Element<InfoBar>("sheet.failure").IsOpen, "the failure was not said");
        await up.Type("sheet.name", "home");

        Assert.IsNull(field.Description);
        await Views.Until(() => up.Model.Sent.Contains(new Intent.DismissSheetFailure()), "the failure stayed with the name");
    }

    [UITestMethod]
    public async Task TheCodeFieldAppearsOnceTheToolAsksForIt()
    {
        var signingIn = Snapshots.Read(1) with
        {
            Sheet = Add,
            SheetText = Items.AddAccount(),
            SigningIn = Items.Running("claude", Address),
            SigningInText = Items.SigningIn("Claude Code"),
        };
        await using var up = await Up.Over(signingIn);
        Assert.IsFalse(up.Visible("sheet.code"), "a code field before the tool asks");
        Assert.AreEqual("", up.Sheet.PrimaryButtonText);
        Assert.AreEqual("Waiting for your browser", AutomationProperties.GetName(up.Element<ProgressRing>("Waiting")));

        up.App.Apply(signingIn with { Revision = 2, SigningIn = Items.Running("claude", Address, wantsCode: true) });
        await Views.Until(() => up.Visible("sheet.code"), "no code field once the tool asked");
        Assert.AreEqual("Submit code", up.Sheet.PrimaryButtonText);
        Assert.IsFalse(up.Sheet.IsPrimaryButtonEnabled, "Submit code with no code");
        await up.Type("sheet.code", "fixture-code#state");
        Views.Press(up.Part("PrimaryButton"));

        await Views.Until(() => up.Model.Sent.Contains(new Intent.PasteCode("fixture-code#state")), "the code was not sent");
        Assert.AreEqual("", up.Element<TextBox>("sheet.code").Text);
    }

    // The fixture's address only goes to the opener a test hands over, never to a browser.
    [UITestMethod]
    public async Task TheSignInPageIsOfferedOnceTheToolPrintsIt()
    {
        var signingIn = Snapshots.Read(1) with
        {
            Sheet = Add,
            SheetText = Items.AddAccount(),
            SigningIn = Items.Running("claude"),
            SigningInText = Items.SigningIn("Claude Code"),
        };
        await using var up = await Up.Over(signingIn);
        Assert.IsFalse(Views.Button(up.Sheet, "Open").IsEnabled, "Open with nothing to open");
        Assert.IsFalse(Views.Button(up.Sheet, "Copy link").IsEnabled);

        up.App.Apply(signingIn with { Revision = 2, SigningIn = Items.Running("claude", Address) });
        await Views.Until(() => Views.Button(up.Sheet, "Open").IsEnabled, "Open stayed dimmed");
        Views.Press(Views.Button(up.Sheet, "Open"));

        Assert.AreEqual(new Uri(Address), up.Opened.Single());
    }

    [UITestMethod]
    public async Task ClosingASignInStopsItAndClosesTheSheet()
    {
        await using var up = await Up.Over(Snapshots.Read(1) with
        {
            Sheet = Add,
            SheetText = Items.AddAccount(),
            SigningIn = Items.Running("codex"),
            SigningInText = Items.SigningIn("Codex"),
        });

        Views.Press(up.Part("CloseButton"));

        await Views.Until(() => up.Dialogs.Open is null, "the sheet stayed up");
        CollectionAssert.AreEqual(new Intent[] { new Intent.CancelSignIn(), new Intent.CloseSheet() }, up.Model.Sent.ToArray());
    }

    // A name being saved cannot be withdrawn, so nothing in the sheet can be pressed and it stays up.
    [UITestMethod]
    public async Task ASheetSavingANameCannotBeClosed()
    {
        await using var up = await Up.Over(Snapshots.Read(1) with
        {
            Sheet = new Sheet.Name("claude", "dana@work.example"),
            SheetText = Items.Rename("") with { Title = "Name this account", Confirm = "Save", Saving = true },
        });

        Assert.IsFalse(up.Part("CloseButton").IsEnabled, "Cancel while saving");
        Assert.IsFalse(up.Element<TextBox>("sheet.name").IsEnabled);
        up.Sheet.Hide();
        await Task.Delay(300);

        Assert.AreSame(up.Sheet, up.Dialogs.Open, "the sheet closed while saving");
        Assert.IsEmpty(up.Model.Sent);
    }

    [UITestMethod]
    public async Task TheModelClosingTheSheetTakesItDownWithoutAWord()
    {
        var naming = Snapshots.Read(1) with
        {
            Sheet = new Sheet.Name("claude", "dana@work.example"),
            SheetText = Items.Rename("") with { Title = "Name this account", Confirm = "Save" },
        };
        await using var up = await Up.Over(naming);

        up.App.Apply(naming with { Revision = 2, Sheet = null, SheetText = null });

        await Views.Until(() => up.Dialogs.Open is null, "the sheet stayed up");
        Assert.IsEmpty(up.Model.Sent);
    }

    [UITestMethod]
    public async Task AnotherSheetIsPutUpAfresh()
    {
        var adding = Snapshots.Read(1) with { Sheet = Add, SheetText = Items.AddAccount() };
        await using var up = await Up.Over(adding);
        var first = up.Sheet;

        up.App.Apply(adding with { Revision = 2, Sheet = new Sheet.Rename("claude", "work"), SheetText = Items.Rename("work") });

        await Views.Until(() => up.Dialogs.Open is AccountSheet next && next != first, "the rename sheet did not replace the first");
        Assert.AreEqual("Rename “work”", up.Sheet.Title);
        Assert.IsEmpty(up.Model.Sent);
    }

    [UITestMethod]
    public async Task PutAwayWaitsForTheLookAndThenPutsTheLoginAway()
    {
        var stowing = Snapshots.Read(1) with { Sheet = new Sheet.Stow(), StowText = Items.Stow() };
        await using var up = await Up.Over(stowing);
        Assert.Contains("Finding out whose login it is…", Views.Texts(up.Sheet));
        Assert.IsFalse(up.Sheet.IsPrimaryButtonEnabled, "Put away before the look");

        up.App.Apply(stowing with { Revision = 2, StowText = Items.Stow("The file holds work’s login, signed in 2 days ago.") });
        await Views.Until(() => up.Sheet.IsPrimaryButtonEnabled, "Put away stayed dimmed");
        Views.Press(up.Part("PrimaryButton"));

        Assert.Contains("The file holds work’s login, signed in 2 days ago.", Views.Texts(up.Sheet));
        await Views.Until(() => up.Model.Sent.Contains(new Intent.Stow()), "nothing was put away");
    }

    // The sheet over a window's content, up once it is in front, and taken down after each test, since one dialog shows at a time.
    private sealed class Up : IAsyncDisposable
    {
        private Up(StandInModel model, AppModel app, DialogHost dialogs, WindowSheets sheets, List<Uri> opened)
        {
            (Model, App, Dialogs, Sheets, Opened) = (model, app, dialogs, sheets, opened);
        }

        public StandInModel Model { get; }

        public AppModel App { get; }

        public DialogHost Dialogs { get; }

        public WindowSheets Sheets { get; }

        public List<Uri> Opened { get; }

        public AccountSheet Sheet => (AccountSheet)Dialogs.Open!;

        public static async Task<Up> Over(Snapshot snapshot)
        {
            var model = new StandInModel(snapshot);
            var app = new AppModel(model);
            var content = await Views.Shown(new Grid());
            var dialogs = new DialogHost(() => content.XamlRoot);
            var opened = new List<Uri>();
            var up = new Up(model, app, dialogs, new WindowSheets(app, dialogs, opened.Add), opened);
            await Views.Until(() => dialogs.Open is AccountSheet { IsLoaded: true }, "no sheet");
            return up;
        }

        public T Element<T>(string automationId)
            where T : DependencyObject =>
            Views.Descendants(Sheet).OfType<T>().FirstOrDefault(found => AutomationProperties.GetAutomationId(found) == automationId)
            ?? Views.Descendants(Sheet).OfType<T>().FirstOrDefault(found => found is FrameworkElement { Name: var name } && name == automationId)
            ?? throw new AssertFailedException($"no {typeof(T).Name} {automationId} in the sheet");

        public bool Visible(string automationId) =>
            Views.Visible<TextBox>(Sheet).Any(found => AutomationProperties.GetAutomationId(found) == automationId);

        // The dialog's own buttons, its template's parts. The sheet's InfoBars have a CloseButton of their own.
        public Button Part(string name) =>
            Views.Descendants(Sheet).OfType<Button>().Single(button => button.Name == name && !InInfoBar(button));

        public async Task Type(string automationId, string text)
        {
            var field = Element<TextBox>(automationId);
            var changed = new TaskCompletionSource();
            void Changed(object sender, TextChangedEventArgs args) => changed.TrySetResult();
            field.TextChanged += Changed;
            field.Text = text;
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            field.TextChanged -= Changed;
        }

        public async ValueTask DisposeAsync()
        {
            Sheets.Dispose();
            await Views.Until(() => Dialogs.Open is null, "the sheet stayed up after its test");
        }

        private bool InInfoBar(DependencyObject element)
        {
            for (var parent = VisualTreeHelper.GetParent(element); parent is not null && parent != Sheet; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is InfoBar)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
