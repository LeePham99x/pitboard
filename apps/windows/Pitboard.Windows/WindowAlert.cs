using Pitboard.Core;

namespace Pitboard.Windows;

// What the main window puts in front of everything: a switch that failed, or the question before quitting an app.
public abstract record WindowAlert
{
    private WindowAlert()
    {
    }

    public static WindowAlert? Of(AppModel model) =>
        model.FailureAlert is { } failure ? new Failed(failure)
        : (model.QuitConfirmation, model.QuitQuestion) is ({ } question, { } asked) ? new QuitAsked(question, asked.Qualified)
        : null;

    // One ContentDialog shows per thread at a time, and a sheet is one, so its alert is said in the sheet.
    public static bool InSheet(AppModel model) => model.Sheet is not null;

    public abstract Intent Answered(bool confirmed);

    public sealed record Failed(AlertText Text) : WindowAlert
    {
        public override Intent Answered(bool confirmed) => new Intent.DismissFailure();
    }

    public sealed record QuitAsked(Question Question, string Qualified) : WindowAlert
    {
        public override Intent Answered(bool confirmed) =>
            confirmed ? new Intent.QuitAndSwitch(Qualified) : new Intent.KeepAppOpen();
    }
}
