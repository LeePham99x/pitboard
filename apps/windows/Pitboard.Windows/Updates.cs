namespace Pitboard.Windows;

// The updater, as the macOS app's Updates: one that cannot update says so and offers nothing.
public interface IUpdates
{
    bool Available { get; }

    // The version a scheduled check found, waiting to be installed.
    string? Waiting { get; }

    void Check();
}

public sealed class NoUpdates : IUpdates
{
    public bool Available => false;

    public string? Waiting => null;

    public void Check()
    {
    }
}
