namespace Pitboard.Windows;

// A launch a second copy hands over, which can arrive while this copy starts, before it can open its window.
// AppInstance raises a redirected launch once, to the handlers connected then.
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
