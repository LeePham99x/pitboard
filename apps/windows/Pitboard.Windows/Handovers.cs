namespace Pitboard.Windows;

// AppInstance raises a redirected launch once, which can be before this copy can take it.
public sealed class Handovers
{
    private readonly Lock _lock = new();
    private readonly List<string?> _held = [];
    private Action<string?>? _take;

    // From AppInstance.Activated, on a thread of its own, with the launch's Pitboard link or none.
    public void Arrived(string? link)
    {
        lock (_lock)
        {
            if (_take is not null)
            {
                _take(link);
            }
            // Every launch without a link held while Pitboard starts asks for the same window.
            else if (link is not null || !_held.Contains(null))
            {
                _held.Add(link);
            }
        }
    }

    // Under the lock, so the app takes each launch in the order it arrived.
    public void Ready(Action<string?> take)
    {
        lock (_lock)
        {
            _take = take;
            foreach (var link in _held)
            {
                take(link);
            }

            _held.Clear();
        }
    }
}
