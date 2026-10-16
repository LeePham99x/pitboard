using Pitboard.Core;

namespace Pitboard.Windows.Tests;

[TestClass]
public sealed class WindowAlertTests
{
    private static readonly AlertText Failure = new("Couldn’t switch to spare", "Claude Code is signing in.");
    private static readonly Question QuitChatGPT = new("Quit ChatGPT to switch?", "ChatGPT signs in again.", "Quit ChatGPT and switch");
    private static readonly QuitQuestion Asked = new(Qualified: "codex/spare", AppId: "com.openai.chat", Name: "ChatGPT");

    private static AppModel Model(Snapshot snapshot) => new(snapshot);

    [TestMethod]
    public void AFailureIsSaidBeforeAQuestion()
    {
        var model = Model(Snapshots.Read(1) with { FailureAlert = Failure, QuitConfirmation = QuitChatGPT, QuitQuestion = Asked });

        Assert.AreEqual(new WindowAlert.Failed(Failure), WindowAlert.Of(model));
    }

    [TestMethod]
    public void TheQuestionNamesTheAccountItSwitchesTo()
    {
        var model = Model(Snapshots.Read(1) with { QuitConfirmation = QuitChatGPT, QuitQuestion = Asked });
        var alert = WindowAlert.Of(model)!;

        Assert.AreEqual(new Intent.QuitAndSwitch("codex/spare"), alert.Answered(confirmed: true));
        Assert.AreEqual(new Intent.KeepAppOpen(), alert.Answered(confirmed: false));
    }

    [TestMethod]
    public void TheReplaceQuestionNamesTheAccountItPutsInPlace()
    {
        var replace = new ReplaceQuestion(
            Qualified: "claude/work",
            Question: new Question("Replace Claude Code’s login?", "Anthropic no longer accepts it.", "Replace"));
        var alert = WindowAlert.Of(Model(Snapshots.Read(1) with { ReplaceQuestion = replace }))!;

        Assert.AreEqual(new WindowAlert.ReplaceAsked(replace.Question, "claude/work"), alert);
        Assert.AreEqual(new Intent.ReplaceAndSwitch("claude/work"), alert.Answered(confirmed: true));
        Assert.AreEqual(new Intent.KeepLogin(), alert.Answered(confirmed: false));
    }

    [TestMethod]
    public void ClosingAFailureDismissesIt()
    {
        Assert.AreEqual(new Intent.DismissFailure(), new WindowAlert.Failed(Failure).Answered(confirmed: false));
    }

    [TestMethod]
    public void NothingIsAskedWithoutBothPartsOfTheQuestion()
    {
        Assert.IsNull(WindowAlert.Of(Model(Snapshots.Read(1) with { QuitConfirmation = QuitChatGPT })));
        Assert.IsNull(WindowAlert.Of(Model(Snapshots.Read(1) with { QuitQuestion = Asked })));
    }

    // One ContentDialog shows per thread at a time, and the sheet is one.
    [TestMethod]
    public void WhileASheetIsUpItsAlertIsSaidInTheSheet()
    {
        Assert.IsFalse(WindowAlert.InSheet(Model(Snapshots.Read(1))));
        Assert.IsTrue(WindowAlert.InSheet(Model(Snapshots.Read(1) with { Sheet = new Sheet.Add(Provider: null) })));
    }
}
