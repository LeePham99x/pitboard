using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

// One Pitboard at a time: they share the fixture's folder, the mouse and the foreground.
[assembly: DoNotParallelize]

namespace Pitboard.UITests;

// A Pitboard.exe started directly, in seven homes of its own, as AGENTS.md has a built one run.
internal sealed class Launched : IDisposable
{
    public static readonly string[] Homes =
        ["USERPROFILE", "HOME", "APPDATA", "LOCALAPPDATA", "CODEX_HOME", "CLAUDE_CONFIG_DIR", "PITBOARD_HOME"];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Shortly = TimeSpan.FromSeconds(10);

    private readonly DirectoryInfo _root;
    private readonly string _exe;
    private readonly string? _fixture;

    private Launched(DirectoryInfo root, string exe, string? fixture, Dictionary<string, string> homes)
    {
        _root = root;
        _exe = exe;
        _fixture = fixture;
        HomeFolders = homes;
        Process = Process.Start(StartInfo())!;
    }

    public Process Process { get; }

    public Dictionary<string, string> HomeFolders { get; }

    public UIA3Automation Automation { get; } = new();

    // The person's own: a known folder, which no variable moves.
    public static string[] DataFolders { get; } =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pitboard App"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pitboard App Debug"),
    ];

    public static Launched Start(string exe, string? fixture = null)
    {
        var root = Directory.CreateTempSubdirectory("pitboard-ui-");
        var homes = Homes.ToDictionary(home => home, home => root.CreateSubdirectory(home).FullName);
        return new Launched(root, exe, fixture, homes);
    }

    // A second launch in the same homes, as from Start, which the running copy answers with its window.
    public Window OpenWindow()
    {
        using var again = Process.Start(StartInfo())!;
        if (!again.WaitForExit(Patience))
        {
            again.Kill();
            throw new AssertFailedException($"a second launch did not hand over to the first within {Patience}");
        }

        return Window("Pitboard");
    }

    public Window Window(string title)
    {
        var found = Retry.WhileNull(
            () => FlaUI.Core.Application.Attach(Process).GetAllTopLevelWindows(Automation)
                .FirstOrDefault(window => window.Title == title),
            Patience, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;
        return found ?? throw new AssertFailedException($"Pitboard showed no window titled {title} within {Patience}");
    }

    // Looked for in every window of Pitboard's, since a menu or a dialog may draw in a window of its own.
    public AutomationElement Find(Func<ConditionFactory, ConditionBase> condition, string what)
    {
        var found = Retry.WhileNull(() => Look(condition), Shortly, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;
        return found ?? throw new AssertFailedException($"Pitboard showed no {what} within {Shortly}");
    }

    public bool Gone(Func<ConditionFactory, ConditionBase> condition) =>
        Retry.WhileTrue(() => Look(condition) is not null, Shortly, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;

    public static AutomationElement Within(AutomationElement scope, Func<ConditionFactory, ConditionBase> condition, string what) =>
        scope.FindFirstDescendant(condition) ?? throw new AssertFailedException($"no {what} in {scope.Name}");

    public AutomationElement Row(string qualified) => Find(found => found.ByAutomationId($"account.{qualified}"), $"row {qualified}");

    public AutomationElement Named(string name) => Find(found => found.ByName(name), $"'{name}'");

    public void Invoke(string name) => Named(name).Patterns.Invoke.Pattern.Invoke();

    public void Select(string pane) =>
        Find(found => found.ByAutomationId(pane), pane).Patterns.SelectionItem.Pattern.Select();

    public bool Selected(string pane) =>
        Retry.WhileFalse(
            () => Find(found => found.ByAutomationId(pane), pane).Patterns.SelectionItem.Pattern.IsSelected.Value,
            Shortly, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;

    // A message box's line, or any line, is a Text element named by it.
    public static string Said(AutomationElement scope) =>
        string.Join(" ", Lines(scope));

    public static IEnumerable<string> Lines(AutomationElement scope) =>
        scope.FindAllDescendants(found => found.ByControlType(FlaUI.Core.Definitions.ControlType.Text)).Select(text => text.Name);

    public static void Dismiss(Window box) =>
        box.FindFirstDescendant(found => found.ByControlType(FlaUI.Core.Definitions.ControlType.Button))!
            .AsButton().Invoke();

    private AutomationElement? Look(Func<ConditionFactory, ConditionBase> condition) =>
        FlaUI.Core.Application.Attach(Process).GetAllTopLevelWindows(Automation)
            .Select(window => window.FindFirstDescendant(condition))
            .FirstOrDefault(found => found is not null);

    private ProcessStartInfo StartInfo()
    {
        var start = new ProcessStartInfo(_exe) { UseShellExecute = false, WorkingDirectory = _root.FullName };
        foreach (var (home, folder) in HomeFolders)
        {
            start.Environment[home] = folder;
        }

        start.Environment.Remove("PITBOARD_FIXTURE");
        if (_fixture is not null)
        {
            start.Environment["PITBOARD_FIXTURE"] = _fixture;
        }

        return start;
    }

    public string[] Written() =>
        HomeFolders.Values.Concat(DataFolders.Where(Directory.Exists))
            .SelectMany(Directory.EnumerateFileSystemEntries)
            .ToArray();

    public void Dispose()
    {
        if (!Process.HasExited)
        {
            Process.Kill(entireProcessTree: true);
            Process.WaitForExit();
        }

        Process.Dispose();
        Automation.Dispose();
        _root.Delete(recursive: true);
    }
}
