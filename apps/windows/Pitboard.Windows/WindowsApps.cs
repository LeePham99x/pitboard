using Pitboard.Core;
using Windows.Win32.Foundation;

namespace Pitboard.Windows;

internal sealed record AppProcess(uint Id, uint Parent, string? Family, string? Aumid, bool OwnsAWindow);

// Store apps such as ChatGPT, by their AUMID, asked to quit and never forced.
public sealed class WindowsApps : AppControl
{
    private readonly Func<IReadOnlyList<AppProcess>> _listed;
    private readonly Action<string> _activate;
    private readonly Action _pitboardForward;

    // pitboardForward brings Pitboard's window in front of the app it opened, where the window is open.
    public WindowsApps(Action pitboardForward)
        : this(PackagedApps.Listed, PackagedApps.Activate, pitboardForward)
    {
    }

    internal WindowsApps(Func<IReadOnlyList<AppProcess>> listed, Action<string> activate, Action pitboardForward)
    {
        _listed = listed;
        _activate = activate;
        _pitboardForward = pitboardForward;
    }

    // Where its own AUMID cannot be read it still runs, by the core's.
    public string? Running(string app) => Windowed(app) is { } running ? running.Aumid ?? app : null;

    public void RequestQuit(string app) => _ = Quit(app);

    public void Reopen(string location, bool front)
    {
        _activate(location);
        if (!front)
        {
            _pitboardForward();
        }
    }

    // A test waits for RmShutdown; the model polls Running instead.
    internal Task<WIN32_ERROR> Quit(string app) =>
        Windowed(app) is { } running ? PackagedApps.Close(running.Id) : Task.FromResult(WIN32_ERROR.ERROR_SUCCESS);

    // Never a codex it runs, which has no window. Of several with windows, the one the package did not start.
    private AppProcess? Windowed(string app)
    {
        var family = app.Split('!')[0];
        var its = _listed().Where(process => string.Equals(process.Family, family, StringComparison.OrdinalIgnoreCase)).ToList();
        var windowed = its.Where(process => process.OwnsAWindow).ToList();
        return windowed.FirstOrDefault(process => its.All(other => other.Id != process.Parent)) ?? windowed.FirstOrDefault();
    }
}
