namespace Pitboard.Windows;

// The updater, as the macOS app's Updates: one that cannot update says why and offers nothing.
public interface IUpdates
{
    // Raised on the app's thread when Waiting changes.
    event EventHandler? Changed;

    bool Available { get; }

    string? Refusal { get; }

    // A beta finds no later beta, only the next full release.
    bool Beta { get; }

    string? Waiting { get; }

    bool ChecksAutomatically { get; set; }

    // Once Pitboard's icon is in the notification area.
    void Start();

    void Check();

    void Shutdown();
}

public sealed class NoUpdates(string refusal = SettingsPage.NoUpdateKey) : IUpdates
{
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public bool Available => false;

    public string? Refusal => refusal;

    public bool Beta => false;

    public string? Waiting => null;

    public bool ChecksAutomatically
    {
        get => false;
        set { }
    }

    public void Start()
    {
    }

    public void Check()
    {
    }

    public void Shutdown()
    {
    }
}
