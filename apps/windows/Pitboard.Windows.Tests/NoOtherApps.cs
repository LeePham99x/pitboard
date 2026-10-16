using Pitboard.Core;

namespace Pitboard.Windows.Tests;

// For a fixture whose machine runs no app: nothing here reaches one of this PC's.
internal sealed class NoOtherApps : AppControl
{
    public string? Running(string app) => null;

    public void RequestQuit(string app)
    {
    }

    public void Reopen(string location, bool front)
    {
    }
}
