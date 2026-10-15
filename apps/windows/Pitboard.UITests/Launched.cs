using System.Diagnostics;
using FlaUI.Core.AutomationElements;
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

    private readonly DirectoryInfo _root;

    private Launched(DirectoryInfo root, Process process, Dictionary<string, string> homes)
    {
        _root = root;
        Process = process;
        HomeFolders = homes;
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
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = root.FullName };
        var homes = Homes.ToDictionary(home => home, home => root.CreateSubdirectory(home).FullName);
        foreach (var (home, folder) in homes)
        {
            start.Environment[home] = folder;
        }

        start.Environment.Remove("PITBOARD_FIXTURE");
        if (fixture is not null)
        {
            start.Environment["PITBOARD_FIXTURE"] = fixture;
        }

        return new Launched(root, Process.Start(start)!, homes);
    }

    public Window Window(string title)
    {
        var found = Retry.WhileNull(
            () => FlaUI.Core.Application.Attach(Process).GetAllTopLevelWindows(Automation)
                .FirstOrDefault(window => window.Title == title),
            Patience, TimeSpan.FromMilliseconds(250), ignoreException: true).Result;
        return found ?? throw new AssertFailedException($"Pitboard showed no window titled {title} within {Patience}");
    }

    // A message box's line is a Text element named by it.
    public static string Said(Window box) =>
        string.Join(" ", box.FindAllDescendants(found => found.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
            .Select(text => text.Name));

    public static void Dismiss(Window box) =>
        box.FindFirstDescendant(found => found.ByControlType(FlaUI.Core.Definitions.ControlType.Button))!
            .AsButton().Invoke();

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
