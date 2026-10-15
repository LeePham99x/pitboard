using Pitboard.Core;

namespace Pitboard.Windows.Tests;

// Hands out the snapshot a test gives it and keeps every intent, so nothing a test sends reaches a core.
internal sealed class StandInModel(Snapshot first) : IPitboardModel
{
    private readonly List<Intent> _sent = [];

    public IReadOnlyList<Intent> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public void Send(Intent intent)
    {
        lock (_sent)
        {
            _sent.Add(intent);
        }
    }

    public void Shutdown()
    {
    }

    public Snapshot Snapshot() => first;
}
