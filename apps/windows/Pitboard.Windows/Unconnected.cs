using Pitboard.Core;

namespace Pitboard.Windows;

// Until Pitboard finds ChatGPT on Windows, no other app is known to run.
public sealed class NoOtherApps : AppControl
{
    public string? Running(string app) => null;

    public void RequestQuit(string app)
    {
    }

    public void Reopen(string location, bool front)
    {
    }
}
