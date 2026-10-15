using Pitboard.Core;

namespace Pitboard.Windows;

// What the sheet over the main window shows and sends, by the macOS sheets' rules.
public abstract record SheetPage
{
    private SheetPage()
    {
    }

    public abstract bool Closable { get; }

    public virtual bool EscapeCloses => Closable;

    // Told apart as the macOS app tells them, so one replacing another is put up afresh.
    public static string Key(Sheet sheet) => sheet switch
    {
        Sheet.Add add => $"add/{add.Provider}",
        Sheet.SignInAgain again => $"again/{again.Provider}/{again.Label}",
        Sheet.Name name => $"name/{name.Provider}",
        Sheet.Rename rename => $"rename/{rename.Provider}/{rename.Label}",
        Sheet.Stow => "stow",
        _ => throw new ArgumentOutOfRangeException(nameof(sheet)),
    };

    // A sheet on its way out keeps what it showed, so it does not flash its form as it goes.
    public static SheetPage? Of(AppModel model, SheetPage? shown)
    {
        if (model.Sheet is not { } sheet)
        {
            return shown;
        }

        if (sheet is Sheet.Stow)
        {
            return model.StowText is { } stow ? new Stowing(stow) : shown;
        }

        // The model runs one sign-in at a time, so a sign-in sheet shows the one running, whichever sheet started it.
        var signsIn = sheet is Sheet.Add or Sheet.SignInAgain;
        if (signsIn && (model.SigningIn, model.SigningInText) is ({ } running, { } said))
        {
            return new SigningIn(running, said);
        }

        return model.SheetText switch
        {
            { } text when signsIn => new Form(sheet, text, Running: model.SigningIn is not null),
            { } text => new Naming(sheet, text),
            null => shown,
        };
    }

    public virtual IReadOnlyList<Intent> Closed() => [new Intent.CloseSheet()];

    public sealed record Form(Sheet Sheet, SheetText Text, bool Running) : SheetPage
    {
        public override bool Closable => !Text.Saving;

        public string Message(string tool) => Chosen(tool)?.Message ?? Text.Message;

        public string Confirm(string tool) => Chosen(tool)?.Confirm ?? Text.Confirm;

        // Null where Pitboard found the tool.
        public Missing? NotFound(string tool) => Chosen(tool)?.Missing;

        public string? Problem(string typed) => PitboardFfiMethods.CheckName(Sheet, typed).Problem;

        // Null while Sign In cannot be pressed.
        public Intent? Start(string tool, string typed) =>
            !Text.Saving && !Running && NotFound(tool) is null && PitboardFfiMethods.CheckName(Sheet, typed).Name is { } name
                ? new Intent.SignIn(tool, name)
                : null;

        private SheetTool? Chosen(string tool) => Text.Tools.FirstOrDefault(offered => offered.Code == tool);
    }

    public sealed record Naming(Sheet Sheet, SheetText Text) : SheetPage
    {
        public override bool Closable => !Text.Saving;

        public string? Problem(string typed) => PitboardFfiMethods.CheckName(Sheet, typed).Problem;

        public Intent? Save(string typed) =>
            (Text.Saving ? null : PitboardFfiMethods.CheckName(Sheet, typed).Name, Sheet) switch
            {
                ({ } name, Sheet.Name named) => new Intent.Enrol(named.Provider, name),
                ({ } name, Sheet.Rename renamed) => new Intent.Rename(renamed.Provider, renamed.Label, name),
                _ => null,
            };
    }

    public sealed record SigningIn(RunningSignIn SignIn, SigningInText Text) : SheetPage
    {
        public override bool Closable => Text.Cancel.Enabled;

        public override bool EscapeCloses => Text.Cancel.Enabled && Text.EscapeCancels;

        // Held back until the tool prints it.
        public Uri? Page => Uri.TryCreate(SignIn.Url, UriKind.Absolute, out var page) ? page : null;

        public override IReadOnlyList<Intent> Closed() => [Text.Cancel.Intent, new Intent.CloseSheet()];

        public Intent? Submit(string code) => SignIn.WantsCode && code.Length > 0 ? new Intent.PasteCode(code) : null;
    }

    public sealed record Stowing(StowText Text) : SheetPage
    {
        public override bool Closable => !Text.Saving;

        public Intent? PutAway() => Text.CanConfirm ? new Intent.Stow() : null;
    }
}
