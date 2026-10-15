using System.Diagnostics;
using System.Text;
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
    private readonly bool _ownsHomes;
    private readonly StringBuilder _said = new();

    private Launched(DirectoryInfo root, string exe, string? fixture, Dictionary<string, string> homes, bool ownsHomes)
    {
        _root = root;
        _exe = exe;
        _fixture = fixture;
        _ownsHomes = ownsHomes;
        HomeFolders = homes;
        var start = StartInfo();
        start.RedirectStandardError = true;
        Process = Process.Start(start)!;
        Process.ErrorDataReceived += (_, line) =>
        {
            lock (_said)
            {
                _said.AppendLine(line.Data);
            }
        };
        Process.BeginErrorReadLine();
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
        return new Launched(root, exe, fixture, homes, ownsHomes: true);
    }

    // Launched again now, in the same homes, as from Start.
    public Launched Again() => new(_root, _exe, _fixture, HomeFolders, ownsHomes: false);

    // A second launch once Pitboard runs, as from Start, which the running copy answers with its window.
    public Window OpenWindow()
    {
        if (!Retry.WhileFalse(() => NotificationArea.HiddenWindows(Process).Any(), Patience, TimeSpan.FromMilliseconds(250)).Result)
        {
            throw new AssertFailedException($"Pitboard did not start within {Patience}: {State()}");
        }

        using (var again = Again())
        {
            if (!again.HandedOver(out var why))
            {
                throw new AssertFailedException($"a second launch did not hand over to the first: {why}");
            }
        }

        return Window("Pitboard");
    }

    public bool HandedOver(out string why)
    {
        var quit = Process.WaitForExit(Patience);
        why = quit ? State() : $"it went on running after {Patience}";
        return quit && Process.ExitCode == 0;
    }

    public Window Window(string title)
    {
        var found = Retry.WhileNull(
            () => FlaUI.Core.Application.Attach(Process).GetAllTopLevelWindows(Automation)
                .FirstOrDefault(window => window.Title == title),
            Patience, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;
        return found ?? throw new AssertFailedException($"Pitboard showed no window titled {title} within {Patience}: {State()}");
    }

    // A WinUI app that fails fast exits 0xC000027B and leaves only what it wrote to its standard error.
    public string State()
    {
        string state;
        if (Process.HasExited)
        {
            Process.WaitForExit();
            state = $"it exited 0x{Process.ExitCode:X8}";
        }
        else
        {
            var titles = FlaUI.Core.Application.Attach(Process).GetAllTopLevelWindows(Automation)
                .Select(window => $"'{window.Title}'")
                .ToArray();
            state = titles.Length == 0 ? "it is running, with no window" : $"it is running, with {string.Join(", ", titles)}";
        }

        lock (_said)
        {
            return $"{state}, saying: {_said.ToString().Trim()}";
        }
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

    public TextBox Field(string automationId) => Find(found => found.ByAutomationId(automationId), automationId).AsTextBox();

    // A button the model holds back until it can be pressed.
    public bool Enables(string name) =>
        Retry.WhileFalse(() => Look(found => found.ByName(name))?.IsEnabled == true, Shortly, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;

    // Pitboard's icon, in Explorer's notification area.
    public AutomationElement Icon() =>
        NotificationArea.Icon(Automation) ?? throw new AssertFailedException($"Pitboard's icon is not in the notification area within {Shortly}");

    // The icon's menu, from a right click.
    public void OpenMenu()
    {
        Icon().RightClick();
        Find(found => found.ByName("Open Pitboard"), "menu");
    }

    public void Choose(string item) => Named(item).Click();

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

    // OK through UI Automation is a BM_CLICK, which may fail on an inactive box (E_UNEXPECTED on windows-2025).
    public static void Dismiss(Window box) => box.Patterns.Window.Pattern.Close();

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

    // Asked first, so Pitboard removes its icon: a killed one leaves it in the notification area until hovered.
    public void Dispose()
    {
        if (!Process.HasExited)
        {
            NotificationArea.AskToEnd(Process);
            if (!Process.WaitForExit(Shortly))
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit();
            }
        }

        Process.Dispose();
        Automation.Dispose();
        if (_ownsHomes)
        {
            _root.Delete(recursive: true);
        }
    }
}
