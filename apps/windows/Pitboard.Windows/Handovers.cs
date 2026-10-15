namespace Pitboard.Windows;

// AppInstance raises a redirected launch once, which can be before this copy can open its window.
public sealed class Handovers
{
    private readonly Lock _lock = new();
    private Action? _reopen;
    private bool _held;

    // From AppInstance.Activated, on a thread of its own.
    public void Arrived()
    {
        Action? reopen;
        lock (_lock)
        {
            reopen = _reopen;
            _held |= reopen is null;
        }

        reopen?.Invoke();
    }

    public void Ready(Action reopen)
    {
        bool held;
        lock (_lock)
        {
            _reopen = reopen;
            held = _held;
            _held = false;
        }

        if (held)
        {
            reopen();
        }
    }
}
